# 更新日志（CHANGELOG）

所有显著变更记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [iOS 0.1.0] - 2026-11-14

### 新增（iOS 端，协议 v1.1 第 1~7 节）

- **完整 Xcode 工程**（`ios/`）：SwiftUI + iOS 16+，零第三方依赖，包格式 / JS 桥 /
  权限模型与 Android·PC 完全对齐 —— **同一个 zip 包三端通用**
  - 首页列表 / fileImporter 导入 / 内置示例 / 长按管理菜单（权限/存储/导出/删除）
  - WKWebView 运行时：`box://<id>/` 自定义 scheme 映射本地目录（等价 WebViewAssetLoader）
  - **同步 JS 桥**：`nativebridge://` scheme + 同步 XHR 垫片，`Native.*` 12 个 API
    同步返回 JSON（与 Android @JavascriptInterface 同语义），参数宽松转换
  - 每小程序独立权限（通知/振动/剪贴板/定位 CLLocationManager 垫片）
  - 纯 Swift `ZipArchive`（Apple Compression + CRC32），迁移格式互导
  - 悬浮返回键、`<input type=file>` 系统选择器、应用+数据打包分享导出
  - 「添加到桌面」等价物：动态 Quick Actions（长按 App 图标直达，最多 4 个）
  - 图标哈希按协议第 6 节对齐 Java `String.hashCode()`（同名三端同色）
- **macOS 云构建**（`.github/workflows/ios-build.yml`）：模拟器编译检查 +
  真机未签名构建 → `MiniAppBox-0.1.0-unsigned.zip` 发布到 Release
- **侧载指南**（`ios/README.md`）：Windows + Sideloadly + 免费 Apple ID 安装步骤
- 协议文档：PROTOCOL.md 增加 iOS 实现声明与平台差异表（第 5 节）

### 已知范围（iOS 0.1.0）

- 第 8/9 节（URL 导入、设备同步）暂未实现，v1.2 候选
- `toast` 用系统短提示呈现（见协议第 5 节差异表）；`vibrate` 映射触觉反馈轻重档

## [0.2.0] - 2026-11-08（手机端 0.2.0 / versionCode 5 · 电脑端 PC 0.2.0）

两端同版发布：M3 美化 + 后台常驻 + 链接导入 + 设备同步。互通协议升 **v1.1**（新增第 8/9 节）。

### 新增（两端共有）

- **从 GitHub / Gitee 链接导入小程序**：支持 `用户/仓库`（自动探测默认分支）、
  `/tree/分支/子目录` 与 `.zip` 直链；仓库根没有 index.html 时自动扫描子目录，
  多应用仓库弹选择框可全选（协议第 8 节）
- **设备同步（局域网 · IPv4 + IPv6）**：UDP 广播/多播自动发现 + 手动 IP 兜底，
  HTTP 双栈传输（IPv6 `::` DualMode）；连接后列出应用差异，逐项选方向
  （推送/拉取/跳过）或一键"时间新者胜"；同步携带 JS 数据、保 id 安装
  （协议第 9 节；storeHash 两端逐字节一致的指纹算法）
- **Material Design 3 美化**：手机端接入 Material You 动态取色（Android 12+ 随壁纸变色）、
  状态栏 Surface 化；电脑端全新自绘 M3 主题（Baseline 配色、药丸按钮、圆角卡片列表）

### 手机端

- 补上 **INTERNET / ACCESS_NETWORK_STATE / CHANGE_WIFI_MULTICAST_STATE** 权限
  （此前 WebView 外链与本次新功能都依赖它）
- 新增 `UrlImporter.kt` / `SyncEngine.kt`（服务+发现+客户端）/ `SyncActivity.kt`；
  `AppRepo` 增加 `importDirectory` / `installSynced` / `storeHash`
- 同步监听仅在同步页存活期间运行（按用户选择不做安卓常驻服务），页面保持屏幕常亮
- 首页右上角菜单：从链接导入 / 设备同步

### 电脑端（PC）

- **托盘后台常驻**：关闭窗口缩到托盘，同步服务随托盘运行（可关），
  托盘菜单 打开/同步/允许同步/退出
- 新增 `MaterialTheme.cs` / `UrlImporter.cs` / `UrlImportDialog.cs` /
  `SyncServer.cs` / `SyncClient.cs` / `Discovery.cs` / `SyncDialog.cs` / `AppSettings.cs`
- 首页改 M3 自绘卡片列表，「＋ 导入」下拉三种导入方式；命令行新增 `--import-url`
- 自检扩至 **46 项断言**全通过（新增 URL 解析 / storeHash / 保 id 安装 /
  真实 HTTP 回环同步）；GUI 自动化实测：M3 观感、直链导入、
  双实例同步（发现→连接→一键→数据迁移）、托盘常驻全通过
- 实现注记：WebView2 `AddHostObjectToScript` 同步封送在本环境返回 0x80070057 不可靠，
  桥继续沿用 0.1.0 的"同步 XHR + WebResourceRequested"方案（详见 RunForm 注释）

### 修复

- 差异对比行缺失对方应用信息导致"数据不同"项自动方向判定崩溃、拉取方向静默失效（PC SyncDialog）

## [PC 0.1.0] - 2026-11-08

第一个 Windows 电脑端版本（源码在 [`pc/`](pc/)，C# / .NET 10 WinForms + WebView2）。

### 新增（电脑端）

- **与手机端 1:1 的容器设计**：本地处理（完全离线可用）、按小程序隔离的容器目录、
  四项权限引擎（通知/振动/剪贴板/定位，实时生效）——设计思路与 Android 端一致
