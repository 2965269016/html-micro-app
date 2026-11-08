package com.example.htmlmicroapp

import android.content.Context
import android.net.wifi.WifiManager
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.net.URL

/**
 * 设备同步引擎（互通协议第 9 节，与电脑端 pc/SyncServer·SyncClient·Discovery 对应）：
 *  - TCP HTTP 子集，端口 47777，IPv6 :: 双栈监听（兼容 IPv4-mapped）
 *  - UDP 47778 发现：IPv4 广播 + IPv6 多播 ff02::1（尽力而为），收到探测原样回发
 *  - 端点：GET /sync/info、GET /sync/app?id=、POST /sync/app?id=（保 id 安装）
 * 仅在同步页存活期间运行（用户选择不做安卓常驻服务）。
 */
class SyncEngine(
    private val context: Context,
    private val deviceName: () -> String,
    private val enabled: () -> Boolean
) {
    companion object {
        const val TCP_PORT = 47777
        const val UDP_PORT = 47778
        private const val MAX_BODY = 512L * 1024 * 1024
    }

    class Peer(val name: String, val host: String, val tcpPort: Int) {
        val key: String get() = "$host:$tcpPort"
    }

    class RemoteApp(val id: String, val name: String, val installedAt: Long, val storeHash: String)

    class PeerInfo(val device: String, val apps: List<RemoteApp>)

    var onActivity: ((String) -> Unit)? = null
    var onAppsChanged: (() -> Unit)? = null
    var onPeerFound: ((Peer) -> Unit)? = null

    private val instanceId = java.util.UUID.randomUUID().toString()
    private var serverSocket: ServerSocket? = null
    private var udpSocket: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    @Volatile private var running = false
    private var acceptThread: Thread? = null
    private var udpThread: Thread? = null
    private var probeThread: Thread? = null

    val isRunning: Boolean get() = running

    // ------------------------------------------------------------ 生命周期

    @Synchronized
    fun start() {
        if (running) return
        running = true

        // 组播锁（部分 Wi-Fi 驱动默认丢弃组播/广播）
        try {
            val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
            if (wifi != null) {
                multicastLock = wifi.createMulticastLock("miniappbox-sync").apply {
                    setReferenceCounted(false)
                    acquire()
                }
            }
        } catch (_: Exception) { }

        try {
            val ss = ServerSocket()
            try {
                ss.bind(InetSocketAddress(InetAddress.getByName("::"), TCP_PORT))
            } catch (_: Exception) {
                ss.bind(InetSocketAddress(InetAddress.getByName("0.0.0.0"), TCP_PORT)) // 无 IPv6 栈时降级
            }
            serverSocket = ss
            acceptThread = thread("sync-accept") { acceptLoop(ss) }
            emit("同步服务已启动（端口 $TCP_PORT，IPv4/IPv6）")
        } catch (e: Exception) {
            running = false
            emit("同步服务启动失败：${e.message ?: e.javaClass.simpleName}（端口可能被占用）")
            return
        }

        try {
            val socket = MulticastSocket(UDP_PORT)
            try {
                val group = InetAddress.getByName("ff02::1")
                for (nic in NetworkInterface.getNetworkInterfaces()) {
                    if (!nic.isUp) continue
                    try { socket.joinGroup(InetSocketAddress(group, UDP_PORT), nic) } catch (_: Exception) { }
                }
            } catch (_: Exception) { }
            socket.broadcast = true
            udpSocket = socket
            udpThread = thread("sync-udp") { udpLoop(socket) }
            probeThread = thread("sync-probe") {
                while (running) {
                    sendProbe(null)
                    try { Thread.sleep(3000) } catch (_: InterruptedException) { break }
                }
            }
        } catch (e: Exception) {
            emit("自动发现不可用（${e.message ?: "UDP 被占用"}），可手动输入 IP 连接")
        }
    }

    @Synchronized
    fun stop() {
        if (!running) return
        running = false
        try { serverSocket?.close() } catch (_: Exception) { }
        try { udpSocket?.close() } catch (_: Exception) { }
        try { multicastLock?.release() } catch (_: Exception) { }
        serverSocket = null
        udpSocket = null
        multicastLock = null
        emit("同步服务已停止")
    }

    private fun thread(name: String, block: () -> Unit): Thread =
        Thread { try { block() } catch (_: Throwable) { } }.apply {
            this.name = name
            isDaemon = true
            start()
        }

    private fun emit(msg: String) {
        android.util.Log.d("SyncEngine", msg)
        onActivity?.invoke(msg)
    }

    // ------------------------------------------------------------ 服务端

    private fun acceptLoop(ss: ServerSocket) {
        while (running) {
            val client = try { ss.accept() } catch (_: Exception) { break }
            thread("sync-conn") { handleClient(client) }
        }
    }

    private fun handleClient(client: Socket) {
        try {
            client.soTimeout = 10000
            client.use { c ->
                val input = c.getInputStream()
                val requestLine = readLine(input) ?: return
                val parts = requestLine.split(" ")
                if (parts.size < 2) return
                val method = parts[0].uppercase()
                val rawPath = parts[1]

                var contentLength = 0
                while (true) {
                    val line = readLine(input) ?: break
                    if (line.isEmpty()) break
                    val idx = line.indexOf(':')
                    if (idx > 0 && line.substring(0, idx).trim().equals("Content-Length", ignoreCase = true))
                        contentLength = line.substring(idx + 1).trim().toIntOrNull() ?: 0
                }

                val body: ByteArray? = if (contentLength > 0) {
                    if (contentLength > MAX_BODY) return
                    val buf = ByteArray(contentLength)
                    var read = 0
                    while (read < contentLength) {
                        val n = input.read(buf, read, contentLength - read)
                        if (n <= 0) break
                        read += n
                    }
                    buf
                } else null

                val uri = try { URI("http://local$rawPath") } catch (_: Exception) { return }
                val path = uri.path ?: ""
                val query = uri.query ?: ""
                val params = query.split('&').filter { it.contains('=') }.associate {
                    val p = it.split('=', limit = 2)
                    p[0] to java.net.URLDecoder.decode(p[1], "UTF-8")
                }

                if (method == "GET" && path == "/sync/info") {
                    respond(c, 200, "application/json; charset=utf-8", infoJson().toByteArray(Charsets.UTF_8))
                } else if (method == "GET" && path == "/sync/app") {
                    val id = params["id"]
                    val app = id?.let { AppRepo.loadMeta(context, it) }
                    if (id == null || app == null) {
                        respond(c, 404, "application/json; charset=utf-8", failJson("not_found").toByteArray(Charsets.UTF_8))
                        return
                    }
                    val bytes = exportZip(id)
                    respond(c, 200, "application/zip", bytes)
                    emit("已向对方发送「${app.name}」")
                } else if (method == "POST" && path == "/sync/app") {
                    val id = params["id"]
                    if (id == null || body == null || body.isEmpty()) {
                        respond(c, 400, "application/json; charset=utf-8", failJson("bad_request").toByteArray(Charsets.UTF_8))
                        return
                    }
                    val tmp = File(context.cacheDir, "recv_${System.currentTimeMillis()}.zip")
                    try {
                        tmp.writeBytes(body)
                        val app = AppRepo.installSynced(context, tmp, id)
                        respond(c, 200, "application/json; charset=utf-8", "{\"ok\":true}".toByteArray(Charsets.UTF_8))
                        emit("已从对方接收「${app.name}」")
                        onAppsChanged?.invoke()
                    } catch (e: Exception) {
                        respond(c, 400, "application/json; charset=utf-8", failJson(e.message ?: "install_failed").toByteArray(Charsets.UTF_8))
                    } finally {
                        tmp.delete()
                    }
                } else {
                    respond(c, 404, "application/json; charset=utf-8", failJson("not_found").toByteArray(Charsets.UTF_8))
                }
            }
        } catch (_: Exception) { }
    }

    private fun readLine(input: java.io.InputStream): String? {
        val sb = StringBuilder()
        while (sb.length < 32 * 1024) {
            val b = input.read()
            if (b <= 0) return if (sb.isEmpty()) null else sb.toString()
            if (b == '\n'.code) {
                if (sb.isNotEmpty() && sb.last() == '\r') sb.setLength(sb.length - 1)
                return sb.toString()
            }
            sb.append(b.toChar())
        }
        return sb.toString()
    }

    private fun respond(socket: Socket, code: Int, contentType: String, data: ByteArray) {
        val reason = when (code) { 200 -> "OK"; 400 -> "Bad Request"; 404 -> "Not Found"; else -> "Error" }
        val head = "HTTP/1.1 $code $reason\r\nContent-Type: $contentType\r\nContent-Length: ${data.size}\r\nConnection: close\r\n\r\n"
        socket.getOutputStream().let {
            it.write(head.toByteArray(Charsets.US_ASCII))
            it.write(data)
            it.flush()
        }
    }

    private fun infoJson(): String {
        val apps = JSONArray()
        for (app in AppRepo.list(context)) {
            apps.put(JSONObject()
                .put("id", app.id)
                .put("name", app.name)
                .put("installedAt", app.installedAt)
                .put("size", AppRepo.usageBytes(context, app.id))
                .put("storeHash", AppRepo.storeHash(context, app.id)))
        }
        return JSONObject()
            .put("proto", 1)
            .put("device", deviceName())
            .put("apps", apps)
            .toString()
    }

    private fun failJson(msg: String) = JSONObject().put("ok", false).put("error", msg).toString()

    private fun exportZip(id: String): ByteArray {
        val out = ByteArrayOutputStream()
        AppRepo.export(context, id, out)
        return out.toByteArray()
    }

    // ------------------------------------------------------------ 发现（UDP）

    private fun probePayload(): ByteArray {
        return JSONObject()
            .put("proto", 1)
            .put("id", instanceId)
            .put("device", deviceName())
            .put("tcp", TCP_PORT)
            .toString().toByteArray(Charsets.UTF_8)
    }

    /** dest 为空则 IPv4 广播 + IPv6 多播各发一轮。 */
    fun sendProbe(dest: InetAddress?) {
        val payload = probePayload()
        val socket = udpSocket ?: return
        try {
            if (dest != null) {
                socket.send(DatagramPacket(payload, payload.size, dest, UDP_PORT))
            } else {
                try { socket.send(DatagramPacket(payload, payload.size, InetAddress.getByName("255.255.255.255"), UDP_PORT)) } catch (_: Exception) { }
                try { socket.send(DatagramPacket(payload, payload.size, InetAddress.getByName("ff02::1"), UDP_PORT)) } catch (_: Exception) { }
            }
        } catch (_: Exception) { }
    }

    private fun udpLoop(socket: DatagramSocket) {
        val buf = ByteArray(4096)
        while (running) {
            val packet = DatagramPacket(buf, buf.size)
            try {
                socket.receive(packet)
                val peer = parseProbe(packet) ?: continue
                onPeerFound?.invoke(peer)
                // 回发自己的探测（单播），让对方也发现自己
                sendProbe(packet.address)
            } catch (_: Exception) { }
        }
    }

    private fun parseProbe(packet: DatagramPacket): Peer? {
        return try {
            val json = JSONObject(String(packet.data, 0, packet.length, Charsets.UTF_8))
            if (json.optInt("proto") != 1) return null
            if (json.optString("id") == instanceId) return null // 自己的回声
            val addr = packet.address
            val host = if (addr.isLinkLocalAddress && addr.hostAddress?.contains('%') == false) addr.hostAddress ?: "" else addr.hostAddress ?: ""
            Peer(json.optString("device", "?"), host, if (json.has("tcp")) json.optInt("tcp") else TCP_PORT)
        } catch (_: Exception) {
            null
        }
    }

    // ------------------------------------------------------------ 客户端

    private fun baseUrl(host: String, port: Int): String {
        val h = if (host.contains(':') && !host.startsWith("[")) "[$host]" else host.removeSurrounding("[", "]")
        return "http://$h:$port"
    }

    fun getInfo(host: String, port: Int): PeerInfo {
        val conn = openClient("${baseUrl(host, port)}/sync/info", 6000)
        val body = conn.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
        conn.disconnect()
        val root = JSONObject(body)
        val apps = mutableListOf<RemoteApp>()
        val arr = root.optJSONArray("apps") ?: JSONArray()
        for (i in 0 until arr.length()) {
            val a = arr.getJSONObject(i)
            apps.add(RemoteApp(
                id = a.optString("id"),
                name = a.optString("name"),
                installedAt = a.optLong("installedAt"),
                storeHash = a.optString("storeHash")
            ))
        }
        return PeerInfo(root.optString("device", "?"), apps)
    }

    fun getApp(host: String, port: Int, id: String): ByteArray {
        val conn = openClient("${baseUrl(host, port)}/sync/app?id=${java.net.URLEncoder.encode(id, "UTF-8")}", 120000)
        val data = conn.inputStream.use { it.readBytes() }
        conn.disconnect()
        return data
    }

    fun pushApp(host: String, port: Int, id: String, zip: ByteArray) {
        val conn = openClient("${baseUrl(host, port)}/sync/app?id=${java.net.URLEncoder.encode(id, "UTF-8")}", 120000)
        try {
            conn.requestMethod = "POST"
            conn.doOutput = true
            conn.setFixedLengthStreamingMode(zip.size)
            conn.setRequestProperty("Content-Type", "application/zip")
            conn.outputStream.use { it.write(zip) }
            val code = conn.responseCode
            val body = (if (code in 200..299) conn.inputStream else conn.errorStream)
                ?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
            if (code !in 200..299) {
                val err = try { JSONObject(body).optString("error") } catch (_: Exception) { body }
                throw Exception("对方拒绝：$err")
            }
        } finally {
            conn.disconnect()
        }
    }

    private fun openClient(url: String, timeoutMs: Int): java.net.HttpURLConnection {
        val conn = URL(url).openConnection() as java.net.HttpURLConnection
        conn.connectTimeout = 6000
        conn.readTimeout = timeoutMs
        conn.setRequestProperty("User-Agent", "MiniAppBox-Android/0.2.0")
        return conn
    }
}
