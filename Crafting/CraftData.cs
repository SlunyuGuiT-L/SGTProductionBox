using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace SGTProductionBox.Crafting;

/// <summary>
/// 配方与材料来源查询。所有表都是懒加载 + 缓存，第一次查询时建索引。
/// 只依赖 Lumina 的 ExcelModule，不碰游戏内存，方便离线验证。
/// </summary>
public sealed class CraftData
{
    private readonly ExcelModule module;

    private List<ItemSearchResult>? allItems;
    private Dictionary<uint, (string Name, ushort Icon)>? itemInfo;
    private Dictionary<uint, List<Recipe>>? recipesByResult;
    private HashSet<uint>? gatherableItems;
    private Dictionary<uint, Offer>? offers;

    public CraftData(ExcelModule module)
    {
        this.module = module;
    }

    private readonly record struct Offer(MaterialKind Kind, string Text, long Cost);

    /// <summary>按名字（或物品 ID）搜索物品，最多返回 <paramref name="maxResults"/> 条。</summary>
    public IReadOnlyList<ItemSearchResult> SearchItems(string query, int maxResults = 20)
    {
        EnsureItems();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ItemSearchResult>();
        }

        query = query.Trim();
        var isId = uint.TryParse(query, out var id);

        return allItems!
            .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (isId && item.ItemId == id))
            .Take(maxResults)
            .ToList();
    }

    public (string Name, ushort Icon) GetItemInfo(uint itemId)
    {
        EnsureItems();
        return itemInfo!.TryGetValue(itemId, out var info) ? info : ($"物品 #{itemId}", (ushort)0);
    }

    /// <summary>取某个物品的配方（多个配方时按配方等级从低到高排）。</summary>
    public IReadOnlyList<Recipe> GetRecipes(uint itemId)
    {
        EnsureRecipes();
        return recipesByResult!.TryGetValue(itemId, out var list) ? list : Array.Empty<Recipe>();
    }

    /// <summary>
    /// 构建配方树。<paramref name="stock"/> 返回某个物品可用的背包数量（返回 0 表示不抵扣），
    /// 用来计算"实际还需要做/采多少"。
    /// </summary>
    public MaterialNode BuildTree(uint itemId, int amount, int maxDepth = 12, Func<uint, int>? stock = null)
    {
        EnsureAll();
        return BuildNode(itemId, Math.Max(1, amount), 0, maxDepth, new HashSet<uint>(), stock);
    }

    /// <summary>把树的叶子按物品合并，得到最终要准备的清单。</summary>
    public IReadOnlyList<(MaterialNode Node, int Total)> Summarize(MaterialNode root)
    {
        var totals = new Dictionary<uint, (MaterialNode Node, int Total)>();

        void Walk(MaterialNode node)
        {
            if (node.Children.Count == 0)
            {
                if (totals.TryGetValue(node.ItemId, out var existing))
                {
                    totals[node.ItemId] = (existing.Node, existing.Total + node.Amount);
                }
                else
                {
                    totals[node.ItemId] = (node, node.Amount);
                }

                return;
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        Walk(root);

        return totals.Values
            .OrderBy(pair => pair.Node.Kind)
            .ThenBy(pair => pair.Node.Name, StringComparer.Ordinal)
            .ToList();
    }

    private MaterialNode BuildNode(uint itemId, int amount, int depth, int maxDepth, HashSet<uint> visited,
        Func<uint, int>? stock)
    {
        var (name, icon) = GetItemInfo(itemId);
        var node = new MaterialNode
        {
            ItemId = itemId,
            Name = name,
            Icon = icon,
            Amount = amount,
        };

        var available = stock is null ? 0 : Math.Max(0, stock(itemId));
        var missing = Math.Max(0, amount - Math.Min(amount, available));
        node.Missing = missing;

        var recipes = GetRecipes(itemId);

        if (recipes.Count > 0 && depth < maxDepth && visited.Add(itemId))
        {
            var recipe = recipes[0];
            node.Kind = MaterialKind.Craftable;
            node.CraftLevel = GetRecipeLevel(recipe);

            var craftsPerItem = Math.Max(1, (int)recipe.AmountResult);
            node.OutputPerCraft = craftsPerItem;

            // 背包里已经有的部分不用再做；只有确实要做的数量才展开材料。
            node.RecipeId = missing > 0 ? recipe.RowId : 0;

            if (missing > 0)
            {
                var craftCount = (int)Math.Ceiling(missing / (double)craftsPerItem);
                var ingredients = recipe.Ingredient;
                var ingredientAmounts = recipe.AmountIngredient;

                for (var i = 0; i < ingredients.Count; i++)
                {
                    var ingredientId = ingredients[i].RowId;
                    if (ingredientId == 0)
                    {
                        continue;
                    }

                    var perCraft = i < ingredientAmounts.Count ? ingredientAmounts[i] : (byte)0;
                    if (perCraft == 0)
                    {
                        continue;
                    }

                    node.Children.Add(BuildNode(
                        ingredientId,
                        perCraft * craftCount,
                        depth + 1,
                        maxDepth,
                        visited,
                        stock));
                }
            }

            visited.Remove(itemId);
            return node;
        }

        node.Kind = ClassifyLeaf(itemId, out var source);
        node.Source = source;
        return node;
    }

    private MaterialKind ClassifyLeaf(uint itemId, out string source)
    {
        if (gatherableItems!.Contains(itemId))
        {
            source = "采集";
            return MaterialKind.Gathered;
        }

        if (offers!.TryGetValue(itemId, out var offer))
        {
            source = offer.Text;
            return offer.Kind;
        }

        source = string.Empty;
        return MaterialKind.Unknown;
    }

    private static byte GetRecipeLevel(Recipe recipe)
        => recipe.RecipeLevelTable.IsValid ? recipe.RecipeLevelTable.Value.ClassJobLevel : (byte)0;

    private void EnsureAll()
    {
        EnsureItems();
        EnsureRecipes();
        EnsureGatherables();
        EnsureOffers();
    }

    private void EnsureItems()
    {
        if (allItems is not null)
        {
            return;
        }

        var list = new List<ItemSearchResult>(40000);
        var map = new Dictionary<uint, (string, ushort)>(40000);

        foreach (var item in module.GetSheet<Item>())
        {
            if (item.RowId == 0)
            {
                continue;
            }

            var name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            list.Add(new ItemSearchResult(item.RowId, name, item.Icon));
            map[item.RowId] = (name, item.Icon);
        }

        allItems = list;
        itemInfo = map;
    }

    private void EnsureRecipes()
    {
        if (recipesByResult is not null)
        {
            return;
        }

        var map = new Dictionary<uint, List<Recipe>>();
        foreach (var recipe in module.GetSheet<Recipe>())
        {
            var resultId = recipe.ItemResult.RowId;
            if (resultId == 0)
            {
                continue;
            }

            if (!map.TryGetValue(resultId, out var list))
            {
                map[resultId] = list = new List<Recipe>(1);
            }

            list.Add(recipe);
        }

        foreach (var list in map.Values)
        {
            // 同一个物品可能有多张配方，优先用等级最低（最容易做）的那张。
            list.Sort((a, b) => GetRecipeLevel(a).CompareTo(GetRecipeLevel(b)));
        }

        recipesByResult = map;
    }

    private void EnsureGatherables()
    {
        if (gatherableItems is not null)
        {
            return;
        }

        var set = new HashSet<uint>();
        foreach (var item in module.GetSheet<GatheringItem>())
        {
            var itemId = item.Item.RowId;
            if (itemId != 0)
            {
                set.Add(itemId);
            }
        }

        gatherableItems = set;
    }

    private void EnsureOffers()
    {
        if (offers is not null)
        {
            return;
        }

        var map = new Dictionary<uint, Offer>();

        // 金币商店（子行表，每个子行是一个售卖位）
        foreach (var row in module.GetSubrowSheet<GilShopItem>().Flatten())
        {
            var itemId = row.Item.RowId;
            if (itemId == 0)
            {
                continue;
            }

            if (!map.ContainsKey(itemId))
            {
                map[itemId] = new Offer(MaterialKind.Vendor, "NPC 金币购买", 0);
            }
        }

        // 特殊商店（诗学、因果、票据等兑换）
        foreach (var shop in module.GetSheet<SpecialShop>())
        {
            foreach (var entry in shop.Item)
            {
                if (entry.ReceiveItems.Count == 0 || entry.ItemCosts.Count == 0)
                {
                    continue;
                }

                var cost = entry.ItemCosts[0];
                var costItemId = cost.ItemCost.RowId;
                if (costItemId == 0 || cost.CurrencyCost == 0)
                {
                    continue;
                }

                foreach (var receive in entry.ReceiveItems)
                {
                    var itemId = receive.Item.RowId;
                    if (itemId == 0 || receive.ReceiveCount == 0)
                    {
                        continue;
                    }

                    if (map.TryGetValue(itemId, out var existing)
                        && existing.Kind == MaterialKind.Currency
                        && existing.Cost <= cost.CurrencyCost)
                    {
                        continue;
                    }

                    map[itemId] = new Offer(
                        MaterialKind.Currency,
                        $"兑换：{GetItemName(costItemId)} ×{cost.CurrencyCost}",
                        cost.CurrencyCost);
                }
            }
        }

        offers = map;
    }

    private string GetItemName(uint itemId)
        => itemInfo!.TryGetValue(itemId, out var info) ? info.Name : $"物品 #{itemId}";
}
