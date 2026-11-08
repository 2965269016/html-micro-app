using System.Text.Json.Nodes;

namespace MiniAppBox;

/// <summary>
/// JS 系统能力桥接（镜像手机端 MiniAppBridge.kt，互通协议 v1）。
///
/// 注册名 "Native"（另注册别名 "LocalStore" 兼容旧示例包）。
/// 所有方法同步返回 JSON 字符串：{"ok":true,...} 或 {"ok":false,"error":"..."}。
/// 权限未开启时返回 {"ok":false,"error":"permission_denied"}。
/// 通过 WebView2 AddHostObjectToScript 暴露，JS 侧经 hostObjects.sync 同步调用。
/// </summary>
[System.Runtime.InteropServices.ComVisible(true)]
public class MiniAppBridge
{
    private readonly string _appId;
    private readonly RunForm _owner;

    public MiniAppBridge(RunForm owner, string appId)
    {
        _owner = owner;
        _appId = appId;
    }

    // ------------------------------------------------------------ JSON 工具

    private static string Ok(params (string k, object v)[] pairs)
    {
        var json = new JsonObject { ["ok"] = true };
        foreach (var (k, v) in pairs) json[k] = ToNode(v);
        return json.ToJsonString(JsonOpts.Compact);
    }

    private static string Fail(string error) =>
        new JsonObject { ["ok"] = false, ["error"] = error }.ToJsonString(JsonOpts.Compact);

    private static string Denied() => Fail("permission_denied");

    private static JsonNode ToNode(object v) => v switch
    {
        null => null,
        JsonNode n => n,
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        _ => JsonValue.Create(v.ToString())
    };

    /// <summary>与手机端 JS 桥一致的宽松参数转换：数字/布尔一律转成字符串，null 保持 null。</summary>
    /// 注：WebView2 宿主对象只支持具体封送类型，"数字→字符串"的宽松转换由
    /// RunForm 注入的 JS 包装层完成，桥本身收到的已是 string/bool/double。
    private static string ErrMsg(Exception e) =>
        string.IsNullOrWhiteSpace(e.Message) ? "unknown" : e.Message;

    // ------------------------------------------------------------ 存储（免权限）

    public string Save(string key, string value)
    {
        try
        {
            if (key == null) return Fail("key_required");
            var store = AppRepo.StoreJson(_appId);
            store[key] = value ?? "";
            AppRepo.SaveStore(_appId, store);
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string Load(string key)
    {
        try
        {
            if (key == null) return Fail("key_required");
            var store = AppRepo.StoreJson(_appId);
            return store.TryGetValue(key, out string v) ? Ok(("value", v)) : Ok(("value", (object)null));
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string Remove(string key)
    {
        try
        {
            if (key == null) return Fail("key_required");
            var store = AppRepo.StoreJson(_appId);
            store.Remove(key);
            AppRepo.SaveStore(_appId, store);
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string Clear()
    {
        try
        {
            AppRepo.ClearData(_appId);
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string Keys()
    {
        try
        {
            var list = new JsonArray();
            foreach (string k in AppRepo.StoreJson(_appId).Keys) list.Add(k);
            return Ok(("keys", new JsonObject { ["list"] = list }));
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    // ------------------------------------------------------------ 通知（需权限）

    public string Notify(string title, string body)
    {
        if (!AppRepo.HasPermission(_appId, AppRepo.PERM_NOTIFICATION)) return Denied();
        try
        {
            // 电脑端以托盘气泡呈现；无法可靠探测系统通知总开关，故恒返回 ok（协议中已注明差异）
            RunOnUi(() => _owner.ShowAppNotification(title ?? _appId, body ?? ""));
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    // ------------------------------------------------------------ 设备（振动需权限）

    public string Vibrate(double ms)
    {
        if (!AppRepo.HasPermission(_appId, AppRepo.PERM_VIBRATE)) return Denied();
        long duration = (long)Math.Round(ms);
        if (duration < 1 || duration > 10_000) duration = 200L; // 与手机端同规则
        try
        {
            // 电脑端无振动器，等效表现为任务栏图标闪烁
            RunOnUi(() => _owner.FlashWindow(duration));
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string Toast(string message)
    {
        try
        {
            RunOnUi(() => _owner.ShowAppToast(message ?? ""));
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string DeviceInfo()
    {
        try
        {
            string version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
            string screen = bounds.HasValue ? bounds.Value.Width + "x" + bounds.Value.Height : "";
            return Ok(
                ("model", "PC"),
                ("brand", "MiniAppBox-PC"),
                ("osVersion", "Windows " + Environment.OSVersion.Version.ToString()),
                ("appVersion", version),
                ("screen", screen));
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    // ------------------------------------------------------------ 剪贴板（需权限）

    public string CopyText(string text)
    {
        if (!AppRepo.HasPermission(_appId, AppRepo.PERM_CLIPBOARD)) return Denied();
        try
        {
            RunOnUi(() => { if (!string.IsNullOrEmpty(text)) System.Windows.Forms.Clipboard.SetText(text); });
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    public string PasteText()
    {
        if (!AppRepo.HasPermission(_appId, AppRepo.PERM_CLIPBOARD)) return Denied();
        try
        {
            string text = RunOnUi(() => System.Windows.Forms.Clipboard.GetText());
            return Ok(("text", string.IsNullOrEmpty(text) ? null : text));
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    // ------------------------------------------------------------ 分享（免权限）

    public string Share(string title, string text)
    {
        try
        {
            // 电脑端无系统分享面板，等效实现：复制到剪贴板 + toast 提示（协议中已注明差异）
            RunOnUi(() =>
            {
                if (!string.IsNullOrEmpty(text)) System.Windows.Forms.Clipboard.SetText(text);
            });
            RunOnUi(() => _owner.ShowAppToast("内容已复制到剪贴板，可粘贴分享"));
            return Ok();
        }
        catch (Exception e) { return Fail(ErrMsg(e)); }
    }

    // ------------------------------------------------------------ 线程辅助

    /// <summary>hostObjects.sync 调用通常已在 UI 线程执行；万一不在则切回 UI 线程。</summary>
    private void RunOnUi(Action action)
    {
        if (_owner != null && _owner.IsHandleCreated && _owner.InvokeRequired) _owner.BeginInvoke(action);
        else action();
    }

    private string RunOnUi(Func<string> func)
    {
        if (_owner != null && _owner.IsHandleCreated && _owner.InvokeRequired)
            return (string)_owner.Invoke(func);
        return func();
    }
}
