//
//  RunView.swift
//  MiniAppBox
//
//  小程序运行时：WKWebView + box:// 内容映射 + nativebridge:// 同步桥
//  + 悬浮返回键（与 Android 版交互一致，无标题栏）。
//

import SwiftUI
import WebKit

struct RunView: View {

    let app: MiniApp
    @Environment(\.dismiss) private var dismiss

    @State private var canGoBack = false
    @State private var filePicker: FilePickerPackage?

    var body: some View {
        ZStack(alignment: .topLeading) {
            WebViewContainer(
                app: app,
                canGoBack: $canGoBack,
                filePicker: $filePicker,
                onExit: { dismiss() }
            )
            .ignoresSafeArea(.container, edges: .bottom)

            // 悬浮返回键
            Button {
                if canGoBack {
                    WebViewContainer.host?.goBack()
                } else {
                    dismiss()
                }
            } label: {
                Image(systemName: "chevron.left")
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(.black.opacity(0.4), in: Circle())
            }
            .padding(.leading, 10)
            .padding(.top, 6)
        }
        .navigationBarBackButtonHidden(true)
        .navigationTitle("")
        .statusBarHidden(false)
        .sheet(item: $filePicker) { pkg in
            DocumentPickerWrapper(package: pkg)
        }
    }
}

// ------------------------------------------------------------ WKWebView 容器

struct WebViewContainer: UIViewRepresentable {

    let app: MiniApp
    @Binding var canGoBack: Bool
    @Binding var filePicker: FilePickerPackage?
    let onExit: () -> Void

    static var host: WebViewHostRef?

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.preferences.javaScriptEnabled = true

        // 内容 scheme
        config.setURLSchemeHandler(ContentSchemeHandler(), forURLScheme: "box")
        // 同步桥 scheme
        config.setURLSchemeHandler(BridgeSchemeHandler(), forURLScheme: "nativebridge")

        config.userContentController.addUserScript(BridgeInjection.script)

        let geo = GeoBroker()
        geo.attach(to: config)

        let webView = WKWebView(frame: .zero, configuration: config)
        webView.navigationDelegate = context.coordinator
        webView.uiDelegate = context.coordinator
        webView.allowsBackForwardNavigationGestures = true
        webView.isOpaque = false
        webView.backgroundColor = .systemBackground

        WebViewHost.currentAppId = app.id
        let hostRef = WebViewHostRef(webView: webView)
        Self.host = hostRef

        let url = URL(string: "box://\(app.id)/index.html")!
        webView.load(URLRequest(url: url))
        return webView
    }

    func updateUIView(_ webView: WKWebView, context: Context) {
        DispatchQueue.main.async {
            canGoBack = webView.canGoBack
        }
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(parent: self)
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate {

        private let parent: WebViewContainer

        init(parent: WebViewContainer) {
            self.parent = parent
        }

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            DispatchQueue.main.async { self.parent.canGoBack = webView.canGoBack }
        }

        // <input type=file>：交给系统文件选择器
        func webView(
            _ webView: WKWebView,
            runFilePickerPanelWith parameters: WKFilePickerParameters,
            initiatedByFrame frame: WKFrameInfo,
            completionHandler: @escaping ([URL]?) -> Void
        ) {
            DispatchQueue.main.async {
                self.parent.filePicker = FilePickerPackage(completion: completionHandler)
            }
        }

        // 定位：权限已在 GeoBroker 中门控（此处放行 JS prompt）
        func webView(
            _ webView: WKWebView,
            requestGeolocationPermissionFor origin: URL,
            completionHandler: @escaping (Bool) -> Void
        ) {
            completionHandler(true)
        }

        func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                     for navigationAction: WKNavigationAction,
                     windowFeatures: WKWindowFeatures) -> WKWebView? {
            nil
        }

        func webView(_ webView: WKWebView,
                     didFailProvisionalNavigation navigation: WKNavigation!,
                     withError error: Error) {
            let html = """
            <html><body style="font-family:-apple-system;padding:24px">
            <h2>页面加载失败</h2><p>该小程序包可能已损坏（缺少 index.html），请回盒子删除后重新导入。</p></body></html>
            """
            webView.loadHTMLString(html, baseURL: nil)
        }
    }
}

final class WebViewHostRef {
    private weak var webView: WKWebView?
    init(webView: WKWebView) { self.webView = webView }
    func goBack() { webView?.goBack() }
    func takeFocus() {}
}

struct FilePickerPackage: Identifiable {
    let id = UUID()
    let completion: ([URL]?) -> Void
}

// ------------------------------------------------------------ 文件选择器桥接

struct DocumentPickerWrapper: UIViewControllerRepresentable {

    let package: FilePickerPackage

    func makeUIViewController(context: Context) -> UIDocumentPickerViewController {
        let picker = UIDocumentPickerViewController(forOpeningContentTypes: [.data], asCopy: true)
        picker.allowsMultipleSelection = true
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ vc: UIDocumentPickerViewController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(package: package) }

    final class Coordinator: NSObject, UIDocumentPickerDelegate {
        let package: FilePickerPackage
        init(package: FilePickerPackage) { self.package = package }

        func documentPicker(_ controller: UIDocumentPickerViewController,
                            didPickDocumentsAt urls: [URL]) {
            package.completion(urls)
        }

        func documentPickerWasCancelled(_ controller: UIDocumentPickerViewController) {
            package.completion(nil)
        }
    }
}
