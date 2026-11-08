using System.Text.Json.Nodes;

namespace MiniAppBox;

/// <summary>
/// 仓库层自检（--selftest）：不经 GUI 直接验证 AppRepo 与桥的存储语义。
/// 用环境变量 MINIAPPBOX_HOME 隔离测试数据，不碰真实用户数据。
/// </summary>
public static class SelfTest
{
    public static void Run()
    {
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Console.WriteLine($"  [通过] {name}"); }
            else { fail++; Console.WriteLine($"  [失败] {name}  {detail}"); }
        }

        Console.WriteLine("== 小程序盒子 PC 版 仓库自检 ==");
        Console.WriteLine($"数据根目录: {AppRepo.RootDir}");

        // 1. 无包时列表为空
        Check("初始列表为空", AppRepo.List().Count == 0);

        // 2. 导入一个临时构造的最小包
        string tmp = Path.Combine(Path.GetTempPath(), "miniappbox_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        string zipPath = Path.Combine(tmp, "自检小程序.zip");
        string srcDir = Path.Combine(tmp, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "index.html"), "<!DOCTYPE html><html><body>自检</body></html>");
        File.WriteAllText(Path.Combine(srcDir, "manifest.json"), "{\"name\":\"自检小程序\"}");
        System.IO.Compression.ZipFile.CreateFromDirectory(srcDir, zipPath);

        MiniApp app;
        try
        {
            app = AppRepo.Import(zipPath, "自检小程序.zip");
        }
        catch (Exception e)
        {
            Check("导入最小包", false, e.Message);
            Console.WriteLine($"== 结果: 通过 {pass} / 失败 {fail} ==");
            Environment.Exit(1);
            return;
        }
        Check("导入最小包并读取 manifest.name", app.Name == "自检小程序", $"实际 name={app.Name}");
        Check("id 格式 app_yyyyMMdd_HHmmss",
            app.Id.StartsWith("app_") && app.Id.Length == 4 + 8 + 1 + 6, $"实际 id={app.Id}");
        Check("index.html 落位", File.Exists(Path.Combine(AppRepo.AppDir(app.Id), "index.html")));

        // 3. 列表与排序
        Check("导入后列表含 1 项", AppRepo.List().Count == 1);

        // 4. 桥：存储语义（bridge 的 owner 为 null 时仅存储/设备类方法可用）
        var bridge = new MiniAppBridge(null, app.Id);
        Check("save 返回 {ok:true}", bridge.Save("k1", "值1") == "{\"ok\":true}");
        Check("load 读回字符串", bridge.Load("k1") == "{\"ok\":true,\"value\":\"值1\"}", bridge.Load("k1"));
        Check("load 不存在的 key 返回 value:null", bridge.Load("nope") == "{\"ok\":true,\"value\":null}");
        Check("save(null) → key_required", bridge.Save(null, "x") == "{\"ok\":false,\"error\":\"key_required\"}");
        Check("save(null值) 存空串（与手机端一致）", bridge.Save("nul", null) == "{\"ok\":true}"
            && bridge.Load("nul") == "{\"ok\":true,\"value\":\"\"}");
        Check("keys 列表", bridge.Keys().Contains("\"k1\"") && bridge.Keys().Contains("\"nul\""));
        Check("remove", bridge.Remove("k1") == "{\"ok\":true}" && bridge.Load("k1").Contains("null"));
        Check("clear", bridge.Clear() == "{\"ok\":true}" && bridge.Keys() == "{\"ok\":true,\"keys\":{\"list\":[]}}");

        // 5. 权限引擎：默认全关，开启后桥放行
        Check("未授权时 notify → permission_denied",
            bridge.Notify("t", "b") == "{\"ok\":false,\"error\":\"permission_denied\"}");
        Check("未授权时 copyText → permission_denied",
            bridge.CopyText("x") == "{\"ok\":false,\"error\":\"permission_denied\"}");
        Check("未授权时 pasteText → permission_denied",
            bridge.PasteText() == "{\"ok\":false,\"error\":\"permission_denied\"}");
        Check("未授权时 vibrate → permission_denied",
            bridge.Vibrate(100) == "{\"ok\":false,\"error\":\"permission_denied\"}");
        AppRepo.SetPermission(app.Id, AppRepo.PERM_CLIPBOARD, true);
        Check("授权后 copyText 放行", bridge.CopyText("剪贴板内容") == "{\"ok\":true}");
        Check("授权后 pasteText 读回", bridge.PasteText().Contains("剪贴板内容"), bridge.PasteText());
        AppRepo.SetPermission(app.Id, AppRepo.PERM_CLIPBOARD, false);
        Check("实时关闭权限立即生效", bridge.CopyText("x") == "{\"ok\":false,\"error\":\"permission_denied\"}");

        // 6. meta.json 与手机端同构
        string metaText = File.ReadAllText(Path.Combine(AppRepo.AppDir(app.Id), "meta.json"));
        var meta = JsonNode.Parse(metaText) as JsonObject;
        Check("meta.json 含 id/name/installedAt/permissions",
            meta != null && meta.ContainsKey("id") && meta.ContainsKey("name")
            && meta.ContainsKey("installedAt") && meta.ContainsKey("permissions"));
        Check("permissions 实时持久化", ((JsonObject)meta["permissions"])["clipboard"]?.GetValue<bool>() == false);

        // 7. 导出格式：HTML + __meta__/meta.json + __data__/store.json
        bridge.Save("进度", "42");
        string exportZip = Path.Combine(tmp, "export.zip");
        AppRepo.Export(app.Id, exportZip);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(exportZip))
        {
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
            Check("导出含 index.html", names.Contains("index.html"));
            Check("导出含 __meta__/meta.json", names.Contains("__meta__/meta.json"));
            Check("导出含 __data__/store.json", names.Contains("__data__/store.json"));
            Check("导出不含内部 meta.json",
                !names.Any(n => n == "meta.json" || (n.StartsWith("__data__/") && n != "__data__/store.json")));
        }

        // 8. 二次导入同一包 → 数据随包还原（互通迁移路径）
        var app2 = AppRepo.Import(exportZip, "export.zip");
        Check("再次导入分配新 id", app2.Id != app.Id);
        Check("导入还原 __data__/store.json 数据",
            AppRepo.StoreJson(app2.Id).TryGetValue("进度", out string v) && v == "42",
            string.Join(",", AppRepo.StoreJson(app2.Id)));

        // 9. zip-slip 防护：构造带 ../ 的恶意包
        string evilZip = Path.Combine(tmp, "evil.zip");
        using (var fs = File.Create(evilZip))
        using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("pwn");
        }
        AppRepo.ImportException slip = null;
        try { AppRepo.Import(evilZip, "evil.zip"); }
        catch (AppRepo.ImportException ex) { slip = ex; }
        bool noEscape = !File.Exists(Path.Combine(AppRepo.AppsRoot, "escape.txt"));
        Check("zip-slip 路径被跳过", noEscape || slip != null, "逃逸文件出现在目录外!");

        // 10. 清理
        AppRepo.Delete(app.Id);
        AppRepo.Delete(app2.Id);
        Check("删除后目录与存储一并清除", !Directory.Exists(AppRepo.AppDir(app.Id)) && !File.Exists(AppRepo.StoreFile(app.Id)));

        try { Directory.Delete(tmp, true); } catch { }
        try { Directory.Delete(AppRepo.RootDir, true); } catch { }

        Console.WriteLine($"== 结果: 通过 {pass} / 失败 {fail} ==");
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
