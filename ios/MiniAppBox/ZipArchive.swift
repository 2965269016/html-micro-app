//
//  ZipArchive.swift
//  MiniAppBox
//
//  纯 Swift + Apple Compression 框架实现的 ZIP 读写（零第三方依赖）。
//  支持读取 method 0（store）/ 8（deflate），写入 deflate。
//  包格式与 Android 版（AppRepo）完全兼容。
//

import Foundation
import Compression

enum ZipError: Error, LocalizedError {
    case corrupt(String)
    case unsupported(String)

    var errorDescription: String? {
        switch self {
        case .corrupt(let m): return "压缩包损坏：\(m)"
        case .unsupported(let m): return "不支持的压缩包：\(m)"
        }
    }
}

struct ZipEntryData {
    let path: String
    let data: Data
}

enum ZipArchive {

    // MARK: - CRC32

    private static let crcTable: [UInt32] = {
        (0..<256).map { i -> UInt32 in
            var c: UInt32 = UInt32(i)
            for _ in 0..<8 {
                c = (c & 1) != 0 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1)
            }
            return c
        }
    }()

    static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFFFFFF
        for byte in data {
            crc = crcTable[Int((crc ^ UInt32(byte)) & 0xFF)] ^ (crc >> 8)
        }
        return crc ^ 0xFFFFFFFF
    }

    // MARK: - 读取

    /// 读取 zip 全部条目（跳过目录条目）。
    static func read(data: Data) throws -> [ZipEntryData] {
        let bytes = [UInt8](data)
        guard let eocd = findEOCD(bytes) else {
            throw ZipError.corrupt("找不到 End of Central Directory")
        }

        func u16(_ off: Int) -> Int { Int(bytes[off]) | (Int(bytes[off + 1]) << 8) }
        func u32(_ off: Int) -> Int {
            Int(bytes[off])
                | (Int(bytes[off + 1]) << 8)
                | (Int(bytes[off + 2]) << 16)
                | (Int(bytes[off + 3]) << 24)
        }

        let totalEntries = u16(eocd + 10)
        let cdOffset = u32(eocd + 16)

        var results: [ZipEntryData] = []
        var cursor = cdOffset

        for _ in 0..<totalEntries {
            guard cursor + 46 <= bytes.count, u32(cursor) == 0x02014B50 else {
                throw ZipError.corrupt("Central Directory 签名错误")
            }
            let flags = u16(cursor + 8)
            let method = u16(cursor + 10)
            let compSize = u32(cursor + 20)
            let nameLen = u16(cursor + 28)
            let extraLen = u16(cursor + 30)
            let commentLen = u16(cursor + 32)
            let localOffset = u32(cursor + 42)

            let nameData = Data(bytes[(cursor + 46)..<(cursor + 46 + nameLen)])
            guard let name = String(data: nameData, encoding: .utf8)
                    ?? String(data: nameData, encoding: .isoLatin1) else {
                throw ZipError.corrupt("文件名解码失败")
            }

            cursor += 46 + nameLen + extraLen + commentLen

            // 目录条目 / 加密 / macOS 元数据目录跳过
            if name.hasSuffix("/") || (flags & 0x01) != 0 || name.hasPrefix("__MACOSX/") {
                continue
            }
            if method != 0 && method != 8 {
                throw ZipError.unsupported("压缩方法 \(method)（仅支持 store/deflate）")
            }

            // Local File Header：真实数据起点需读 local 头自己的名字/扩展长度
            guard localOffset + 30 <= bytes.count, u32(localOffset) == 0x04034B50 else {
                throw ZipError.corrupt("Local Header 签名错误")
            }
            let localNameLen = u16(localOffset + 26)
            let localExtraLen = u16(localOffset + 28)
            let dataStart = localOffset + 30 + localNameLen + localExtraLen
            guard dataStart + compSize <= bytes.count else {
                throw ZipError.corrupt("数据越界")
            }

            let compressed = Data(bytes[dataStart..<(dataStart + compSize)])
            let uncompressed: Data
            if method == 0 {
                uncompressed = compressed
            } else {
                uncompressed = try inflate(compressed, expected: -1)
            }

            // 统一路径分隔符，跳过路径穿越
            let normalized = name.replacingOccurrences(of: "\\", with: "/")
            if normalized.contains("..") { continue }
            results.append(ZipEntryData(path: normalized, data: uncompressed))
        }
        return results
    }

    /// 从末尾向前扫描 EOCD 签名（兼容短注释）。
    private static func findEOCD(_ bytes: [UInt8]) -> Int? {
        let sig: [UInt8] = [0x50, 0x4B, 0x05, 0x06]
        let scanStart = max(0, bytes.count - 66_000)
        var i = bytes.count - 22
        while i >= scanStart {
            if [UInt8](bytes[i..<(i + 4)]) == sig { return i }
            i -= 1
        }
        return nil
    }

    // MARK: - 写入

    /// 把条目打包成 zip（deflate 压缩，小于 32 字节或压不动时用 store）。
    static func write(entries: [ZipEntryData]) -> Data {
        var out = Data()
        var central = Data()
        var count = 0

        func dosTime(_ date: Date) -> (Int, Int) {
            let cal = Calendar(identifier: .gregorian)
            let c = cal.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
            let year = max(1980, c.year ?? 1980)
            let d = ((year - 1980) << 9) | ((c.month ?? 1) << 5) | (c.day ?? 1)
            let t = ((c.hour ?? 0) << 11) | ((c.minute ?? 0) << 5) | ((c.second ?? 0) / 2)
            return (d, t)
        }
        let (dosDate, dosTimeV) = dosTime(Date())

        for entry in entries {
            let rawData = entry.data
            let crc = crc32(rawData)
            let stored: Data
            let method: UInt16
            let deflated = deflate(rawData)
            if deflated.count < rawData.count && rawData.count > 32 {
                stored = deflated
                method = 8
            } else {
                stored = rawData
                method = 0
            }

            let nameBytes = Array(entry.path.utf8)

            // Local File Header
            out.appendLE32(0x04034B50)
            out.appendLE16(20)          // version needed
            out.appendLE16(0x0800)      // flags: UTF-8 names
            out.appendLE16(method)
            out.appendLE16(dosTimeV)
            out.appendLE16(dosDate)
            out.appendLE32(Int(crc))
            out.appendLE32(stored.count)
            out.appendLE32(rawData.count)
            out.appendLE16(nameBytes.count)
            out.appendLE16(0)           // extra len
            out.append(contentsOf: nameBytes)
            out.append(stored)

            // Central Directory 记录
            central.appendLE32(0x02014B50)
            central.appendLE16(20)      // version made by
            central.appendLE16(20)      // version needed
            central.appendLE16(0x0800)
            central.appendLE16(method)
            central.appendLE16(dosTimeV)
            central.appendLE16(dosDate)
            central.appendLE32(Int(crc))
            central.appendLE32(stored.count)
            central.appendLE32(rawData.count)
            central.appendLE16(nameBytes.count)
            central.appendLE16(0)       // extra
            central.appendLE16(0)       // comment
            central.appendLE16(0)       // disk start
            central.appendLE16(0)       // internal attrs
            central.appendLE32(0)       // external attrs
            central.appendLE32(out.count) // local header offset（此刻 out 已含 local 条目）
            central.append(contentsOf: nameBytes)

            count += 1
        }

        let cdOffset = out.count
        out.append(central)

        // EOCD
        out.appendLE32(0x06054B50)
        out.appendLE16(0)           // disk
        out.appendLE16(0)           // cd disk
        out.appendLE16(count)       // entries on disk
        out.appendLE16(count)       // entries total
        out.appendLE32(central.count)
        out.appendLE32(cdOffset)
        out.appendLE16(0)           // comment len

        return out
    }

    // MARK: - 压缩 / 解压（Apple Compression = raw deflate）

    private static func deflate(_ data: Data) -> Data {
        guard !data.isEmpty else { return Data() }
        let dstCapacity = data.count + 64
        var dst = Data(count: dstCapacity)
        let written = dst.withUnsafeMutableBytes { dstPtr -> Int in
            data.withUnsafeBytes { srcPtr -> Int in
                compression_encode_buffer(
                    dstPtr.bindMemory(to: UInt8.self).baseAddress!, dstCapacity,
                    srcPtr.bindMemory(to: UInt8.self).baseAddress!, data.count,
                    nil, 0, COMPRESSION_ZLIB
                )
            }
        }
        if written <= 0 { return Data() }
        return dst.prefix(written)
    }

    private static func inflate(_ data: Data, expected: Int) throws -> Data {
        // expected = -1 时从 4KB 起步指数扩容
        var capacity = expected > 0 ? expected : 4096
        while capacity <= 256 * 1024 * 1024 {
            var dst = Data(count: capacity)
            let written = dst.withUnsafeMutableBytes { dstPtr -> Int in
                data.withUnsafeBytes { srcPtr -> Int in
                    compression_decode_buffer(
                        dstPtr.bindMemory(to: UInt8.self).baseAddress!, capacity,
                        srcPtr.bindMemory(to: UInt8.self).baseAddress!, data.count,
                        nil, COMPRESSION_ZLIB
                    )
                }
            }
            // 放不下时 written == 0，扩容重试
            if written > 0 {
                return dst.prefix(written)
            }
            capacity *= 4
        }
        throw ZipError.corrupt("解压失败（数据过大或损坏）")
    }
}

private extension Data {
    mutating func appendLE16(_ v: Int) {
        append(UInt8(v & 0xFF))
        append(UInt8((v >> 8) & 0xFF))
    }
    mutating func appendLE32(_ v: Int) {
        append(UInt8(v & 0xFF))
        append(UInt8((v >> 8) & 0xFF))
        append(UInt8((v >> 16) & 0xFF))
        append(UInt8((v >> 24) & 0xFF))
    }
}
