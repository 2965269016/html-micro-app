package com.example.htmlmicroapp

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import org.json.JSONObject

/**
 * JS 系统能力桥接。
 *
 * 注册名 "Native"（另注册别名 "LocalStore" 兼容旧示例包）。
 * 所有方法同步返回 JSON 字符串：{"ok":true,...} 或 {"ok":false,"error":"..."}。
 * 权限未开启时返回 {"ok":false,"error":"permission_denied"}。
 */
class MiniAppBridge(
    private val context: Context,
    private val appId: String
) {

    private val main = Handler(Looper.getMainLooper())

    private val prefs get() = AppRepo.storePrefs(context, appId)

    // ------------------------------------------------------------ JSON 工具

    private fun ok(vararg pairs: Pair<String, Any?>): String {
        val json = JSONObject().put("ok", true)
        pairs.forEach { (k, v) -> json.put(k, v ?: JSONObject.NULL) }
        return json.toString()
    }

    private fun fail(error: String): String =
        JSONObject().put("ok", false).put("error", error).toString()

    private fun denied() = fail("permission_denied")

    // ------------------------------------------------------------ 存储（免权限）

    @android.webkit.JavascriptInterface
    fun save(key: String?, value: String?): String = try {
        if (key == null) fail("key_required")
        else {
            prefs.edit().putString(key, value ?: "").apply()
            ok()
        }
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    @android.webkit.JavascriptInterface
    fun load(key: String?): String = try {
        if (key == null) fail("key_required")
        else {
            val v = prefs.getString(key, null)
            ok("value" to v)
        }
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    @android.webkit.JavascriptInterface
    fun remove(key: String?): String = try {
        if (key == null) fail("key_required")
        else {
            prefs.edit().remove(key).apply()
            ok()
        }
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    @android.webkit.JavascriptInterface
    fun clear(): String = try {
        prefs.edit().clear().apply()
        ok()
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    @android.webkit.JavascriptInterface
    fun keys(): String = try {
        ok("keys" to JSONObject().put("list", prefs.all.keys.toList()))
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    // ------------------------------------------------------------ 通知（需权限）

    @android.webkit.JavascriptInterface
    fun notify(title: String?, body: String?): String {
        if (!AppRepo.hasPermission(context, appId, AppRepo.PERM_NOTIFICATION)) return denied()
        val nm = NotificationManagerCompat.from(context)
        if (!nm.areNotificationsEnabled()) return fail("system_notification_disabled")

        try {
            if (Build.VERSION.SDK_INT >= 26) {
                val channel = NotificationChannel(
                    CHANNEL_ID, "小程序通知", NotificationManager.IMPORTANCE_DEFAULT
                )
                nm.createNotificationChannel(channel)
            }
            val notification = NotificationCompat.Builder(context, CHANNEL_ID)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title ?: appId)
                .setContentText(body ?: "")
                .setAutoCancel(true)
                .build()
            nm.notify(NOTIFY_TAG, System.currentTimeMillis().toInt(), notification)
            return ok()
        } catch (e: SecurityException) {
            return fail("system_notification_disabled")
        } catch (e: Exception) {
            return fail(e.message ?: "unknown")
        }
    }

    // ------------------------------------------------------------ 设备（振动需权限）

    @android.webkit.JavascriptInterface
    fun vibrate(ms: Long): String {
        if (!AppRepo.hasPermission(context, appId, AppRepo.PERM_VIBRATE)) return denied()
        val duration = if (ms in 1..10_000) ms else 200L
        try {
            val vibrator = if (Build.VERSION.SDK_INT >= 31) {
                val vm = context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager
                vm.defaultVibrator
            } else {
                @Suppress("DEPRECATION")
                context.getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
            }
            if (!vibrator.hasVibrator()) return fail("no_vibrator")
            if (Build.VERSION.SDK_INT >= 26) {
                vibrator.vibrate(VibrationEffect.createOneShot(duration, VibrationEffect.DEFAULT_AMPLITUDE))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(duration)
            }
            return ok()
        } catch (e: Exception) { return fail(e.message ?: "unknown") }
    }

    @android.webkit.JavascriptInterface
    fun toast(message: String?): String = try {
        main.post { Toast.makeText(context, message ?: "", Toast.LENGTH_SHORT).show() }
        ok()
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    @android.webkit.JavascriptInterface
    fun deviceInfo(): String = try {
        val pm = context.packageManager
        val versionName = try {
            pm.getPackageInfo(context.packageName, 0).versionName ?: ""
        } catch (_: Exception) { "" }
        val dm = context.resources.displayMetrics
        ok(
            "model" to Build.MODEL,
            "brand" to Build.BRAND,
            "osVersion" to "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
            "appVersion" to versionName,
            "screen" to "${dm.widthPixels}x${dm.heightPixels}"
        )
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    // ------------------------------------------------------------ 剪贴板（需权限）

    @android.webkit.JavascriptInterface
    fun copyText(text: String?): String {
        if (!AppRepo.hasPermission(context, appId, AppRepo.PERM_CLIPBOARD)) return denied()
        return try {
            val cm = context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
            cm.setPrimaryClip(android.content.ClipData.newPlainText("miniapp", text ?: ""))
            ok()
        } catch (e: Exception) { fail(e.message ?: "unknown") }
    }

    @android.webkit.JavascriptInterface
    fun pasteText(): String {
        if (!AppRepo.hasPermission(context, appId, AppRepo.PERM_CLIPBOARD)) return denied()
        return try {
            val cm = context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
            val item = cm.primaryClip?.getItemAt(0)
            ok("text" to item?.coerceToText(context)?.toString())
        } catch (e: Exception) { fail(e.message ?: "unknown") }
    }

    // ------------------------------------------------------------ 分享（免权限）

    @android.webkit.JavascriptInterface
    fun share(title: String?, text: String?): String = try {
        val intent = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(android.content.Intent.EXTRA_SUBJECT, title ?: "")
            putExtra(android.content.Intent.EXTRA_TEXT, text ?: "")
        }
        val chooser = android.content.Intent.createChooser(intent, title ?: "分享").apply {
            addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        main.post { context.startActivity(chooser) }
        ok()
    } catch (e: Exception) { fail(e.message ?: "unknown") }

    companion object {
        private const val CHANNEL_ID = "miniapp_notify"
        private const val NOTIFY_TAG = "miniapp"
    }
}
