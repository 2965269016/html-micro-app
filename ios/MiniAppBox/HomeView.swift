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
                    Button {
                        showImporter = true
                    } label: {
                        Label("导入 HTML 包", systemImage: "square.and.arrow.down")
                    }
                }
            }
        }
        .environmentObject(appState)
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
