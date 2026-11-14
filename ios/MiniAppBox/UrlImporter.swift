//
//  UrlImporter.swift
//  MiniAppBox
//
//  GitHub / Gitee 仓库链接与 zip 直链的解析、下载、应用根扫描
//  （互通协议第 8 节；与 Android UrlImporter.kt / PC UrlImporter.cs 行为一致）。
//

import Foundation

struct RepoRef {
    let kind: String          // "github" | "gitee" | "zip"
    var user: String = ""
    var repo: String = ""
    var branch: String = ""
    var subPath: String = ""
    let zipUrl: String

    var displayName: String { repo.isEmpty ? "(zip)" : repo }
}

enum UrlError: LocalizedError {
    case unrecognized
    case branchProbeFailed
    case downloadFailed(Int, String)
    case noAppFound
    case subPathNotFound(String)

    var errorDescription: String? {
        switch self {
        case .unrecognized:
            return "支持：github.com/用户/仓库、gitee.com/用户/仓库（可带 /tree/分支/子目录），或 .zip 直链。"
        case .branchProbeFailed:
            return "仓库默认分支探测失败（检查链接或网络，GitHub 直连不畅可换 Gitee 链接）"
        case .downloadFailed(let code, let url):
            return "下载失败：HTTP \(code)（\(url)）"
        case .noAppFound:
            return "仓库里没有找到含 index.html 的目录"
        case .subPathNotFound(let p):
            return "仓库里找不到子目录 \(p)（或其中没有 index.html）"
        }
    }
}

// MARK: - 候选应用（多应用仓库扫描结果）

struct RepoCandidate: Identifiable {
    let path: String          // 仓库内相对目录（"" = 整个仓库根）
    let entries: [ZipEntryData]
    var id: String { path }

    /// 显示名：manifest.json 的 name > 目录名
    var suggestedName: String {
        if let manifest = entries.first(where: { $0.path == "manifest.json" }),
           let json = try? JSONSerialization.jsonObject(with: manifest.data) as? [String: Any],
           let n = json["name"] as? String, !n.isEmpty {
            return n
        }
        return path.split(separator: "/").last.map(String.init) ?? "(root)"
    }
}

// MARK: - 解析 / 下载

enum UrlImporter {

    static let skippedDirNames: Set<String> = ["__data__", "__meta__", "node_modules"]

    /// 识别 github.com / gitee.com 仓库（含 tree/{branch}[/{path}]）与 *.zip 直链；不认识返回 nil。
    static func parse(_ raw: String) -> RepoRef? {
        let u = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !u.isEmpty, let parsed = URL(string: u),
              let scheme = parsed.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else { return nil }

        let path = parsed.path
        if path.lowercased().hasSuffix(".zip") {
            return RepoRef(kind: "zip", zipUrl: u)
        }

        let seg = path.split(separator: "/").map(String.init).filter { !$0.isEmpty }
        guard seg.count >= 2, let host = parsed.host?.lowercased() else { return nil }
        let isGithub = host == "github.com" || host == "www.github.com"
        let isGitee = host == "gitee.com" || host == "www.gitee.com"
        guard isGithub || isGitee else { return nil }

        var repoName = seg[1]
        if repoName.hasSuffix(".git") { repoName = String(repoName.dropLast(4)) }
        var ref = RepoRef(kind: isGithub ? "github" : "gitee",
                          user: seg[0], repo: repoName, zipUrl: "")
        if seg.count >= 4 && seg[2].lowercased() == "tree" {
            ref.branch = seg[3]
            if seg.count > 4 { ref.subPath = seg.drop(4).joined(separator: "/") }
        }
        return ref
    }

    /// 把仓库引用解析成可下载的 zip 地址（可能探测默认分支，阻塞网络，请在 Task 里调用）。
    static func resolveZipURL(_ ref: RepoRef) async throws -> URL {
        if ref.kind == "zip" { return URL(string: ref.zipUrl)! }

        if !ref.branch.isEmpty { return branchZipURL(ref, ref.branch) }

        // 默认分支：先问 API（短超时），失败则按序试探
        var branch: String?
        let api = ref.kind == "github"
            ? "https://api.github.com/repos/\(ref.user)/\(ref.repo)"
            : "https://gitee.com/api/v5/repos/\(ref.user)/\(ref.repo)"
        if let data = try? await httpData(URL(string: api)!, timeout: 4),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let b = json["default_branch"] as? String, !b.isEmpty {
            branch = b
        }

        let candidates: [String]
        if let branch { candidates = [branch] }
        else if ref.kind == "github" { candidates = ["main", "master"] }
        else { candidates = ["master", "main"] }

        for c in candidates {
            let zip = branchZipURL(ref, c)
            if await urlExists(zip) { return zip }
        }
        throw UrlError.branchProbeFailed
    }

