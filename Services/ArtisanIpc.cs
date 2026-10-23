using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace SGTProductionBox.Services;

/// <summary>
/// Artisan 的 IPC 封装：Artisan.CraftItem(recipeId, amount)。
/// 没装 Artisan 时调用会抛 IpcError，这里统一转成可读的提示。
/// </summary>
public sealed class ArtisanIpc : IDisposable
{
    private readonly ICallGateSubscriber<ushort, int, object> craftItem;
    private readonly ICallGateSubscriber<bool> enduranceStatus;

    public ArtisanIpc(IDalamudPluginInterface pluginInterface)
    {
        craftItem = pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        enduranceStatus = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
    }

    /// <summary>Artisan 是否已安装并加载（通过 ping 它的 IPC 判断）。</summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                enduranceStatus.InvokeFunc();
                return true;
            }
            catch (IpcError)
            {
                return false;
            }
        }
    }

    /// <summary>Artisan 是否正在制作（未安装时返回 false）。</summary>
    public bool IsCrafting()
    {
        try
        {
            return enduranceStatus.InvokeFunc();
        }
        catch (IpcError)
        {
            return false;
        }
    }

    /// <summary>让 Artisan 按配方制作指定数量。</summary>
    public bool TryCraft(uint recipeId, int amount, out string error)
    {
        try
        {
            craftItem.InvokeAction((ushort)recipeId, amount);
            error = string.Empty;
            return true;
        }
        catch (IpcError e)
        {
            error = "Artisan 未安装或接口不可用（需要安装 Artisan）。" + e.Message;
            return false;
        }
    }

    public void Dispose()
    {
    }
}
