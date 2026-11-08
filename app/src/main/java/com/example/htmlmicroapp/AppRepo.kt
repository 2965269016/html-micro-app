package com.example.htmlmicroapp

import android.content.Context
import android.net.Uri
import org.json.JSONObject
import java.io.File
import java.io.InputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

/**
 * 小程序仓库：HTML 小程序包的导入、列表、删除、权限、存储与打包。
 *
 * 目录布局：
 *   filesDir/apps/<id>/            小程序 HTML 文件（index.html 必须在根）
 *   filesDir/apps/<id>/meta.json   {"id","name","installedAt","permissions":{...}}
 *   shared_prefs/store_<id>.xml    该小程序的 JS 存储（MiniAppBridge 使用）
 *
 * 打包导出格式（zip）：
 *   <HTML 文件...>
 *   __meta__/meta.json
 *   __data__/store.json            {"key":"value",...}（无数据时省略）
 */
object AppRepo {

    data class MiniApp(
        val id: String,
        val name: String,
        val installedAt: Long
    )

    const val PERM_NOTIFICATION = "notification"
    const val PERM_VIBRATE = "vibrate"
    const val PERM_CLIPBOARD = "clipboard"
    const val PERM_GEOLOCATION = "geolocation"
    val ALL_PERMS = listOf(PERM_NOTIFICATION, PERM_VIBRATE, PERM_CLIPBOARD, PERM_GEOLOCATION)

    private const val DATA_DIR = "__data__"
    private const val META_DIR = "__meta__"

    class ImportException(message: String) : Exception(message)

    // ------------------------------------------------------------------ 基础路径

    fun appsRoot(context: Context): File = File(context.filesDir, "apps")

    fun appDir(context: Context, id: String): File = File(appsRoot(context), id)

    fun storePrefs(context: Context, id: String) =
        context.getSharedPreferences("store_$id", Context.MODE_PRIVATE)

    fun storePrefsFile(context: Context, id: String): File? {
        val parent = context.filesDir.parentFile ?: return null
        return File(parent, "shared_prefs/store_$id.xml")
    }

    // ------------------------------------------------------------------ 导入

