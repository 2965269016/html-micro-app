//
//  MiniAppStore.swift
//  MiniAppBox
//
//  小程序仓库：导入 / 列表 / 删除 / 权限 / 存储统计 / 打包导出。
//  与 Android 版（AppRepo.kt）逻辑与包格式完全对齐。
//

import Foundation

struct MiniApp: Identifiable, Equatable {
    let id: String
    let name: String
    let installedAt: Date
}

enum MiniAppError: LocalizedError {
    case missingIndex
    case unreadable
    var errorDescription: String? {
        switch self {
        case .missingIndex: return "包内根目录缺少 index.html"
        case .unreadable: return "无法读取所选文件"
        }
    }
}

final class MiniAppStore: ObservableObject {

    static let shared = MiniAppStore()

    static let permNotification = "notification"
    static let permVibrate = "vibrate"
    static let permClipboard = "clipboard"
    static let permGeolocation = "geolocation"
    static let allPerms = [permNotification, permVibrate, permClipboard, permGeolocation]

    static let dataDirName = "__data__"
    static let metaDirName = "__meta__"

    @Published var apps: [MiniApp] = []

    let root: URL   // Documents/apps

    private init() {
        let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        root = docs.appendingPathComponent("apps", isDirectory: true)
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        reload()
    }

    // MARK: - 路径

    static func appDir(_ id: String) -> URL {
        shared.root.appendingPathComponent(id, isDirectory: true)
    }

    static func storeDefaults(_ id: String) -> UserDefaults {
        UserDefaults(suiteName: "store_\(id)") ?? .standard
    }

    // MARK: - 列表

