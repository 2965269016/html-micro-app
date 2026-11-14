# 小程序互通协议 v1.1

**这是手机端（Android）、电脑端（Windows）与 iOS 端宿主共同实现的规范。**
任何一端改包格式、桥 API 或权限模型，必须先改本文档并同步两端实现（"电脑端跟手机端同步更新"的契约就在这里）。

- 协议版本：**v1.1**（v1：2026-11-08 定稿；v1.1：2026-11-08 新增 URL 导入与设备同步）
- 手机端实现：`app/src/main/java/com/example/htmlmicroapp/`（Kotlin）
- 电脑端实现：`pc/`（C# / .NET 10 WinForms + WebView2）
- iOS 端实现：`ios/`（SwiftUI + WKWebView，实现第 1~8 节；第 9 节设备同步暂未实现，v1.2 候选）
- 小程序包本身是**跨端中立**的：同一个 zip 在两端都能导入运行

---

## 1. 小程序包格式（导入）

| 项 | 约定 |
|---|---|
| 容器 | zip（UTF-8 文件名，支持 `/` 与 `\` 两种分隔符） |
| 入口 | zip 根目录必须有 `index.html`（名字固定） |
| 清单 | 可选 `manifest.json`，宿主只读 `{"name": "显示名称"}`；省略时用 zip 文件名 |
| 目录 | 其余 css/js/图片按相对路径，保持目录结构 |
| 保留名 | `__data__/`、`__meta__/` 为宿主保留目录，小程序不得创建 |
| 单层纠正 | 解压后若只有一个顶层目录且根级无 index.html，自动把该目录内容上移一层 |
| 安全 | zip 条目路径含 `..` 或绝对路径/盘符一律跳过（zip-slip 防护） |

## 2. 宿主存储布局（两端各自存放，格式互通）

```
Android: filesDir/apps/<id>/            PC: %LOCALAPPDATA%/MiniAppBox/apps/<id>/
Android: filesDir/apps/<id>/meta.json   PC: 同名同格式
Android: shared_prefs/store_<id>.xml    PC: %LOCALAPPDATA%/MiniAppBox/store_<id>.json（JSON 键值对）
```

- `meta.json`：`{"id","name","installedAt","permissions":{...}}`（2 空格缩进 JSON；`installedAt` 为毫秒时间戳；`permissions` 里只出现被用户操作过的键，未出现的视为 `false`）
- `id`：`"app_" + yyyyMMdd_HHmmss`（导入时宿主分配）
- JS 存储：字符串键值对（复杂对象由小程序自行 `JSON.stringify`）

## 3. 迁移格式（导出 zip / 跨端互导）

```
<包内 HTML 及所有文件>            （跳过内部 meta.json 与 __data__/）
__meta__/meta.json                宿主原样写入
__data__/store.json               {"key":"value",...} 该小程序全部 JS 数据
```

- **手机端导出 → 电脑端导入**：应用、数据、权限开关全部还原 ✅
- **电脑端导出 → 手机端导入**：应用、数据还原 ✅；权限开关 v1 不还原（手机端现状导入时重建 meta），属已知差异，后续版本可补
- `store.json` 缺失或损坏时忽略，不影响小程序本体导入

## 4. JS 桥接 API（全局对象 `Native`，别名 `LocalStore`）

所有方法**同步**返回 JSON 字符串，`JSON.parse` 后使用：

- 成功：`{"ok":true, ...附加字段}`；失败：`{"ok":false,"error":"错误码"}`
- 未授权：`error = "permission_denied"`（每次调用实时校验，开关立即生效）
- 参数宽松：数字/布尔传入字符串参数位时自动转字符串（如 `save(123,"x")` 存到键 `"123"`）

| 方法 | 需权限 | 返回 / 行为 |
|---|---|---|
| `save(key, value)` | 无 | `{ok:true}`；`key` 为 null → `key_required`；value 为 null 时存空串 |
| `load(key)` | 无 | `{ok:true,value:"..."}`；不存在时 `value` 为 null |
| `remove(key)` / `clear()` | 无 | `{ok:true}` |
| `keys()` | 无 | `{ok:true,keys:{list:[...]}}`（顺序不保证） |
| `notify(title, body)` | notification | `{ok:true}`；系统总开关关闭 → `system_notification_disabled` |
| `vibrate(ms)` | vibrate | `{ok:true}`；ms 不在 1~10000 时回落 200；无振动器 → `no_vibrator` |
| `toast(message)` | 无 | `{ok:true}`（短暂浮现式提示） |
| `deviceInfo()` | 无 | `{ok:true,model,brand,osVersion,appVersion,screen}` |
| `copyText(text)` / `pasteText()` | clipboard | `{ok:true}` / `{ok:true,text:"..."}` |
| `share(title, text)` | 无 | `{ok:true}`（拉起系统分享） |
| `<input type="file">` | 无 | 宿主自动弹系统文件选择器 |
| HTML 地理定位 API | geolocation | 宿主按权限开关自动放行/拒绝，无需 JS 判断 |

## 5. 平台差异（v1 允许的呈现差异，语义等价）

| 能力 | Android | Windows PC | iOS |
|---|---|---|---|
| `notify` 呈现 | 通知栏通知 | 托盘气泡通知（Windows 通知中心）；无法可靠探测系统总开关，**恒返回 `{ok:true}`** | 通知中心通知；可探测系统总开关，关闭时返回 `system_notification_disabled` |
| `vibrate` | 真实振动 | **任务栏图标闪烁** ms 毫秒（无振动硬件） | 触觉反馈脉冲（按 ms 映射轻重档，无时长概念） |
| `share` | 系统分享面板 | **复制文本到剪贴板 + toast 提示**（Windows 无统一分享面板） | 系统分享面板（UIActivityViewController，同 Android） |
| `toast` | Android Toast | 底部浮现深色提示条，约 2 秒淡出 | 居中短提示（UIAlertController，需点「好」关闭） |
| `deviceInfo` 字段值 | 如 `model=Pixel` | `model="PC"`、`brand="MiniAppBox-PC"`、`osVersion="Windows 10.0.26300"` | `model="iPhone"`、`brand="Apple"`、`osVersion="iOS 17.x"` |
| 新窗口 / target=_blank | 不打开（默认） | 在同一窗口内导航 | 不打开（默认，同 Android） |
| 文件选择 | 系统选择器 | WebView2 内置打开文件对话框 | UIDocumentPicker 系统选择器 |

除上表外，各端行为必须一致；新增差异先改本文档。

## 6. 图标

两端都不要求包内带图标：宿主按 `name` 首字 + 10 色调色板（`#FF6B6B #4ECDC4 #45B7D1 #96CEB4 #FFEAA7 #DDA0DD #98D8C8 #F7DC6F #BB8FCE #85C1E9`）哈希生成，哈希算法为 Java `String.hashCode()` —— **同名小程序两端图标同色**。包内带图标文件属 v2 候选扩展。