    /** 从 SAF Uri 导入一个 HTML 小程序包（zip）。 */
    fun import(context: Context, uri: Uri, fallbackName: String): MiniApp {
        val tmp = File(context.cacheDir, "import_${System.currentTimeMillis()}.zip")
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                tmp.outputStream().use { input.copyTo(it) }
            } ?: throw ImportException("无法读取所选文件")
            return importZip(context, tmp, fallbackName)
        } finally {
            tmp.delete()
        }
    }

    /** 从任意输入流导入（用于内置示例）。 */
    fun importStream(context: Context, input: InputStream, fallbackName: String): MiniApp {
        val tmp = File(context.cacheDir, "import_${System.currentTimeMillis()}.zip")
        try {
            tmp.outputStream().use { input.copyTo(it) }
            return importZip(context, tmp, fallbackName)
        } finally {
            tmp.delete()
        }
    }

    private fun importZip(context: Context, zipFile: File, fallbackName: String): MiniApp {
        val stage = File(context.cacheDir, "stage_${System.currentTimeMillis()}")
        try {
            unzip(zipFile, stage)
            normalizeRoot(stage)
            if (!File(stage, "index.html").isFile) {
                throw ImportException("包内根目录缺少 index.html")
            }

            val id = "app_" + SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            val dest = appDir(context, id)
            if (!stage.renameTo(dest)) stage.copyRecursively(dest, overwrite = true)

            // 还原随包数据（如果有）
            restoreStore(context, id, File(dest, "$DATA_DIR/store.json"))

            val name = readManifestName(File(dest, "manifest.json"))
                ?: fallbackName.removeSuffix(".zip").removeSuffix(".ZIP").ifBlank { "未命名小程序" }
            val meta = MiniApp(id, name, System.currentTimeMillis())
            saveMeta(context, meta)
            return meta
        } finally {
            stage.deleteRecursively()
        }
    }

    internal fun unzip(zipFile: File, destDir: File) {
        destDir.mkdirs()
        try {
            ZipInputStream(zipFile.inputStream().buffered()).use { zis ->
                var entry: ZipEntry? = zis.nextEntry
                while (entry != null) {
                    val name = entry.name.replace("\\", "/")
                    if (!name.contains("..")) { // zip-slip 防护
                        val outFile = File(destDir, name)
                        if (entry.isDirectory) {
                            outFile.mkdirs()
                        } else {
                            outFile.parentFile?.mkdirs()
                            outFile.outputStream().use { zis.copyTo(it) }
                        }
                    }
                    zis.closeEntry()
                    entry = zis.nextEntry
                }
            }
        } catch (e: ImportException) {
            throw e
        } catch (e: Exception) {
            throw ImportException("解压失败：${e.message}")
        }
    }

    /** 若解压后只有一个顶层目录（且没有根级 index.html），把该目录内容上移一层。 */
    private fun normalizeRoot(stage: File) {
        if (File(stage, "index.html").isFile) return
        val children = stage.listFiles() ?: return
        val dirs = children.filter { it.isDirectory }
        val files = children.filter { it.isFile }
        if (dirs.size == 1 && files.isEmpty()) {
            val inner = dirs[0]
            inner.listFiles()?.forEach { it.renameTo(File(stage, it.name)) }
            inner.delete()
        }
    }

    private fun readManifestName(manifest: File): String? = try {
        if (manifest.isFile) JSONObject(manifest.readText()).optString("name").ifBlank { null } else null
    } catch (_: Exception) { null }

    // ------------------------------------------------------------------ 元数据

    fun saveMeta(context: Context, app: MiniApp) {
        val json = JSONObject()
            .put("id", app.id)
            .put("name", app.name)
            .put("installedAt", app.installedAt)
            .put("permissions", permissions(context, app.id))
        File(appDir(context, app.id), "meta.json").writeText(json.toString(2))
    }

    fun loadMeta(context: Context, id: String): MiniApp? = try {
        val json = JSONObject(File(appDir(context, id), "meta.json").readText())
        MiniApp(
            id = json.optString("id", id),
            name = json.optString("name", id),
            installedAt = json.optLong("installedAt", 0L)
        )
    } catch (_: Exception) { null }

    fun list(context: Context): List<MiniApp> =
        appsRoot(context).listFiles { f -> f.isDirectory }
            ?.mapNotNull { loadMeta(context, it.name) }
            ?.sortedByDescending { it.installedAt }
            ?: emptyList()

    // ------------------------------------------------------------------ 权限

    fun permissions(context: Context, id: String): JSONObject {
        val meta = File(appDir(context, id), "meta.json")
        return try {
            JSONObject(meta.readText()).optJSONObject("permissions") ?: JSONObject()
        } catch (_: Exception) { JSONObject() }
    }

    fun hasPermission(context: Context, id: String, key: String): Boolean =
        permissions(context, id).optBoolean(key, false)

    fun setPermission(context: Context, id: String, key: String, value: Boolean) {
        val metaFile = File(appDir(context, id), "meta.json")
        val json = try { JSONObject(metaFile.readText()) } catch (_: Exception) { JSONObject() }
        val perms = json.optJSONObject("permissions") ?: JSONObject()
        perms.put(key, value)
        json.put("permissions", perms)
        metaFile.writeText(json.toString(2))
    }

    // ------------------------------------------------------------------ 删除

    fun delete(context: Context, id: String) {
        appDir(context, id).deleteRecursively()
        storePrefs(context, id).edit().clear().commit()
    }

    // ------------------------------------------------------------------ 存储管理

    /** 小程序总占用（HTML 目录 + JS 存储文件）。 */
    fun usageBytes(context: Context, id: String): Long {
        var total = dirSize(appDir(context, id))
        storePrefsFile(context, id)?.let { total += it.length() }
        return total
    }

    private fun dirSize(dir: File): Long {
        if (!dir.exists()) return 0L
        return dir.walkTopDown().filter { it.isFile }.sumOf { it.length() }
    }

    fun clearData(context: Context, id: String) {
        storePrefs(context, id).edit().clear().commit()
    }

    fun storeJson(context: Context, id: String): JSONObject {
        val json = JSONObject()
        storePrefs(context, id).all.forEach { (k, v) -> json.put(k, v?.toString() ?: "") }
        return json
    }

    private fun restoreStore(context: Context, id: String, dataFile: File) {
        if (!dataFile.isFile) return
        try {
            val json = JSONObject(dataFile.readText())
            val editor = storePrefs(context, id).edit()
            json.keys().forEach { k -> editor.putString(k, json.optString(k)) }
            editor.commit()
        } catch (_: Exception) { /* 数据损坏时忽略，不影响小程序本身 */ }
    }

    /** 把一个已解压的目录导入为小程序（URL 导入的多应用仓库用）。 */
    fun importDirectory(context: Context, srcDir: File, fallbackName: String): MiniApp {
        val stage = File(context.cacheDir, "dirimport_${System.currentTimeMillis()}")
        try {
            srcDir.copyRecursively(stage, overwrite = true)
            normalizeRoot(stage)
            if (!File(stage, "index.html").isFile) throw ImportException("目录缺少 index.html")
            val id = "app_" + SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            val dest = appDir(context, id)
            if (dest.exists()) dest.deleteRecursively()
            if (!stage.renameTo(dest)) stage.copyRecursively(dest, overwrite = true)
            restoreStore(context, id, File(dest, "$DATA_DIR/store.json"))
            val name = readManifestName(File(dest, "manifest.json"))
                ?: fallbackName.removeSuffix(".zip").removeSuffix(".ZIP").ifBlank { "未命名小程序" }
            val meta = MiniApp(id, name, System.currentTimeMillis())
            saveMeta(context, meta)
            return meta
        } finally {
            stage.deleteRecursively()
        }
    }

    // ------------------------------------------------------------------ 同步（协议 9.3 / 9.4）

    /**
     * 同步安装：沿用发送端的 expectedId（与普通导入不同）。
     * id 已存在则替换 HTML 目录；store 与 meta 采用包内数据。
     */
    fun installSynced(context: Context, zipFile: File, expectedId: String): MiniApp {
        val stage = File(context.cacheDir, "sync_" + System.currentTimeMillis())
        try {
            unzip(zipFile, stage)
            normalizeRoot(stage)
            if (!File(stage, "index.html").isFile) throw ImportException("同步包内缺少 index.html")

            val dest = appDir(context, expectedId)
            dest.parentFile?.mkdirs()
            if (dest.exists()) dest.deleteRecursively()
            if (!stage.renameTo(dest)) stage.copyRecursively(dest, overwrite = true)

            // meta：优先包内 __meta__/meta.json（含权限开关）
            val metaSrc = File(dest, "$META_DIR/meta.json")
            val app: MiniApp = if (metaSrc.isFile) {
                try {
                    val json = JSONObject(metaSrc.readText())
                    MiniApp(
                        id = expectedId,
                        name = json.optString("name").ifBlank { expectedId },
                        installedAt = json.optLong("installedAt", 0L)
                    )
                } catch (_: Exception) {
                    MiniApp(expectedId, expectedId, 0L)
                }
            } else {
                MiniApp(expectedId, readManifestName(File(dest, "manifest.json")) ?: expectedId, 0L)
            }
            saveMeta(context, app.copy(installedAt = if (app.installedAt > 0) app.installedAt else System.currentTimeMillis()))

            // store 采用包内数据（同步 = 完整采纳发送端数据）
            clearData(context, expectedId)
            restoreStore(context, expectedId, File(dest, "$DATA_DIR/store.json"))
            return app
        } finally {
            stage.deleteRecursively()
        }
    }

    /**
     * JS 存储指纹（协议 9.4）：键按 UTF-8 字节序排序后拼 "key\tvalue\n"，
     * SHA-256 取前 16 字节 hex。与电脑端 C# 实现逐字节一致。
     */
    fun storeHash(context: Context, id: String): String {
        val prefs = storePrefs(context, id)
        val sorted = prefs.all.keys.sortedWith { a, b ->
            val ba = a.toByteArray(Charsets.UTF_8)
            val bb = b.toByteArray(Charsets.UTF_8)
            val n = minOf(ba.size, bb.size)
            for (i in 0 until n) {
                val x = ba[i].toInt() and 0xFF
                val y = bb[i].toInt() and 0xFF
                if (x != y) return@sortedWith x - y
            }
            ba.size - bb.size
        }
        val sb = StringBuilder()
        for (key in sorted) sb.append(key).append('\t').append(prefs.getString(key, "") ?: "").append('\n')
        val digest = java.security.MessageDigest.getInstance("SHA-256").digest(sb.toString().toByteArray(Charsets.UTF_8))
        return digest.take(16).joinToString("") { "%02x".format(it) }
    }

    // ------------------------------------------------------------------ 打包导出

    /** 把小程序（HTML + meta + 数据）打包写入 output。 */
    fun export(context: Context, id: String, output: java.io.OutputStream) {
        val dir = appDir(context, id)
        ZipOutputStream(output.buffered()).use { zos ->
            // HTML 及包内文件（跳过 meta.json 与 __data__，末尾统一写）
            dir.walkTopDown().filter { it.isFile }.forEach { f ->
                val rel = f.relativeTo(dir).invariantSeparatorsPath
                if (rel == "meta.json" || rel.startsWith("$DATA_DIR/")) return@forEach
                putEntry(zos, rel, f.inputStream())
            }
            // meta
            putEntry(zos, "$META_DIR/meta.json", File(dir, "meta.json").inputStream())
            // 数据
            val data = storeJson(context, id).toString(2).toByteArray(Charsets.UTF_8)
            zos.putNextEntry(ZipEntry("$DATA_DIR/store.json"))
            zos.write(data)
            zos.closeEntry()
        }
    }

    private fun putEntry(zos: ZipOutputStream, name: String, input: InputStream) {
        zos.putNextEntry(ZipEntry(name))
        input.use { it.copyTo(zos) }
        zos.closeEntry()
    }
}
