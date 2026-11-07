package com.example.htmlmicroapp

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.webkit.GeolocationPermissions
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.ImageButton
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.webkit.WebViewAssetLoader
import java.io.File

/**
 * 小程序运行时：加载指定 id 的 HTML 小程序。
 *
 * 启动参数（Intent extras）：
 *   app_id   小程序 id（AppRepo 分配）
 *   app_name 小程序显示名（顶栏标题）
 */
class RunActivity : AppCompatActivity() {

    private lateinit var webView: WebView
    private var appId: String? = null

    // <input type=file> 原生文件选择回调
    private var fileUploadCallback: ValueCallback<Array<Uri>>? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_run)

        appId = intent.getStringExtra(EXTRA_APP_ID)
        if (appId.isNullOrBlank()) {
            Toast.makeText(this, "缺少小程序参数", Toast.LENGTH_SHORT).show()
            finish()
            return
        }
        val id = appId!!

        // 悬浮返回键：网页有历史则后退，否则退出小程序
        findViewById<ImageButton>(R.id.btn_back).setOnClickListener {
            if (this::webView.isInitialized && webView.canGoBack()) webView.goBack() else finish()
        }

        webView = findViewById(R.id.webview)
        setupWebView(id)
        webView.loadUrl(runtimeUrl(id))
    }

    private fun runtimeUrl(id: String) =
        "https://appassets.androidplatform.net/apps/$id/index.html"

    @SuppressLint("SetJavaScriptEnabled")
    private fun setupWebView(id: String) {
        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.allowFileAccess = true
        webView.settings.allowContentAccess = true
        webView.settings.setGeolocationEnabled(true)

        val appDir = AppRepo.appDir(this, id)
        val assetLoader = WebViewAssetLoader.Builder()
            .addPathHandler("/apps/$id/", WebViewAssetLoader.PathHandler { path ->
                serveFile(appDir, path)
            })
            .build()

        webView.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(
                view: WebView,
                request: WebResourceRequest
            ): WebResourceResponse? = assetLoader.shouldInterceptRequest(request.url)

            override fun onReceivedError(
                view: WebView,
                request: WebResourceRequest,
                error: android.webkit.WebResourceError
            ) {
                if (request.isForMainFrame) {
                    view.loadData(ERROR_HTML, "text/html", "UTF-8")
                }
            }
        }

        webView.webChromeClient = object : WebChromeClient() {
            // <input type=file>：交给系统文件选择器
            override fun onShowFileChooser(
                view: WebView,
                callback: ValueCallback<Array<Uri>>,
                params: FileChooserParams
            ): Boolean {
                fileUploadCallback?.onReceiveValue(null)
                fileUploadCallback = callback
                return try {
                    startActivityForResult(params.createIntent(), REQ_FILE_CHOOSER)
                    true
                } catch (e: Exception) {
                    fileUploadCallback = null
                    false
                }
            }

            // 定位：按该小程序的权限开关放行
            override fun onGeolocationPermissionsShowPrompt(
                origin: String,
                callback: GeolocationPermissions.Callback
            ) {
                val allowed = AppRepo.hasPermission(
                    this@RunActivity, id, AppRepo.PERM_GEOLOCATION
                )
                callback.invoke(origin, allowed, false)
            }
        }

        // 系统能力桥接：Native（新）+ LocalStore（旧包兼容别名）
        val bridge = MiniAppBridge(this, id)
        webView.addJavascriptInterface(bridge, "Native")
        webView.addJavascriptInterface(bridge, "LocalStore")
    }

    private fun serveFile(appDir: File, path: String): WebResourceResponse? {
        val relative = path.trimStart('/')
        val file = File(appDir, relative)
        if (!file.exists() || !file.isFile) return null
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
            "mp3", "wav", "ogg" -> "audio/mpeg"
            "mp4", "webm" -> "video/mp4"
            else -> "application/octet-stream"
        }
        return WebResourceResponse(mime, null, file.inputStream())
    }

    // ------------------------------------------------------------ 文件选择结果

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        if (requestCode == REQ_FILE_CHOOSER) {
            val callback = fileUploadCallback
            fileUploadCallback = null
            val uris = WebChromeClient.FileChooserParams.parseResult(resultCode, data)
            callback?.onReceiveValue(uris)
        } else {
            super.onActivityResult(requestCode, resultCode, data)
        }
    }

    // ------------------------------------------------------------ 返回键

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (this::webView.isInitialized && webView.canGoBack()) {
            webView.goBack()
        } else {
            @Suppress("DEPRECATION")
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        if (this::webView.isInitialized) webView.destroy()
    }

    companion object {
        private const val EXTRA_APP_ID = "app_id"
        private const val EXTRA_APP_NAME = "app_name"
        private const val REQ_FILE_CHOOSER = 1001

        private const val ERROR_HTML = """
            <html><body style="font-family:sans-serif;padding:24px">
            <h2>页面加载失败</h2><p>该小程序包可能已损坏（缺少 index.html），
            请回到盒子删除后重新导入。</p></body></html>
        """

        /** 供桌面快捷方式 / 盒子列表构造启动 Intent。 */
        fun intentFor(activity: Context, appId: String, appName: String): Intent =
            Intent(activity, RunActivity::class.java).apply {
                // 快捷方式 intent 必须带 action，否则部分桌面会抛 IllegalArgumentException
                action = Intent.ACTION_VIEW
                putExtra(EXTRA_APP_ID, appId)
                putExtra(EXTRA_APP_NAME, appName)
            }
    }
}
