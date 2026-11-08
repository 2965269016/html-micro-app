package com.example.htmlmicroapp

import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * GitHub / Gitee 仓库链接与 zip 直链的解析与下载（互通协议第 8 节）。
 * 与电脑端 pc/UrlImporter.cs 行为一致；全部方法为阻塞 IO，调用方自行放线程。
 */
object UrlImporter {

    class RepoRef(
        val kind: String,          // "github" | "gitee" | "zip"
        val user: String = "",
        val repo: String = "",
        var branch: String = "",
        var subPath: String = "",
        val zipUrl: String = ""
    ) {
        val displayName: String get() = repo.ifBlank { "(zip)" }
    }

    /** 识别 github.com / gitee.com 仓库（含 tree/{branch}[/{path}]）与 *.zip 直链；不认识返回 null。 */
    fun parse(url: String): RepoRef? {
        val u = url.trim()
        if (u.isBlank()) return null
        val parsed = try { URL(u) } catch (_: Exception) { return null }
        if (parsed.protocol != "http" && parsed.protocol != "https") return null

        val path = parsed.path ?: ""
        if (path.endsWith(".zip", ignoreCase = true)) return RepoRef(kind = "zip", zipUrl = u)

        val seg = path.trim('/').split('/').filter { it.isNotEmpty() }
        if (seg.size < 2) return null
        val host = parsed.host?.lowercase() ?: return null
        val isGithub = host == "github.com" || host == "www.github.com"
        val isGitee = host == "gitee.com" || host == "www.gitee.com"
        if (!isGithub && !isGitee) return null

        val repoName = if (seg[1].endsWith(".git")) seg[1].dropLast(4) else seg[1]
        val ref = RepoRef(kind = if (isGithub) "github" else "gitee", user = seg[0], repo = repoName)
        if (seg.size >= 4 && seg[2].equals("tree", ignoreCase = true)) {
            ref.branch = seg[3]
            if (seg.size > 4) ref.subPath = seg.drop(4).joinToString("/")
        }
        return ref
    }

    /** 把仓库引用解析成可下载的 zip 地址（可能探测网络）。 */
    fun resolveZipUrl(ref: RepoRef): String {
        if (ref.kind == "zip") return ref.zipUrl
        if (ref.branch.isNotEmpty()) return branchZip(ref, ref.branch)

        // 默认分支：先问 API（短超时），失败则试探
        var branch: String? = null
        try {
            val api = if (ref.kind == "github")
                "https://api.github.com/repos/${ref.user}/${ref.repo}"
            else
                "https://gitee.com/api/v5/repos/${ref.user}/${ref.repo}"
            val body = httpGet(api, connectTimeoutMs = 4000, readTimeoutMs = 4000)
            if (body != null) branch = JSONObject(body).optString("default_branch").ifBlank { null }
        } catch (_: Exception) {
        }
        val candidates = when {
            branch != null -> listOf(branch)
            ref.kind == "github" -> listOf("main", "master")
            else -> listOf("master", "main")
        }
        for (c in candidates) {
            val zip = branchZip(ref, c)
            if (urlExists(zip)) return zip
        }
        throw Exception("仓库默认分支探测失败（检查链接或网络，GitHub 直连不畅可换 Gitee 链接）")
    }

    private fun branchZip(ref: RepoRef, branch: String): String = when (ref.kind) {
        "github" -> "https://codeload.github.com/${ref.user}/${ref.repo}/zip/refs/heads/${URLEncoder.encode(branch, "UTF-8")}"
        "gitee" -> "https://gitee.com/${ref.user}/${ref.repo}/repository/archive/${URLEncoder.encode(branch, "UTF-8")}.zip"
        else -> throw IllegalArgumentException(ref.kind)
    }

    /** 下载 zip 并解压到临时 stage；返回含 index.html 的应用根目录列表（0/1/多个）。 */
    fun downloadAndFindRoots(ref: RepoRef, destZip: File): Pair<List<File>, File> {
        val zipUrl = resolveZipUrl(ref)
        httpDownload(zipUrl, destZip)

        val stage = File(destZip.parentFile, "stage_" + System.currentTimeMillis())
        try {
            AppRepo.unzip(destZip, stage)
            var roots = findAppRoots(stage)
            if (ref.subPath.isNotEmpty()) {
                val sub = File(stage, ref.subPath.replace('/', File.separatorChar))
                val stagePath = stage.canonicalPath + File.separator
                val subPath = sub.canonicalPath
                if (!subPath.startsWith(stagePath) || !File(sub, "index.html").isFile)
                    throw Exception("仓库里找不到子目录 ${ref.subPath}（或其中没有 index.html）")
                roots = listOf(sub)
            }
            if (roots.isEmpty()) throw Exception("仓库里没有找到含 index.html 的目录")
            return Pair(roots, stage)
        } catch (e: Exception) {
            stage.deleteRecursively()
            throw e
        }
    }

    /** 扫描目录树（深度 ≤3，跳过点目录/保留目录），返回含 index.html 的目录。 */
    fun findAppRoots(dir: File): List<File> {
        val result = mutableListOf<File>()
        if (File(dir, "index.html").isFile) return listOf(dir)
        scan(dir, 0, result)
        result.sortBy { it.absolutePath }
        return result
    }

    private fun scan(dir: File, depth: Int, result: MutableList<File>) {
        if (depth > 3 || result.size > 64) return
        if (depth > 0) {
            val name = dir.name
            if (name.startsWith(".") || name == "__data__" || name == "__meta__" || name == "node_modules") return
        }
        if (File(dir, "index.html").isFile) {
            result.add(dir)
            return
        }
        val children = dir.listFiles() ?: return
        for (child in children) if (child.isDirectory) scan(child, depth + 1, result)
    }

    // ------------------------------------------------------------ HTTP 基础

    private fun open(url: String, connectTimeoutMs: Int, readTimeoutMs: Int): HttpURLConnection {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = connectTimeoutMs
        conn.readTimeout = readTimeoutMs
        conn.instanceFollowRedirects = true
        conn.setRequestProperty("User-Agent", "MiniAppBox-Android/0.2.0")
        return conn
    }

    private fun httpGet(url: String, connectTimeoutMs: Int = 12000, readTimeoutMs: Int = 120000): String? {
        val conn = open(url, connectTimeoutMs, readTimeoutMs)
        return try {
            if (conn.responseCode !in 200..299) null
            else conn.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    private fun urlExists(url: String): Boolean {
        // 先 HEAD，个别托管（如 Gitee archive）不支持时退一步 GET 一个字节区间
        if (probe(url, "HEAD")) return true
        return probe(url, "GET", rangeOnly = true)
    }

    private fun probe(url: String, method: String, rangeOnly: Boolean = false): Boolean = try {
        val conn = open(url, 6000, 6000)
        conn.requestMethod = method
        if (rangeOnly) conn.setRequestProperty("Range", "bytes=0-0")
        val ok = (conn.responseCode in 200..299) || (rangeOnly && conn.responseCode == 206)
        conn.disconnect()
        ok
    } catch (_: Exception) {
        false
    }

    private fun httpDownload(url: String, dest: File) {
        val conn = open(url, 12000, 120000)
        try {
            if (conn.responseCode !in 200..299)
                throw Exception("下载失败：HTTP ${conn.responseCode}（$url）")
            dest.parentFile?.mkdirs()
            conn.inputStream.use { input ->
                dest.outputStream().use { output -> input.copyTo(output) }
            }
        } finally {
            conn.disconnect()
        }
    }
}
