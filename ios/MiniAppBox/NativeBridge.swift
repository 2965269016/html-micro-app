//
//  NativeBridge.swift
//  MiniAppBox
//
//  JS 系统能力桥（iOS 版）：
//   · box://<id>/<path>            小程序内容（等价 Android WebViewAssetLoader）
//   · nativebridge://<cmd> POST    同步桥（等价 Android 同步 @JavascriptInterface）
//     JS 垫片用同步 XHR 调用，Native.save(...) 仍同步返回 JSON 字符串
//     （与 Android / skill 规范一致；同 PC 端 WebView2 已验证的同步 XHR 方案）
//
//  权限模型与 Android 对齐：notify/vibrate/copyText/pasteText/geo 未授权返回 permission_denied。
//

import WebKit
import UIKit
import CoreLocation
import UserNotifications

// MARK: - JS 垫片（document start 注入）

enum BridgeInjection {

    static let script: WKUserScript = {
        let js = """
        (function(){
          function call(cmd, args) {
            try {
              var xhr = new XMLHttpRequest();
              xhr.open('POST', 'nativebridge://' + cmd, false);
              xhr.send(JSON.stringify(args || {}));
              return xhr.responseText;
            } catch (e) {
              return JSON.stringify({ok:false, error: String(e && e.message || e)});
            }
          }
          var Native = {
            // 协议第 4 节：参数宽松——数字/布尔传参自动转字符串
            save: function(k,v){ return call('save', {key: String(k), value: v==null ? '' : String(v)}); },
            load: function(k){ return call('load', {key: String(k)}); },
            remove: function(k){ return call('remove', {key: String(k)}); },
            clear: function(){ return call('clear', {}); },
            keys: function(){ return call('keys', {}); },
            notify: function(t,b){ return call('notify', {title:String(t), body:b==null?'':String(b)}); },
            vibrate: function(ms){ var n = Number(ms); if (!isFinite(n)) n = 200; return call('vibrate', {ms: n}); },
            toast: function(m){ return call('toast', {message: m==null?'':String(m)}); },
            deviceInfo: function(){ return call('deviceInfo', {}); },
            copyText: function(t){ return call('copyText', {text: t==null?'':String(t)}); },
            pasteText: function(){ return call('pasteText', {}); },
            share: function(t,x){ return call('share', {title:String(t), text:x==null?'':String(x)}); }
          };
          window.Native = Native;
          window.LocalStore = Native;   // 旧包兼容别名

          // 定位垫片（异步，经 message handler）
          window.__geoSeq = 0;
          window.__geoCbs = {};
          if (navigator.geolocation) {
            navigator.geolocation.getCurrentPosition = function(ok, err, opt) {
              var id = 'geo' + (++window.__geoSeq);
              window.__geoCbs[id] = {ok: ok, err: err};
              window.webkit.messageHandlers.boxGeo.postMessage({id: id});
            };
            navigator.geolocation.watchPosition = function(){ return 0; };
            navigator.geolocation.clearWatch = function(){};
          }

          // 悬浮胶囊安全区（左上返回 / 右上退出），供网页避让
          window.__miniapp_safe_insets = {top: 56, left: 56, right: 56, bottom: 0};
          document.documentElement.style.setProperty('--box-safe-top', '56px');
          document.documentElement.style.setProperty('--box-safe-side', '56px');
        })();
        """
        return WKUserScript(source: js, injectionTime: .atDocumentStart, forMainFrameOnly: false)
    }()

    static func geoCallbackJS(id: String, json: String) -> String {
        let payload = json.replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "'", with: "\\'")
            .replacingOccurrences(of: "\n", with: "")
        return """
        (function(){
          var cb = window.__geoCbs && window.__geoCbs['\(id)'];
          if (!cb) return;
          var data = \(payload);
          (data.ok ? cb.ok : function(e){ cb.err({code: e.error || 1, message: e.error || 'error', PERMISSION_DENIED: 1}); })(data.ok ? data.pos : data);
          delete window.__geoCbs['\(id)'];
        })();
        """
    }
}

// MARK: - 内容 scheme（box://<id>/<path>）