- **互通协议 v1**（新增 [PROTOCOL.md](PROTOCOL.md)）：包格式、桥 API、权限模型、迁移格式的两端共同契约
- **同一个 zip 双端通用**：手机端导出的包在电脑端导入后应用+数据+权限全部还原；
  电脑端导出的包可导回手机端（数据还原）
- JS 桥 `Native`（别名 `LocalStore`）：13 个方法与手机端同签名、同返回 JSON、同错误码；
  经 WebView2 `AddHostObjectToScript` + 文档创建注入实现**同步**调用，现有小程序零改动
- 首页卡片列表 + 右键菜单（打开/权限管理/存储管理/导出/删除）＋「＋ 导入小程序」
- 动态图标与手机端同算法同调色板（Java `String.hashCode` 哈希），同名小程序两端同色
- `--selftest` 仓库层自检命令（30 项断言：导入/存储/权限/导出/zip-slip/迁移还原）

### 平台差异（语义等价，详见协议文档第 5 节）

- `vibrate` → 任务栏图标闪烁；`share` → 复制到剪贴板 + toast；`notify` → 托盘气泡（恒返回 ok）

### 构建

```bash
cd pc && dotnet publish -c Release -r win-x64 --self-contained true
```

## [0.1.2] - 2026-11-07

### 变更

- **运行页去边框**：移除 MaterialToolbar 标题栏，改为微信小程序式悬浮返回键
  （半透明圆形按钮盖在网页内容上），网页应用获得全屏空间；
  返回键行为：网页有历史则后退，否则退出回盒子

## [0.1.1] - 2026-11-07

### 修复

- **「添加到桌面」闪退**：快捷方式 Intent 补上 `ACTION_VIEW`（缺失 action 时部分桌面 /
  ShortcutService 校验抛异常导致闪退）；同时为 `requestPinShortcut` 增加异常兜底，
  在不支持的定制桌面（MIUI 等）上改为 Snackbar 提示而不是崩溃

## [0.1.0] - 2026-11-07

第一个正式版本：从「单应用 WebView 壳」改造为「小程序盒子」。

### 架构改造

- **小程序盒子模式**：APK 变为通用宿主，HTML 小程序包（zip）随时从手机导入运行，
  新增/更新小程序不再需要重新构建 APK
- **纯原生 M3 外壳**：首页/列表/对话框全部使用 Material Design 3 原生控件
  （MaterialToolbar、RecyclerView 卡片、ExtendedFAB、MaterialAlertDialog、Snackbar），
  外壳 UI 不含 WebView；仅小程序内容页使用系统 WebView 渲染
- 首页空状态支持一键载入内置示例小程序

### 新增

- **系统能力 JS 桥接**（全局对象 `Native`，别名 `LocalStore` 兼容旧包）：
  - 存储：`save` / `load` / `remove` / `clear` / `keys`（按小程序隔离）
  - 通知：`notify`（Android 13+ 对接 POST_NOTIFICATIONS 运行时权限）
  - 设备：`vibrate` / `toast` / `deviceInfo`
  - 剪贴板：`copyText` / `pasteText`
  - 分享：`share`（系统分享面板）
  - 文件：HTML `<input type=file>` 原生文件选择（WebView onShowFileChooser）
  - 定位：HTML 地理定位 API 按小程序权限开关自动放行/拒绝
- **按小程序独立权限管理**：长按首页卡片 → 权限管理，可分别开关
  通知/振动/剪贴板/定位；桥接 API 未授权时返回 `permission_denied`
- **按小程序存储管理**：查看占用大小、一键清除该小程序全部 JS 数据
- **应用+数据一键打包**：导出 zip（HTML + `__meta__/meta.json` + `__data__/store.json`），
  可在任意设备导入后自动还原数据；导入端自动识别并恢复
- **每小程序独立桌面快捷方式**：图标按小程序名称首字+配色动态生成，删除小程序时同步失效
- **内置示例小程序**：演示全部 Native API（通知/振动/剪贴板/分享/设备信息/本地存储待办）
- **ZCode skill 交付**：`skill/html-miniapp-dev`——任何人让 AI 写"能调用系统能力的网页小程序"
  时，AI 按此规范产出可直接导入的 zip 包

### 变更

- 应用名改为「小程序盒子」；versionCode 2 / versionName 0.1.0
- minSdk 24 / targetSdk 34 不变；新增 VIBRATE、POST_NOTIFICATIONS、定位权限声明
- 移除 FileProvider、静态 shortcuts.xml、旧版 MainActivity/LocalStore
  （LocalStore 功能并入 MiniAppBridge，JS 层完全兼容）

### 云构建

- 主流水线：GitHub Actions（ubuntu runner 自带 Android SDK，JDK17 + Gradle 8.4），
  push main 自动构建并发布 Release（app-debug.apk）
- Gitee 作为镜像仓库；Gitee Go 流水线因步骤插件类型兼容问题弃用（`.workflow/` 已移除）

### 修复（开发期构建问题记录）

- Kotlin 插件 ID 误用裸 `org.jetbrains.kotlin`（应为 `org.jetbrains.kotlin.android`）
- `ShortcutManager` 包名误写为 `android.app`（应为 `android.content.pm`）
- `WebViewAssetLoader.Builder` 误传 Context 参数、`addPathHandler` 缺失闭括号

## [Unreleased]

- 小程序详情页（独立界面管理权限/存储，替代对话框）
- URL 在线导入
- release 签名构建（当前为 debug 签名）
