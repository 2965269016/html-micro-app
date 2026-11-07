# 功能日志（FEATURES）

「小程序盒子」v0.1.0 完整功能清单。更新日志见 [CHANGELOG.md](CHANGELOG.md)。

## 1. 盒子（原生外壳，Material Design 3）

| 功能 | 说明 |
|------|------|
| 小程序列表 | 卡片式列表：动态生成首字图标 + 名称 + 安装日期 |
| 导入 HTML 包 | 右下角「导入」→ 系统文件选择器选 zip（微信/QQ 收到的文件保存后即可导入） |
| 载入内置示例 | 空列表时一键载入演示包 |
| 打开 | 点卡片进入运行时 |
| 权限管理 | 长按 → 权限管理：通知/振动/剪贴板/定位 四项独立开关 |
| 存储管理 | 长按 → 存储管理：查看占用、清除数据、打包导出 |
| 添加到桌面 | 长按 → 添加到桌面：生成该小程序专属快捷方式（首字图标），点击直达 |
| 删除 | 长按 → 删除（二次确认），HTML 与数据一并清除，桌面快捷方式同步失效 |

## 2. 运行时（RunActivity）

| 功能 | 说明 |
|------|------|
| 本地渲染 | WebViewAssetLoader 把小程序目录映射到 `https://appassets.androidplatform.net/apps/<id>/`，无网络也能跑 |
| 原生顶栏 | 返回键 + 小程序名称（微信小程序顶栏风格） |
| 历史导航 | 物理返回键优先走 WebView 后退 |
| 文件选择 | HTML `<input type=file>` 自动拉起系统选择器 |
| 定位控制 | 按小程序定位权限开关放行 HTML 地理定位 |
| 错误兜底 | 包损坏时显示原生错误提示页 |

## 3. JS 桥接 API（全局 `Native`，别名 `LocalStore`）

所有方法同步返回 JSON：`{"ok":true,...}` / `{"ok":false,"error":"..."}`。

### 存储（免权限，按小程序隔离）

```js
Native.save(key, value)          // {ok:true}  value 仅字符串
Native.load(key)                 // {ok:true, value:"..."}  不存在时 value=null
Native.remove(key)               // {ok:true}
Native.clear()                   // {ok:true}
Native.keys()                    // {ok:true, keys:{list:[...]}}
```

### 系统能力

```js
Native.notify(title, body)       // 通知      【需权限: 通知】
Native.vibrate(ms)               // 振动      【需权限: 振动】  ms 1~10000
Native.copyText(text)            // 复制      【需权限: 剪贴板】
Native.pasteText()               // 粘贴      【需权限: 剪贴板】 {ok:true, text:"..."}
Native.toast(msg)                // 轻提示    【免权限】
Native.deviceInfo()              // 设备信息  【免权限】 {model, brand, osVersion, appVersion, screen}
Native.share(title, text)        // 系统分享  【免权限】
HTML 地理定位 API                // 定位      【需权限: 定位】 宿主自动放行/拒绝
<input type="file">              // 文件      【免权限】
```

> `error:"permission_denied"` = 该能力未在盒子里授权；`"system_notification_disabled"` =
> 系统层通知总开关被关闭。

## 4. 权限模型

- 权限**按小程序隔离**：每个小程序在首页长按 → 权限管理中独立开关
- 开关持久化在该小程序 `meta.json` 的 `permissions` 字段
- 通知/定位开启时，若宿主缺 Android 运行时权限会自动弹系统授权框
- 桥接层每次调用实时校验，关掉立即生效

## 5. 存储与打包

### 数据隔离

每个小程序的 JS 数据存在独立 SharedPreferences（`store_<id>`），互不可见；
「存储管理 → 清除数据」只影响当前小程序。

### 一键打包格式（应用+数据）

```
导出 zip
├── index.html 等        小程序全部文件（不含 meta.json / __data__）
├── __meta__/meta.json   {id, name, installedAt, permissions}
└── __data__/store.json  {"key":"value", ...}   JS 全部键值对
```

- 导出：长按 → 存储管理 → 打包导出 → 选择保存位置（免存储权限）
- 导入：正常导入 zip；检测到 `__data__/store.json` 自动还原数据
- 用途：换机迁移、备份、把带数据的成品分享给别人

## 6. 开发者：如何写一个小程序

- **方式一（推荐）**：使用 `html-miniapp-dev` skill —— 对 AI 说
  「帮我写一个xx网页小程序，用小程序盒子」即可按规范产出 zip
- **方式二**：手写 HTML（入口必须是根目录 `index.html`，可选 `manifest.json` 声明名称），
  打成 zip 后从盒子导入
- 规范全文见 [skill/html-miniapp-dev/SKILL.md](skill/html-miniapp-dev/SKILL.md)

## 7. 云构建

| 环节 | 说明 |
|------|------|
| 主流水线 | GitHub Actions：push main 自动 `gradle assembleDebug` 并发布 Release |
| 下载 | `https://github.com/2965269016/html-micro-app/releases` 取 app-debug.apk |
| 镜像 | Gitee（lujunbo2008/html-micro-app）仅作代码镜像 |
| 本地打包 HTML 示例 | `bash pack_html.sh`（html_app_src/ → assets/html_app.zip） |

## 8. 技术参数

| 项 | 值 |
|----|----|
| 版本 | 0.1.0 (versionCode 2) |
| 包名 | com.example.htmlmicroapp |
| minSdk / targetSdk | 24 / 34 |
| 构建链 | Kotlin 1.9.21 · AGP 8.2.2 · Gradle 8.4 · JDK 17 |
| UI | Material Design 3 (material 1.11.0) |
| 关键依赖 | androidx.webkit 1.9.0（WebViewAssetLoader）、appcompat 1.6.1、core-ktx 1.12.0 |