final class ContentSchemeHandler: NSObject, WKURLSchemeHandler {

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url else { return task.didFailWithError(URLError(.badURL)) }
        let id = url.host ?? ""
        // 相对路径资源解析时 host 可能为空，用 referer 回查当前小程序
        var appId = id
        if appId.isEmpty {
            if let ref = task.request.value(forHTTPHeaderField: "Referer"),
               let refURL = URL(string: ref),
               let refHost = refURL.host, !refHost.isEmpty {
                appId = refHost
            }
        }
        guard appId.hasPrefix("app_") else { return task.didFailWithError(URLError(.badURL)) }

        var path = url.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if path.isEmpty { path = "index.html" }
        if path.contains("..") { return task.didFailWithError(URLError(.fileDoesNotExist)) }

        let file = MiniAppStore.appDir(appId).appendingPathComponent(path)
        guard file.isFile, FileManager.default.fileExists(atPath: file.path) else {
            let resp = URLResponse(url: url, mimeType: "text/plain",
                                   expectedContentLength: 0, textEncodingName: nil)
            task.didReceive(resp)
            task.didReceive(Data("404".utf8))
            task.didFinish()
            return
        }

        let mime = Self.mime(for: file)
        let data = (try? Data(contentsOf: file)) ?? Data()
        let resp = URLResponse(url: url, mimeType: mime, expectedContentLength: data.count,
                               textEncodingName: mime.hasPrefix("text") ? "utf-8" : nil)
        task.didReceive(resp)
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}

    static func mime(for file: URL) -> String {
        switch file.pathExtension.lowercased() {
        case "html", "htm": return "text/html"
        case "css": return "text/css"
        case "js", "mjs": return "application/javascript"
        case "json": return "application/json"
        case "png": return "image/png"
        case "jpg", "jpeg": return "image/jpeg"
        case "gif": return "image/gif"
        case "webp": return "image/webp"
        case "svg": return "image/svg+xml"
        case "ico": return "image/x-icon"
        case "txt": return "text/plain"
        case "woff": return "font/woff"
        case "woff2": return "font/woff2"
        case "ttf": return "font/ttf"
        case "mp3", "wav", "ogg": return "audio/mpeg"
        case "mp4", "webm": return "video/mp4"
        default: return "application/octet-stream"
        }
    }
}

// MARK: - 同步桥 scheme（nativebridge://<cmd>）

final class BridgeSchemeHandler: NSObject, WKURLSchemeHandler {

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url,
              let cmd = url.host else { return task.didFailWithError(URLError(.badURL)) }

        let argsData = Self.readBody(task.request)
        let args = (try? JSONSerialization.jsonObject(with: argsData)) as? [String: Any] ?? [:]

        let result = BridgeCore.dispatch(cmd: cmd, args: args, webView: webView)
        let body = (try? JSONSerialization.data(withJSONObject: result)) ?? Data(#"{"ok":false}"#.utf8)

        let resp = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1",
                                   headerFields: ["Content-Type": "application/json",
                                                  "Access-Control-Allow-Origin": "*"])!
        task.didReceive(resp)
        task.didReceive(body)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}

    private static func readBody(_ request: URLRequest) -> Data {
        if let body = request.httpBody { return body }
        if let stream = request.httpBodyStream {
            stream.open()
            defer { stream.close() }
            var data = Data()
            let bufSize = 16 * 1024
            let buf = UnsafeMutablePointer<UInt8>.allocate(capacity: bufSize)
            defer { buf.deallocate() }
            while stream.hasBytesAvailable {
                let n = stream.read(buf, maxLength: bufSize)
                if n <= 0 { break }
                data.append(buf, count: n)
            }
            return data
        }
        return Data()
    }
}

// MARK: - 桥接命令实现（与 Android MiniAppBridge 一一对应）

enum BridgeCore {

    private static func ok(_ extra: [String: Any] = [:]) -> [String: Any] {
        var d: [String: Any] = ["ok": true]
        extra.forEach { d[$0.key] = $0.value }
        return d
    }

    private static func fail(_ error: String) -> [String: Any] {
        ["ok": false, "error": error]
    }

