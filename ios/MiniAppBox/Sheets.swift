//
//  Sheets.swift
//  MiniAppBox
//
//  权限管理 / 存储管理 / 系统分享（与 Android 长按菜单功能对齐）。
//

import SwiftUI
import UIKit
import UserNotifications
import CoreLocation

// ------------------------------------------------------------ 权限管理

struct PermissionsSheet: View {

    let app: MiniApp
    @Environment(\.dismiss) private var dismiss

    @State private var notification = false
    @State private var vibrate = false
    @State private var clipboard = false
    @State private var geolocation = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Toggle(isOn: $notification) { Text("通知") }
                    Toggle(isOn: $vibrate) { Text("振动") }
                    Toggle(isOn: $clipboard) { Text("剪贴板") }
                    Toggle(isOn: $geolocation) { Text("定位") }
                } footer: {
                    Text("这些权限仅授予「\(app.name)」，随时可以关闭。")
                }
            }
            .navigationTitle("权限管理")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("保存") { save() }
                }
            }
        }
        .onAppear(perform: load)
        .presentationDetents([.medium])
    }

    private func load() {
        let perms = MiniAppStore.permissions(id: app.id)
        notification = perms[MiniAppStore.permNotification] ?? false
        vibrate = perms[MiniAppStore.permVibrate] ?? false
        clipboard = perms[MiniAppStore.permClipboard] ?? false
        geolocation = perms[MiniAppStore.permGeolocation] ?? false
    }

    private func save() {
        MiniAppStore.setPermission(id: app.id, key: MiniAppStore.permNotification, value: notification)
        MiniAppStore.setPermission(id: app.id, key: MiniAppStore.permVibrate, value: vibrate)
        MiniAppStore.setPermission(id: app.id, key: MiniAppStore.permClipboard, value: clipboard)
        MiniAppStore.setPermission(id: app.id, key: MiniAppStore.permGeolocation, value: geolocation)

        // 系统层授权请求（Android 同款体验）
        if notification {
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in }
        }
        if geolocation {
            CLLocationManager().requestWhenInUseAuthorization()
        }
        dismiss()
    }
}

// ------------------------------------------------------------ 存储管理

struct StorageSheet: View {

    let app: MiniApp
    @Binding var exportShare: SharePackage?
    @Environment(\.dismiss) private var dismiss

    @State private var usage: Int64 = 0
    @State private var confirmClear = false

    var body: some View {
        NavigationStack {
            Form {
                Section("占用") {
                    LabeledContent("总占用", value: humanSize(usage))
                }
                Section {
                    Button {
                        exportShare = export()
                    } label: {
                        Label("应用+数据 打包导出", systemImage: "square.and.arrow.up")
                    }
                    Button(role: .destructive) {
                        confirmClear = true
                    } label: {
                        Label("清除数据", systemImage: "trash")
                    }
                } footer: {
                    Text("打包导出：应用与数据一并导出为 zip，可在任意设备（含 Android 版）导入还原。\n清除数据：删除该小程序保存的全部 JS 数据。")
                }
            }
            .navigationTitle("存储管理")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("完成") { dismiss() }
                }
            }
            .alert("确认清除", isPresented: $confirmClear) {
                Button("清除", role: .destructive) {
                    MiniAppStore.clearData(id: app.id)
                    usage = MiniAppStore.usageBytes(id: app.id)
                }
                Button("取消", role: .cancel) {}
            } message: {
                Text("将删除「\(app.name)」保存的全部 JS 数据，且不可恢复。")
            }
        }
        .onAppear { usage = MiniAppStore.usageBytes(id: app.id) }
        .presentationDetents([.medium])
    }

    private func humanSize(_ bytes: Int64) -> String {
        let kb = 1024.0, mb = kb * 1024.0
        if Double(bytes) >= mb { return String(format: "%.1f MB", Double(bytes) / mb) }
        if Double(bytes) >= kb { return String(format: "%.1f KB", Double(bytes) / kb) }
        return "\(bytes) B"
    }

    private func export() -> SharePackage? {
        do {
            let data = try MiniAppStore.exportPackage(app)
            let safe = app.name.replacingOccurrences(
                of: "[\\\\/:*?\"<>|]", with: "_", options: .regularExpression
            )
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("\(safe).zip")
            try data.write(to: url)
            return SharePackage(url: url)
        } catch {
            return nil
        }
    }
}

// ------------------------------------------------------------ 系统分享

struct ShareSheet: UIViewControllerRepresentable {

    let items: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    func updateUIViewController(_ vc: UIActivityViewController, context: Context) {}
}