    func reload() {
        let fm = FileManager.default
        let dirs = (try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: nil)) ?? []
        apps = dirs
            .filter { $0.hasDirectoryPath }
            .compactMap { url -> MiniApp? in
                guard url.lastPathComponent.hasPrefix("app_") else { return nil }
                return Self.loadMeta(url)
            }
            .sorted { $0.installedAt > $1.installedAt }
    }

    private static func loadMeta(_ dir: URL) -> MiniApp? {
        let metaFile = dir.appendingPathComponent("meta.json")
        guard let data = try? Data(contentsOf: metaFile),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return MiniApp(id: dir.lastPathComponent,
                           name: dir.lastPathComponent,
                           installedAt: Date(timeIntervalSince1970: 0))
        }
        let ts = (json["installedAt"] as? Double) ?? 0
        return MiniApp(id: dir.lastPathComponent,
                       name: (json["name"] as? String) ?? dir.lastPathComponent,
                       installedAt: Date(timeIntervalSince1970: ts / 1000))
    }

    // MARK: - 导入

    /// 从文件选择器导入（处理 security scope）。
    @discardableResult
    func importURL(_ url: URL, fallbackName: String) throws -> MiniApp {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url) else { throw MiniAppError.unreadable }
        return importData(data, fallbackName: fallbackName)
    }

    /// 从内存导入（内置示例 / 文件选择器 / URL 导入共用）。
    /// subdir 非空时只导入该子目录（URL 导入 /tree/分支/子目录 用）。
    @discardableResult
    func importData(_ data: Data, fallbackName: String, subdir: String = "") throws -> MiniApp {
        var entries = try ZipArchive.read(data: data)
        if !subdir.isEmpty {
            let prefix = subdir + "/"
            entries = entries.compactMap { e in
                e.path.hasPrefix(prefix)
                    ? ZipEntryData(path: String(e.path.dropFirst(prefix.count)), data: e.data) : nil
            }
        }
        try normalizeSingleRoot(&entries)
        guard entries.contains(where: { $0.path == "index.html" }) else {
            throw MiniAppError.missingIndex
        }
        return try importEntries(entries, fallbackName: fallbackName)
    }

    /// 核心写入：把一批已就位的条目落盘为一个小程序（URL 多应用导入也走这里）。
    @discardableResult
    func importEntries(_ entries: [ZipEntryData], fallbackName: String) throws -> MiniApp {
        let id = "app_" + Self.timestamp()
        let dest = Self.appDir(id)
        try FileManager.default.createDirectory(at: dest, withIntermediateDirectories: true)

        var manifestName: String?
        for entry in entries {
            if entry.path.hasPrefix(Self.dataDirName + "/") || entry.path == "meta.json" {
                if entry.path == Self.dataDirName + "/store.json" {
                    Self.restoreStore(id: id, json: entry.data)
                }
                continue
            }
            let target = dest.appendingPathComponent(entry.path)
            try FileManager.default.createDirectory(
                at: target.deletingLastPathComponent(), withIntermediateDirectories: true
            )
            try entry.data.write(to: target)
            if entry.path == "manifest.json",
               let json = try? JSONSerialization.jsonObject(with: entry.data) as? [String: Any],
               let n = json["name"] as? String, !n.isEmpty {
                manifestName = n
            }
        }

        let cleanName = fallbackName
            .replacingOccurrences(of: ".zip", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let app = MiniApp(id: id,
                          name: manifestName ?? (cleanName.isEmpty ? "未命名小程序" : cleanName),
                          installedAt: Date())
        saveMeta(app)
        reload()
        return app
    }

    /// 若只有一个顶层目录且根级无 index.html，把该目录内容上移一层（协议第 1 节）。
    private func normalizeSingleRoot(_ entries: inout [ZipEntryData]) {
        guard !entries.contains(where: { $0.path == "index.html" }) else { return }
        let topDirs = Set(entries.compactMap { $0.path.split(separator: "/").first.map(String.init) })
        let topFiles = entries.filter { !$0.path.contains("/") }
        if topDirs.count == 1 && topFiles.isEmpty {
            let prefix = topDirs.first! + "/"
            entries = entries.map { e in
                e.path.hasPrefix(prefix)
                    ? ZipEntryData(path: String(e.path.dropFirst(prefix.count)), data: e.data) : e
            }
        }
    }

    /// 载入随 App 内置的示例包。
    @discardableResult
    func importDemo() throws -> MiniApp {
        guard let url = Bundle.main.url(forResource: "demo", withExtension: "zip"),
              let data = try? Data(contentsOf: url) else {
            throw MiniAppError.unreadable
        }
        return try importData(data, fallbackName: "内置示例")
    }

    private static func timestamp() -> String {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd_HHmmss"
        f.locale = Locale(identifier: "en_US_POSIX")
        return f.string(from: Date())
    }

    // MARK: - 元数据 / 权限

    private func saveMeta(_ app: MiniApp) {
        let json: [String: Any] = [
            "id": app.id,
            "name": app.name,
            "installedAt": app.installedAt.timeIntervalSince1970 * 1000,
            "permissions": Self.permissions(id: app.id)
        ]
        if let data = try? JSONSerialization.data(withJSONObject: json, options: [.prettyPrinted]) {
            try? data.write(to: Self.appDir(app.id).appendingPathComponent("meta.json"))
        }
    }

    static func permissions(id: String) -> [String: Bool] {
        let metaFile = appDir(id).appendingPathComponent("meta.json")
        guard let data = try? Data(contentsOf: metaFile),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let perms = json["permissions"] as? [String: Bool] else { return [:] }
        return perms
    }

    static func hasPermission(id: String, key: String) -> Bool {
        permissions(id: id)[key] ?? false
    }

    static func setPermission(id: String, key: String, value: Bool) {
        let metaFile = appDir(id).appendingPathComponent("meta.json")
        var json = (try? JSONSerialization.jsonObject(with: Data(contentsOf: metaFile))
                    as? [String: Any]) ?? [String: Any]()
        var perms = (json["permissions"] as? [String: Bool]) ?? [:]
        perms[key] = value
        json["permissions"] = perms
        if let data = try? JSONSerialization.data(withJSONObject: json, options: [.prettyPrinted]) {
            try? data.write(to: metaFile)
        }
    }

    // MARK: - 删除 / 存储

    func delete(_ app: MiniApp) {
        try? FileManager.default.removeItem(at: Self.appDir(app.id))
        Self.storeDefaults(app.id).removePersistentDomain(forName: "store_\(app.id)")
        reload()
    }

    static func usageBytes(id: String) -> Int64 {
        let fm = FileManager.default
        var total: Int64 = 0
        if let en = fm.enumerator(at: appDir(id), includingPropertiesForKeys: [.fileSizeKey]) {
            for case let url as URL in en {
                total += Int64((try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0)
            }
        }
        let defaults = storeDefaults(id)
        defaults.dictionaryRepresentation().forEach { _, v in
            if let s = v as? String { total += Int64(s.utf8.count) }
            else if let d = v as? Data { total += Int64(d.count) }
            else { total += 64 }
        }
        return total
    }

    static func clearData(id: String) {
        let d = storeDefaults(id)
        d.dictionaryRepresentation().keys.forEach { d.removeObject(forKey: $0) }
    }

    /// JS 存储快照（导出用）。
    static func storeJSON(id: String) -> Data? {
        let dict = storeDefaults(id).dictionaryRepresentation()
            .filter { $0.value is String }
            .mapValues { $0 as! String }
        return try? JSONSerialization.data(withJSONObject: dict, options: [.prettyPrinted])
    }

    private static func restoreStore(id: String, json: Data) {
        guard let dict = try? JSONSerialization.jsonObject(with: json) as? [String: String] else {
            return
        }
        let d = storeDefaults(id)
        dict.forEach { d.set($0.value, forKey: $0.key) }
    }

    // MARK: - 打包导出（HTML + __meta__/meta.json + __data__/store.json）

    static func exportPackage(_ app: MiniApp) throws -> Data {
        let dir = appDir(app.id)
        let fm = FileManager.default
        var entries: [ZipEntryData] = []

        if let en = fm.enumerator(at: dir, includingPropertiesForKeys: nil) {
            for case let url as URL in en where url.hasDirectoryPath == false {
                let rel = url.path.replacingOccurrences(of: dir.path + "/", with: "")
                if rel == "meta.json" || rel.hasPrefix(dataDirName + "/") { continue }
                if let data = try? Data(contentsOf: url) {
                    entries.append(ZipEntryData(path: rel, data: data))
                }
            }
        }

        let metaFile = dir.appendingPathComponent("meta.json")
        if let metaData = try? Data(contentsOf: metaFile) {
            entries.append(ZipEntryData(path: metaDirName + "/meta.json", data: metaData))
        }
        if let storeData = storeJSON(id: app.id) {
            entries.append(ZipEntryData(path: dataDirName + "/store.json", data: storeData))
        }
        return ZipArchive.write(entries: entries)
    }
}
