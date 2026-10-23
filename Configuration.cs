using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using SGTProductionBox.Crafting;

namespace SGTProductionBox;

/// <summary>
/// 插件配置：生产清单都保存在这里，跟着 Dalamud 的插件配置一起持久化。
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>所有生产清单。</summary>
    public List<CraftList> CraftLists { get; set; } = new();

    /// <summary>当前选中的清单下标。</summary>
    public int ActiveListIndex { get; set; }

    /// <summary>是否显示 Artisan 制作按钮。</summary>
    public bool UseArtisan { get; set; } = true;

    /// <summary>主窗口背景不透明度。</summary>
    public float WindowOpacity { get; set; } = 1.0f;

    /// <summary>自动流程：采集完成后回哪里。0 = 不返回，1 = 个人房屋，2 = 部队房屋。</summary>
    public int AutoReturnTarget { get; set; } = 1;

    /// <summary>不使用背包现有数量、需要全部重新准备的材料 ID（"手工"模式）。</summary>
    public List<uint> ManualMaterials { get; set; } = new();

    /// <summary>手机推送渠道（PushProvider）。</summary>
    public int PushProvider { get; set; }

    /// <summary>推送 Token / Webhook 地址。</summary>
    public string PushToken { get; set; } = string.Empty;

    /// <summary>自动流程失败时推送到手机。</summary>
    public bool PushOnError { get; set; } = true;

    /// <summary>自动流程完成时推送到手机。</summary>
    public bool PushOnDone { get; set; } = true;

    public bool UsesInventory(uint itemId) => !ManualMaterials.Contains(itemId);

    /// <summary>切换某个材料是否使用背包数量。</summary>
    public void ToggleManual(uint itemId)
    {
        if (!ManualMaterials.Remove(itemId))
        {
            ManualMaterials.Add(itemId);
        }

        Save();
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>只重置显示/联动设置，不动用户的清单。</summary>
    public void ResetSettings()
    {
        UseArtisan = true;
        WindowOpacity = 1.0f;
    }
}
