using System.Net.Http;
using System.Text.Json;

namespace MiniAppBox;

/// <summary>设备同步客户端（协议第 9 节）：连接对端、取清单、拉取/推送应用包。</summary>
public static class SyncClient
{
    public sealed class RemoteApp
    {
        public string Id = "", Name = "";
        public long InstalledAt, Size;
        public string StoreHash = "";
    }

    public sealed class PeerInfo
    {
        public string Device = "";
        public List<RemoteApp> Apps = new();
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>IPv6 地址自动加 []。</summary>
    private static string BaseUrl(string host, int port) =>
        $"http://{(host.Contains(':') ? "[" + host + "]" : host)}:{port}";

    /// <summary>取对方应用清单；失败抛异常。</summary>
    public static PeerInfo GetInfo(string host, int port)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        string json = Http.GetStringAsync($"{BaseUrl(host, port)}/sync/info", cts.Token).GetAwaiter().GetResult();
        var root = JsonDocument.Parse(json).RootElement;
        var info = new PeerInfo { Device = root.TryGetProperty("device", out var d) ? d.GetString() ?? "?" : "?" };
        if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in apps.EnumerateArray())
            {
                info.Apps.Add(new RemoteApp
                {
                    Id = a.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    InstalledAt = a.TryGetProperty("installedAt", out var t) && t.TryGetInt64(out var l) ? l : 0,
                    Size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0,
                    StoreHash = a.TryGetProperty("storeHash", out var h) ? h.GetString() ?? "" : ""
                });
            }
        }
        return info;
    }

    /// <summary>从对方拉取应用包。</summary>
    public static byte[] GetApp(string host, int port, string id)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        byte[] data = Http.GetByteArrayAsync($"{BaseUrl(host, port)}/sync/app?id={Uri.EscapeDataString(id)}", cts.Token)
            .GetAwaiter().GetResult();
        if (data.Length == 0) throw new Exception("对方返回空数据");
        return data;
    }

    /// <summary>把应用包推送给对方（对方保 id 安装）。</summary>
    public static void PushApp(string host, int port, string id, byte[] zip)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        using var resp = Http.PostAsync($"{BaseUrl(host, port)}/sync/app?id={Uri.EscapeDataString(id)}", content, cts.Token)
            .GetAwaiter().GetResult();
        string body = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
        {
            string err = body;
            try { err = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() ?? body; } catch { }
            throw new Exception($"对方拒绝：{err}");
        }
    }
}
