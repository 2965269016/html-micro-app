using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniAppBox;

/// <summary>
/// 小程序仓库：HTML 小程序包的导入、列表、删除、权限、存储与打包。
/// 设计与手机端 AppRepo.kt 一一对应（互通协议 v1）。
///
/// 目录布局：
///   %LOCALAPPDATA%/MiniAppBox/apps/&lt;id&gt;/          小程序 HTML 文件（index.html 必须在根）
///   %LOCALAPPDATA%/MiniAppBox/apps/&lt;id&gt;/meta.json  {"id","name","installedAt","permissions":{...}}
///   %LOCALAPPDATA%/MiniAppBox/store_&lt;id&gt;.json      该小程序的 JS 存储（MiniAppBridge 使用）
///
/// 打包导出格式（与手机端互通）：
///   &lt;HTML 文件...&gt;
///   __meta__/meta.json
///   __data__/store.json   {"key":"value",...}
/// </summary>
public sealed class MiniApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long InstalledAt { get; set; }
}

public static class AppRepo
{
    public const string PERM_NOTIFICATION = "notification";
    public const string PERM_VIBRATE = "vibrate";
    public const string PERM_CLIPBOARD = "clipboard";
    public const string PERM_GEOLOCATION = "geolocation";
    public static readonly string[] AllPerms =
        { PERM_NOTIFICATION, PERM_VIBRATE, PERM_CLIPBOARD, PERM_GEOLOCATION };

    private const string DATA_DIR = "__data__";
    private const string META_DIR = "__meta__";

    public class ImportException : Exception
    {
        public ImportException(string message) : base(message) { }
    }

    private static readonly JsonSerializerOptions IndentOpt = JsonOpts.Indented;

    // ------------------------------------------------------------ 基础路径
    // 允许用环境变量 MINIAPPBOX_HOME 重定向根目录（--selftest 隔离测试数据用）

    public static string RootDir =>
        Environment.GetEnvironmentVariable("MINIAPPBOX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniAppBox");

    public static string AppsRoot => Path.Combine(RootDir, "apps");
    public static string AppDir(string id) => Path.Combine(AppsRoot, id);
    public static string StoreFile(string id) => Path.Combine(RootDir, "store_" + id + ".json");

    // ------------------------------------------------------------ 导入

    /// <summary>导入一个 HTML 小程序包（zip），返回新装小程序元数据（分配新 id）。</summary>
    public static MiniApp Import(string zipPath, string fallbackName)
    {
        string stage = Path.Combine(Path.GetTempPath(), "miniappbox_stage_" + Guid.NewGuid().ToString("N"));
        try
        {
            Unzip(zipPath, stage);
            return ImportStage(stage, fallbackName);
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { /* 清理失败可忽略 */ }
        }
    }

    /// <summary>把一个已解压的目录导入为小程序（URL 导入的多应用仓库用）。</summary>
    public static MiniApp ImportDirectory(string srcDir, string fallbackName)
    {
        string stage = Path.Combine(Path.GetTempPath(), "miniappbox_stage_" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(srcDir, stage);
            return ImportStage(stage, fallbackName);
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { /* 清理失败可忽略 */ }
        }
    }

    private static MiniApp ImportStage(string stage, string fallbackName)
    {
        NormalizeRoot(stage);
        if (!File.Exists(Path.Combine(stage, "index.html")))
            throw new ImportException("包内根目录缺少 index.html");

        string dest = AllocateAppDir();
        MoveOrCopy(stage, dest);

        // 还原随包数据（如果有）
        RestoreStore(Path.GetFileName(dest), Path.Combine(dest, DATA_DIR, "store.json"));

        string name = ReadManifestName(Path.Combine(dest, "manifest.json"))
            ?? FallbackName(fallbackName);
        var app = new MiniApp
        {
            Id = Path.GetFileName(dest),
            Name = name,
            InstalledAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        };
        SaveMeta(app);
        return app;
    }

