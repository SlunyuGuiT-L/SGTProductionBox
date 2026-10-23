using System.Collections.Generic;

namespace SGTProductionBox.Crafting;

/// <summary>材料来源分类。</summary>
public enum MaterialKind
{
    /// <summary>有配方，需要制作（半成品或成品）。</summary>
    Craftable,

    /// <summary>可以采集（采矿/园艺/捕鱼）。</summary>
    Gathered,

    /// <summary>NPC 金币购买。</summary>
    Vendor,

    /// <summary>用货币/票据兑换。</summary>
    Currency,

    /// <summary>没有识别出来源。</summary>
    Unknown,
}

/// <summary>一个生产清单。</summary>
public sealed class CraftList
{
    public string Name { get; set; } = "新清单";
    public List<CraftEntry> Entries { get; set; } = new();
}

/// <summary>清单里的一项：要做什么、做多少个。</summary>
public sealed class CraftEntry
{
    public uint ItemId { get; set; }
    public int Amount { get; set; } = 1;
}

/// <summary>配方树上的一个节点。</summary>
public sealed class MaterialNode
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ushort Icon { get; init; }
    public int Amount { get; set; }

    /// <summary>扣除背包后的实际缺口（没有 stock 函数时等于 Amount）。</summary>
    public int Missing { get; set; }

    public MaterialKind Kind { get; set; }
    public uint RecipeId { get; set; }
    public byte CraftLevel { get; set; }

    /// <summary>这张配方一次制作产出几个（用于算制作次数）。</summary>
    public int OutputPerCraft { get; set; } = 1;

    public string Source { get; set; } = string.Empty;
    public List<MaterialNode> Children { get; } = new();
}

/// <summary>物品搜索结果。</summary>
public readonly record struct ItemSearchResult(uint ItemId, string Name, ushort Icon);
