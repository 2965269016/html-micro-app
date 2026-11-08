# 小程序盒子（HTML MicroApp Box）

「网页小程序」宿主，HTML 小程序包（zip）随时导入，导入即出现在列表里，点开就用 ——
就像微信里的小程序，但盒子是你自己的。**加小程序永远不需要重新构建安装包。**

- **手机端**：Kotlin + Material Design 3（Android）· 版本 **0.1.2**
  包名 `com.example.htmlmicroapp` · minSdk 24 / targetSdk 34
- **电脑端**：C# / .NET 10 + WinForms + WebView2（Windows 10/11 x64）· 版本 **0.1.0**，源码在 [`pc/`](pc/)
- 两端共用同一份[《小程序互通协议》](PROTOCOL.md)：**同一个 zip 小程序包在手机和电脑上都能导入运行**，
  数据随包迁移；外壳均为纯原生 UI，小程序内容由系统 WebView 渲染

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
pc/                      电脑端（Windows，.NET 10 WinForms + WebView2）
├── HomeForm.cs          首页（列表/导入/右键管理）
├── RunForm.cs           运行时（WebView2 虚拟域名映射 + 定位门控）
├── AppRepo.cs           数据层（与手机端同构）
├── MiniAppBridge.cs     JS 桥接（同步语义与手机端一致）
└── SelfTest.cs          --selftest 仓库层自检
PROTOCOL.md              手机端/电脑端互通协议 v1（改接口先改这里）
skill/html-miniapp-dev/  ZCode skill（AI 按此规范写小程序）
html_app_src/            内置示例小程序源码（pack_html.sh 打包进 assets）
.github/workflows/       GitHub Actions 云构建 + Release
```

## 电脑端（Windows）

```bash
cd pc
dotnet publish -c Release -r win-x64 --self-contained true \
  -o ../小程序盒子-PC     # 自包含（.NET 运行库已打包，拷到任何 Win10/11 x64 都能跑）
```

- 数据目录：`%LOCALAPPDATA%\MiniAppBox`（每个小程序独立隔离）
- 手机端导出的 zip 在电脑端「＋ 导入小程序」直接选择即可，应用+数据+权限一并还原
- 电脑端导出的 zip 同样可以导回手机端（数据还原；权限开关暂不还原，见协议文档）

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