    /// <summary>
    /// 同步安装：沿用发送端的 expectedId（与普通导入不同，见协议 9.3）。
    /// id 已存在则替换 HTML 目录；store 与 meta 采用包内数据。
    /// </summary>
    public static MiniApp InstallSynced(string zipPath, string expectedId)
    {
        string stage = Path.Combine(Path.GetTempPath(), "miniappbox_sync_" + Guid.NewGuid().ToString("N"));
        try
        {
            Unzip(zipPath, stage);
            NormalizeRoot(stage);
            if (!File.Exists(Path.Combine(stage, "index.html")))
                throw new ImportException("同步包内缺少 index.html");

            string dest = AppDir(expectedId);
            Directory.CreateDirectory(AppsRoot);
            if (Directory.Exists(dest))
            {
                // 旧目录先删（store_<id>.json 是独立文件，不受影响，稍后按包内数据覆盖）
                Directory.Delete(dest, true);
            }
            MoveOrCopy(stage, dest);

            // meta：优先包内 __meta__/meta.json（含权限开关）
            MiniApp app;
            string metaSrc = Path.Combine(dest, META_DIR, "meta.json");
            if (File.Exists(metaSrc))
            {
                app = LoadMetaFromJson(metaSrc, expectedId) ?? new MiniApp { Id = expectedId, Name = FallbackName(expectedId) };
            }
            else
            {
                app = new MiniApp
                {
                    Id = expectedId,
                    Name = ReadManifestName(Path.Combine(dest, "manifest.json")) ?? FallbackName(expectedId)
                };
            }
            app.Id = expectedId;
            app.InstalledAt = app.InstalledAt > 0 ? app.InstalledAt : DateTimeOffset.Now.ToUnixTimeMilliseconds();
            SaveMeta(app);

            // store 采用包内数据（同步=完整采纳发送端数据）
            ClearData(expectedId);
            RestoreStore(expectedId, Path.Combine(dest, DATA_DIR, "store.json"));
            return app;
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { /* 清理失败可忽略 */ }
        }
    }

