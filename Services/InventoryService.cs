using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;

namespace SGTProductionBox.Services;

/// <summary>
/// 统计玩家背包里每种物品的数量（按基础物品 ID，HQ 也算在一起）。
/// 背包内容会变，所以按秒刷新一次，并提供版本号给界面做缓存失效。
/// </summary>
public sealed class InventoryService
{
    private static readonly string[] ContainerNames =
    [
        "Inventory1", "Inventory2", "Inventory3", "Inventory4",
        "SaddleBag1", "SaddleBag2", "PremiumSaddleBag1", "PremiumSaddleBag2",
        "Crystals",
    ];

    private readonly IGameInventory inventory;
    private readonly Dictionary<uint, int> counts = new();

    private DateTime nextRefresh = DateTime.MinValue;

    public InventoryService(IGameInventory inventory)
    {
        this.inventory = inventory;
    }

    /// <summary>每次重新统计后自增，界面用它判断要不要重算。</summary>
    public int Version { get; private set; }

    public void RefreshIfDue()
    {
        if (DateTime.UtcNow < nextRefresh)
        {
            return;
        }

        Refresh();
    }

    public void Refresh()
    {
        nextRefresh = DateTime.UtcNow.AddSeconds(1);
        var fresh = new Dictionary<uint, int>();

        foreach (var name in ContainerNames)
        {
            if (!Enum.TryParse<GameInventoryType>(name, out var type))
            {
                continue;
            }

            try
            {
                foreach (var item in inventory.GetInventoryItems(type))
                {
                    if (item.IsEmpty || item.BaseItemId == 0)
                    {
                        continue;
                    }

                    var itemId = item.BaseItemId;
                    fresh[itemId] = fresh.GetValueOrDefault(itemId) + (int)item.Quantity;
                }
            }
            catch
            {
                // 某些容器在未登录/未加载时不可读，忽略即可。
            }
        }

        // 数量没有变化就不增加版本号，避免界面每秒重建配方树。
        if (fresh.Count == counts.Count
            && fresh.All(pair => counts.TryGetValue(pair.Key, out var old) && old == pair.Value))
        {
            return;
        }

        counts.Clear();
        foreach (var pair in fresh)
        {
            counts[pair.Key] = pair.Value;
        }

        Version++;
    }

    public int Count(uint itemId) => counts.GetValueOrDefault(itemId);
}
