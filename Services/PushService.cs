using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SGTProductionBox.Services;

/// <summary>推送渠道。</summary>
public enum PushProvider
{
    Off = 0,
    ServerChan = 1,
    PushPlus = 2,
    Bark = 3,
    WeCom = 4,
    Custom = 5,
}

/// <summary>
/// 把报错/流程结果推送到手机（Server酱、PushPlus、Bark、企业微信机器人、自定义 Webhook）。
/// 发送是异步的，不会卡游戏线程。
/// </summary>
public sealed class PushService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly IPluginLog log;

    public PushService(IPluginLog log)
    {
        this.log = log;
    }

    public void Notify(PushProvider provider, string token, string title, string content)
    {
        if (provider == PushProvider.Off || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var key = token.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await SendAsync(provider, key, title, content).ConfigureAwait(false);
                log.Information($"[推送] 已发送：{title}");
            }
            catch (Exception e)
            {
                log.Warning($"[推送] 发送失败：{e.Message}");
            }
        });
    }

    private static async Task SendAsync(PushProvider provider, string token, string title, string content)
    {
        switch (provider)
        {
            case PushProvider.ServerChan:
                using (var response = await Http.PostAsync(
                           $"https://sctapi.ftqq.com/{token}.send",
                           new FormUrlEncodedContent(new Dictionary<string, string>
                           {
                               ["title"] = title,
                               ["desp"] = content,
                           })).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }

                break;

            case PushProvider.PushPlus:
                using (var response = await Http.PostAsync(
                           "https://www.pushplus.plus/send",
                           Json(new { token, title, content, template = "txt" })).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }

                break;

            case PushProvider.Bark:
                var barkUrl = $"https://api.day.app/{token}/{Uri.EscapeDataString(title)}/{Uri.EscapeDataString(content)}";
                using (var response = await Http.GetAsync(barkUrl).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }

                break;

            case PushProvider.WeCom:
                using (var response = await Http.PostAsync(
                           token,
                           Json(new { msgtype = "text", text = new { content = title + "\n" + content } })).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }

                break;

            case PushProvider.Custom:
                using (var response = await Http.PostAsync(token, Json(new { title, content })).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }

                break;
        }
    }

    private static StringContent Json(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public void Dispose()
    {
    }
}
