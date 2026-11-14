# iOS 版「小程序盒子」安装指南

> 与 Android 版功能对齐：导入 zip → 列表 → 点开即用 → Native JS 桥（通知/振动/剪贴板/
> 分享/定位/本地存储，按小程序独立授权）→ 应用+数据一键打包。

## 为什么不能像 APK 一样直接装？

iOS 系统要求所有 App 都必须经过 **Apple 签名** 才能安装。云构建产出的包是**未签名**
的（因为签名需要你本人的 Apple ID 证书），所以需要下面一步「侧载」把签名补上。

免费 Apple ID 即可完成，无需付费开发者账号。

---

## 安装步骤（Windows 电脑 + 数据线，约 10 分钟）

### 准备

1. 一台 Windows 电脑 + iPhone 数据线
2. iPhone 上装好 iTunes（或至少安装 Apple 设备驱动，装 iTunes 自动搞定）
   下载：https://www.apple.com.cn/itunes/
3. 下载 [Sideloadly](https://sideloadly.io)（Windows 版）并安装
4. 从 [GitHub Releases](https://github.com/2965269016/html-micro-app/releases) 或
   Actions 制品下载 `MiniAppBox-x.x.x-unsigned.zip`

### 侧载

1. iPhone 连上电脑，手机上点「信任此电脑」
2. 打开 Sideloadly：
   - **IPA / APP 文件**：选下载的 zip（Sideloadly 能直接识别）或先解压取 `Payload/MiniAppBox.app`
   - **Apple ID**：填你的 Apple ID 邮箱
   - 点 **Start**
3. 完成后 iPhone 桌面出现「小程序盒子」图标

### 信任开发者证书（首次必做）

手机上第一次打开会提示「不受信任的开发者」：

> 设置 → 通用 → VPN 与设备管理 → 找到你的 Apple ID → 点「信任」

### 有效期

免费证书 **7 天**有效。到期后 App 打不开，重新用 Sideloadly 签一次即可（数据保留）。
想免去重签：AltStore（装在手机上可自动续签），或购买 Apple Developer（$99/年，长期有效）。

---

## 开发者路径（可选）

有 Mac 的话直接用 Xcode 打开 `ios/MiniAppBox.xcodeproj`：

```bash
xcodebuild -project ios/MiniAppBox.xcodeproj -target MiniAppBox \
  -configuration Release -sdk iphoneos -destination 'generic/platform=iOS' build
```

有 Apple Developer 账号的话，在 CI 里配置证书即可产出可长期安装的签名包。

---

## 云构建说明

`.github/workflows/ios-build.yml`（macos-14 runner）：

1. **模拟器构建**：先编译一遍快速暴露 Swift 错误
2. **真机未签名构建**：`xcodebuild CODE_SIGNING_ALLOWED=NO` → `MiniAppBox.app`
3. 打包成 `MiniAppBox-x.x.x-unsigned.zip`（Payload 结构，Sideloadly 直接可用）
4. 发布到 Release（tag `ios-build-0.1.0`）+ Actions 制品

## 目录结构

```
ios/
├── MiniAppBox.xcodeproj/project.pbxproj   手写工程文件（无签名配置）
└── MiniAppBox/
    ├── MiniAppBoxApp.swift   入口 + Quick Actions（长按图标直达，最多 4 个）
    ├── HomeView.swift        首页：列表/导入/长按管理（SwiftUI）
    ├── RunView.swift         运行时：WKWebView + 悬浮返回键
    ├── MiniAppStore.swift    数据层（导入/权限/存储/打包，与 Android 对齐）
    ├── NativeBridge.swift    box:// 内容映射 + nativebridge:// 同步 JS 桥 + 定位垫片
    ├── ZipArchive.swift      纯 Swift zip 读写（零第三方依赖）
    ├── IconGen.swift         首字圆形动态图标
    ├── Sheets.swift          权限/存储/分享弹层
    ├── Info.plist / Assets.xcassets / Resources/demo.zip
```

## JS 桥接说明（与 Android 一致）

`Native.*` API 完全相同（`LocalStore` 为别名）。iOS 通过 **`nativebridge://` 自定义
scheme + 同步 XHR 垫片**实现同步返回，HTML 代码无需任何平台判断 —— 用
`html-miniapp-dev` skill 写的包 Android/iOS 通用。