    private static MiniApp LoadMetaFromJson(string metaPath, string fallbackId)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(metaPath));
            string name = json?["name"]?.ToString();
            return new MiniApp
            {
                Id = json?["id"]?.ToString() ?? fallbackId,
                Name = string.IsNullOrWhiteSpace(name) ? FallbackName(fallbackId) : name,
                InstalledAt = json?["installedAt"]?.GetValue<long>() ?? 0L
            };
        }
        catch { return null; }
    }

    private static string AllocateAppDir()
    {
        Directory.CreateDirectory(AppsRoot);
        // id 与手机端同格式："app_" + yyyyMMdd_HHmmss
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string id = "app_" + DateTime.Now.ToString("yyyyMMdd_HHmmss",
                System.Globalization.CultureInfo.InvariantCulture);
            string dest = AppDir(id);
            if (!Directory.Exists(dest)) return dest;
            Thread.Sleep(1000); // 同一秒内重复导入时避让
        }
        throw new ImportException("导入过于频繁，请稍后再试");
    }

    /// <summary>对应手机端 stage.renameTo(dest) 失败时 copyRecursively 的兜底逻辑。</summary>
    private static void MoveOrCopy(string src, string dst)
    {
        try { Directory.Move(src, dst); }
        catch { CopyDir(src, dst); }
    }

    internal static void Unzip(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            string root = Path.GetFullPath(destDir);
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(name) || name.Contains("..")) continue; // zip-slip 防护
                string target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
                string full = Path.GetFullPath(target);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && full != root) continue; // 绝对路径/盘符等逃逸路径一律跳过

                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(full);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                using var es = entry.Open();
                using var fs = File.Create(full);
                es.CopyTo(fs);
            }
        }
        catch (ImportException) { throw; }
        catch (Exception e) { throw new ImportException("解压失败：" + e.Message); }
    }

    /// <summary>若解压后只有一个顶层目录（且没有根级 index.html），把该目录内容上移一层。</summary>
    private static void NormalizeRoot(string stage)
    {
        if (File.Exists(Path.Combine(stage, "index.html"))) return;
        string[] dirs = Directory.GetDirectories(stage);
        string[] files = Directory.GetFiles(stage);
        if (dirs.Length != 1 || files.Length != 0) return;

        string inner = dirs[0];
        foreach (string child in Directory.GetFileSystemEntries(inner))
        {
            string target = Path.Combine(stage, Path.GetFileName(child));
            if (Directory.Exists(child))
            {
                if (Directory.Exists(target)) CopyDir(child, target);
                else Directory.Move(child, target);
            }
            else
            {
                File.Move(child, target, true);
            }
        }
        try { Directory.Delete(inner); } catch { /* 内含未移出文件时保留 */ }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src)) File.Move(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static string FallbackName(string fileName)
    {
        string n = fileName ?? "";
        if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        n = n.Trim();
        return n.Length > 0 ? n : "未命名小程序";
    }

    private static string ReadManifestName(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return null;
            var node = JsonNode.Parse(File.ReadAllText(manifestPath));
            string name = node?["name"]?.ToString();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------ 元数据

    public static void SaveMeta(MiniApp app)
    {
        Directory.CreateDirectory(AppDir(app.Id));
        var json = new JsonObject
        {
            ["id"] = app.Id,
            ["name"] = app.Name,
            ["installedAt"] = app.InstalledAt,
            ["permissions"] = PermissionsNode(app.Id) ?? new JsonObject()
        };
        File.WriteAllText(Path.Combine(AppDir(app.Id), "meta.json"), json.ToJsonString(IndentOpt));
    }

    public static MiniApp LoadMeta(string id)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppDir(id), "meta.json")));
            return new MiniApp
            {
                Id = json?["id"]?.ToString() ?? id,
                Name = json?["name"]?.ToString() ?? id,
                InstalledAt = json?["installedAt"]?.GetValue<long>() ?? 0L
            };
        }
        catch { return null; }
    }

    public static List<MiniApp> List()
    {
        var result = new List<MiniApp>();
        if (!Directory.Exists(AppsRoot)) return result;
        foreach (string dir in Directory.GetDirectories(AppsRoot))
        {
            var app = LoadMeta(Path.GetFileName(dir));
            if (app != null) result.Add(app);
        }
        result.Sort((a, b) => b.InstalledAt.CompareTo(a.InstalledAt)); // 新装的排前面
        return result;
    }

    // ------------------------------------------------------------ 权限

    private static JsonObject PermissionsNode(string id)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppDir(id), "meta.json")));
            return json?["permissions"] as JsonObject ?? new JsonObject();
        }
        catch { return new JsonObject(); }
    }

    public static bool HasPermission(string id, string key)
    {
        var perms = PermissionsNode(id);
        return perms[key] is JsonValue v && v.TryGetValue(out bool b) && b;
    }

    public static void SetPermission(string id, string key, bool value)
    {
        string metaPath = Path.Combine(AppDir(id), "meta.json");
        JsonObject json;
        try { json = (JsonObject)JsonNode.Parse(File.ReadAllText(metaPath)); }
        catch { json = new JsonObject(); }
        var perms = json["permissions"] as JsonObject ?? new JsonObject();
        perms[key] = value;
        json["permissions"] = perms;
        json["id"] ??= id;
        File.WriteAllText(metaPath, json.ToJsonString(IndentOpt));
    }

    // ------------------------------------------------------------ 删除

    public static void Delete(string id)
    {
        string dir = AppDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        try { File.Delete(StoreFile(id)); } catch { /* 不存在即目标状态 */ }
    }

    // ------------------------------------------------------------ 存储管理

    /// <summary>小程序总占用（HTML 目录 + JS 存储文件）。</summary>
    public static long UsageBytes(string id)
    {
        long total = DirSize(AppDir(id));
        string store = StoreFile(id);
        if (File.Exists(store)) total += new FileInfo(store).Length;
        return total;
    }

    private static long DirSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0L;
        long total = 0L;
        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; } catch { /* 文件被占用时跳过 */ }
        }
        return total;
    }

    /// <summary>清空该小程序的 JS 数据（保留 HTML 本体）。</summary>
    public static void ClearData(string id) => File.Delete(StoreFile(id));

    public static Dictionary<string, string> StoreJson(string id)
    {
        var dict = new Dictionary<string, string>();
        string file = StoreFile(id);
        try
        {
            if (!File.Exists(file)) return dict;
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject obj) return dict;
            foreach (var kv in obj)
                dict[kv.Key] = kv.Value is JsonValue v && v.TryGetValue(out string s)
                    ? s : (kv.Value?.ToJsonString(JsonOpts.Compact) ?? "");
        }
        catch { /* 数据损坏时当作空，不影响小程序本身 */ }
        return dict;
    }

    /// <summary>
    /// JS 存储指纹（协议 9.4）：按键 UTF-8 序排序后拼 "key\tvalue\n"，SHA-256 前 16 字节 hex。
    /// 两端（C#/Kotlin）逐字节一致，用于同步时快速判断数据是否相同。
    /// </summary>
    public static string StoreHash(string id)
    {
        // 协议 9.4 要求按 UTF-8 字节序排序（与 Kotlin 端逐字节一致，而非 UTF-16 序）
        var entries = StoreJson(id)
            .OrderBy(kv => kv.Key, new Utf8ByteComparer())
            .Select(kv => kv.Key + "\t" + kv.Value + "\n");
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(string.Concat(entries));
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(payload);
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static void RestoreStore(string id, string dataFile)
    {
        if (!File.Exists(dataFile)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(dataFile)) is not JsonObject obj) return;
            var store = StoreJson(id);
            foreach (var kv in obj)
            {
                if (kv.Value is JsonValue v && v.TryGetValue(out string s))
                    store[kv.Key] = s;
                else if (kv.Value is JsonObject or JsonArray)
                    store[kv.Key] = kv.Value.ToJsonString(JsonOpts.Compact); // 非字符串兜底，与手机端 optString 语义近似
            }
            SaveStore(id, store);
        }
        catch { /* 数据损坏时忽略，不影响小程序本身 */ }
    }

    /// <summary>把 JS 存储写入磁盘（store_&lt;id&gt;.json，字符串键值对）。</summary>
    public static void SaveStore(string id, Dictionary<string, string> store)
    {
        Directory.CreateDirectory(RootDir);
        var obj = new JsonObject();
        foreach (var kv in store) obj[kv.Key] = kv.Value;
        File.WriteAllText(StoreFile(id), obj.ToJsonString(IndentOpt));
    }

    // ------------------------------------------------------------ 打包导出

    /// <summary>把小程序（HTML + meta + 数据）打包写入 outZip，格式与手机端互通。</summary>
    public static void Export(string id, string outZip)
    {
        string dir = AppDir(id);
        using var fs = File.Create(outZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        AddFileEntry(zip, Path.Combine(dir, "meta.json"), META_DIR + "/meta.json");
        AddFileEntry(zip, StoreFile(id), DATA_DIR + "/store.json");
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (rel == "meta.json" || rel.StartsWith(DATA_DIR + "/")) continue;
            AddFileEntry(zip, file, rel);
        }
    }

    private static void AddFileEntry(ZipArchive zip, string srcPath, string entryName)
    {
        if (!File.Exists(srcPath)) return;
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = File.OpenRead(srcPath);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}

/// <summary>按 UTF-8 字节序比较字符串（协议 9.4 storeHash 的两端一致排序）。</summary>
internal sealed class Utf8ByteComparer : System.Collections.Generic.IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        byte[] bx = System.Text.Encoding.UTF8.GetBytes(x ?? "");
        byte[] by = System.Text.Encoding.UTF8.GetBytes(y ?? "");
        int n = Math.Min(bx.Length, by.Length);
        for (int i = 0; i < n; i++)
            if (bx[i] != by[i]) return bx[i] - by[i];
        return bx.Length - by.Length;
    }
}
