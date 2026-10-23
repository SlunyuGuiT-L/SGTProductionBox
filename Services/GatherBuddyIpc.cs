using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace SGTProductionBox.Services;

/// <summary>
/// GatherBuddyReborn 的 IPC 封装。
/// 它只公开了"开关自动采集 / 查状态 / 识别物品名"，没有"采集指定物品"的接口；
/// 按物品采集只能用它的聊天命令 /gather &lt;物品名&gt;。
/// </summary>
public sealed class GatherBuddyIpc : IDisposable
{
    private const string Prefix = "GatherBuddyReborn.";

    private readonly ICallGateSubscriber<int> version;
    private readonly ICallGateSubscriber<bool> isAutoGatherEnabled;
    private readonly ICallGateSubscriber<string> autoGatherStatusText;
    private readonly ICallGateSubscriber<bool, object> setAutoGatherEnabled;

    public GatherBuddyIpc(IDalamudPluginInterface pluginInterface)
    {
        version = pluginInterface.GetIpcSubscriber<int>(Prefix + "Version");
        isAutoGatherEnabled = pluginInterface.GetIpcSubscriber<bool>(Prefix + "IsAutoGatherEnabled");
        autoGatherStatusText = pluginInterface.GetIpcSubscriber<string>(Prefix + "GetAutoGatherStatusText");
        setAutoGatherEnabled = pluginInterface.GetIpcSubscriber<bool, object>(Prefix + "SetAutoGatherEnabled");
    }

    /// <summary>GBR 已安装并加载时返回 true。</summary>
    public bool TryGetVersion(out int value)
    {
        try
        {
            value = version.InvokeFunc();
            return true;
        }
        catch (IpcError)
        {
            value = 0;
            return false;
        }
    }

    public bool TryGetStatus(out bool enabled, out string status)
    {
        try
        {
            enabled = isAutoGatherEnabled.InvokeFunc();
            status = autoGatherStatusText.InvokeFunc();
            return true;
        }
        catch (IpcError)
        {
            enabled = false;
            status = string.Empty;
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string error)
    {
        try
        {
            setAutoGatherEnabled.InvokeAction(enabled);
            error = string.Empty;
            return true;
        }
        catch (IpcError e)
        {
            error = "GatherBuddyReborn 未安装或接口不可用。" + e.Message;
            return false;
        }
    }

    public void Dispose()
    {
    }
}
