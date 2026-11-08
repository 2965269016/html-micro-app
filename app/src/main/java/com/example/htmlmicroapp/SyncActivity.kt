package com.example.htmlmicroapp

import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.ArrayAdapter
import android.widget.LinearLayout
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.appbar.MaterialToolbar
import com.google.android.material.button.MaterialButton
import com.google.android.material.materialswitch.MaterialSwitch
import com.google.android.material.textfield.TextInputEditText

/**
 * 设备同步页（互通协议第 9 节）：局域网发现/手动连接 → 应用差异对比 →
 * 逐项选方向（推送给对方 / 从对方获取 / 跳过）或一键"时间新者胜"。
 * 监听仅在本页面存活期间运行（用户选择不做安卓常驻服务），因此请停留在本页直到同步完成。
 */
class SyncActivity : AppCompatActivity() {

    private class DiffRow(
        val local: AppRepo.MiniApp?,
        val remote: SyncEngine.RemoteApp?,
        val state: String,
        val spinner: Spinner
    )

    private lateinit var engine: SyncEngine
    private lateinit var prefs: android.content.SharedPreferences
    private val main = Handler(Looper.getMainLooper())

    private lateinit var etDeviceName: TextInputEditText
    private lateinit var swAllow: MaterialSwitch
    private lateinit var llPeers: LinearLayout
    private lateinit var etManual: TextInputEditText
    private lateinit var tvPeer: TextView
    private lateinit var llDiff: LinearLayout
    private lateinit var tvStatus: TextView
    private lateinit var tvLog: TextView
    private lateinit var btnConnect: MaterialButton
    private lateinit var btnRun: MaterialButton
    private lateinit var btnAuto: MaterialButton

    private val peers = LinkedHashMap<String, SyncEngine.Peer>()
    private val rows = mutableListOf<DiffRow>()
    private var remote: SyncEngine.PeerInfo? = null
    private var remoteHost = ""
    private var remotePort = SyncEngine.TCP_PORT
    private var busy = false

    private fun log(msg: String) {
        main.post {
            tvLog.append("[${android.text.format.DateFormat.format("HH:mm:ss", java.util.Date())}] $msg\n")
        }
    }

    private fun status(msg: String) = main.post { tvStatus.text = msg }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // 动态取色（Material You，Android 12+ 随壁纸）
        com.google.android.material.color.DynamicColors.applyToActivityIfAvailable(this)
        setContentView(R.layout.activity_sync)
        window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        prefs = getSharedPreferences("box_settings", MODE_PRIVATE)
        etDeviceName = findViewById(R.id.et_device_name)
        swAllow = findViewById(R.id.sw_allow)
        llPeers = findViewById(R.id.ll_peers)
        etManual = findViewById(R.id.et_manual)
        tvPeer = findViewById(R.id.tv_peer)
        llDiff = findViewById(R.id.ll_diff)
        tvStatus = findViewById(R.id.tv_status)
        tvLog = findViewById(R.id.tv_log)
        btnConnect = findViewById(R.id.btn_connect)
        btnRun = findViewById(R.id.btn_run)
        btnAuto = findViewById(R.id.btn_auto)

        findViewById<MaterialToolbar>(R.id.toolbar).setNavigationOnClickListener { finish() }

        etDeviceName.setText(prefs.getString("device_name", android.os.Build.MODEL ?: "安卓设备"))
        etDeviceName.setOnFocusChangeListener { _, has ->
            if (!has) {
                prefs.edit().putString("device_name", etDeviceName.text.toString().trim()).apply()
            }
        }
        swAllow.isChecked = prefs.getBoolean("sync_enabled", true)
        swAllow.setOnCheckedChangeListener { _, checked ->
            prefs.edit().putBoolean("sync_enabled", checked).apply()
            if (checked) engine.start() else engine.stop()
            log(if (checked) "已开启接收（停留本页期间可被连接）" else "已关闭接收")
        }