## 7. 版本化约定

1. 不兼容变更（删方法、改返回结构、删权限项）→ 升大版本（v2），两端同步发布
2. 兼容新增（新方法、新权限项、manifest 新字段）→ 小版本递增（v1.x），文档先行
3. 每个宿主版本发布的协议版本写入各自 CHANGELOG
4. 小程序侧建议按"特性检测"写法（如 `if (window.Native && Native.save)`），对协议演化天然兼容

---

## 8. URL 导入（v1.1 新增）

各端宿主（手机 / 电脑 / iOS）都支持把 GitHub / Gitee 仓库链接直接导入为小程序：

| 支持的链接 | 解析 |
|---|---|
| `github.com/{u}/{r}` | 默认分支 → `codeload.github.com/{u}/{r}/zip/refs/heads/{branch}`（默认分支探测：API 失败则按 main→master 试探） |
| `github.com/{u}/{r}/tree/{branch}[/{path}]` | 指定分支[及子目录] |
| `gitee.com/{u}/{r}` / `.../tree/{branch}[/{path}]` | `{u}/{r}/repository/archive/{branch}.zip`（默认分支探测：API 失败则 master→main 试探） |
| 任意 `*.zip` 直链 | 直接下载导入 |

- 仓库根含 `index.html` → 整个仓库就是一个小程序；否则扫描所有子目录（跳过点目录），
  含 `index.html` 的每个子目录视为一个小程序（1 个自动选，多个弹选择框，可全选）
