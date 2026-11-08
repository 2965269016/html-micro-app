using System.Net.Http;

namespace MiniAppBox;

/// <summary>
/// GitHub / Gitee 仓库链接与 zip 直链的解析与下载（协议第 8 节）。
/// 解析不发网络请求；默认分支解析失败时按 main→master / master→main 试探。
/// </summary>
public static class UrlImporter
{
    public sealed class RepoRef
    {
        public string Kind;            // "github" | "gitee" | "zip"
        public string User = "", Repo = "", Branch = "", SubPath = "", ZipUrl = "";
        public string DisplayName => Repo.Length > 0 ? Repo : "(zip)";
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(12)
        });
        c.Timeout = TimeSpan.FromSeconds(120);
        // GitHub 下载必须带 UA
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MiniAppBox/" + (typeof(UrlImporter).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"));
        return c;
    }

    // ------------------------------------------------------------ 解析（纯字符串，不发请求，自检覆盖）

    /// <summary>识别 github.com / gitee.com 仓库（含 tree/{branch}[/{path}]）与 *.zip 直链；不认识返回 null。</summary>
    public static RepoRef? Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")) return null;

        // zip 直链（路径以 .zip 结尾，忽略查询串）
        if (uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return new RepoRef { Kind = "zip", ZipUrl = url };

        string[] seg = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length < 2) return null;

        bool isGithub = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                        || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase);
        bool isGitee = uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.Equals("www.gitee.com", StringComparison.OrdinalIgnoreCase);
        if (!isGithub && !isGitee) return null;

        var r = new RepoRef { Kind = isGithub ? "github" : "gitee", User = seg[0], Repo = seg[1].EndsWith(".git", StringComparison.Ordinal) ? seg[1][..^4] : seg[1] };

        // /tree/{branch}[/{path...}]
        if (seg.Length >= 4 && seg[2].Equals("tree", StringComparison.OrdinalIgnoreCase))
        {
            r.Branch = seg[3];
            if (seg.Length > 4) r.SubPath = string.Join('/', seg[4..]);
        }
        return r;
    }

    // ------------------------------------------------------------ zip URL 解析（可能带网络探测）

    /// <summary>把仓库引用解析成可下载的 zip 地址；subPath 输出应用子目录（可为空）。</summary>
    public static async Task<string> ResolveZipUrlAsync(RepoRef r)
    {
        if (r.Kind == "zip") return r.ZipUrl;
        if (r.Branch.Length > 0) return BranchZip(r, r.Branch);

        // 默认分支：先问 API（短超时），失败则试探
        string? branch = null;
        try
        {
            string api = r.Kind == "github"
                ? $"https://api.github.com/repos/{r.User}/{r.Repo}"
                : $"https://gitee.com/api/v5/repos/{r.User}/{r.Repo}";
            using var resp = await Http.GetAsync(api, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
                branch = doc.RootElement.TryGetProperty("default_branch", out var b) ? b.GetString() : null;
            }
        }
        catch { /* API 不可达（国内网络常见），走试探 */ }

        foreach (string candidate in branch != null
                     ? new[] { branch }
                     : r.Kind == "github" ? new[] { "main", "master" } : new[] { "master", "main" })
        {
            string zip = BranchZip(r, candidate);
            if (await UrlExistsAsync(zip)) return zip;
        }
        throw new Exception("仓库默认分支探测失败（检查链接或网络，GitHub 直连不畅可换 Gitee 链接）");
    }

    private static string BranchZip(RepoRef r, string branch) => r.Kind switch
    {
        "github" => $"https://codeload.github.com/{r.User}/{r.Repo}/zip/refs/heads/{Uri.EscapeDataString(branch)}",
        "gitee" => $"https://gitee.com/{r.User}/{r.Repo}/repository/archive/{Uri.EscapeDataString(branch)}.zip",
        _ => throw new InvalidOperationException(r.Kind)
    };

    private static async Task<bool> UrlExistsAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode) return true;
            // 有些托管对 HEAD 返回 405，退一步用 GET 拉一个字节区间
            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var resp2 = await Http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead);
            return (int)resp2.StatusCode < 400;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ 下载与多应用识别

    public sealed class Downloaded
    {
        public List<string> Roots = new();
        public string StageDir = "";
        public string RepoName = "";
    }

    /// <summary>下载 zip 并解压到临时 stage；Roots 为含 index.html 的应用根（支持 URL 子路径/多应用仓库）。
    /// 调用方导入完成后负责 CleanupStage(StageDir)。</summary>
    public static async Task<Downloaded> DownloadAndFindRootsAsync(RepoRef r, string destPath)
    {
        string zipUrl = await ResolveZipUrlAsync(r);
        using (var resp = await Http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"下载失败：HTTP {(int)resp.StatusCode}（{zipUrl}）");
            await using var fs = File.Create(destPath);
            await resp.Content.CopyToAsync(fs);
        }

        var result = new Downloaded { RepoName = r.DisplayName };
        string stage = Path.Combine(Path.GetTempPath(), "miniappbox_url_" + Guid.NewGuid().ToString("N"));
        result.StageDir = stage;
        try
        {
            // 复用 AppRepo 的解压（zip-slip 防护一致）
            AppRepo.Unzip(destPath, stage);

            var roots = FindAppRoots(stage);
            if (r.SubPath.Length > 0)
            {
                string sub = FindSubRoot(stage, r.SubPath);
                roots = sub != null ? new List<string> { sub } : throw new Exception($"仓库里找不到子目录 {r.SubPath}（或其中没有 index.html）");
            }
            result.Roots = roots;
            if (roots.Count == 0)
                throw new Exception("仓库里没有找到含 index.html 的目录（既不在根目录，也没有任何子目录包含）");
            return result;
        }
        catch
        {
            try { Directory.Delete(stage, true); } catch { }
            throw;
        }
    }

    /// <summary>扫描目录树（深度 ≤3，跳过点目录/保留目录），返回含 index.html 的目录列表。</summary>
    public static List<string> FindAppRoots(string dir)
    {
        var result = new List<string>();
        if (File.Exists(Path.Combine(dir, "index.html"))) { result.Add(dir); return result; }
        Scan(dir, 0, result);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static void Scan(string dir, int depth, List<string> result)
    {
        if (depth > 3 || result.Count > 64) return;
        string name = Path.GetFileName(dir) ?? "";
        if (depth > 0 && (name.StartsWith('.') || name is "__data__" or "__meta__" or "node_modules")) return;
        if (File.Exists(Path.Combine(dir, "index.html"))) { result.Add(dir); return; }
        try
        {
            foreach (string sub in Directory.GetDirectories(dir)) Scan(sub, depth + 1, result);
        }
        catch { /* 无权限目录跳过 */ }
    }

    private static string? FindSubRoot(string stage, string subPath)
    {
        string p = Path.GetFullPath(Path.Combine(new[] { stage }.Concat(subPath.Split('/')).ToArray()));
        if (!p.StartsWith(Path.GetFullPath(stage), StringComparison.Ordinal)) return null;
        if (File.Exists(Path.Combine(p, "index.html"))) return p;
        return null;
    }

    /// <summary>删除下载/解压产生的临时目录。</summary>
    public static void CleanupStage(string stageRoot) => Directory.Delete(stageRoot, true);
}