        engine = SyncEngine(
            this,
            deviceName = {
                prefs.getString("device_name", null)?.takeIf { it.isNotBlank() }
                    ?: (android.os.Build.MODEL ?: "安卓设备")
            },
            enabled = { swAllow.isChecked }
        )
        engine.onActivity = { log(it) }
        engine.onAppsChanged = { main.post { log("本机应用列表已更新"); if (remote != null) refreshDiff() } }
        engine.onPeerFound = { peer ->
            main.post {
                if (peers.containsKey(peer.key)) return@post
                peers[peer.key] = peer
                val row = MaterialButton(this, null, com.google.android.material.R.attr.materialButtonOutlinedStyle).apply {
                    text = "${peer.name}　[${peer.host}]:${peer.tcpPort}"
                    isAllCaps = false
                    setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 13f)
                }
                row.setOnClickListener {
                    connect(peer.host, peer.tcpPort)
                }
                llPeers.addView(row)
                log("发现设备：${peer.name} [${peer.host}]:${peer.tcpPort}")
            }
        }

        btnConnect.setOnClickListener {
            val input = etManual.text.toString().trim().replace('：', ':')
            if (input.isEmpty()) {
                Toast.makeText(this, "输入 IP:端口 或先点按上方发现的设备", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            val colon = input.lastIndexOf(':')
            if (colon <= 0) {
                Toast.makeText(this, "格式应为 IP:端口", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            val host = input.substring(0, colon).trim('[', ']')
            val port = input.substring(colon + 1).trim(']').toIntOrNull() ?: SyncEngine.TCP_PORT
            connect(host, port)
        }
        btnAuto.setOnClickListener { autoFill(); execute() }
        btnRun.setOnClickListener { execute() }

        if (swAllow.isChecked) engine.start()
        log(if (swAllow.isChecked) "本机可作为同步接收端（停留本页期间有效）" else "接收已关闭，可打开上方开关")
    }

    override fun onDestroy() {
        engine.stop()
        super.onDestroy()
    }

    // ------------------------------------------------------------ 连接与差异

    private fun setBusy(b: Boolean, statusText: String = "") {
        busy = b
        main.post {
            btnConnect.isEnabled = !b
            btnRun.isEnabled = !b
            btnAuto.isEnabled = !b
            tvStatus.text = statusText
        }
    }

    private fun connect(host: String, port: Int) {
        if (busy) return
        setBusy(true, "连接 $host:$port …")
        Thread {
            try {
                val info = engine.getInfo(host, port)
                remote = info
                remoteHost = host
                remotePort = port
                main.post {
                    tvPeer.text = "对方：${info.device}（$host:$port，${info.apps.size} 个应用）"
                    buildDiff()
                    log("已连接 ${info.device}")
                }
            } catch (e: Exception) {
                main.post {
                    tvPeer.text = ""
                    Toast.makeText(this, "连接失败：${e.message}", Toast.LENGTH_LONG).show()
                    log("连接失败：${e.message}")
                }
            } finally {
                setBusy(false)
            }
        }.start()
    }

    private fun buildDiff() {
        val info = remote ?: return
        rows.clear()
        llDiff.removeAllViews()

        val local = AppRepo.list(this).associateBy { it.id }
        val remoteById = info.apps.associateBy { it.id }

        for ((id, la) in local) {
            val ra = remoteById[id]
            val state = when {
                ra == null -> "仅本机"
                ra.storeHash == AppRepo.storeHash(this, id) -> "两边一致"
                else -> "数据不同"
            }
            addDiffRow(la, ra, state)
        }
        for ((id, ra) in remoteById) {
            if (local.containsKey(id)) continue
            addDiffRow(null, ra, "仅对方")
        }

        if (rows.isEmpty()) {
            val hint = TextView(this).apply {
                text = "两边应用一致，无需同步。"
                setPadding(8, 24, 8, 24)
                setTextColor(getColor(android.R.color.darker_gray))
            }
            llDiff.addView(hint)
        }
    }

    private fun addDiffRow(local: AppRepo.MiniApp?, remoteApp: SyncEngine.RemoteApp?, state: String) {
        val rowView = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = android.view.Gravity.CENTER_VERTICAL
            setPadding(0, 12, 0, 12)
        }
        val name = TextView(this).apply {
            text = local?.name ?: remoteApp?.name ?: "?"
            textSize = 15f
            ellipsize = android.text.TextUtils.TruncateAt.END
            maxLines = 1
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.2f)
        }
        val stateView = TextView(this).apply {
            text = state
            textSize = 12f
            setTextColor(if (state == "数据不同") getColor(android.R.color.holo_red_dark) else getColor(android.R.color.darker_gray))
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 0.7f)
        }
        val spinner = Spinner(this).apply {
            adapter = ArrayAdapter(
                this@SyncActivity,
                android.R.layout.simple_spinner_dropdown_item,
                arrayOf("跳过", "推送给对方", "从对方获取")
            )
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        rowView.addView(name)
        rowView.addView(stateView)
        rowView.addView(spinner)
        llDiff.addView(rowView)
        rows.add(DiffRow(local, remoteApp, state, spinner))
    }

    private fun autoFill() {
        for (row in rows) {
            when (row.state) {
                "仅本机" -> row.spinner.setSelection(1)
                "仅对方" -> row.spinner.setSelection(1)
                "数据不同" -> {
                    val localNewer = (row.local?.installedAt ?: 0) >= (row.remote?.installedAt ?: 0)
                    row.spinner.setSelection(if (localNewer) 1 else 2)
                }
                else -> row.spinner.setSelection(0)
            }
        }
    }

    // ------------------------------------------------------------ 执行

    private fun execute() {
        val info = remote ?: run {
            Toast.makeText(this, "先连接一台设备", Toast.LENGTH_SHORT).show()
            return
        }
        if (busy) return
        val plan = rows.map { Triple(it, it.spinner.selectedItemPosition, it) }
            .filter { it.second > 0 }
        if (plan.isEmpty()) {
            Toast.makeText(this, "没有需要同步的项", Toast.LENGTH_SHORT).show()
            return
        }
        setBusy(true, "同步中…")
        Thread {
            var ok = 0
            var fail = 0
            for ((row, dir, _) in plan) {
                try {
                    if (dir == 1 && row.local != null) {
                        val out = java.io.ByteArrayOutputStream()
                        AppRepo.export(this, row.local.id, out)
                        engine.pushApp(remoteHost, remotePort, row.local.id, out.toByteArray())
                        log("↑ 已推送「${row.local.name}」")
                    } else if (row.remote != null && (dir == 2 || row.local == null)) {
                        val data = engine.getApp(remoteHost, remotePort, row.remote.id)
                        val tmp = java.io.File(cacheDir, "pull_${System.currentTimeMillis()}.zip")
                        try {
                            tmp.writeBytes(data)
                            AppRepo.installSynced(this, tmp, row.remote.id)
                        } finally {
                            tmp.delete()
                        }
                        log("↓ 已获取「${row.remote.name}」")
                    } else {
                        continue
                    }
                    ok++
                } catch (e: Exception) {
                    fail++
                    log("× ${row.local?.name ?: row.remote?.name ?: "?"} 失败：${e.message}")
                }
            }
            main.post {
                log("同步完成：成功 $ok，失败 $fail")
                if (remote != null) refreshDiff()
            }
            setBusy(false)
        }.start()
    }

    private fun refreshDiff() {
        Thread {
            try {
                val info = engine.getInfo(remoteHost, remotePort)
                remote = info
                main.post { buildDiff() }
            } catch (_: Exception) { }
        }.start()
    }
}
