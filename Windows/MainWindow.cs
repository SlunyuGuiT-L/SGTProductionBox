using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using SGTProductionBox.Crafting;

namespace SGTProductionBox.Windows;

/// <summary>
/// 主窗口：左边管理清单，右边展开配方树和材料汇总。
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private string searchQuery = string.Empty;
    private string newListName = string.Empty;
    private uint selectedItemId;
    private int selectedAmount = 1;
    private string kindFilter = "all";
    private bool showTree;
    private bool openStartFlow;
    private bool forceOpenActiveList;
    private MaterialNode? tree;
    private string treeKey = string.Empty;
    private Dictionary<uint, int> nodeTotals = new();
    private List<MaterialRow> rows = new();
    private string rowsKey = string.Empty;
    private readonly Dictionary<(uint ItemId, int Amount), MaterialNode> treeCache = new();
    private string treesKey = string.Empty;

    public MainWindow(Plugin plugin)
        : base("SGT Production Box###SGTProductionBoxMain", ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(1080, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(plugin.Configuration.WindowOpacity);
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        if (config.CraftLists.Count == 0)
        {
            config.CraftLists.Add(new CraftList { Name = "默认清单" });
            config.ActiveListIndex = 0;
            config.Save();
        }

        using (var left = ImRaii.Child("##left", new Vector2(320, 0), true))
        {
            if (left.Success)
            {
                DrawLeftPanel();
            }
        }

        ImGui.SameLine();

        using (var right = ImRaii.Child("##right", Vector2.Zero, true))
        {
            if (right.Success)
            {
                DrawRightPanel();
            }
        }

        if (openStartFlow)
        {
            ImGui.OpenPopup("##start-flow");
            openStartFlow = false;
        }

        DrawStartFlowPopup();
    }

    /// <summary>给 /mg add 用：按名字添加第一个匹配的物品。</summary>
    public bool AddItemByName(string name)
    {
        var result = plugin.CraftData.SearchItems(name, 1).FirstOrDefault();
        if (result.ItemId == 0)
        {
            return false;
        }

        AddEntry(result.ItemId);
        selectedItemId = result.ItemId;
        var entry = CurrentList().Entries.FirstOrDefault(item => item.ItemId == result.ItemId);
        selectedAmount = Math.Max(1, entry?.Amount ?? 1);
        tree = null;
        return true;
    }

    /// <summary>给自动流程用：取当前清单。</summary>
    public CraftList ActiveCraftList() => CurrentList();

    private void DrawLeftPanel()
    {
        DrawListHeader();
        ImGui.Separator();
        DrawSearch();
        ImGui.Separator();
        DrawAllLists();
    }

    private void DrawListHeader()
    {
        var config = plugin.Configuration;
        var lists = config.CraftLists;
        var index = Math.Clamp(config.ActiveListIndex, 0, lists.Count - 1);

        // 顶部保持原来的下拉框样式。
        if (ImGui.BeginCombo("##list-combo", lists[index].Name))
        {
            for (var i = 0; i < lists.Count; i++)
            {
                if (ImGui.Selectable(lists[i].Name, i == index))
                {
                    SwitchToList(i);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("新建"))
        {
            newListName = $"清单 {lists.Count + 1}";
            ImGui.OpenPopup("new-list");
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(lists.Count <= 1))
        {
            if (ImGui.Button("删除"))
            {
                lists.RemoveAt(index);
                config.ActiveListIndex = Math.Clamp(index, 0, lists.Count - 1);
                ResetSelection();
                config.Save();
            }
        }

        if (ImGui.BeginPopup("new-list"))
        {
            ImGui.InputText("清单名称", ref newListName, 64);

            if (ImGui.Button("创建") && !string.IsNullOrWhiteSpace(newListName))
            {
                lists.Add(new CraftList { Name = newListName.Trim() });
                config.ActiveListIndex = lists.Count - 1;
                ResetSelection();
                config.Save();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void SwitchToList(int index)
    {
        plugin.Configuration.ActiveListIndex = index;
        ResetSelection();
        forceOpenActiveList = true;
        plugin.Configuration.Save();
    }

    private void ResetSelection()
    {
        selectedItemId = 0;
        selectedAmount = 1;
        tree = null;
        showTree = false;
    }

    private void DrawSearch()
    {
        ImGui.InputTextWithHint("##search", "搜索物品名或 ID，回车添加", ref searchQuery, 128);

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            ImGui.TextDisabled("例如：爆发药 / 8级暗物质 / 44123");
            return;
        }

        var results = plugin.CraftData.SearchItems(searchQuery, 12);

        using var child = ImRaii.Child("##search-results", new Vector2(0, 150), true);
        if (!child.Success)
        {
            return;
        }

        foreach (var result in results)
        {
            ImGui.PushID((int)result.ItemId);
            DrawItemIcon(result.Icon);
            ImGui.SameLine();

            if (ImGui.Selectable($"{result.Name}##add", false, ImGuiSelectableFlags.None,
                    new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                AddEntry(result.ItemId);
                selectedItemId = result.ItemId;
                tree = null;
            }

            ImGui.PopID();
        }

        if (results.Count == 0)
        {
            ImGui.TextDisabled("没找到匹配的物品。");
        }
    }

    /// <summary>每个清单一个可折叠分组，展开后显示该清单的物品。</summary>
    private void DrawAllLists()
    {
        var config = plugin.Configuration;
        var forceOpen = forceOpenActiveList;
        forceOpenActiveList = false;

        for (var i = 0; i < config.CraftLists.Count; i++)
        {
            var list = config.CraftLists[i];
            var active = i == config.ActiveListIndex;

            ImGui.PushID(i);

            if (active && forceOpen)
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }

            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.85f, 1.0f, 1.0f));
            }

            var open = ImGui.CollapsingHeader($"{list.Name}（{list.Entries.Count}）###list{i}");

            if (active)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !active)
            {
                SwitchToList(i);
            }

            if (open)
            {
                DrawEntryRows(list);
            }

            ImGui.PopID();
        }
    }

    private void DrawEntryRows(CraftList list)
    {
        foreach (var entry in list.Entries.ToList())
        {
            var (name, icon) = plugin.CraftData.GetItemInfo(entry.ItemId);

            ImGui.PushID((int)entry.ItemId);

            // 物品名放在最前面，占满剩余宽度，点击选中。
            var nameWidth = Math.Max(60f, ImGui.GetContentRegionAvail().X - 150f * ImGuiHelpers.GlobalScale);
            if (ImGui.Selectable(name, selectedItemId == entry.ItemId, ImGuiSelectableFlags.None,
                    new Vector2(nameWidth, 0)))
            {
                plugin.Configuration.ActiveListIndex = plugin.Configuration.CraftLists.IndexOf(list);
                selectedItemId = entry.ItemId;
                selectedAmount = Math.Max(1, entry.Amount);
                tree = null;
                plugin.Configuration.Save();
            }

            if (ImGui.BeginPopupContextItem("entry-menu"))
            {
                if (ImGui.MenuItem("删除"))
                {
                    list.Entries.Remove(entry);
                    if (selectedItemId == entry.ItemId)
                    {
                        selectedItemId = 0;
                        tree = null;
                    }

                    plugin.Configuration.Save();
                }

                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered() && icon != 0)
            {
                ImGui.BeginTooltip();
                DrawItemIcon(icon, 40);
                ImGui.EndTooltip();
            }

            // 数量输入框：可以直接打字，右侧再用按钮微调。
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f * ImGuiHelpers.GlobalScale);
            var amount = entry.Amount;
            if (ImGui.InputInt("##amount", ref amount, 0, 0))
            {
                entry.Amount = Math.Clamp(amount, 1, 99999);
                plugin.Configuration.Save();
                tree = null;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("-"))
            {
                entry.Amount = Math.Max(1, entry.Amount - 1);
                plugin.Configuration.Save();
                tree = null;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("+"))
            {
                entry.Amount++;
                plugin.Configuration.Save();
                tree = null;
            }

            ImGui.PopID();
        }
    }

    private static Dictionary<uint, int> Aggregate(MaterialNode root)
    {
        var totals = new Dictionary<uint, int>();

        void Walk(MaterialNode node)
        {
            totals[node.ItemId] = totals.GetValueOrDefault(node.ItemId) + node.Amount;
            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        Walk(root);
        return totals;
    }

    /// <summary>把"总共要采多少、制作几次、兑换/购买多少"汇总成一行。</summary>
    private string BuildTotalsLine(MaterialNode root)
    {
        int gathered = 0, currency = 0, vendor = 0, unknown = 0;
        foreach (var (node, total) in plugin.CraftData.Summarize(root))
        {
            switch (node.Kind)
            {
                case MaterialKind.Gathered:
                    gathered += total;
                    break;
                case MaterialKind.Currency:
                    currency += total;
                    break;
                case MaterialKind.Vendor:
                    vendor += total;
                    break;
                default:
                    unknown += total;
                    break;
            }
        }

        var crafts = 0;
        var seen = new HashSet<uint>();

        void Walk(MaterialNode node)
        {
            if (node.RecipeId != 0 && seen.Add(node.ItemId))
            {
                var total = nodeTotals.GetValueOrDefault(node.ItemId);
                var output = Math.Max(1, node.OutputPerCraft);
                crafts += (int)Math.Ceiling(total / (double)output);
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        Walk(root);

        var parts = new List<string>();
        if (crafts > 0)
        {
            parts.Add($"制作 {crafts} 次");
        }

        if (gathered > 0)
        {
            parts.Add($"采集 {gathered}");
        }

        if (currency > 0)
        {
            parts.Add($"兑换 {currency}");
        }

        if (vendor > 0)
        {
            parts.Add($"购买 {vendor}");
        }

        if (unknown > 0)
        {
            parts.Add($"未知 {unknown}");
        }

        return parts.Count == 0 ? "无需材料" : string.Join(" · ", parts);
    }

    private void DrawRightPanel()
    {
        var list = CurrentList();

        plugin.Inventory.RefreshIfDue();
        EnsureRows(list);

        DrawTopRow(list);
        DrawFilterChips();
        DrawCardsArea();
    }

    /// <summary>顶部三栏：左边配方树，中间大圆环，右边制作/采集汇总。</summary>
    private void DrawTopRow(CraftList list)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var avail = ImGui.GetContentRegionAvail();
        var height = 250f * scale;

        // 三栏平均分宽度。
        var columnWidth = Math.Max(120f * scale, (avail.X - spacing * 2f) / 3f);

        using (var left = ImRaii.Child("##top-tree", new Vector2(columnWidth, height), true))
        {
            if (left.Success)
            {
                DrawTreePanelContent();
            }
        }

        ImGui.SameLine();

        using (var center = ImRaii.Child("##top-hero", new Vector2(columnWidth, height), false))
        {
            if (center.Success)
            {
                DrawHero(list);
            }
        }

        ImGui.SameLine();

        using (var right = ImRaii.Child("##top-summary", new Vector2(columnWidth, height), true))
        {
            if (right.Success)
            {
                DrawSummaryColumn();
            }
        }
    }

    private void EnsureRows(CraftList list)
    {
        var signature = $"{plugin.Configuration.ActiveListIndex}|{list.Name}|{string.Join(';', list.Entries.Select(e => $"{e.ItemId}:{e.Amount}"))}|{plugin.Inventory.Version}|{string.Join(',', plugin.Configuration.ManualMaterials)}|{string.Join(',', plugin.Configuration.RecipeOverrides.Select(pair => $"{pair.Key}:{pair.Value}"))}";
        if (signature == rowsKey)
        {
            return;
        }

        rowsKey = signature;
        // 库存变了就强制重建配方树，避免用到旧的库存快照算缺口。
        treeCache.Clear();
        treesKey = string.Empty;
        rows = BuildRows(list);
    }

    /// <summary>顶部大状态区：完成度圆环 + 标题 + 汇总 + 按钮。</summary>
    private void DrawHero(CraftList list)
    {
        var totalNeed = rows.Sum(row => row.Need);
        var haveTotal = rows.Sum(row => Math.Min(row.Have, row.Need));
        var missing = rows.Sum(row => row.Gap);
        var crafts = rows.Sum(row => row.Crafts);
        var ratio = totalNeed == 0 ? 0f : Math.Clamp(haveTotal / (float)totalNeed, 0f, 1f);
        var complete = rows.Count > 0 && missing == 0;

        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail();
        var radius = Math.Clamp(avail.X * 0.15f, 34f * scale, 52f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + new Vector2(avail.X / 2f, radius + 6f * scale);
        var draw = ImGui.GetWindowDrawList();

        var fill = complete
            ? new Vector4(0.13f, 0.36f, 0.26f, 1f)
            : new Vector4(0.13f, 0.20f, 0.31f, 1f);
        draw.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 64);
        draw.AddCircle(
            center,
            radius,
            ImGui.GetColorU32(complete ? new Vector4(0.40f, 0.90f, 0.60f, 1f) : new Vector4(0.45f, 0.60f, 0.90f, 1f)),
            64,
            2f * scale);

        if (complete)
        {
            var white = ImGui.GetColorU32(new Vector4(0.75f, 1f, 0.85f, 1f));
            var s = radius * 0.55f;
            draw.AddLine(
                center + new Vector2(-s * 0.9f, 0f),
                center + new Vector2(-s * 0.15f, s * 0.7f),
                white,
                6f * scale);
            draw.AddLine(
                center + new Vector2(-s * 0.15f, s * 0.7f),
                center + new Vector2(s * 0.95f, -s * 0.7f),
                white,
                6f * scale);
        }
        else
        {
            if (ratio > 0f)
            {
                draw.PathArcTo(center, radius - 4f * scale, -MathF.PI / 2f, -MathF.PI / 2f + MathF.PI * 2f * ratio, 64);
                draw.PathStroke(ImGui.GetColorU32(new Vector4(0.55f, 0.75f, 1f, 1f)), 0, 4f * scale);
            }

            var percent = $"{ratio:P0}";
            var textSize = ImGui.CalcTextSize(percent);
            draw.AddText(center - textSize / 2f, ImGui.GetColorU32(new Vector4(0.85f, 0.92f, 1f, 1f)), percent);
        }

        ImGui.Dummy(new Vector2(avail.X, radius * 2f + 14f * scale));

        var title = rows.Count == 0
            ? "清单是空的，先搜索添加物品"
            : complete
                ? "材料已齐，可以去制作了"
                : $"还差 {missing} 件材料";

        if (ImGui.CalcTextSize(title).X > avail.X - 8f * scale)
        {
            title = complete ? "材料已齐" : $"还差 {missing} 件";
        }
        var subtitle = rows.Count == 0
            ? "搜索物品加入清单后，这里会显示完成度"
            : $"{list.Name} · {rows.Count} 种材料 · 制作 {crafts} 次 · 已有 {haveTotal}/{totalNeed}";

        if (ImGui.CalcTextSize(subtitle).X > avail.X - 8f * scale)
        {
            subtitle = $"{rows.Count} 种材料 · 制作 {crafts} 次";
        }

        if (ImGui.CalcTextSize(subtitle).X > avail.X - 8f * scale)
        {
            subtitle = $"已有 {haveTotal}/{totalNeed}";
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (avail.X - ImGui.CalcTextSize(title).X) / 2f));
        ImGui.TextColored(
            complete ? new Vector4(0.60f, 0.95f, 0.70f, 1f) : new Vector4(0.90f, 0.94f, 1f, 1f),
            title);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (avail.X - ImGui.CalcTextSize(subtitle).X) / 2f));
        ImGui.TextDisabled(subtitle);

        ImGui.Spacing();
        DrawHeroButtons(avail.X);
    }

    private void DrawHeroButtons(float availableWidth)
    {
        string[] labels = { "一键开始", "刷新背包", "复制清单", "设置" };
        var padding = ImGui.GetStyle().FramePadding;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var widths = labels.Select(label => ImGui.CalcTextSize(label).X + padding.X * 2f).ToArray();
        var total = widths.Sum() + spacing * (labels.Length - 1);

        // 中间栏变窄时换成短标签，避免按钮被裁掉。
        if (total > availableWidth - 4f)
        {
            labels = new[] { "开始", "刷新", "复制", "设置" };
            widths = labels.Select(label => ImGui.CalcTextSize(label).X + padding.X * 2f).ToArray();
            total = widths.Sum() + spacing * (labels.Length - 1);
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (availableWidth - total) / 2f));

        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            if (!ImGui.Button(labels[i], new Vector2(widths[i], 0f)))
            {
                continue;
            }

            switch (i)
            {
                case 0:
                    openStartFlow = true;
                    break;
                case 1:
                    plugin.Inventory.Refresh();
                    rowsKey = string.Empty;
                    break;
                case 2:
                    ImGui.SetClipboardText(string.Join(
                        '\n',
                        rows.Select(row => $"{row.Name} ×{row.Need}（已有 {row.Have}，还需 {Math.Max(0, row.Need - row.Have)}）")));
                    Plugin.ChatGui.Print("[SGT] 材料清单已复制到剪贴板。");
                    break;
                default:
                    plugin.ToggleConfigUi();
                    break;
            }
        }
    }

    /// <summary>一键开始流程的确认弹窗。</summary>
    private void DrawStartFlowPopup()
    {
        if (!ImGui.BeginPopup("##start-flow"))
        {
            return;
        }

        var config = plugin.Configuration;

        ImGui.TextColored(new Vector4(0.85f, 0.90f, 1.00f, 1f), "一键开始流程");
        ImGui.Separator();
        ImGui.TextWrapped("1. 等待 GBR 采集完成（清单里的采集材料够数）");
        ImGui.TextWrapped("2. 返回房屋");
        ImGui.TextWrapped("3. 开始制作");
        ImGui.Spacing();
        ImGui.TextWrapped("请确认 GatherBuddyReborn 已经设置好要采集的材料。");

        if (plugin.GatherBuddy.TryGetStatus(out var enabled, out var statusText))
        {
            ImGui.Text($"GBR 自动采集：{(enabled ? "已开启" : "已关闭")}");
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                ImGui.TextDisabled(statusText);
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.50f, 1f), "未检测到 GatherBuddyReborn");
        }

        string[] targets = { "不返回（原地制作）", "个人房屋", "部队房屋" };
        var target = Math.Clamp(config.AutoReturnTarget, 0, 2);
        if (ImGui.Combo("返回地点", ref target, targets, targets.Length))
        {
            config.AutoReturnTarget = target;
            config.Save();
        }

        var autoEnable = config.AutoEnableGather;
        if (ImGui.Checkbox("开始时自动开启 GBR 自动采集", ref autoEnable))
        {
            config.AutoEnableGather = autoEnable;
            config.Save();
        }

        ImGui.Spacing();

        if (ImGui.Button("开始流程"))
        {
            if (config.AutoEnableGather)
            {
                plugin.GatherBuddy.TrySetEnabled(true, out _);
            }

            plugin.StartAutomation();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("取消"))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>右侧一栏：上半是制作，下半是采集。</summary>
    private void DrawSummaryColumn()
    {
        var craftRows = rows.Where(row => row.Kind == MaterialKind.Craftable)
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
        var gatherRows = rows.Where(row => row.Kind == MaterialKind.Gathered)
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ToList();

        ImGui.TextDisabled($"制作 · {craftRows.Count} 种 · {craftRows.Sum(row => row.Crafts)} 次");
        DrawSummaryRows(craftRows, row => $"×{row.Gap}（{row.Crafts} 次）");

        ImGui.Separator();

        var missing = gatherRows.Sum(row => row.Gap);
        ImGui.TextDisabled($"采集 · {gatherRows.Count} 种 · 还需 {missing}");
        DrawSummaryRows(gatherRows, row =>
        {
            var left = row.Gap;
            return left > 0 ? $"×{left}" : "已够";
        });
    }

    private void DrawSummaryRows(List<MaterialRow> items, Func<MaterialRow, string> format)
    {
        if (items.Count == 0)
        {
            ImGui.TextDisabled("暂无");
        }

        foreach (var row in items)
        {
            ImGui.PushID((int)row.ItemId);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorOf(row.Kind));
            if (ImGui.Selectable($"{row.Name}  {format(row)}##summary-row", false))
            {
                selectedItemId = row.ItemId;
                selectedAmount = Math.Max(1, row.Need);
                tree = null;
                showTree = true;
            }

            ImGui.PopStyleColor();
            ImGui.PopID();
        }
    }

    /// <summary>筛选标签。</summary>
    private void DrawFilterChips()
    {
        var filters = new (string Key, string Label)[]
        {
            ("all", "全部"),
            ("missing", "仅缺料"),
            ("craft", "制作"),
            ("gather", "采集"),
            ("currency", "兑换"),
            ("vendor", "购买"),
        };

        var scale = ImGuiHelpers.GlobalScale;
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < filters.Length; i++)
        {
            var (key, label) = filters[i];
            var padding = new Vector2(10f, 4f) * scale;
            var size = ImGui.CalcTextSize(label) + padding * 2f;
            var pos = ImGui.GetCursorScreenPos();

            ImGui.PushID(key);
            ImGui.InvisibleButton("chip", size);
            var hovered = ImGui.IsItemHovered();
            var active = kindFilter == key;
            if (ImGui.IsItemClicked())
            {
                kindFilter = key;
            }

            var background = active
                ? new Vector4(0.30f, 0.55f, 0.90f, 1f)
                : hovered
                    ? new Vector4(0.22f, 0.28f, 0.40f, 1f)
                    : new Vector4(0.14f, 0.18f, 0.26f, 1f);

            draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(background), size.Y / 2f);
            draw.AddText(pos + padding, ImGui.GetColorU32(new Vector4(0.90f, 0.93f, 1f, 1f)), label);
            ImGui.PopID();

            if (i < filters.Length - 1)
            {
                ImGui.SameLine();
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("全背包"))
        {
            foreach (var row in rows)
            {
                plugin.Configuration.ManualMaterials.Remove(row.ItemId);
            }

            plugin.Configuration.Save();
            rowsKey = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("全手工"))
        {
            foreach (var row in rows)
            {
                if (!plugin.Configuration.ManualMaterials.Contains(row.ItemId))
                {
                    plugin.Configuration.ManualMaterials.Add(row.ItemId);
                }
            }

            plugin.Configuration.Save();
            rowsKey = string.Empty;
        }

        ImGui.Spacing();
    }
    private void DrawCardsArea()
    {
        var filtered = rows.Where(MatchesFilter).ToList();

        if (filtered.Count == 0)
        {
            ImGui.TextDisabled(rows.Count == 0 ? "清单是空的，先在左边搜索添加物品。" : "当前筛选没有材料。");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var cardHeight = 120f * scale;

        foreach (var group in filtered.GroupBy(row => row.Kind).OrderBy(group => group.Key))
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"{KindName(group.Key)} · {group.Count()} 种");

            var groupRows = group.ToList();
            var width = ImGui.GetContentRegionAvail().X;
            var columns = Math.Max(1, (int)((width + spacing) / (270f * scale + spacing)));
            var cardWidth = Math.Max(220f * scale, (width - spacing * (columns - 1)) / columns);

            var index = 0;
            foreach (var row in groupRows)
            {
                if (index % columns != 0)
                {
                    ImGui.SameLine();
                }

                DrawCard(row, cardWidth, cardHeight);
                index++;
            }
        }
    }

    private bool MatchesFilter(MaterialRow row) => kindFilter switch
    {
        "missing" => row.Gap > 0,
        "craft" => row.Kind == MaterialKind.Craftable,
        "gather" => row.Kind == MaterialKind.Gathered,
        "currency" => row.Kind == MaterialKind.Currency,
        "vendor" => row.Kind == MaterialKind.Vendor,
        _ => true,
    };

    private void DrawCard(MaterialRow row, float width, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var color = ColorOf(row.Kind);
        var missing = Math.Max(0, row.Gap);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 6f * scale)
            .Push(ImGuiStyleVar.ChildBorderSize, 1f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * scale);
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.13f, 0.19f, 0.95f));
        using var border = ImRaii.PushColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.45f));

        var id = $"##card{row.ItemId}";
        if (ImGui.BeginChild(id, new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            if (row.Icon != 0)
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(row.Icon)).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, new Vector2(34f, 34f) * scale);
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            var displayName = row.Name.Length > 15 ? row.Name[..14] + "…" : row.Name;
            ImGui.TextUnformatted(displayName);
            if (ImGui.IsItemHovered())
            {
                var tip = displayName != row.Name ? row.Name + "\n点击复制名称" : "点击复制名称";
                ImGui.SetTooltip(tip);
            }

            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(row.Name);
                Plugin.ChatGui.Print($"[SGT] 已复制材料名：{row.Name}");
            }

            var tagClicked = TagPill("##kind-tag", KindName(row.Kind), color, "点击查看配方树");
            ImGui.SameLine();
            var modeColor = row.Manual
                ? new Vector4(0.95f, 0.70f, 0.40f, 1f)
                : new Vector4(0.55f, 0.80f, 1.00f, 1f);
            var modeClicked = TagPill(
                "##mode-tag",
                row.Manual ? "手工" : "背包",
                modeColor,
                row.Manual ? "点击改为使用背包数量" : "点击改为手工：忽略背包，全部重新准备");
            ImGui.EndGroup();

            if (modeClicked)
            {
                plugin.Configuration.ToggleManual(row.ItemId);
                rowsKey = string.Empty;
            }

            var fraction = row.Need == 0 ? 0f : Math.Clamp(row.Have / (float)row.Need, 0f, 1f);
            // 第三个参数传空字符串：不传会默认显示百分比，数字会画在很矮的进度条上被裁掉。
            ImGui.ProgressBar(fraction, new Vector2(-1f, 9f * scale), string.Empty);

            var progressText = $"现有 {row.Have} / {row.Need}";
            if (row.Manual)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.72f, 0.45f, 1f), $"手工 · 需要全部准备 {row.Need}");
            }
            else if (missing > 0)
            {
                progressText += $" · 还需 {missing}";
                ImGui.TextColored(new Vector4(1.0f, 0.62f, 0.40f, 1f), progressText);
            }
            else
            {
                ImGui.TextDisabled(progressText + " · 已备齐");
            }

            var caption = row.Kind == MaterialKind.Craftable ? $"制作 {row.Crafts} 次" : row.Source;
            if (string.IsNullOrEmpty(caption))
            {
                caption = " ";
            }

            var fullCaption = caption;
            if (caption.Length > 22)
            {
                caption = caption[..21] + "…";
            }

            ImGui.TextDisabled(caption);
            if (caption != fullCaption && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(fullCaption);
            }

            if (tagClicked)
            {
                selectedItemId = row.ItemId;
                selectedAmount = Math.Max(1, row.Need);
                tree = null;
                showTree = true;
            }
        }

        ImGui.EndChild();
    }

    /// <summary>卡片上的小标签：类型标签点击打开配方树，模式标签点击切换背包/手工。</summary>
    private static bool TagPill(string id, string text, Vector4 color, string tooltip)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(6f, 2f) * scale;
        var size = ImGui.CalcTextSize(text) + padding * 2f;
        var pos = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(id, size);
        var clicked = ImGui.IsItemClicked();
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(
            pos,
            pos + size,
            ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, hovered ? 0.38f : 0.22f)),
            size.Y / 2f);
        draw.AddRect(
            pos,
            pos + size,
            ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.75f)),
            size.Y / 2f);
        draw.AddText(pos + padding, ImGui.GetColorU32(color), text);

        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }

    /// <summary>把当前清单所有条目的材料合并成一张表。</summary>
    private List<MaterialRow> BuildRows(CraftList list)
    {
        var totals = new Dictionary<uint, (int Need, int Missing)>();
        var nodes = new Dictionary<uint, MaterialNode>();

        void Walk(MaterialNode node)
        {
            var current = totals.TryGetValue(node.ItemId, out var value) ? value : default;
            totals[node.ItemId] = (current.Need + node.Amount, current.Missing + node.Missing);

            if (!nodes.TryGetValue(node.ItemId, out var existing)
                || (existing.Kind != MaterialKind.Craftable && node.Kind == MaterialKind.Craftable))
            {
                nodes[node.ItemId] = node;
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        foreach (var root in GetListTrees(list))
        {
            Walk(root);
        }

        return totals
            .Select(pair =>
            {
                var node = nodes[pair.Key];
                var manual = !plugin.Configuration.UsesInventory(pair.Key);
                return new MaterialRow
                {
                    ItemId = pair.Key,
                    Name = node.Name,
                    Kind = node.Kind,
                    Need = pair.Value.Need,
                    Missing = pair.Value.Missing,
                    Have = manual ? 0 : plugin.Inventory.Count(pair.Key),
                    Manual = manual,
                    Output = Math.Max(1, node.OutputPerCraft),
                    Icon = node.Icon,
                    Source = node.Source,
                };
            })
            .OrderBy(row => row.Kind)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>缓存每个清单条目的配方树；背包数量或手工标记变化时重建。</summary>
    private List<MaterialNode> GetListTrees(CraftList list)
    {
        var key = $"{string.Join(';', list.Entries.Select(entry => $"{entry.ItemId}:{entry.Amount}"))}"
                  + $"|{plugin.Inventory.Version}|{string.Join(',', plugin.Configuration.ManualMaterials)}"
                  + $"|{string.Join(',', plugin.Configuration.RecipeOverrides.Select(pair => $"{pair.Key}:{pair.Value}"))}";
        if (key != treesKey)
        {
            treesKey = key;
            treeCache.Clear();
        }

        int Stock(uint itemId) => plugin.Configuration.UsesInventory(itemId) ? plugin.Inventory.Count(itemId) : 0;

        var result = new List<MaterialNode>(list.Entries.Count);
        foreach (var entry in list.Entries)
        {
            var cacheKey = (entry.ItemId, entry.Amount);
            if (!treeCache.TryGetValue(cacheKey, out var root))
            {
                root = plugin.CraftData.BuildTree(entry.ItemId, entry.Amount, 12, Stock, plugin.Configuration.RecipeOverrides);
                treeCache[cacheKey] = root;
            }

            result.Add(root);
        }

        return result;
    }

    /// <summary>左侧配方树栏：默认提示，点击卡片类型标签后显示。</summary>
    private void DrawTreePanelContent()
    {
        if (!showTree || selectedItemId == 0)
        {
            ImGui.TextDisabled("配方树");
            ImGui.Separator();
            ImGui.TextWrapped("点击卡片上的类型标签，查看该材料的配方树。");
            return;
        }

        var key = $"{selectedItemId}|{selectedAmount}";
        if (tree is null || treeKey != key)
        {
            tree = plugin.CraftData.BuildTree(selectedItemId, selectedAmount);
            treeKey = key;
            nodeTotals = Aggregate(tree);
        }

        if (ImGui.SmallButton("关闭"))
        {
            showTree = false;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("类型标签切换");
        ImGui.Separator();

        DrawTreeHeader(tree);

        using var scroll = ImRaii.Child("##tree-scroll", new Vector2(0, 0), false);
        if (scroll.Success)
        {
            DrawNode(tree, true);
        }
    }

    private sealed class MaterialRow
    {
        public uint ItemId;
        public string Name = string.Empty;
        public MaterialKind Kind;
        public int Need;
        public int Missing;
        public int Have;
        public bool Manual;
        public int Output = 1;
        public ushort Icon;
        public string Source = string.Empty;

        /// <summary>实际缺口：至少是"需求 - 现有"，防止配方树里的旧库存快照把缺口算成 0。</summary>
        public int Gap => Manual ? Need : Math.Max(Missing, Math.Max(0, Need - Have));

        public int Crafts => Kind == MaterialKind.Craftable
            ? (int)Math.Ceiling(Math.Max(0, Gap) / (double)Math.Max(1, Output))
            : 0;
    }

    private void DrawTreeHeader(MaterialNode root)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.9f, 1.0f, 1.0f));
        ImGui.TextWrapped(root.Name);
        ImGui.PopStyleColor();

        ImGui.TextDisabled($"需要 ×{root.Amount}" + (root.CraftLevel > 0 ? $" · 等级 {root.CraftLevel}" : string.Empty));

        if (root.RecipeId == 0)
        {
            ImGui.TextDisabled("这个物品没有配方。");
            return;
        }

        ImGui.TextWrapped("所需全部数量：" + BuildTotalsLine(root));

        if (plugin.Configuration.UseArtisan)
        {
            if (ImGui.SmallButton($"Artisan 制作 ×{root.Amount}"))
            {
                Craft(root);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("检查"))
            {
                Plugin.ChatGui.Print(plugin.Artisan.IsCrafting()
                    ? "[SGT] Artisan 正在制作中。"
                    : "[SGT] Artisan 空闲 / 未安装。");
            }
        }
        else
        {
            ImGui.TextDisabled("Artisan 联动已关闭。");
        }
    }

    private void DrawNode(MaterialNode node, bool isRoot = false)
    {
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (node.Children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (isRoot)
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        var marker = node.Kind switch
        {
            MaterialKind.Craftable => "[制作]",
            MaterialKind.Gathered => "[采集]",
            MaterialKind.Vendor => "[购买]",
            MaterialKind.Currency => "[兑换]",
            _ => "[未知]",
        };

        var label = $"{marker} {node.Name}  ×{node.Amount}";
        if (nodeTotals.TryGetValue(node.ItemId, out var total) && total != node.Amount)
        {
            label += $"（总 {total}）";
        }

        bool open;
        using (ImRaii.PushColor(ImGuiCol.Text, ColorOf(node.Kind)))
        {
            open = ImGui.TreeNodeEx($"{label}##{node.ItemId}", flags);
        }

        if (node.RecipeId != 0)
        {
            ImGui.SameLine();
            ImGui.PushID((int)node.ItemId);
            if (ImGui.SmallButton("制作"))
            {
                Craft(node);
            }

            ImGui.PopID();
        }

        var recipes = plugin.CraftData.GetRecipes(node.ItemId);
        if (recipes.Count > 1)
        {
            ImGui.SameLine();
            ImGui.PushID((int)node.ItemId);
            if (ImGui.SmallButton("配方"))
            {
                ImGui.OpenPopup("##recipe-menu");
            }

            if (ImGui.BeginPopup("##recipe-menu"))
            {
                var current = node.RecipeId != 0
                    ? node.RecipeId
                    : plugin.Configuration.RecipeOverrides.GetValueOrDefault(node.ItemId, recipes[0].RowId);

                foreach (var recipe in recipes)
                {
                    var recipeLabel = $"Lv{CraftData.GetRecipeLevel(recipe)} · #{recipe.RowId}"
                                      + (recipe.IsSecondary ? "（次）" : string.Empty);
                    if (ImGui.MenuItem(recipeLabel, string.Empty, current == recipe.RowId))
                    {
                        plugin.Configuration.RecipeOverrides[node.ItemId] = recipe.RowId;
                        plugin.Configuration.Save();
                        tree = null;
                        treeCache.Clear();
                        treesKey = string.Empty;
                        rowsKey = string.Empty;
                    }
                }

                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        if (ImGui.IsItemHovered())
        {
            var tip = KindName(node.Kind);
            if (node.CraftLevel > 0)
            {
                tip += $" · 配方等级 {node.CraftLevel}";
            }

            if (node.RecipeId != 0)
            {
                tip += $" · 配方 #{node.RecipeId}";
            }

            if (!string.IsNullOrEmpty(node.Source))
            {
                tip += $"\n{node.Source}";
            }

            var allTotal = nodeTotals.GetValueOrDefault(node.ItemId, node.Amount);
            var have = plugin.Inventory.Count(node.ItemId);
            tip += $"\n需要 {allTotal} · 已有 {have} · 还需 {Math.Max(0, allTotal - have)}";

            ImGui.SetTooltip(tip);
        }

        if (!open)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            DrawNode(child);
        }

        ImGui.TreePop();
    }

    private void Craft(MaterialNode node)
    {
        if (node.RecipeId == 0)
        {
            return;
        }

        // 同一个材料可能在多个分支里出现，按"全清单还没准备的数量"交给 Artisan。
        var row = rows.FirstOrDefault(item => item.ItemId == node.ItemId);
        var amount = row is not null ? Math.Max(1, row.Gap) : node.Amount;

        if (plugin.Artisan.TryCraft(node.RecipeId, amount, out var error))
        {
            Plugin.ChatGui.Print($"[SGT] 已把 {node.Name} ×{amount} 交给 Artisan。");
        }
        else
        {
            Plugin.ChatGui.PrintError($"[SGT] {error}");
        }
    }

    private void AddEntry(uint itemId)
    {
        var list = CurrentList();
        var existing = list.Entries.FirstOrDefault(entry => entry.ItemId == itemId);

        if (existing is not null)
        {
            existing.Amount++;
        }
        else
        {
            list.Entries.Add(new CraftEntry { ItemId = itemId, Amount = 1 });
        }

        forceOpenActiveList = true;
        plugin.Configuration.Save();
    }

    private CraftList CurrentList()
    {
        var config = plugin.Configuration;
        if (config.CraftLists.Count == 0)
        {
            config.CraftLists.Add(new CraftList { Name = "默认清单" });
        }

        config.ActiveListIndex = Math.Clamp(config.ActiveListIndex, 0, config.CraftLists.Count - 1);
        return config.CraftLists[config.ActiveListIndex];
    }

    private void DrawItemIcon(ushort iconId, float size = 20f)
    {
        if (iconId == 0)
        {
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, new Vector2(size, size) * ImGuiHelpers.GlobalScale);
    }

    private static Vector4 ColorOf(MaterialKind kind) => kind switch
    {
        MaterialKind.Craftable => new Vector4(0.92f, 0.92f, 0.95f, 1.0f),
        MaterialKind.Gathered => new Vector4(0.55f, 0.90f, 0.60f, 1.0f),
        MaterialKind.Vendor => new Vector4(0.95f, 0.85f, 0.45f, 1.0f),
        MaterialKind.Currency => new Vector4(0.70f, 0.78f, 1.00f, 1.0f),
        _ => new Vector4(0.62f, 0.62f, 0.62f, 1.0f),
    };

    private static string KindName(MaterialKind kind) => kind switch
    {
        MaterialKind.Craftable => "制作",
        MaterialKind.Gathered => "采集",
        MaterialKind.Vendor => "购买",
        MaterialKind.Currency => "兑换",
        _ => "未知",
    };
}
