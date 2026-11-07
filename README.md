# 小程序盒子（HTML MicroApp Box）

用 Kotlin + Material Design 3 写的「网页小程序」宿主 App：HTML 小程序包（zip）随时导入，
导入即出现在列表里，点开就用 —— 就像微信里的小程序，但盒子是你自己的。
**加小程序永远不需要重新构建 APK。**

- 版本：**0.1.0**（[更新日志](CHANGELOG.md) · [功能日志](FEATURES.md)）
- 包名：`com.example.htmlmicroapp` · minSdk 24 / targetSdk 34
- 外壳：纯原生 Material Design 3（不用 WebView）；小程序内容：系统 WebView 渲染

---

## 三步上手

### 1. 安装

从 [Releases](https://github.com/2965269016/html-micro-app/releases) 下载 `app-debug.apk`
安装（允许未知来源）。

### 2. 导入小程序

点右下角「导入」选一个 zip 包（根目录必须有 `index.html`）。
第一次玩可以点「载入内置示例」。

### 3. 日常使用

- 点卡片打开；长按卡片：打开 / 权限管理 / 存储管理 / 添加到桌面 / 删除
- 「添加到桌面」会为该小程序生成专属快捷方式（首字图标），一键直达

---

## 写一个小程序

让 AI 用 `html-miniapp-dev` skill 写（说「帮我写一个xx网页小程序，用小程序盒子」），
或手写 HTML 后打成 zip。JS 里可以直接调用系统能力：

```js
Native.save("hits", "42");                                  // 本地存储（隔离）
JSON.parse(Native.notify("标题", "内容"));                    // 系统通知【需授权】
JSON.parse(Native.vibrate(300));                             // 振动【需授权】
JSON.parse(Native.deviceInfo());                             // 设备信息
JSON.parse(Native.copyText("复制到剪贴板"));                   // 剪贴板【需授权】
Native.share("分享", "内容");                                 // 系统分享
```

每个小程序的权限独立开关（长按卡片 → 权限管理），数据独立存储（长按 → 存储管理），
支持「应用+数据」一键打包导出/迁移（`__data__/store.json` 随包还原）。

完整 API 与打包规范见 [FEATURES.md](FEATURES.md) 和 [skill/html-miniapp-dev/SKILL.md](skill/html-miniapp-dev/SKILL.md)。

---

## 项目结构

```
app/src/main/java/com/example/htmlmicroapp/
├── HomeActivity.kt      盒子首页（M3 列表/导入/长按管理）+ AppsAdapter
├── RunActivity.kt       运行时（WebViewAssetLoader + 文件选择 + 定位门控）
├── AppRepo.kt           数据层：导入/列表/删除/权限/存储/打包
├── MiniAppBridge.kt     JS 桥接：Native（LocalStore 别名）
└── IconGenerator.kt     首字 + 配色动态图标
skill/html-miniapp-dev/  ZCode skill（AI 按此规范写小程序）
html_app_src/            内置示例小程序源码（pack_html.sh 打包进 assets）
.github/workflows/       GitHub Actions 云构建 + Release
```

## 云构建

push 到 main 自动触发 GitHub Actions 构建，APK 发布到
[Releases](https://github.com/2965269016/html-micro-app/releases)。
Gitee（lujunbo2008/html-micro-app）为代码镜像。

## 本地开发

```bash
bash pack_html.sh   # html_app_src/ → app/src/main/assets/html_app.zip
bash init_repo.sh   # 一键建仓推送（GitHub + Gitee 双仓库）
bash get_apk.sh     # 查询最新 Release 的 APK 下载地址
```
