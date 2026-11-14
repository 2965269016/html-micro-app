//
//  MiniAppBoxApp.swift
//  MiniAppBox
//
//  App 入口：SwiftUI 生命周期 + Quick Actions（长按图标直达小程序）。
//

import SwiftUI
import UIKit

@main
struct MiniAppBoxApp: App {

    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var launchState = LaunchState.shared

    var body: some Scene {
        WindowGroup {
            RootChooser()
                .environmentObject(launchState)
        }
    }
}

/// 全局待打开的小程序（Quick Action / 冷启动路由）
final class LaunchState: ObservableObject {
    static let shared = LaunchState()
    @Published var pendingAppId: String?
}

/// 首页 或 运行页
struct RootChooser: View {
    @EnvironmentObject private var launchState: LaunchState

    var body: some View {
        if let id = launchState.pendingAppId,
           let app = findApp(id) {
            RunView(app: app)
        } else {
            HomeView()
        }
    }

    private func findApp(_ id: String) -> MiniApp? {
        MiniAppStore.shared.apps.first { $0.id == id }
    }
}

final class AppDelegate: NSObject, UIApplicationDelegate, ObservableObject {

    func application(
        _ application: UIApplication,
        configurationForConnecting connectingSceneSession: UISceneSession,
        options: UIScene.ConnectionOptions
    ) -> UISceneConfiguration {
        let config = UISceneConfiguration(name: nil, sessionRole: connectingSceneSession.role)
        config.delegateClass = SceneDelegate.self
        return config
    }
}

final class SceneDelegate: NSObject, UIWindowSceneDelegate {

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession,
               options connectionOptions: UIScene.ConnectionOptions) {
        if let item = connectionOptions.shortcutItem,
           let id = QuickActions.appId(from: item) {
            LaunchState.shared.pendingAppId = id
        }
    }

    func windowScene(_ windowScene: UIWindowScene,
                     performActionFor shortcutItem: UIApplicationShortcutItem,
                     completionHandler: @escaping (Bool) -> Void) {
        if let id = QuickActions.appId(from: shortcutItem) {
            LaunchState.shared.pendingAppId = id
            completionHandler(true)
        } else {
            completionHandler(false)
        }
    }
}
