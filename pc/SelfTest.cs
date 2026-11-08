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

        // 11. URL 解析（纯字符串，无网络）
        var g1 = UrlImporter.Parse("https://github.com/foo/bar");
        Check("github 链接解析", g1 != null && g1.Kind == "github" && g1.User == "foo" && g1.Repo == "bar" && g1.Branch == "");
        var g2 = UrlImporter.Parse("http://github.com/foo/bar/tree/dev/webapp/");
        Check("github tree 分支+子目录解析", g2 != null && g2.Branch == "dev" && g2.SubPath == "webapp");
        var g3 = UrlImporter.Parse("https://gitee.com/u/r.git");
        Check("gitee 链接解析（.git 后缀剥离）", g3 != null && g3.Kind == "gitee" && g3.Repo == "r");
        var g4 = UrlImporter.Parse("https://example.com/app/dist/app.zip?token=x");
        Check("zip 直链解析", g4 != null && g4.Kind == "zip" && g4.ZipUrl.Contains("app.zip"));
        Check("无关链接返回 null", UrlImporter.Parse("https://example.com/hello") == null);

        // 12. storeHash（协议 9.4：与键序无关，与内容有关）
        bridge.Clear(); // 先清掉第 7 节遗留的测试数据
        bridge.Save("b", "2");
        bridge.Save("a", "1");
        string hashA = AppRepo.StoreHash(app.Id);
        bridge.Clear();
        bridge.Save("a", "1");
        bridge.Save("b", "2");
        Check("storeHash 与写入顺序无关", AppRepo.StoreHash(app.Id) == hashA,
            $"h1={hashA} h2={AppRepo.StoreHash(app.Id)} 键={string.Join(",", AppRepo.StoreJson(app.Id).Keys)}");
        bridge.Save("a", "改");
        Check("storeHash 随内容变化", AppRepo.StoreHash(app.Id) != hashA);

        // 13. 保 id 安装（同步路径，协议 9.3）
        string syncedId = "app_synced_selftest";
        var synced = AppRepo.InstallSynced(exportZip, syncedId);
        Check("InstallSynced 沿用指定 id", synced.Id == syncedId && Directory.Exists(AppRepo.AppDir(syncedId)));
        Check("InstallSynced 名称来自 __meta__", synced.Name == "自检小程序", synced.Name);
        Check("InstallSynced 采纳包内 store 数据",
            AppRepo.StoreJson(syncedId).TryGetValue("进度", out string pv) && pv == "42");
        string indexBefore = File.ReadAllText(Path.Combine(AppRepo.AppDir(syncedId), "index.html"));
        AppRepo.InstallSynced(exportZip, syncedId);
        Check("InstallSynced 重复安装=替换不报错", File.Exists(Path.Combine(AppRepo.AppDir(syncedId), "index.html")));

        // 14. 回环同步（真实 HTTP：Server + Client 本机互通，IPv4-mapped 走双栈监听）
        Environment.SetEnvironmentVariable("MINIAPPBOX_SYNC_PORT", "48777");
        var serverSettings = new AppSettings { DeviceName = "自检机" };
        using (var server = new SyncServer(serverSettings))
        {
            string hashBefore = AppRepo.StoreHash(syncedId);
            server.Start();
            Thread.Sleep(300);
            var info = SyncClient.GetInfo("127.0.0.1", 48777);
            Check("GET /sync/info 返回设备与应用", info.Device == "自检机"
                && info.Apps.Any(a => a.Id == syncedId));
            Check("info 里 storeHash 与本地一致",
                info.Apps.FirstOrDefault(a => a.Id == syncedId)?.StoreHash == hashBefore);
            byte[] pulled = SyncClient.GetApp("127.0.0.1", 48777, syncedId);
            Check("GET /sync/app 拉到 zip", pulled.Length > 100 && pulled[0] == 'P' && pulled[1] == 'K');
            AppRepo.Delete(syncedId);
            Check("删除后 info 不再包含", !SyncClient.GetInfo("127.0.0.1", 48777).Apps.Any(a => a.Id == syncedId));
            SyncClient.PushApp("127.0.0.1", 48777, syncedId, pulled);
            Check("POST /sync/app 推送=保 id 重建",
                Directory.Exists(AppRepo.AppDir(syncedId)) && AppRepo.StoreHash(syncedId) == hashBefore);
            server.Stop();
        }
        Environment.SetEnvironmentVariable("MINIAPPBOX_SYNC_PORT", null);

        // 15. 清理
        AppRepo.Delete(app.Id);
        AppRepo.Delete(app2.Id);
        Check("删除后目录与存储一并清除", !Directory.Exists(AppRepo.AppDir(app.Id)) && !File.Exists(AppRepo.StoreFile(app.Id)));

        try { Directory.Delete(tmp, true); } catch { }
        try { Directory.Delete(AppRepo.RootDir, true); } catch { }

        Console.WriteLine($"== 结果: 通过 {pass} / 失败 {fail} ==");
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
