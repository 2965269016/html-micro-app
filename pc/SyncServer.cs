using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MiniAppBox;

/// <summary>
/// 设备同步服务端（协议第 9 节）：双栈监听 IPv6 ::（DualMode 兼收 IPv4），
/// 端点：GET /sync/info、GET /sync/app?id=、POST /sync/app?id=（保 id 安装）。
/// </summary>
public sealed class SyncServer : IDisposable
{
    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("MINIAPPBOX_SYNC_PORT"), out int p) && p > 0 ? p : 47777;

    private TcpListener? _listener;
    private volatile bool _running;

    /// <summary>给同步窗口展示的动态信息。</summary>
    public event Action<string>? Activity;
    /// <summary>收到推送、应用列表变化时通知 UI 刷新。</summary>
    public event Action? AppsChanged;

    private readonly AppSettings _settings;

    public SyncServer(AppSettings settings) => _settings = settings;

    public bool Running => _running;

    public void Start()
    {
        if (_running) return;
        var listener = new TcpListener(IPAddress.IPv6Any, Port);
        listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false); // 双栈
        listener.Start(8);
        _listener = listener;
        _running = true;
        var t = new Thread(AcceptLoop) { IsBackground = true, Name = "sync-accept" };
        t.Start();
        Activity?.Invoke($"同步服务已启动（端口 {Port}，IPv4/IPv6 双栈）");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _listener?.Stop(); } catch { /* 已关闭 */ }
        _listener = null;
        Activity?.Invoke("同步服务已停止");
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch { break; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private void Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 10_000;
                client.SendTimeout = 60_000;
                using var stream = client.GetStream();
                var req = ReadRequest(stream);
                if (req == null) { WriteJson(stream, 400, FailJson("bad_request")); return; }

                (string path, var query) = SplitPath(req.Value.Path);

                if (req.Value.Method == "GET" && path == "/sync/info")
                {
                    WriteJson(stream, 200, BuildInfoJson());
                }
                else if (req.Value.Method == "GET" && path == "/sync/app")
                {
                    string? id = query("id");
                    var app = id == null ? null : AppRepo.LoadMeta(id);
                    if (app == null) { WriteJson(stream, 404, FailJson("not_found")); return; }
                    byte[] zip = ExportToBytes(id!);
                    WriteBytes(stream, 200, "application/zip", zip);
                    Raise($"已向对方发送「{app.Name}」");
                }
                else if (req.Value.Method == "POST" && path == "/sync/app")
                {
                    string? id = query("id");
                    if (id == null || req.Value.Body == null || req.Value.Body.Length == 0
                        || req.Value.Body.Length > 512L * 1024 * 1024)
                    { WriteJson(stream, 400, FailJson("bad_request")); return; }

                    string tmp = Path.Combine(Path.GetTempPath(), "miniappbox_recv_" + Guid.NewGuid().ToString("N") + ".zip");
                    File.WriteAllBytes(tmp, req.Value.Body);
                    try
                    {
                        var app = AppRepo.InstallSynced(tmp, id);
                        WriteJson(stream, 200, "{\"ok\":true}");
                        Raise($"已从对方接收「{app.Name}」");
                        AppsChanged?.Invoke();
                    }
                    catch (Exception e)
                    {
                        WriteJson(stream, 400, FailJson(e.Message));
                    }
                    finally { try { File.Delete(tmp); } catch { } }
                }
                else
                {
                    WriteJson(stream, 404, FailJson("not_found"));
                }
            }
        }
        catch { /* 连接中断，忽略 */ }
    }

    // ------------------------------------------------------------ 请求解析（最小 HTTP 子集）

    private struct Req { public string Method, Path; public byte[]? Body; }

    private static Req? ReadRequest(NetworkStream stream)
    {
        var header = new StringBuilder();
        var buf = new byte[1];
        while (header.Length < 32 * 1024)
        {
            int n = stream.Read(buf, 0, 1);
            if (n <= 0) return null;
            header.Append((char)buf[0]);
            if (header.Length >= 4 && header[^1] == '\n' && header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
        string[] lines = header.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length == 0) return null;
        string[] parts = lines[0].Split(' ');
        if (parts.Length < 2) return null;

        int contentLength = 0;
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(colon + 1)..].Trim(), out contentLength);
        }
        var req = new Req { Method = parts[0].ToUpperInvariant(), Path = parts[1] };

        if (contentLength > 0)
        {
            if (contentLength > 512L * 1024 * 1024) return null;
            var body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = stream.Read(body, read, contentLength - read);
                if (n <= 0) return null;
                read += n;
            }
            req.Body = body;
        }
        return req;
    }

    private static (string, Func<string, string?>) SplitPath(string raw)
    {
        int q = raw.IndexOf('?');
        if (q < 0) return (raw, _ => null);
        string path = raw[..q];
        var map = new Dictionary<string, string>();
        foreach (string pair in raw[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string k = Uri.UnescapeDataString(pair[..eq]);
            string v = Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", " "));
            map[k] = v;
        }
        return (path, k => map.GetValueOrDefault(k));
    }

    // ------------------------------------------------------------ 响应与数据

    private string BuildInfoJson()
    {
        var apps = new JsonArray();
        foreach (var app in AppRepo.List())
        {
            apps.Add(new JsonObject
            {
                ["id"] = app.Id,
                ["name"] = app.Name,
                ["installedAt"] = app.InstalledAt,
                ["size"] = AppRepo.UsageBytes(app.Id),
                ["storeHash"] = AppRepo.StoreHash(app.Id)
            });
        }
        return new JsonObject
        {
            ["proto"] = 1,
            ["device"] = _settings.EffectiveDeviceName,
            ["apps"] = apps
        }.ToJsonString(JsonOpts.Compact);
    }

    private static byte[] ExportToBytes(string id)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "miniappbox_export_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            AppRepo.Export(id, tmp);
            return File.ReadAllBytes(tmp);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static string FailJson(string msg) =>
        new JsonObject { ["ok"] = false, ["error"] = msg }.ToJsonString(JsonOpts.Compact);

    private static void WriteJson(NetworkStream s, int code, string json) =>
        WriteBytes(s, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

    private static void WriteBytes(NetworkStream s, int code, string contentType, byte[] data)
    {
        string reason = code switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", _ => "Error" };
        string head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        s.Write(headBytes, 0, headBytes.Length);
        s.Write(data, 0, data.Length);
        s.Flush();
    }

    private void Raise(string msg)
    {
        Activity?.Invoke(msg);
        Console.WriteLine("[sync] " + msg);
    }

    public void Dispose() => Stop();
}
