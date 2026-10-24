using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using SGTProductionBox.Crafting;

namespace SGTProductionBox.Services;

/// <summary>
/// 自动流程：等采集材料齐（GBR 采集完成）→ 回个人/部队房屋 → 依次把制作任务交给 Artisan。
/// 决策依据是背包里"采集材料是否已经够数"，不依赖解析 GBR 的状态文本。
/// </summary>
public sealed class ProductionAutomation
{
    private enum Phase
    {
        Idle,
        Gather,
        Return,
        Arrive,
        Craft,
        Done,
        Failed,
    }

    private readonly CraftData craftData;
    private readonly InventoryService inventory;
    private readonly GatherBuddyIpc gatherBuddy;
    private readonly LifestreamIpc lifestream;
    private readonly ArtisanIpc artisan;
    private readonly IChatGui chat;
    private readonly PushService push;
    private readonly Configuration config;

    private readonly Dictionary<uint, int> gatherNeeds = new();
    private readonly Queue<CraftJob> jobs = new();

    private Phase phase = Phase.Idle;
    private DateTime nextTick = DateTime.MinValue;
    private DateTime readyAt;
    private DateTime deadline;
    private int destination;
    private int finishedJobs;
    private bool jobSent;

    public ProductionAutomation(
        CraftData craftData,
        InventoryService inventory,
        GatherBuddyIpc gatherBuddy,
        LifestreamIpc lifestream,
        ArtisanIpc artisan,
        IChatGui chat,
        PushService push,
        Configuration config)
    {
        this.craftData = craftData;
        this.inventory = inventory;
        this.gatherBuddy = gatherBuddy;
        this.lifestream = lifestream;
        this.artisan = artisan;
        this.chat = chat;
        this.push = push;
        this.config = config;
    }

    private readonly record struct CraftJob(uint RecipeId, int Amount, string Name);

    public bool Running => phase is Phase.Gather or Phase.Return or Phase.Arrive or Phase.Craft;

    public string Status { get; private set; } = "未运行";

    /// <summary>开始自动流程。returnTarget：0 = 不返回，1 = 个人房屋，2 = 部队房屋。</summary>
    public void Start(CraftList list, int returnTarget, IReadOnlySet<uint>? manualMaterials = null)
    {
        Stop(silent: true);

        destination = returnTarget;
        gatherNeeds.Clear();
        jobs.Clear();
        finishedJobs = 0;
        jobSent = false;
        inventory.Refresh();
        var manual = manualMaterials ?? new HashSet<uint>();

        var craftTotals = new Dictionary<uint, (uint RecipeId, int Amount, int Depth, string Name)>();

        // 背包里已有的部分会被扣掉；手工材料返回 0，按全量计算。
        int Stock(uint itemId) => manual.Contains(itemId) ? 0 : inventory.Count(itemId);

        void Walk(MaterialNode node, int depth)
        {
            if (node.RecipeId != 0)
            {
                craftTotals.TryGetValue(node.ItemId, out var current);
                craftTotals[node.ItemId] = (
                    node.RecipeId,
                    current.Amount + node.Missing,
                    Math.Max(current.Depth, depth),
                    node.Name);
            }
            else if (node.Kind == MaterialKind.Gathered)
            {
                gatherNeeds[node.ItemId] = gatherNeeds.GetValueOrDefault(node.ItemId) + node.Missing;
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }

        foreach (var entry in list.Entries)
        {
            Walk(craftData.BuildTree(entry.ItemId, entry.Amount, 12, Stock, config.RecipeOverrides), 0);
        }

        foreach (var pair in craftTotals
                     .OrderByDescending(pair => pair.Value.Depth)
                     .ThenBy(pair => pair.Value.Name, StringComparer.Ordinal))
        {
            jobs.Enqueue(new CraftJob(pair.Value.RecipeId, pair.Value.Amount, pair.Value.Name));
        }

        if (jobs.Count == 0)
        {
            phase = Phase.Failed;
            Status = "清单里没有可制作的物品。";
            chat.PrintError("[SGT] " + Status);
            return;
        }

        phase = gatherNeeds.Count > 0 && GatherMissing() > 0 ? Phase.Gather : Phase.Return;
        nextTick = DateTime.MinValue;
        Status = phase == Phase.Gather ? $"等待采集：还差 {GatherMissing()} 个材料" : "准备回房屋…";
        chat.Print($"[SGT] 自动流程开始：{jobs.Count} 项制作任务，{DestinationName()}。");
    }

    public void Stop(bool silent = false)
    {
        if (phase is Phase.Done or Phase.Failed or Phase.Idle)
        {
            phase = Phase.Idle;
            Status = "未运行";
            return;
        }

        phase = Phase.Idle;
        Status = "已停止";
        if (!silent)
        {
            chat.Print("[SGT] 自动流程已停止。");
        }
    }

    public void Tick()
    {
        if (!Running)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < nextTick)
        {
            return;
        }

        nextTick = now.AddMilliseconds(500);
        inventory.RefreshIfDue();

        switch (phase)
        {
            case Phase.Gather:
            {
                var missing = GatherMissing();
                if (missing > 0)
                {
                    var gbr = gatherBuddy.TryGetStatus(out var enabled, out var statusText) && enabled
                        ? $"（GBR：{statusText}）"
                        : string.Empty;
                    Status = $"等待采集：还差 {missing} 个材料{gbr}";
                    return;
                }

                // 材料够了就先关掉 GBR 自动采集，避免和传送/制作打架。
                gatherBuddy.TrySetEnabled(false, out _);
                Status = "采集材料已齐，准备回房屋…";
                phase = Phase.Return;
                break;
            }

            case Phase.Return:
            {
                if (destination == 0)
                {
                    StartCrafting();
                    break;
                }

                var toFc = destination == 2;
                if (!lifestream.TryTeleport(toFc, out var error))
                {
                    Fail(error);
                    break;
                }

                Status = toFc ? "正在返回部队房屋…" : "正在返回个人房屋…";
                readyAt = now.AddSeconds(4);
                deadline = now.AddMinutes(4);
                phase = Phase.Arrive;
                break;
            }

            case Phase.Arrive:
            {
                if (now < readyAt)
                {
                    Status = "传送中…";
                    return;
                }

                if (!lifestream.TryIsBusy(out var busy))
                {
                    Fail("Lifestream 接口不可用。");
                    break;
                }

                if (!busy)
                {
                    if (destination != 0 && !IsInsideHouse())
                    {
                        Fail("没有进入房屋。请在 Lifestream 里把这套房子的进入方式设为「进入房屋」。");
                        break;
                    }

                    StartCrafting();
                }
                else if (now > deadline)
                {
                    Fail("返回房屋超时。");
                }
                else
                {
                    Status = "传送中…";
                }

                break;
            }

            case Phase.Craft:
                TickCrafting(now);
                break;
        }
    }