    private static func denied() -> [String: Any] { fail("permission_denied") }

    static func dispatch(cmd: String, args: [String: Any], webView: WKWebView) -> [String: Any] {
        guard let appId = WebViewHost.currentAppId else { return fail("no_app") }

        switch cmd {

        // ---- 存储（免权限） ----
        case "save":
            guard let key = args["key"] as? String else { return fail("key_required") }
            let value = (args["value"] as? String) ?? ""
            MiniAppStore.storeDefaults(appId).set(value, forKey: key)
            return ok()

        case "load":
            guard let key = args["key"] as? String else { return fail("key_required") }
            let v = MiniAppStore.storeDefaults(appId).string(forKey: key)
            return ok(["value": v ?? NSNull()])

        case "remove":
            guard let key = args["key"] as? String else { return fail("key_required") }
            MiniAppStore.storeDefaults(appId).removeObject(forKey: key)
            return ok()

        case "clear":
            MiniAppStore.clearData(id: appId)
            return ok()

        case "keys":
            let keys = Array(MiniAppStore.storeDefaults(appId)
                .dictionaryRepresentation().keys)
            return ok(["keys": ["list": keys]])

        // ---- 通知（需权限） ----
        case "notify":
            guard MiniAppStore.hasPermission(id: appId, key: MiniAppStore.permNotification) else {
                return denied()
            }
            let title = (args["title"] as? String) ?? appId
            let body = (args["body"] as? String) ?? ""
            return NotificationGate.notify(title: title, body: body)

        // ---- 设备 ----
        case "vibrate":
            guard MiniAppStore.hasPermission(id: appId, key: MiniAppStore.permVibrate) else {
                return denied()
            }
            // iOS 无时长振动：按 Android 的 ms 语义映射触觉反馈轻重档
            let ms = (args["ms"] as? Double) ?? 200
            let style: UIImpactFeedbackGenerator.FeedbackStyle =
                ms < 300 ? .light : (ms < 700 ? .medium : .heavy)
            DispatchQueue.main.async {
                let g = UIImpactFeedbackGenerator(style: style)
                g.prepare()
                g.impactOccurred()
            }
            return ok()

        case "toast":
            let message = (args["message"] as? String) ?? ""
            DispatchQueue.main.async {
                let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
                alert.addAction(UIAlertAction(title: "好", style: .cancel))
                TopMOST.present(alert)
            }
            return ok()

        case "deviceInfo":
            let size = UIScreen.main.bounds.size
            let ver = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
            return ok([
                "model": UIDevice.current.model,
                "brand": "Apple",
                "osVersion": "iOS \(UIDevice.current.systemVersion)",
                "appVersion": ver,
                "screen": "\(Int(size.width))x\(Int(size.height))"
            ])

        // ---- 剪贴板（需权限） ----
        case "copyText":
            guard MiniAppStore.hasPermission(id: appId, key: MiniAppStore.permClipboard) else {
                return denied()
            }
            UIPasteboard.general.string = (args["text"] as? String) ?? ""
            return ok()

        case "pasteText":
            guard MiniAppStore.hasPermission(id: appId, key: MiniAppStore.permClipboard) else {
                return denied()
            }
            return ok(["text": UIPasteboard.general.string ?? NSNull()])

        // ---- 分享（免权限） ----
        case "share":
            let title = (args["title"] as? String) ?? ""
            let text = (args["text"] as? String) ?? ""
            DispatchQueue.main.async {
                let items: [Any] = text.isEmpty ? [title] : ["\(title)\n\(text)"]
                let vc = UIActivityViewController(activityItems: items, applicationActivities: nil)
                TopMOST.present(vc)
            }
            return ok()

        default:
            return fail("unknown_command")
        }
    }
}

// MARK: - 通知

