using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MiniAppBox;

/// <summary>
/// 局域网设备发现（协议 9.1）：UDP 47778，IPv4 广播 255.255.255.255 + IPv6 多播 ff02::1（尽力而为）。
/// 收到他人探测时原样回发自己的探测包（单播），双方互进发现列表；用随机实例 id 过滤回声。
/// </summary>
public sealed class Discovery : IDisposable
{
    public const int UdpPort = 47778;

    public sealed class Peer
    {
        public string Name = "", Host = "";
        public int TcpPort;
        public DateTime Seen;
        public string Display => $"{Name}　[{Host}]:{TcpPort}";
        public string Key => Host + ":" + TcpPort;
    }

    public event Action<Peer>? Found;

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly Func<string> _deviceName;
    private readonly Func<int> _tcpPort;
    private UdpClient? _v4, _v6;
    private volatile bool _running;
    private System.Threading.Timer? _probeTimer;

    public Discovery(Func<string> deviceName, Func<int> tcpPort)
    {
        _deviceName = deviceName;
        _tcpPort = tcpPort;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        try
        {
            _v4 = new UdpClient();
            _v4.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _v4.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));
            _v4.EnableBroadcast = true;
            var t4 = new Thread(() => ReceiveLoop(_v4, AddressFamily.InterNetwork)) { IsBackground = true, Name = "disc-v4" };
            t4.Start();
        }
        catch { _v4 = null; }

        try
        {
            _v6 = new UdpClient(AddressFamily.InterNetworkV6);
            _v6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _v6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, UdpPort));
            // 加入所有接口的 ff02::1 链路本地多播组（尽力而为）
            byte[] group = IPAddress.Parse("ff02::1").GetAddressBytes();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = nic.GetIPProperties();
                if (ipProps.GetIPv6Properties() == null) continue;
                try
                {
                    _v6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership,
                        new MulticastOption(IPAddress.Parse("ff02::1"), ipProps.GetIPv6Properties()!.Index));
                }
                catch { /* 个别接口不支持，忽略 */ }
            }
            var t6 = new Thread(() => ReceiveLoop(_v6, AddressFamily.InterNetworkV6)) { IsBackground = true, Name = "disc-v6" };
            t6.Start();
        }
        catch { _v6 = null; }

        _probeTimer = new System.Threading.Timer(_ => SendProbe(null, null), null, 200, 3000);
    }

    /// <summary>立即发送一轮探测（dest 为空则广播/多播）。</summary>
    public void SendProbe(IPAddress? dest, AddressFamily? family)
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            new Dictionary<string, object>
            {
                ["proto"] = 1, ["id"] = _instanceId,
                ["device"] = _deviceName(), ["tcp"] = _tcpPort()
            }.ToJson());
        try { _v4?.Send(payload, payload.Length, new IPEndPoint(dest ?? IPAddress.Broadcast, UdpPort)); }
        catch { /* 无网络接口时忽略 */ }

        if (_v6 != null)
        {
            try
            {
                if (dest != null && dest.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _v6.Send(payload, payload.Length, new IPEndPoint(dest, UdpPort));
                }
                else
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        var v6props = nic.GetIPProperties().GetIPv6Properties();
                        if (nic.OperationalStatus != OperationalStatus.Up || v6props == null) continue;
                        try
                        {
                            var group = IPAddress.Parse("ff02::1");
                            group.ScopeId = v6props.Index;
                            _v6.Send(payload, payload.Length, new IPEndPoint(group, UdpPort));
                        }
                        catch { /* 单个接口失败忽略 */ }
                    }
                }
            }
            catch { /* IPv6 不可用时忽略 */ }
        }
    }

    private void ReceiveLoop(UdpClient client, AddressFamily family)
    {
        while (_running)
        {
            try
            {
                IPEndPoint remote = new(IPAddress.Any, 0);
                if (family == AddressFamily.InterNetworkV6) remote = new IPEndPoint(IPAddress.IPv6Any, 0);
                byte[] data = client.Receive(ref remote);
                var peer = ParseProbe(data, remote.Address);
                if (peer == null) continue;
                Found?.Invoke(peer);
                // 回发自己的探测（单播），让对方也发现自己（用对方的地址族选栈）
                if (family == AddressFamily.InterNetwork) SendProbe(remote.Address, AddressFamily.InterNetwork);
                else SendProbe(remote.Address, AddressFamily.InterNetworkV6);
            }
            catch (ObjectDisposedException) { break; }
            catch { /* 单次收包失败继续 */ }
        }
    }

    private Peer? ParseProbe(byte[] data, IPAddress from)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(data)).RootElement;
            if (!json.TryGetProperty("proto", out var p) || p.GetInt32() != 1) return null;
            if (json.TryGetProperty("id", out var idEl) && idEl.GetString() == _instanceId) return null; // 自己的回声
            return new Peer
            {
                Name = json.TryGetProperty("device", out var d) ? d.GetString() ?? "?" : "?",
                TcpPort = json.TryGetProperty("tcp", out var t) ? t.GetInt32() : SyncServer.Port,
                Host = from.IsIPv4MappedToIPv6 ? from.MapToIPv4().ToString() : from.ToString(),
                Seen = DateTime.Now
            };
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _running = false;
        _probeTimer?.Dispose();
        try { _v4?.Close(); } catch { }
        try { _v6?.Close(); } catch { }
    }
}

/// <summary>极简 Dictionary→JSON（探测包字段全是无特殊字符的简单值）。</summary>
internal static class ProbeJson
{
    public static string ToJson(this Dictionary<string, object> dict)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":");
            sb.Append(kv.Value switch
            {
                string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
                int i => i.ToString(),
                long l => l.ToString(),
                bool b => b ? "true" : "false",
                _ => "\"" + kv.Value + "\""
            });
        }
        return sb.Append('}').ToString();
    }
}
