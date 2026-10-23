using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace SGTProductionBox.Services;

/// <summary>
/// Lifestream 的 IPC 封装：回个人房屋 / 部队房屋、查询是否在忙。
/// 走的是 Lifestream.TeleportToHome / TeleportToFC / IsBusy。
/// </summary>
public sealed class LifestreamIpc : IDisposable
{
    private readonly ICallGateSubscriber<bool> teleportToHome;
    private readonly ICallGateSubscriber<bool> teleportToFc;
    private readonly ICallGateSubscriber<bool> isBusy;

    public LifestreamIpc(IDalamudPluginInterface pluginInterface)
    {
        teleportToHome = pluginInterface.GetIpcSubscriber<bool>("Lifestream.TeleportToHome");
        teleportToFc = pluginInterface.GetIpcSubscriber<bool>("Lifestream.TeleportToFC");
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
    }

    /// <summary>Lifestream 是否已安装并加载。</summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                isBusy.InvokeFunc();
                return true;
            }
            catch (IpcError)
            {
                return false;
            }
        }
    }

    /// <summary>让 Lifestream 返回个人房屋或部队房屋。</summary>
    public bool TryTeleport(bool toFc, out string error)
    {
        try
        {
            var ok = toFc ? teleportToFc.InvokeFunc() : teleportToHome.InvokeFunc();
            if (!ok)
            {
                error = toFc ? "Lifestream 拒绝传送（可能没有部队房屋或当前在忙）。" : "Lifestream 拒绝传送（可能没有个人房屋或当前在忙）。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (IpcError e)
        {
            error = "Lifestream 未安装或接口不可用。" + e.Message;
            return false;
        }
    }

    public bool TryIsBusy(out bool busy)
    {
        try
        {
            busy = isBusy.InvokeFunc();
            return true;
        }
        catch (IpcError)
        {
            busy = false;
            return false;
        }
    }

    public void Dispose()
    {
    }
}
