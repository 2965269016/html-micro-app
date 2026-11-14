//
//  IconGen.swift
//  MiniAppBox
//
//  首字 + 配色圆形动态图标（与 Android IconGenerator 同规则）。
//

import UIKit

enum IconGen {

    private static let palette: [UIColor] = [
        UIColor(hex: 0xFF6B6B),
        UIColor(hex: 0x4ECDC4),
        UIColor(hex: 0x45B7D1),
        UIColor(hex: 0x96CEB4),
        UIColor(hex: 0xFFEAA7),
        UIColor(hex: 0xDDA0DD),
        UIColor(hex: 0x98D8C8),
        UIColor(hex: 0xF7DC6F),
        UIColor(hex: 0xBB8FCE),
        UIColor(hex: 0x85C1E9)
    ]

    static func image(name: String, size: CGFloat = 108) -> UIImage {
        let first = String(name.first.map(String.init) ?? "?")
        // 协议第 6 节：哈希算法 = Java String.hashCode()，保证同名跨端同色
        let color = palette[abs(javaHash(first)) % palette.count]

        let renderer = UIGraphicsImageRenderer(size: CGSize(width: size, height: size))
        return renderer.image { ctx in
            color.setFill()
            ctx.fill(CGRect(x: 0, y: 0, width: size, height: size))

            let attrs: [NSAttributedString.Key: Any] = [
                .font: UIFont.systemFont(ofSize: size * 0.45, weight: .semibold),
                .foregroundColor: UIColor.white
            ]
            let text = first as NSString
            let textSize = text.size(withAttributes: attrs)
            text.draw(
                at: CGPoint(x: (size - textSize.width) / 2,
                            y: (size - textSize.height) / 2),
                withAttributes: attrs
            )
        }
    }
}

extension UIColor {
    convenience init(hex: UInt32) {
        self.init(
            red: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1
        )
    }
}

/// Java String.hashCode() 的 Swift 等价实现（协议第 6 节图标同色契约）
func javaHash(_ s: String) -> Int32 {
    var h: Int32 = 0
    for unit in s.utf16 {
        h = (h &* 31) &+ Int32(unit)
    }
    return h
}
