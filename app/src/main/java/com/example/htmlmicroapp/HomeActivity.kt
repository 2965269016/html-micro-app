package com.example.htmlmicroapp

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.appbar.MaterialToolbar
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.floatingactionbutton.ExtendedFloatingActionButton
import com.google.android.material.snackbar.Snackbar
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * 小程序盒子首页：已导入小程序列表 + 导入入口 + 管理菜单。
 */
class HomeActivity : AppCompatActivity() {

    private lateinit var adapter: AppsAdapter
    private lateinit var rootView: View
    private lateinit var emptyState: View

    // 当前"存储管理"对话框正在操作的小程序（导出回调时使用）
    private var exportTarget: AppRepo.MiniApp? = null

    private val importLauncher = registerForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) importFromUri(uri)
    }

    private val exportLauncher = registerForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.CreateDocument("application/zip")
    ) { uri ->
        val app = exportTarget
        if (uri != null && app != null) exportTo(app, uri)
        exportTarget = null
    }

    private val runtimePermLauncher = registerForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.RequestMultiplePermissions()
    ) { /* 权限结果不改变 meta 开关，开关由用户显式保存 */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // 动态取色（Material You，Android 12+ 随壁纸变色）
        com.google.android.material.color.DynamicColors.applyToActivityIfAvailable(this)
        setContentView(R.layout.activity_home)

        rootView = findViewById(R.id.home_root)
        emptyState = findViewById(R.id.empty_state)
        val toolbar = findViewById<MaterialToolbar>(R.id.toolbar)
        toolbar.title = getString(R.string.app_name)
        toolbar.inflateMenu(R.menu.menu_home)
        toolbar.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                R.id.action_import_url -> { showUrlImportDialog(); true }
                R.id.action_sync -> { startActivity(android.content.Intent(this, SyncActivity::class.java)); true }
                else -> false
            }
        }

        adapter = AppsAdapter(
            onOpen = { app -> runApp(app) },
            onLongPress = { app -> showAppMenu(app) }
        )
        val recycler = findViewById<RecyclerView>(R.id.recycler)
        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = adapter

        findViewById<ExtendedFloatingActionButton>(R.id.fab_import).setOnClickListener {
            importLauncher.launch(arrayOf("application/zip", "application/octet-stream"))
        }
        findViewById<com.google.android.material.button.MaterialButton>(R.id.btn_load_demo)
            .setOnClickListener { loadDemo() }
    }

    override fun onResume() {
        super.onResume()
        reload()
    }

    // ------------------------------------------------------------ 列表

    private fun reload() {
        val apps = AppRepo.list(this)
        adapter.submit(apps)
        emptyState.visibility = if (apps.isEmpty()) View.VISIBLE else View.GONE
    }

    private fun runApp(app: AppRepo.MiniApp) {
        startActivity(RunActivity.intentFor(this, app.id, app.name))
    }

    // ------------------------------------------------------------ 导入

    private fun importFromUri(uri: android.net.Uri) {
        val name = queryDisplayName(uri) ?: "导入的小程序"
        try {
            val app = AppRepo.import(this, uri, name)
            reload()
            Snackbar.make(rootView, "已导入「${app.name}」", Snackbar.LENGTH_SHORT).show()
        } catch (e: AppRepo.ImportException) {
            MaterialAlertDialogBuilder(this)
                .setTitle("导入失败")
                .setMessage(e.message)
                .setPositiveButton("知道了", null)
                .show()
        } catch (e: Exception) {
            MaterialAlertDialogBuilder(this)
                .setTitle("导入失败")
                .setMessage(e.message ?: "未知错误")
                .setPositiveButton("知道了", null)
                .show()
        }
    }

    private fun loadDemo() {
        try {
            assets.open("html_app.zip").use { input ->
                val app = AppRepo.importStream(this, input, "内置示例")
                reload()
                Snackbar.make(rootView, "已载入「${app.name}」", Snackbar.LENGTH_SHORT).show()
            }
        } catch (e: Exception) {
            Snackbar.make(rootView, "内置示例载入失败", Snackbar.LENGTH_SHORT).show()
        }
    }

    // ------------------------------------------------------------ 从链接导入（GitHub / Gitee / zip 直链）

    private fun showUrlImportDialog() {
        val input = android.widget.EditText(this).apply {
            hint = "https://github.com/用户/仓库 或 https://gitee.com/用户/仓库"
            setSingleLine(true)
            setPadding(48, 32, 48, 8)
        }
        MaterialAlertDialogBuilder(this)
            .setTitle("从链接导入小程序")
            .setMessage("支持 GitHub / Gitee 仓库链接（可含 /tree/分支/子目录）和 .zip 直链。")
            .setView(input)
            .setPositiveButton("导入") { _, _ ->
                val url = input.text.toString().trim()
                if (url.isNotEmpty()) importFromUrl(url)
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun importFromUrl(url: String) {
        val repo = UrlImporter.parse(url)
        if (repo == null) {
            MaterialAlertDialogBuilder(this)
                .setTitle("无法识别的链接")
                .setMessage("支持：github.com/用户/仓库、gitee.com/用户/仓库（可带 /tree/分支/子目录），或 .zip 直链。")
                .setPositiveButton("知道了", null)
                .show()
            return
        }
        val progress = MaterialAlertDialogBuilder(this)
            .setTitle("从链接导入")
            .setMessage("解析仓库分支…")
            .setCancelable(false)
            .show()
        Thread {
            var stageDir: java.io.File? = null
            try {
                val zip = java.io.File(cacheDir, "url_${System.currentTimeMillis()}.zip")
                val downloaded = try {
                    UrlImporter.downloadAndFindRoots(repo, zip)
                } finally {
                    zip.delete()
                }
                stageDir = downloaded.second
                val roots = downloaded.first
                val names = roots.map { readRootName(it, repo.displayName) }

                val imported = mutableListOf<String>()
                if (roots.size == 1) {
                    imported += AppRepo.importDirectory(this, roots[0], repo.displayName).name
                } else {
                    // 多应用仓库：主线程弹多选框，选完再逐个导入
                    val checked = BooleanArray(roots.size) { true }
                    val latch = java.util.concurrent.CountDownLatch(1)
                    var picked: List<Int> = emptyList()
                    runOnUiThread {
                        MaterialAlertDialogBuilder(this)
                            .setTitle("仓库里有 ${roots.size} 个小程序")
                            .setMultiChoiceItems(names.toTypedArray(), checked) { _, which, isChecked ->
                                checked[which] = isChecked
                            }
                            .setPositiveButton("导入所选") { _, _ ->
                                picked = checked.indices.filter { checked[it] }
                                latch.countDown()
                            }
                            .setNegativeButton("取消") { _, _ -> latch.countDown() }
                            .setOnCancelListener { latch.countDown() }
                            .show()
                    }
                    latch.await()
                    for (i in picked) {
                        imported += AppRepo.importDirectory(this, roots[i], repo.displayName).name
                    }
                }
                runOnUiThread {
                    reload()
                    Snackbar.make(rootView, "已导入 ${imported.size} 个：${imported.joinToString("、")}", Snackbar.LENGTH_LONG).show()
                    progress.dismiss()
                }
            } catch (e: Exception) {
                runOnUiThread {
                    progress.dismiss()
                    MaterialAlertDialogBuilder(this)
                        .setTitle("导入失败")
                        .setMessage(e.message ?: "未知错误")
                        .setPositiveButton("知道了", null)
                        .show()
                }
            } finally {
                stageDir?.deleteRecursively()
            }
        }.start()
    }

    private fun readRootName(root: java.io.File, fallback: String): String {
        val manifest = java.io.File(root, "manifest.json")
        if (manifest.isFile) {
            try {
                val name = org.json.JSONObject(manifest.readText()).optString("name")
                if (name.isNotBlank()) return name
            } catch (_: Exception) { }
        }
        return root.name.ifBlank { fallback }
    }

    private fun queryDisplayName(uri: android.net.Uri): String? = try {
        contentResolver.query(uri, null, null, null, null)?.use { c ->
            val idx = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
            if (idx >= 0 && c.moveToFirst()) c.getString(idx) else null
        }
    } catch (_: Exception) { null }

    // ------------------------------------------------------------ 长按管理菜单

    private fun showAppMenu(app: AppRepo.MiniApp) {
        val items = arrayOf("打开", "权限管理", "存储管理", "添加到桌面", "删除")
        MaterialAlertDialogBuilder(this)
            .setTitle(app.name)
            .setItems(items) { _, which ->
                when (which) {
                    0 -> runApp(app)
                    1 -> showPermissionsDialog(app)
                    2 -> showStorageDialog(app)
                    3 -> addToHomeScreen(app)
                    4 -> confirmDelete(app)
                }
            }
            .show()
    }

    // ------------------------------------------------------------ 权限管理

    private fun showPermissionsDialog(app: AppRepo.MiniApp) {
        val view = layoutInflater.inflate(R.layout.dialog_permissions, null)
        val switches = mapOf(
            AppRepo.PERM_NOTIFICATION to view.findViewById<com.google.android.material.materialswitch.MaterialSwitch>(R.id.sw_notification),
            AppRepo.PERM_VIBRATE to view.findViewById<com.google.android.material.materialswitch.MaterialSwitch>(R.id.sw_vibrate),
            AppRepo.PERM_CLIPBOARD to view.findViewById<com.google.android.material.materialswitch.MaterialSwitch>(R.id.sw_clipboard),
            AppRepo.PERM_GEOLOCATION to view.findViewById<com.google.android.material.materialswitch.MaterialSwitch>(R.id.sw_geolocation)
        )
        switches.forEach { (key, sw) -> sw.isChecked = AppRepo.hasPermission(this, app.id, key) }

        MaterialAlertDialogBuilder(this)
            .setTitle("「${app.name}」权限管理")
            .setView(view)
            .setPositiveButton("保存") { _, _ ->
                switches.forEach { (key, sw) ->
                    AppRepo.setPermission(this, app.id, key, sw.isChecked)
                    onPermissionSaved(key, sw.isChecked)
                }
                Snackbar.make(rootView, "权限已更新", Snackbar.LENGTH_SHORT).show()
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun onPermissionSaved(key: String, enabled: Boolean) {
        if (!enabled) return
        when (key) {
            AppRepo.PERM_NOTIFICATION -> ensurePostNotifications()
            AppRepo.PERM_GEOLOCATION -> ensureLocation()
        }
    }

    private fun ensurePostNotifications() {
        if (android.os.Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
            != android.content.pm.PackageManager.PERMISSION_GRANTED
        ) {
            runtimePermLauncher.launch(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS))
        }
    }

    private fun ensureLocation() {
        val needed = arrayOf(
            android.Manifest.permission.ACCESS_FINE_LOCATION,
            android.Manifest.permission.ACCESS_COARSE_LOCATION
        )
        val missing = needed.filter {
            checkSelfPermission(it) != android.content.pm.PackageManager.PERMISSION_GRANTED
        }
        if (missing.isNotEmpty()) runtimePermLauncher.launch(missing.toTypedArray())
    }

    // ------------------------------------------------------------ 存储管理

    private fun showStorageDialog(app: AppRepo.MiniApp) {
        val bytes = AppRepo.usageBytes(this, app.id)
        val human = when {
            bytes >= 1024 * 1024 -> "%.1f MB".format(bytes / 1024f / 1024f)
            bytes >= 1024 -> "%.1f KB".format(bytes / 1024f)
            else -> "$bytes B"
        }
        exportTarget = app
        MaterialAlertDialogBuilder(this)
            .setTitle("「${app.name}」存储管理")
            .setMessage("总占用：$human\n\n· 清除数据：删除该小程序保存的所有 JS 数据\n· 打包导出：应用与数据一并导出为 zip，可在任意设备导入还原")
            .setPositiveButton("打包导出") { _, _ ->
                val safeName = app.name.replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val date = SimpleDateFormat("yyyyMMdd", Locale.US).format(Date())
                exportLauncher.launch("$safeName-$date.zip")
            }
            .setNeutralButton("清除数据") { _, _ ->
                MaterialAlertDialogBuilder(this)
                    .setTitle("确认清除")
                    .setMessage("将删除「${app.name}」保存的全部 JS 数据，且不可恢复。")
                    .setPositiveButton("清除") { _, _ ->
                        AppRepo.clearData(this, app.id)
                        Snackbar.make(rootView, "数据已清除", Snackbar.LENGTH_SHORT).show()
                    }
                    .setNegativeButton("取消", null)
                    .show()
            }
            .setNegativeButton("关闭", null)
            .show()
    }

    private fun exportTo(app: AppRepo.MiniApp, uri: android.net.Uri) {
        try {
            contentResolver.openOutputStream(uri)?.use { out ->
                AppRepo.export(this, app.id, out)
            }
            Snackbar.make(rootView, "已导出「${app.name}」（应用+数据）", Snackbar.LENGTH_LONG).show()
        } catch (e: Exception) {
            MaterialAlertDialogBuilder(this)
                .setTitle("导出失败")
                .setMessage(e.message ?: "未知错误")
                .setPositiveButton("知道了", null)
                .show()
        }
    }

    // ------------------------------------------------------------ 桌面快捷方式 / 删除

    private fun addToHomeScreen(app: AppRepo.MiniApp) {
        if (android.os.Build.VERSION.SDK_INT < 26) {
            Snackbar.make(rootView, "系统版本过低，不支持固定快捷方式", Snackbar.LENGTH_SHORT).show()
            return
        }
        try {
            val sm = getSystemService(android.content.pm.ShortcutManager::class.java)
            if (sm == null || !sm.isRequestPinShortcutSupported) {
                Snackbar.make(rootView, "当前桌面不支持固定快捷方式", Snackbar.LENGTH_SHORT).show()
                return
            }
            val icon = android.graphics.drawable.Icon.createWithBitmap(
                IconGenerator.generate(this, app.name)
            )
            val shortcut = android.content.pm.ShortcutInfo.Builder(this, "mini_${app.id}")
                .setShortLabel(app.name)
                .setLongLabel(app.name)
                .setIcon(icon)
                .setIntent(RunActivity.intentFor(this, app.id, app.name))
                .build()
            sm.requestPinShortcut(shortcut, null)
        } catch (e: Exception) {
            // 部分定制桌面（MIUI 等）会抛异常，兜底提示而不是闪退
            Snackbar.make(
                rootView,
                "添加失败：${e.message ?: "桌面不支持"}（可尝试长按应用图标）",
                Snackbar.LENGTH_LONG
            ).show()
        }
    }

    private fun confirmDelete(app: AppRepo.MiniApp) {
        MaterialAlertDialogBuilder(this)
            .setTitle("删除小程序")
            .setMessage("将删除「${app.name}」及其全部数据，且不可恢复。")
            .setPositiveButton("删除") { _, _ ->
                AppRepo.delete(this, app.id)
                disableShortcut(app.id)
                reload()
                Snackbar.make(rootView, "已删除「${app.name}」", Snackbar.LENGTH_SHORT).show()
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun disableShortcut(appId: String) {
        if (android.os.Build.VERSION.SDK_INT >= 26) {
            try {
                getSystemService(android.content.pm.ShortcutManager::class.java)
                    ?.disableShortcuts(listOf("mini_$appId"))
            } catch (_: Exception) { }
        }
    }
}

// ==================================================================== 列表适配器

class AppsAdapter(
    private val onOpen: (AppRepo.MiniApp) -> Unit,
    private val onLongPress: (AppRepo.MiniApp) -> Unit
) : RecyclerView.Adapter<AppsAdapter.ViewHolder>() {

    private val items = mutableListOf<AppRepo.MiniApp>()
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd", Locale.US)

    fun submit(apps: List<AppRepo.MiniApp>) {
        items.clear()
        items.addAll(apps)
        notifyDataSetChanged()
    }

    class ViewHolder(view: View) : RecyclerView.ViewHolder(view) {
        val icon: ImageView = view.findViewById(R.id.item_icon)
        val name: TextView = view.findViewById(R.id.item_name)
        val date: TextView = view.findViewById(R.id.item_date)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_app, parent, false)
        return ViewHolder(view)
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val app = items[position]
        holder.icon.setImageBitmap(IconGenerator.generate(holder.icon.context, app.name))
        holder.name.text = app.name
        holder.date.text = if (app.installedAt > 0) {
            "安装于 ${dateFormat.format(Date(app.installedAt))}"
        } else {
            ""
        }
        holder.itemView.setOnClickListener { onOpen(app) }
        holder.itemView.setOnLongClickListener {
            onLongPress(app)
            true
        }
    }
}
