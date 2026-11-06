package com.example.htmlmicroapp

import android.app.Activity
import android.content.Intent
import android.content.pm.ShortcutInfo
import android.os.Bundle
import android.util.Log
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
import androidx.core.content.getSystemService
import androidx.core.graphics.drawable.toBitmap
import androidx.webkit.WebViewAssetLoader
import java.io.File

/**
 * 离线 HTML 微应用容器主界面。
 *
 * 启动流程：
 *  1. 把 assets/html_app.zip 解压到 filesDir/html_app/
 *  2. 用 WebViewAssetLoader 把该目录映射到 https://appassets.androidplatform.net/
 *  3. WebView 加载 index.html，本地文件以"网络请求"的形式被安全拦截返回
 *  4. 注入 LocalStore 作为 JS 本地存储桥
 *  5. 首次启动时创建桌面快捷方式（无图标资源则用应用名首字生成占位图标）
 */
class MainActivity : Activity() {

    private lateinit var webView: WebView
    private lateinit var localStore: LocalStore
    private var extractedAppDir: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        webView = findViewById(R.id.webview)
        localStore = LocalStore(this)

        initWebView()
        extractHtmlApp()
        createHomeShortcutIfNeeded()
    }

    private fun initWebView() {
        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.allowFileAccess = true
        webView.settings.allowContentAccess = true

        val assetLoader = WebViewAssetLoader.Builder(this)
            .addPathHandler("/") { path ->
                val dir = extractedAppDir ?: return@addPathHandler null
                val file = File(dir, path)
                if (!file.exists() || !file.isFile) return@addPathHandler null

                val mime = when (file.extension.lowercase()) {
                    "html", "htm" -> "text/html"
                    "css" -> "text/css"
                    "js", "mjs" -> "application/javascript"
                    "json" -> "application/json"
                    "png" -> "image/png"
                    "jpg", "jpeg" -> "image/jpeg"
                    "gif" -> "image/gif"
                    "webp" -> "image/webp"
                    "svg" -> "image/svg+xml"
                    "ico" -> "image/x-icon"
                    "txt" -> "text/plain"
                    "woff" -> "font/woff"
                    "woff2" -> "font/woff2"
                    "ttf" -> "font/ttf"
                    else -> "application/octet-stream"
                }
                WebResourceResponse(mime, null, file.inputStream())
            }
            .build()

        webView.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(
                view: WebView,
                request: WebResourceRequest
            ): WebResourceResponse? = assetLoader.shouldInterceptRequest(request.url)

            @Suppress("DEPRECATION")
            override fun onReceivedError(
                view: WebView,
                request: WebResourceRequest,
                error: android.webkit.WebResourceError
            ) {
                if (request.isForMainFrame) {
                    view.loadData(
                        "<h2>页面加载失败 (${error.errorCode})</h2>" +
                                "<p>请确认 html_app.zip 已放入 app/src/main/assets/ 目录，" +
                                "且 zip 根目录有 index.html。</p>",
                        "text/html",
                        "UTF-8"
                    )
                }
            }
        }

        webView.addJavascriptInterface(localStore, "LocalStore")
    }

    private fun extractHtmlApp() {
        try {
            val dest = File(filesDir, "html_app")
            if (dest.exists()) {
                extractedAppDir = dest.absolutePath
                webView.loadUrl(APP_URL)
                return
            }

            assets.open(ASSET_ZIP).use { input ->
                java.util.zip.ZipInputStream(input).use { zis ->
                    var entry = zis.nextEntry
                    while (entry != null) {
                        // 过滤掉 ../ 之类的路径穿越条目，防止 zip slip
                        val name = entry.name.replace("\\", "/")
                        if (name.contains("..")) {
                            zis.closeEntry()
                            entry = zis.nextEntry
                            continue
                        }
                        val outFile = File(dest, name)
                        if (entry.isDirectory) {
                            outFile.mkdirs()
                        } else {
                            outFile.parentFile?.mkdirs()
                            outFile.outputStream().use { zis.copyTo(it) }
                        }
                        zis.closeEntry()
                        entry = zis.nextEntry
                    }
                }
            }

            extractedAppDir = dest.absolutePath
            webView.loadUrl(APP_URL)
        } catch (e: Exception) {
            Log.e(TAG, "Failed to extract HTML app", e)
            Toast.makeText(this, "应用加载失败：${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    /**
     * 只在第一次启动时请求创建桌面快捷方式，避免每次打开都弹确认框。
     */
    private fun createHomeShortcutIfNeeded() {
        val prefs = getSharedPreferences(PREFS, MODE_PRIVATE)
        if (prefs.getBoolean(KEY_SHORTCUT_DONE, false)) return

        val shortcutManager = getSystemService<android.app.ShortcutManager>()
        if (shortcutManager == null || !shortcutManager.isRequestPinShortcutSupported) {
            // 桌面不支持时也标记完成，不再重复尝试
            prefs.edit().putBoolean(KEY_SHORTCUT_DONE, true).apply()
            Toast.makeText(this, "当前桌面不支持固定快捷方式", Toast.LENGTH_SHORT).show()
            return
        }

        val appName = getString(R.string.app_name)
        // 优先用应用自身图标；没有可用图标资源时，用「应用名首字 + 彩色背景」生成占位图标
        val iconBitmap = try {
            packageManager.getApplicationIcon(packageName).toBitmap(192, 192)
        } catch (e: Exception) {
            Log.w(TAG, "fallback to generated icon", e)
            IconGenerator.generate(this, appName)
        }

        val shortcut = ShortcutInfo.Builder(this, SHORTCUT_ID)
            .setShortLabel(appName)
            .setLongLabel(appName)
            .setIcon(android.graphics.drawable.Icon.createWithBitmap(iconBitmap))
            .setIntent(
                Intent(this, MainActivity::class.java).apply {
                    action = Intent.ACTION_VIEW
                }
            )
            .build()

        shortcutManager.requestPinShortcut(shortcut, null)
        prefs.edit().putBoolean(KEY_SHORTCUT_DONE, true).apply()
    }

    override fun onDestroy() {
        super.onDestroy()
        webView.destroy()
    }

    companion object {
        private const val TAG = "MainActivity"
        private const val APP_URL = "https://appassets.androidplatform.net/index.html"
        private const val ASSET_ZIP = "html_app.zip"
        private const val SHORTCUT_ID = "open_html_app"
        private const val PREFS = "html_micro_app_prefs"
        private const val KEY_SHORTCUT_DONE = "shortcut_created"
    }
}
