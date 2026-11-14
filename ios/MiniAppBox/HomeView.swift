//
//  HomeView.swift
//  MiniAppBox
//
//  盒子首页：小程序列表 + 导入 + 长按管理菜单（权限/存储/删除/导出）。
//

import SwiftUI

final class AppState: ObservableObject {
    @Published var runningApp: MiniApp?
}

struct HomeView: View {

    @StateObject private var store = MiniAppStore.shared
    @StateObject private var appState = AppState()

    @State private var showImporter = false
    @State private var permTarget: MiniApp?
    @State private var storageTarget: MiniApp?
    @State private var exportShare: SharePackage?
    @State private var deleteTarget: MiniApp?
    @State private var importError: String?
    @State private var showUrlInput = false
    @State private var urlText = ""
    @State private var importing = false
    @State private var repoPackage: RepoImportPackage?
    @State private var showBadUrl = false
    @State private var importSummary: String?

    var body: some View {
        NavigationStack {
            Group {
                if store.apps.isEmpty {
                    emptyState
                } else {
                    appList
                }
            }
            .navigationTitle("小程序盒子")
            .toolbar {
                ToolbarItemGroup(placement: .topBarTrailing) {
                    Menu {
                        Button {
                            showImporter = true
                        } label: {
                            Label("导入 zip 包", systemImage: "square.and.arrow.down")
                        }
                        Button {
                            showUrlInput = true
                        } label: {
                            Label("从链接导入", systemImage: "link")
                        }
                        Divider()
                        Button {
                            loadDemo()
                        } label: {
                            Label("载入内置示例", systemImage: "sparkles")
                        }
                    } label: {
                        Label("导入", systemImage: "plus.circle.fill")
                    }
                }
            }
        }
        .environmentObject(appState)
        .overlay {
            if importing {
                ZStack {
                    Color.black.opacity(0.25).ignoresSafeArea()
                    VStack(spacing: 12) {
                        ProgressView()
                        Text("解析仓库分支…")
                            .font(.footnote)
                            .foregroundStyle(.white)
                    }
                    .padding(20)
                    .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 14))
                }
            }
        }
        .fileImporter(isPresented: $showImporter,
                      allowedContentTypes: [.zip, .data],
                      allowsMultipleSelection: false) { result in
            switch result {
            case .success(let urls):
                guard let url = urls.first else { return }
                doImport(url)
            case .failure(let error):
                importError = error.localizedDescription
            }
        }
        .alert("从链接导入小程序", isPresented: $showUrlInput) {
            TextField("https://github.com/用户/仓库", text: $urlText)
            Button("导入") {
                let url = urlText.trimmingCharacters(in: .whitespacesAndNewlines)
                if !url.isEmpty { importFromURL(url) }
            }
            Button("取消", role: .cancel) {}
        } message: {
            Text("支持 GitHub / Gitee 仓库链接（可含 /tree/分支/子目录）和 .zip 直链。")
        }
        .sheet(item: $repoPackage) { pkg in
            MultiImportSheet(package: pkg) { selected in
                importCandidates(selected, fallback: pkg.fallbackName)
            }
        }
        .sheet(item: $permTarget) { app in
            PermissionsSheet(app: app)
        }
        .sheet(item: $storageTarget) { app in
            StorageSheet(app: app, exportShare: $exportShare)
        }
        .sheet(item: $exportShare) { pkg in
            ShareSheet(items: [pkg.url])
        }
        .alert("导入失败", isPresented: Binding(
            get: { importError != nil },
            set: { if !$0 { importError = nil } }
        )) {
            Button("知道了", role: .cancel) {}
        } message: {
            Text(importError ?? "")
        }
        .alert("无法识别的链接", isPresented: Binding(
            get: { showBadUrl && importError == nil },
            set: { showBadUrl = $0 }
        )) {
            Button("知道了", role: .cancel) {}
        } message: {
            Text("支持：github.com/用户/仓库、gitee.com/用户/仓库（可带 /tree/分支/子目录），或 .zip 直链。")
        }
        .alert("导入完成", isPresented: Binding(
            get: { importSummary != nil },
            set: { if !$0 { importSummary = nil } }
        )) {
            Button("好", role: .cancel) {}
        } message: {
            Text(importSummary ?? "")
        }
        .alert("删除小程序", isPresented: Binding(
            get: { deleteTarget != nil },
            set: { if !$0 { deleteTarget = nil } }
        )) {
            Button("删除", role: .destructive) {
                if let app = deleteTarget {
                    store.delete(app)
                    QuickActions.sync(apps: store.apps)
                }
            }
            Button("取消", role: .cancel) {}
        } message: {
            Text("将删除「\(deleteTarget?.name ?? "")」及其全部数据，且不可恢复。")
        }
        .onAppear { QuickActions.sync(apps: store.apps) }
    }

    // ------------------------------------------------------------ 列表

    private var appList: some View {
        List {
            ForEach(store.apps) { app in
                Button {
                    appState.runningApp = app
                } label: {
                    AppRow(app: app)
                }
                .buttonStyle(.plain)
                .contextMenu {
                    Button {
                        appState.runningApp = app
                    } label: {
                        Label("打开", systemImage: "play.circle")
                    }
                    Button {
                        permTarget = app
                    } label: {
                        Label("权限管理", systemImage: "lock.shield")
                    }
                    Button {
                        storageTarget = app
                    } label: {
                        Label("存储管理", systemImage: "internaldrive")
                    }
                    Button {
                        exportApp(app)
                    } label: {
                        Label("应用+数据 打包导出", systemImage: "square.and.arrow.up")
                    }
                    Divider()
                    Button(role: .destructive) {
                        deleteTarget = app
                    } label: {
                        Label("删除", systemImage: "trash")
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    private var emptyState: some View {
        VStack(spacing: 16) {
            Image(systemName: "shippingbox")
                .font(.system(size: 56))
                .foregroundStyle(.secondary)
            Text("还没有小程序")
                .font(.body)
                .foregroundStyle(.secondary)
            Text("点右上角「导入」选择 zip 包\n（根目录需有 index.html）")
                .font(.footnote)
                .multilineTextAlignment(.center)
                .foregroundStyle(.tertiary)
            Button {
                loadDemo()
            } label: {
                Label("载入内置示例", systemImage: "sparkles")
            }
            .buttonStyle(.borderedProminent)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // ------------------------------------------------------------ 行为

    private func doImport(_ url: URL) {
        let name = url.deletingPathExtension().lastPathComponent
        do {
            _ = try store.importURL(url, fallbackName: name)
            QuickActions.sync(apps: store.apps)
        } catch {
            importError = error.localizedDescription
        }
    }

    private func loadDemo() {
        do {
            _ = try store.importDemo()
            QuickActions.sync(apps: store.apps)
        } catch {
            importError = error.localizedDescription
        }
    }

    // ------------------------------------------------------------ 从链接导入（GitHub / Gitee / zip 直链）

    private func importFromURL(_ url: String) {
        guard let repo = UrlImporter.parse(url) else {
            showBadUrl = true
            return
        }
        importing = true
        Task {
            do {
                let zipURL = try await UrlImporter.resolveZipURL(repo)
                let data = try await UrlImporter.download(zipURL)
                let entries = try ZipArchive.read(data: data)
                let candidates = try UrlImporter.candidates(in: entries, subPath: repo.subPath)
                await MainActor.run {
                    importing = false
                    if candidates.count == 1 {
                        importCandidates([candidates[0]], fallback: repo.displayName)
                    } else {
                        repoPackage = RepoImportPackage(
                            candidates: candidates, fallbackName: repo.displayName
                        )
                    }
                }
            } catch {
                await MainActor.run {
                    importing = false
                    importError = error.localizedDescription
                }
            }
        }
    }

    private func importCandidates(_ candidates: [RepoCandidate], fallback: String) {
        var imported: [String] = []
        do {
            for c in candidates {
                let app = try store.importEntries(c.entries, fallbackName: fallback)
                imported.append(app.name)
            }
            QuickActions.sync(apps: store.apps)
        } catch {
            importError = error.localizedDescription
            return
        }
        // 轻反馈：复用错误弹层不合适，用列表头部刷新即可；多个时给出摘要
        if imported.count > 1 {
            importSummary = "已导入 \(imported.count) 个：\(imported.joined(separator: "、"))"
        }
    }

    private func exportApp(_ app: MiniApp) {
        do {
            let data = try MiniAppStore.exportPackage(app)
            let safe = app.name.replacingOccurrences(
                of: "[\\\\/:*?\"<>|]", with: "_", options: .regularExpression
            )
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("\(safe).zip")
            try data.write(to: url)
            exportShare = SharePackage(url: url)
        } catch {
            importError = error.localizedDescription
        }
    }
}

// ------------------------------------------------------------ 行组件

struct AppRow: View {
    let app: MiniApp

    var body: some View {
        HStack(spacing: 14) {
            Image(uiImage: IconGen.image(name: app.name))
                .resizable()
                .frame(width: 48, height: 48)
                .clipShape(Circle())
            VStack(alignment: .leading, spacing: 2) {
                Text(app.name)
                    .font(.headline)
                    .foregroundStyle(.primary)
                if app.installedAt.timeIntervalSince1970 > 0 {
                    Text("安装于 \(app.installedAt.formatted(date: .abbreviated, time: .omitted))")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Image(systemName: "chevron.right")
                .font(.footnote)
                .foregroundStyle(.tertiary)
        }
        .padding(.vertical, 2)
    }
}

// ------------------------------------------------------------ 辅助

struct SharePackage: Identifiable {
    let id = UUID()
    let url: URL
}

enum QuickActions {
    /// iOS 没有第三方桌面快捷方式 API，等价物：长按 App 图标的动态 Quick Actions（最多 4 个）
    static func sync(apps: [MiniApp]) {
        let items = apps.prefix(4).map { app -> UIApplicationShortcutItem in
            UIApplicationShortcutItem(
                type: "open_\(app.id)",
                localizedTitle: app.name,
                localizedSubtitle: "小程序",
                icon: UIApplicationShortcutIcon(type: .bookmark),
                userInfo: ["id": app.id as NSSecureCoding]
            )
        }
        UIApplication.shared.shortcutItems = items
    }

    static func appId(from item: UIApplicationShortcutItem) -> String? {
        guard item.type.hasPrefix("open_") else { return nil }
        return item.userInfo?["id"] as? String
    }
}

// ==================================================================== 多应用仓库选择

/// 多应用仓库（URL 导入扫描出多个含 index.html 的目录）的选择与导入
struct RepoImportPackage: Identifiable {
    let id = UUID()
    let candidates: [RepoCandidate]
    let fallbackName: String
}

struct MultiImportSheet: View {

    let package: RepoImportPackage
    let onImport: ([RepoCandidate]) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var selected: Set<String> = []

    var body: some View {
        NavigationStack {
            List(package.candidates) { c in
                Button {
                    if selected.contains(c.id) {
                        selected.remove(c.id)
                    } else {
                        selected.insert(c.id)
                    }
                } label: {
                    HStack(spacing: 12) {
                        Image(systemName: selected.contains(c.id)
                              ? "checkmark.circle.fill" : "circle")
                            .foregroundStyle(selected.contains(c.id)
                                             ? Color.accentColor : .secondary)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(c.suggestedName)
                                .font(.body)
                                .foregroundStyle(.primary)
                            Text(c.path.isEmpty ? "(仓库根)" : c.path)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .buttonStyle(.plain)
            }
            .listStyle(.insetGrouped)
            .navigationTitle("发现 \(package.candidates.count) 个小程序")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("导入所选 (\(selected.count))") {
                        let picked = package.candidates.filter { selected.contains($0.id) }
                        dismiss()
                        if !picked.isEmpty { onImport(picked) }
                    }
                }
            }
        }
        .onAppear { selected = Set(package.candidates.map(\.id)) }
        .interactiveDismissDisabled()
    }
}
