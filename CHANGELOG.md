# 更新日志（CHANGELOG）

所有显著变更记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

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
