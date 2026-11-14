---
name: html-miniapp-dev
description: 为「小程序盒子」Android 宿主编写可导入的 HTML 小程序包。当用户想写网页小工具/HTML 应用并运行在小程序盒子（HTML 微应用容器）里、或想调用系统能力（通知/振动/剪贴板/分享/定位/本地存储）时必须使用本 skill —— 即使用户没提"小程序盒子"四个字，只要目标是"做成手机上的网页小程序"就该加载。
---

# HTML 小程序开发规范（小程序盒子）

你要产出一个可直接导入「小程序盒子」App 的 **zip 包**。该 App 是一个 Android 宿主：
导入 zip → 出现在首页列表 → 点开用 WebView 运行。HTML 通过 `Native` JS 桥接调用系统能力。

## 产物要求（硬性）

1. zip 根目录必须有 `index.html`（入口文件，名字固定）
2. 可选 `manifest.json`：`{"name": "显示名称"}`，省略时用 zip 文件名
3. 其余文件（css/js/图片）按相对路径引用，保持目录结构
4. `__data__/` 与 `__meta__/` 是宿主保留目录名，**不要**在包里创建
5. 完全离线可用的小程序请内联 css/js；外链 CDN 也可用（设备联网时）

## JS 桥接 API

全局对象 `Native`（`LocalStore` 是它的别名，旧代码兼容用）。所有方法**同步**返回
JSON 字符串，先 `JSON.parse` 再用：

```js
const r = JSON.parse(Native.save("todos", JSON.stringify(list)));
if (!r.ok) alert("失败：" + r.error);   // "permission_denied" = 权限未开启
```

### 存储（无需权限，随小程序隔离，宿主卸载才清除）

| 方法 | 返回 |
|------|------|
| `Native.save(key, value)` | `{ok:true}`；value 只能是字符串，复杂对象先 `JSON.stringify` |
| `Native.load(key)` | `{ok:true, value:"..."}`；不存在时 `value` 为 null |
| `Native.remove(key)` / `Native.clear()` | `{ok:true}` |
| `Native.keys()` | `{ok:true, keys:{list:["k1","k2"]}}` |

### 系统能力（部分需用户在盒子里授权）

| 方法 | 需要权限 | 返回 |
|------|---------|------|
| `Native.notify(title, body)` | 通知 | `{ok:true}` 或 `system_notification_disabled` |
| `Native.vibrate(ms)`（1~10000） | 振动 | `{ok:true}` |
| `Native.copyText(text)` / `Native.pasteText()` | 剪贴板 | `{ok:true}` / `{ok:true, text:"..."}` |
| `Native.toast(msg)` | 无 | `{ok:true}` |
| `Native.deviceInfo()` | 无 | `{ok:true, model, brand, osVersion, appVersion, screen}` |
| `Native.share(title, text)` | 无 | `{ok:true}`（拉起系统分享面板） |
| `<input type="file">` | 无 | 自动拉起系统文件选择器，拿到 File 对象照常处理 |
| HTML 地理定位 API | 定位 | 宿主按权限开关自动放行/拒绝，无需 JS 判断 |

**权限错误处理**：`error === "permission_denied"` 时提示用户"回首页长按小程序 → 权限管理开启"，
不要反复重试。

## 最小可用模板

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
  <title>我的小程序</title>
</head>
<body>
  <h1>我的小程序</h1>
  <button onclick="hit()">点我</button>
  <div id="out"></div>
  <script>
    function hit() {
      // 计数存本地
      var n = 1;
      var saved = JSON.parse(Native.load("hits"));
      if (saved.ok && saved.value) n = parseInt(saved.value, 10) + 1;
      Native.save("hits", String(n));
      document.getElementById("out").textContent = "点了 " + n + " 次";
    }
  </script>
</body>
</html>
```

## 交互规范

- 手机竖屏优先；`<meta viewport>` 必须带，按上面模板写
- 运行时顶部有悬浮胶囊（手机端：**左上=返回 / 右上=退出**；电脑端无），盖在网页内容上。
  顶部重要内容必须避让：CSS 用 `padding-top: var(--box-safe-top, 0px)`，
  或 JS 读 `window.__miniapp_safe_insets`（手机端为 {top:56,left:56,right:56}；电脑端未注入，CSS 默认 0px）
- **不要**自己做返回/退出按钮，胶囊已处理（左上历史后退、右上退出）
- 弹窗用 `Native.toast()` / `Native.notify()`，不要用 alert()（部分 WebView 上体验差）
- 输出 zip 时确保 index.html 在 zip 根（不要把整个文件夹拖进去多套一层）

## 打包（终端）

```bash
# 在包含 index.html 的目录里执行（Git Bash / Linux）
zip -r ../my-miniapp.zip .
# Windows PowerShell
Compress-Archive -Path * -DestinationPath ..\my-miniapp.zip
```

产出 `my-miniapp.zip` 后告诉用户：打开小程序盒子 → 右下角「导入」选择该文件。

## URL 导入（GitHub / Gitee 仓库，v1.1 新增）

三端宿主（手机/电脑/iOS，iOS 0.1.2 起）都支持把仓库链接直接导入为小程序：

- **单应用仓库**：仓库根目录（或某个子目录）里有 `index.html` 即可导入；
  根目录没有时，宿主会自动扫描所有子目录，每个含 `index.html` 的子目录视为一个小程序，
  多个时弹选择框（可全选）
- **多应用集合仓库**：形如
  ```
  my-box-apps/
  ├── todos/index.html + manifest.json
  ├── 计算器/index.html + manifest.json
  └── 拾词/index.html + manifest.json
  ```
- 每个应用目录建议带 `manifest.json`（`{"name": "显示名称"}`），没有则用目录/仓库名
- 支持的链接：`github.com/用户/仓库`、`gitee.com/用户/仓库`（可带 `/tree/分支/子目录`）、
  任意 `*.zip` 直链；GitHub 直连不畅时优先给 Gitee 链接
- 产出后告诉用户：**首页右上角菜单 →「从链接导入」粘贴链接**（电脑端：＋ 导入 → 从链接导入）

## 数据可移植性（v1.1 新增，重要）

- 跨设备同步/迁移**只搬 `Native` 存储的数据**（打包为 `__data__/store.json`）
- **不要用 localStorage / IndexedDB** 存用户数据：两端 origin 不同、清缓存会丢、且不参与同步
- 正确姿势：所有需要持久化的数据（进度、设置、记录）一律 `Native.save(key, JSON.stringify(value))`
