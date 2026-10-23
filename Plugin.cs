using System;
using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SGTProductionBox.Crafting;
using SGTProductionBox.Services;
using SGTProductionBox.Windows;

namespace SGTProductionBox;

/// <summary>插件入口。</summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/sgt";
    private const string CommandAlias = "/sgtbox";

    public Configuration Configuration { get; init; }
    public CraftData CraftData { get; init; }
    public ArtisanIpc Artisan { get; init; }
    public InventoryService Inventory { get; init; }
    public GatherBuddyIpc GatherBuddy { get; init; }
    public LifestreamIpc Lifestream { get; init; }
    public ProductionAutomation Automation { get; init; }

    public readonly WindowSystem WindowSystem = new("SGTProductionBox");

    private MainWindow MainWindow { get; init; }
    private ConfigWindow ConfigWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (Configuration.CraftLists.Count == 0)
        {
            Configuration.CraftLists.Add(new CraftList { Name = "默认清单" });
            Configuration.ActiveListIndex = 0;
            Configuration.Save();
        }

        CraftData = new CraftData(DataManager.Excel);
        Artisan = new ArtisanIpc(PluginInterface);
        Inventory = new InventoryService(GameInventory);
        GatherBuddy = new GatherBuddyIpc(PluginInterface);
        Lifestream = new LifestreamIpc(PluginInterface);
        Automation = new ProductionAutomation(CraftData, Inventory, GatherBuddy, Lifestream, Artisan, ChatGui);
        Framework.Update += OnFrameworkUpdate;

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开/关闭 SGT Production Box。参数：config 打开设置，add <物品名> 直接加入当前清单。",
        });

        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "同 /sgt。",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"SGT Production Box 已加载：{PluginInterface.Manifest.Name} {PluginInterface.Manifest.AssemblyVersion}");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        ConfigWindow.Dispose();
        Artisan.Dispose();
        GatherBuddy.Dispose();
        Lifestream.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        Automation.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
    }

    private void OnFrameworkUpdate(IFramework framework) => Automation.Tick();

    /// <summary>从设置窗口启动自动流程：当前清单 → 采集等待 → 回房屋 → Artisan 制作。</summary>
    public void StartAutomation()
    {
        if (!Artisan.IsInstalled)
        {
            ChatGui.PrintError("[SGT] 未检测到 Artisan，无法自动制作。");
            return;
        }

        if (Configuration.AutoReturnTarget != 0 && !Lifestream.IsInstalled)
        {
            ChatGui.PrintError("[SGT] 未检测到 Lifestream，无法自动回房屋。请选择「不返回」或安装 Lifestream。");
            return;
        }

        Automation.Start(
            MainWindow.ActiveCraftList(),
            Configuration.AutoReturnTarget,
            new HashSet<uint>(Configuration.ManualMaterials));
    }

    private void OnCommand(string command, string args)
    {
        var text = args.Trim();

        if (text.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
        {
            var query = text[4..].Trim();
            if (MainWindow.AddItemByName(query))
            {
                ChatGui.Print($"[SGT] 已把「{query}」加入清单。");
            }
            else
            {
                ChatGui.PrintError($"[SGT] 没找到「{query}」，试试更完整的物品名。");
            }

            return;
        }

        switch (text.ToLowerInvariant())
        {
            case "config":
            case "settings":
            case "设置":
                ConfigWindow.Toggle();
                break;

            default:
                MainWindow.Toggle();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    public void ToggleMainUi() => MainWindow.Toggle();
}