enum NotificationGate {
    static func notify(title: String, body: String) -> [String: Any] {
        let center = UNUserNotificationCenter.current()
        var result: [String: Any] = fail("system_notification_disabled")
        let semaphore = DispatchSemaphore(value: 0)
        center.getNotificationSettings { settings in
            if settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional {
                let content = UNMutableNotificationContent()
                content.title = title
                content.body = body
                content.sound = .default
                let req = UNNotificationRequest(identifier: UUID().uuidString,
                                                content: content, trigger: nil)
                center.add(req) { error in
                    result = error == nil ? BridgeCoreOk.ok() : BridgeCoreOk.fail("system_notification_disabled")
                    semaphore.signal()
                }
            } else {
                semaphore.signal()
            }
        }
        semaphore.wait()
        return result
    }
}

// 防 Swift 命名冲突的小马甲（BridgeCore 私有 ok/fail 复用）
private enum BridgeCoreOk {
    static func ok() -> [String: Any] { ["ok": true] }
    static func fail(_ e: String) -> [String: Any] { ["ok": false, "error": e] }
}

// MARK: - 定位（异步：message handler + CLLocationManager）

final class GeoBroker: NSObject, CLLocationManagerDelegate, WKScriptMessageHandler {

    private let manager = CLLocationManager()
    private var pending: [(webView: WKWebView, id: String)] = []
    private var lastLocation: CLLocation?
    private weak var attachedWebView: WKWebView?

    func attach(to configuration: WKWebViewConfiguration) {
        configuration.userContentController.add(self, name: "boxGeo")
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyHundredMeters
        attachedWebView = nil
    }

    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any],
              let id = body["id"] as? String else { return }
        let appId = WebViewHost.currentAppId ?? ""
        guard MiniAppStore.hasPermission(id: appId, key: MiniAppStore.permGeolocation) else {
            reply(webView: message.webView, id: id,
                  json: #"{"ok":false,"error":"permission_denied"}"#)
            return
        }

        let status = CLLocationManager.authorizationStatus
        if status == .notDetermined {
            pending.append((message.webView, id))
            manager.requestWhenInUseAuthorization()
            return
        }
        if status == .denied || status == .restricted {
            reply(webView: message.webView, id: id,
                  json: #"{"ok":false,"error":"system_location_disabled"}"#)
            return
        }
        pending.append((message.webView, id))
        if let loc = lastLocation {
            deliver(loc)
        }
        manager.requestLocation()
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        lastLocation = locations.last
        deliver(lastLocation)
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        flushAll(json: #"{"ok":false,"error":"location_failed"}"#)
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        if status == .authorizedWhenInUse || status == .authorizedAlways {
            manager.requestLocation()
        } else if status == .denied {
            flushAll(json: #"{"ok":false,"error":"system_location_disabled"}"#)
        }
    }

    private func deliver(_ location: CLLocation?) {
        guard let loc = location, !pending.isEmpty else { return }
        let items = pending
        pending.removeAll()
        for item in items {
            let pos: [String: Any] = [
                "ok": true,
                "pos": [
                    "coords": [
                        "latitude": loc.coordinate.latitude,
                        "longitude": loc.coordinate.longitude,
                        "accuracy": loc.horizontalAccuracy
                    ]
                ]
            ]
            let data = (try? JSONSerialization.data(withJSONObject: pos)) ?? Data()
            let json = String(data: data, encoding: .utf8) ?? "{}"
            reply(webView: item.webView, id: item.id, json: json)
        }
    }

    private func flushAll(json: String) {
        let items = pending
        pending.removeAll()
        for item in items { reply(webView: item.webView, id: item.id, json: json) }
    }

    private func reply(webView: WKWebView?, id: String, json: String) {
        DispatchQueue.main.async {
            webView?.evaluateJavaScript(BridgeInjection.geoCallbackJS(id: id, json: json),
                                        completionHandler: nil)
        }
    }
}

// MARK: - 工具

enum WebViewHost {
    /// 当前正在运行的小程序 id（RunView 打开时设置）
    static var currentAppId: String?
}

enum TopMOST {
    static func present(_ vc: UIViewController) {
        DispatchQueue.main.async {
            var top = UIApplication.shared.connectedScenes
                .compactMap { ($0 as? UIWindowScene)?.keyWindow?.rootViewController }
                .first
            while let p = top?.presentedViewController { top = p }
            top?.present(vc, animated: true)
        }
    }
}