    static func branchZipURL(_ ref: RepoRef, _ branch: String) -> URL {
        let enc = branch.addingPercentEncoding(
            withAllowedCharacters: .urlQueryAllowed) ?? branch
        switch ref.kind {
        case "github":
            return URL(string: "https://codeload.github.com/\(ref.user)/\(ref.repo)/zip/refs/heads/\(enc)")!
        case "gitee":
            return URL(string: "https://gitee.com/\(ref.user)/\(ref.repo)/repository/archive/\(enc).zip")!
        default:
            return URL(string: ref.zipUrl)!
        }
    }

    /// 下载 zip 数据。
    static func download(_ url: URL) async throws -> Data {
        guard let (response, data) = try? await httpDataAllowLarge(url, timeout: 120) else {
            throw UrlError.downloadFailed(-1, url.absoluteString)
        }
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200...299).contains(code) else {
            throw UrlError.downloadFailed(code, url.absoluteString)
        }
        return data
    }

    // MARK: - 应用根扫描（对齐 Android findAppRoots：深度 ≤3、跳过点目录/保留目录、命中不再下钻）

    /// 从解压条目中扫描候选应用根；返回 1 个 = 直接导入，多个 = 用户挑选。
    static func candidates(in entries: [ZipEntryData], subPath: String) throws -> [RepoCandidate] {
        var items = entries
        if !subPath.isEmpty {
            let prefix = subPath + "/"
            items = items.compactMap { e in
                guard e.path.hasPrefix(prefix) else { return nil }
                return ZipEntryData(path: String(e.path.dropFirst(prefix.count)), data: e.data)
            }
            guard items.contains(where: { $0.path == "index.html" }) else {
                throw UrlError.subPathNotFound(subPath)
            }
        }

        // 目录 -> 是否直接含 index.html
        var dirsContainingIndex = Set<String>()
        for e in items {
            if let slash = e.path.lastIndex(of: "/") {
                let dir = String(e.path[..<slash])
                if e.path == dir + "/index.html" { dirsContainingIndex.insert(dir) }
            } else if e.path == "index.html" {
                dirsContainingIndex.insert("")   // 仓库根
            }
        }

        // 候选目录：深度 ≤3、不含点目录/保留目录段
        var candidates = dirsContainingIndex.filter { dir in
            let segs = dir.split(separator: "/").map(String.init)
            if segs.count > 3 { return false }
            return !segs.contains { $0.hasPrefix(".") || Self.skippedDirNames.contains($0) }
        }

        // 根目录本身就是应用 → 整仓一个
        if candidates.contains("") {
            return [RepoCandidate(path: "", entries: filtered(entries: items, root: ""))]
        }

        // 命中的目录不再下钻：剔除作为其他候选后代/祖先的冗余，按路径排序
        candidates.sort()
        var kept: [String] = []
        for c in candidates {
            if let parent = kept.last, c.hasPrefix(parent + "/") { continue } // 祖先已命中
            kept.append(c)
        }

        guard !kept.isEmpty else { throw UrlError.noAppFound }
        return kept.map { root in
            RepoCandidate(path: root, entries: filtered(entries: items, root: root))
        }
    }

    /// 取以 root 为根的子树条目（root="" 时返回全部）。
    static func filtered(entries: [ZipEntryData], root: String) -> [ZipEntryData] {
        guard !root.isEmpty else { return entries }
        let prefix = root + "/"
        return entries.compactMap { e in
            guard e.path.hasPrefix(prefix) else { return nil }
            return ZipEntryData(path: String(e.path.dropFirst(prefix.count)), data: e.data)
        }
    }

    // MARK: - HTTP 基础

    private static func httpData(_ url: URL, timeout: TimeInterval) async throws -> Data? {
        var req = URLRequest(url: url, timeoutInterval: timeout)
        req.setValue("MiniAppBox-iOS/0.1.2", forHTTPHeaderField: "User-Agent")
        let (data, _) = try await URLSession.shared.data(for: req)
        return data
    }

    private static func httpDataAllowLarge(_ url: URL, timeout: TimeInterval) async throws -> (URLResponse, Data)? {
        var req = URLRequest(url: url, timeoutInterval: timeout)
        req.setValue("MiniAppBox-iOS/0.1.2", forHTTPHeaderField: "User-Agent")
        return try? await URLSession.shared.data(for: req)
    }

    /// HEAD 探测，个别托管（如 Gitee archive）不支持时退一步 GET 一个字节区间。
    static func urlExists(_ url: URL) async -> Bool {
        if await probe(url, method: "HEAD") { return true }
        return await probe(url, method: "GET", rangeOnly: true)
    }

    private static func probe(_ url: URL, method: String, rangeOnly: Bool = false) async -> Bool {
        var req = URLRequest(url: url, timeoutInterval: 6)
        req.httpMethod = method
        req.setValue("MiniAppBox-iOS/0.1.2", forHTTPHeaderField: "User-Agent")
        if rangeOnly { req.setValue("bytes=0-0", forHTTPHeaderField: "Range") }
        guard let (_, resp) = try? await URLSession.shared.data(for: req) else { return false }
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        return (200...299).contains(code) || (rangeOnly && code == 206)
    }
}