- URL 指定了 `{path}` 子目录 → 直接以该子目录为小程序根
- 名称取包内 `manifest.json` 的 `name`，缺省用仓库名
- 数据可移植性提醒：**同步/迁移只搬 `Native` 存储的数据**（`__data__/store.json`），
  `localStorage` 两端 origin 不同且不参与同步——小程序的数据一律走 `Native.save`

## 9. 设备同步协议（v1.1 新增，IPv4 + IPv6）

### 9.1 传输与发现

- **同步通道**：TCP 上的 HTTP/1.1 子集，端口 **47777**（环境变量 `MINIAPPBOX_SYNC_PORT` 可改），
  双栈监听：IPv6 `::` + DualMode，同时接受原生 IPv6 与 IPv4-mapped-IPv4 连接
- **发现通道**：UDP **47778**，探测包 JSON：`{"proto":1,"device":"设备名","tcp":47777}`
  - IPv4：目的 `255.255.255.255`（子网广播）
  - IPv6：目的 `ff02::1`（链路本地多播，尽力而为；安卓端需 MulticastLock）
  - 设备收到探测后**原样回发自己的探测包**（单播回源地址），双方互进发现列表
  - 自动发现只是便利手段，手动输入 `IP:端口` 永远可用（IPv6 地址带 `[]`）

### 9.2 端点

| 端点 | 返回 |
|---|---|
| `GET /sync/info` | `{"proto":1,"device":"设备名","apps":[{"id","name","installedAt","size","storeHash"}]}` |
| `GET /sync/app?id=<id>` | `200` application/zip（第 3 节迁移格式）／`404` |
| `POST /sync/app?id=<id>` | body 为迁移格式 zip → `200 {"ok":true}`（**保 id 安装**）／`400 {"ok":false,"error":"..."}` |

### 9.3 保 id 安装（与普通导入的区别）

普通导入会分配新 id（时间戳），同步必须**沿用发送端的 id**：
- id 不存在 → 以该 id 新建；meta 优先取包内 `__meta__/meta.json`（含权限开关），否则用 manifest/仓库名
- id 已存在 → 替换 HTML 目录（旧目录先删），store 与 meta 采用包内数据
- 应用包 ≤ 512MB 超限拒绝

### 9.4 storeHash（数据一致性快速判断）

对 JS 存储计算指纹，两端算法必须逐字节一致：
1. 全部键值对按键的 **UTF-8 字节序**排序
2. 每对拼为 `key + "\t" + value + "\n"`（原样字节，不做 JSON 转义），顺序连接
3. UTF-8 编码 → SHA-256 → 取前 **16 字节** → 小写 hex（空存储 = SHA-256 空串的前 16 字节）

### 9.5 冲突策略与运行边界

- 同一应用两端都有且 `storeHash` 不同 → 由发起端用户决定：逐项选方向（推送给对方/从对方获取/跳过）
  或一键"时间新者胜"（按 `installedAt`）
- 安卓端监听**仅同步页存活期间运行**（用户选择不做安卓常驻）；电脑端随托盘常驻运行，托盘菜单可关
- 安全边界：仅限局域网、v1.1 无鉴权——在可信网络使用；后续版本可加配对码
