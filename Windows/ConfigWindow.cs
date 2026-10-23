using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SGTProductionBox.Windows;

/// <summary>设置窗口：只放联动和显示相关的开关。</summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("SGT Production Box · 设置###SGTProductionBoxConfig")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextDisabled("改动立即保存。");
        ImGui.Separator();

        DrawCheckbox("启用 Artisan 联动（制作按钮）", config.UseArtisan, value =>
        {
            config.UseArtisan = value;
            config.Save();
        });

        ImGui.Spacing();

        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat("主窗口不透明度", ref opacity, 0.35f, 1.0f, "%.2f"))
        {
            config.WindowOpacity = opacity;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("采集联动（GatherBuddyReborn）");

        if (!plugin.GatherBuddy.TryGetStatus(out var autoGather, out var statusText))
        {
            ImGui.TextDisabled("未检测到 GatherBuddyReborn（安装并启用后重载本插件即可识别）。");
        }
        else
        {
            ImGui.Text($"自动采集：{(autoGather ? "已开启" : "已关闭")}");
            ImGui.SameLine();
            if (ImGui.SmallButton(autoGather ? "关闭" : "开启"))
            {
                if (!plugin.GatherBuddy.TrySetEnabled(!autoGather, out var error))
                {
                    Plugin.ChatGui.PrintError("[SGT] " + error);
                }
            }

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                ImGui.TextDisabled(statusText);
            }

            ImGui.TextDisabled("GBR 没有“采集指定物品”的 IPC，按物品采集请用 /gather <物品名>。");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("必备插件");

        DrawPluginState("Artisan（制作）", plugin.Artisan.IsInstalled);
        DrawPluginState("Lifestream（回房屋）", plugin.Lifestream.IsInstalled);
        DrawPluginState("GatherBuddyReborn（采集）", plugin.GatherBuddy.TryGetVersion(out _));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("手工材料");

        ImGui.TextDisabled($"当前 {config.ManualMaterials.Count} 种材料忽略背包，需要全部重新准备。");
        if (config.ManualMaterials.Count > 0 && ImGui.Button("全部恢复使用背包"))
        {
            config.ManualMaterials.Clear();
            config.Save();
        }

        ImGui.TextDisabled("在材料卡片上点「背包 / 手工」标签可以逐个切换。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("采集 → 回房屋 → 制作");

        string[] targets = { "不返回（原地制作）", "个人房屋", "部队房屋" };
        var target = config.AutoReturnTarget;
        if (ImGui.Combo("返回地点", ref target, targets, targets.Length))
        {
            config.AutoReturnTarget = Math.Clamp(target, 0, 2);
            config.Save();
        }

        if (ImGui.Button(plugin.Automation.Running ? "停止自动流程" : "开始自动流程"))
        {
            if (plugin.Automation.Running)
            {
                plugin.Automation.Stop();
            }
            else
            {
                plugin.StartAutomation();
            }
        }

        ImGui.TextWrapped("状态：" + plugin.Automation.Status);
        ImGui.TextDisabled("采集完成 = 背包里的采集材料够数；回房屋用 Lifestream，制作用 Artisan。");

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("恢复默认设置"))
        {
            config.ResetSettings();
            config.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button("关闭"))
        {
            IsOpen = false;
        }
    }

    private static void DrawPluginState(string name, bool installed)
    {
        ImGui.Text(name);
        ImGui.SameLine();
        if (installed)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.90f, 0.60f, 1.0f), "● 已安装");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.50f, 1.0f), "● 未检测到");
        }
    }

    private static void DrawCheckbox(string label, bool currentValue, Action<bool> setter)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
        }
    }
}
