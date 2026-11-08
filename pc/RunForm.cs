using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.WinForms;

namespace MiniAppBox;

/// <summary>
/// 小程序运行时：WebView2 加载指定 id 的 HTML 小程序。
/// 对应手机端 RunActivity：
///   - SetVirtualHostNameToFolderMapping ↔ WebViewAssetLoader（本地 https 映射小程序目录）
///   - PermissionRequested 按该小程序权限开关放行地理定位
///   - Native / LocalStore 桥经 AddHostObjectToScript 注入（同步语义）
/// </summary>
public class RunForm : Form
{
    private const string VirtualHost = "appassets.local";

    private readonly MiniApp _app;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private NotifyIcon _tray;
    private IntPtr _iconHandle = IntPtr.Zero;

    public RunForm(MiniApp app)
    {
        _app = app;
        Text = app.Name;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 480);
        Size = new Size(1000, 760);

        var top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = ColorTranslator.FromHtml("#6750A4") };
        var back = new Button
        {
            Text = "←",
            Dock = DockStyle.Left,
            Width = 48,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = ColorTranslator.FromHtml("#6750A4"),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold)
        };
        back.FlatAppearance.BorderSize = 0;
        back.Click += (_, _) => GoBack();
        var title = new Label
        {
            Text = app.Name,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        top.Controls.Add(title);
        top.Controls.Add(back);

        // 先加入填充控件，后加入停靠控件（WinForms 停靠布局按加入的逆序处理）
        Controls.Add(_web);
        Controls.Add(top);

        var bmp = IconGen.Generate(app.Name);
        _iconHandle = bmp.GetHicon();
        Icon = Icon.FromHandle(_iconHandle);
        bmp.Dispose();

        Load += async (_, _) => await InitWeb();
        FormClosed += (_, _) => Cleanup();
    }

    private async Task InitWeb()
    {
        // WebView2 用户数据放进盒子的数据根目录，保持"本地处理"的隔离与可迁移性
        // MINIAPPBOX_CDP=<端口> 环境变量可开启远程调试（自动化测试/排障用）
        var options = new CoreWebView2EnvironmentOptions();
        string cdpPort = Environment.GetEnvironmentVariable("MINIAPPBOX_CDP");
        if (!string.IsNullOrEmpty(cdpPort))
            options.AdditionalBrowserArguments = "--remote-debugging-port=" + cdpPort;
        var env = await CoreWebView2Environment.CreateAsync(
            null, Path.Combine(AppRepo.RootDir, "WebView2"), options);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;

        // 与手机端 WebView 行为对齐：不给最终用户暴露浏览器右键菜单和开发者工具
        // （小程序开发者需要调试时，设 MINIAPPBOX_CDP=<端口> 环境变量后用远程调试）
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, AppRepo.AppDir(_app.Id), CoreWebView2HostResourceAccessKind.Allow);

        // 系统能力桥接：Native（新）+ LocalStore（旧包兼容别名）。
        // 实现方式：页面 JS 经"同步 XHR"请求专用桥域名 /__native__/<方法名>，
        // 宿主在 WebResourceRequested 事件里同步应答 —— 对小程序而言与手机端
        // addJavascriptInterface 一样是同步返回 JSON 串（桥语义见 PROTOCOL.md 第 4 节）。
        // 注意：桥域名必须与虚拟域名映射（VirtualHost）分开，否则请求会被文件映射
        // 吞掉而不触发 WebResourceRequested；跨域调用靠响应头 CORS 放行。
        // （不用 AddHostObjectToScript：其 COM 同步封送在部分环境返回 0x80070057，不可靠）
        _bridge = new MiniAppBridge(this, _app.Id);
        _env = env;
        core.AddWebResourceRequestedFilter(
            $"*://{BridgeHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnNativeRequest;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShimJs);

        // 定位：按该小程序的权限开关放行；其余宿主未提供的权限一律拒绝
        core.PermissionRequested += (_, e) =>
        {
            e.Handled = true;
            bool allow = e.PermissionKind == CoreWebView2PermissionKind.Geolocation
                && AppRepo.HasPermission(_app.Id, AppRepo.PERM_GEOLOCATION);
            e.State = allow
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
        };

        // 新窗口请求在本窗口内打开（与手机端单 WebView 行为一致）
        core.NewWindowRequested += (_, e) =>
        {
            core.Navigate(e.Uri);
        };

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    private MiniAppBridge _bridge;
    private CoreWebView2Environment _env;

    private const string BridgeHost = "bridge.localapp";
    private const string CorsHeaders =
        "Content-Type: application/json; charset=utf-8\r\nAccess-Control-Allow-Origin: *";

    private void Respond(CoreWebView2WebResourceRequestedEventArgs e, string json)
    {
        e.Response = _env.CreateWebResourceResponse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), 200, "OK", CorsHeaders);
    }

    /// <summary>/__native__/<方法> 请求的同步应答：解析 JSON 数组参数 → 调桥 → 返回 JSON 串。</summary>
    private void OnNativeRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            string method = new Uri(e.Request.Uri).AbsolutePath.Split('/').Last();
            string body = "";
            if (e.Request.Content != null)
                using (var sr = new StreamReader(e.Request.Content, System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
                    body = sr.ReadToEnd();

            JsonNode args = null;
            try { args = body.Length > 0 ? JsonNode.Parse(body) : new JsonArray(); }
            catch { args = null; }
            if (args is not JsonArray arr)
            {
                Respond(e, "{\"ok\":false,\"error\":\"bad_request\"}");
                return;
            }
            string ArgStr(int i) => arr.Count > i && arr[i] is JsonValue v && v.TryGetValue(out string sv) ? sv : null;
            double ArgNum(int i) => arr.Count > i && arr[i] is JsonValue n && n.TryGetValue(out double dn) ? dn : 0;

            string result = method switch
            {
                "save" => _bridge.Save(ArgStr(0), ArgStr(1)),
                "load" => _bridge.Load(ArgStr(0)),
                "remove" => _bridge.Remove(ArgStr(0)),
                "clear" => _bridge.Clear(),
                "keys" => _bridge.Keys(),
                "notify" => _bridge.Notify(ArgStr(0), ArgStr(1)),
                "vibrate" => _bridge.Vibrate(ArgNum(0)),
                "toast" => _bridge.Toast(ArgStr(0)),
                "copyText" => _bridge.CopyText(ArgStr(0)),
                "pasteText" => _bridge.PasteText(),
                "share" => _bridge.Share(ArgStr(0), ArgStr(1)),
                "deviceInfo" => _bridge.DeviceInfo(),
                _ => "{\"ok\":false,\"error\":\"unknown_method\"}"
            };

            Respond(e, result);
        }
        catch (Exception)
        {
            Respond(e, "{\"ok\":false,\"error\":\"bridge_error\"}");
        }
    }

    /// <summary>注入到每个文档的桥脚本：全局 Native / LocalStore（宽松参数，同步返回 JSON 串）。</summary>
    private const string BridgeShimJs = """
        (function () {
          function call(name, body) {
            var x = new XMLHttpRequest();
            x.open('POST', 'https://bridge.localapp/__native__/' + name, false);
            x.send(body == null ? '' : body);
            return x.responseText;
          }
          function s(v) { return (typeof v === 'number' || typeof v === 'boolean') ? String(v) : v; }
          function a(list) { return JSON.stringify(list); }
          var api = {
            save: function (k, v) { return call('save', a([s(k), arguments.length > 1 ? s(v) : null])); },
            load: function (k) { return call('load', a([s(k)])); },
            remove: function (k) { return call('remove', a([s(k)])); },
            clear: function () { return call('clear', a([])); },
            keys: function () { return call('keys', a([])); },
            notify: function (t, b) { return call('notify', a([s(t), s(b)])); },
            vibrate: function (ms) { var n = Number(ms); if (!isFinite(n)) n = 200; return call('vibrate', a([Math.round(n)])); },
            toast: function (m) { return call('toast', a([s(m)])); },
            copyText: function (t) { return call('copyText', a([s(t)])); },
            pasteText: function () { return call('pasteText', a([])); },
            share: function (t, x) { return call('share', a([s(t), s(x)])); },
            deviceInfo: function () { return call('deviceInfo', a([])); }
          };
          window.Native = api;
          window.LocalStore = api;
        })();
        """;

    private void GoBack()
    {
        // 悬浮返回键：网页有历史则后退，否则退出小程序
        if (_web.CoreWebView2 != null && _web.CoreWebView2.CanGoBack) _web.CoreWebView2.GoBack();
        else Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Left) || keyData == Keys.BrowserBack) { GoBack(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------ 桥的宿主侧呈现

    public void ShowAppToast(string message) => ToastForm.Show(this, message);

    public void ShowAppNotification(string title, string body)
    {
        if (_tray == null)
        {
            _tray = new NotifyIcon
            {
                Icon = Icon,
                Text = _app.Name,
                Visible = true
            };
        }
        _tray.ShowBalloonTip(3000, title, body, ToolTipIcon.None);
    }

    /// <summary>电脑端对"振动"的等效表现：任务栏图标闪烁 duration 毫秒。</summary>
    public void FlashWindow(long durationMs)
    {
        if (!IsHandleCreated) return;
        var info = new FlashWInfo
        {
            CbSize = (uint)Marshal.SizeOf<FlashWInfo>(),
            Hwnd = Handle,
            DwFlags = FLASHW_ALL | FLASHW_TIMER,
            UCount = uint.MaxValue
        };
        FlashWindowEx(ref info);
        var stop = new System.Windows.Forms.Timer { Interval = (int)Math.Clamp(durationMs, 100, 10_000) };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            stop.Dispose();
            var halt = new FlashWInfo
            {
                CbSize = (uint)Marshal.SizeOf<FlashWInfo>(),
                Hwnd = Handle,
                DwFlags = FLASHW_STOP
            };
            FlashWindowEx(ref halt);
        };
        stop.Start();
    }

    private void Cleanup()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    // ------------------------------------------------------------ Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint DwFlags;
        public uint UCount;
        public uint DwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWInfo pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_ALL = 0x3;
    private const uint FLASHW_TIMER = 0x4;
}
