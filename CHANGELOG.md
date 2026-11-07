# 更新日志（CHANGELOG）

所有显著变更记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

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