    private void TickCrafting(DateTime now)
    {
        if (jobs.Count == 0)
        {
            if (artisan.IsCrafting())
            {
                Status = "Artisan 正在制作…";
                return;
            }

            phase = Phase.Done;
            Status = $"完成：已提交 {finishedJobs} 项制作任务。";
            chat.Print("[SGT] 自动流程完成，制作任务已全部交给 Artisan。");
            if (config.PushOnDone)
            {
                push.Notify(
                    (PushProvider)config.PushProvider,
                    config.PushToken,
                    "SGT 自动流程完成",
                    $"已提交 {finishedJobs} 项制作任务。");
            }

            return;
        }

        if (!jobSent)
        {
            if (artisan.IsCrafting())
            {
                Status = "Artisan 正在制作…";
                return;
            }

            var job = jobs.Dequeue();
            if (!artisan.TryCraft(job.RecipeId, job.Amount, out var error))
            {
                Fail(error);
                return;
            }

            jobSent = true;
            readyAt = now.AddSeconds(3);
            deadline = now.AddMinutes(180);
            Status = $"已提交 {job.Name} ×{job.Amount}，剩余 {jobs.Count} 项";
            return;
        }

        if (now < readyAt)
        {
            Status = "Artisan 正在开始制作…";
            return;
        }

        if (artisan.IsCrafting())
        {
            Status = "Artisan 正在制作…";
            return;
        }

        jobSent = false;
        finishedJobs++;
    }

    private int GatherMissing()
    {
        var missing = 0;
        foreach (var pair in gatherNeeds)
        {
            missing += Math.Max(0, pair.Value - inventory.Count(pair.Key));
        }

        return missing;
    }

    private static unsafe bool IsInsideHouse()
    {
        var manager = HousingManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var indoor = manager->IndoorTerritory;
        return indoor != null && indoor->IsLoaded();
    }

    private void StartCrafting()    {
        phase = Phase.Craft;
        Status = "开始制作…";
        chat.Print($"[SGT] 开始制作，共 {jobs.Count} 项任务。");
    }

    private void Fail(string reason)
    {
        phase = Phase.Failed;
        Status = "失败：" + reason;
        chat.PrintError("[SGT] 自动流程失败：" + reason);
        if (config.PushOnError)
        {
            push.Notify(
                (PushProvider)config.PushProvider,
                config.PushToken,
                "SGT 自动流程失败",
                reason);
        }
    }

    public string DestinationName() => destination switch
    {
        1 => "采集完回个人房屋",
        2 => "采集完回部队房屋",
        _ => "不返回，原地制作",
    };

    public void Dispose()
    {
        Stop(silent: true);
    }
}
