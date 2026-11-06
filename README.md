# HTML 微应用容器

用 Kotlin + WebView 运行的「离线 HTML 微应用」容器。把 AI 写的纯前端小工具打成 zip 塞进 APK，
手机无需联网即可运行；支持本地数据存储和一键添加到桌面。

- 包名：`com.example.htmlmicroapp`
- minSdk 24（Android 7.0+） / targetSdk 34
- 云打包：**Gitee Go**（`.workflow/config.yml`）

---

## 功能

| 功能 | 实现 |
|------|------|
| 解压本地 HTML 应用到私有目录 | `MainActivity.extractHtmlApp()` |
| WebView 安全加载本地资源 | `WebViewAssetLoader` + `https://appassets.androidplatform.net/` |
| JS 本地数据存储 | `@JavascriptInterface LocalStore` |
| 添加到桌面快捷方式 | `ShortcutManager.requestPinShortcut()` |
| 无图标时自动生成占位图标 | `IconGenerator`（应用名首字 + 彩色圆形背景） |

---

## 本地目录结构

```
html-micro-app/
├── app/
│   ├── build.gradle.kts
│   └── src/main/
│       ├── AndroidManifest.xml
│       ├── java/com/example/htmlmicroapp/
│       │   ├── MainActivity.kt      # 核心入口
│       │   ├── LocalStore.kt        # JS 存储桥
│       │   └── IconGenerator.kt     # 动态图标
│       └──/res/                    # 图标 / 主题 / 布局 / shortcuts
├── .github/workflows/build.yml     # 备选：GitHub Actions
├── .workflow/config.yml            # 主：Gitee Go 流水线
├── ci/build_and_publish.sh         # 云端构建 + 发布脚本
├── init_repo.sh                    # 一键建仓推送
├── get_apk.sh                      # 获取 APK 下载地址
└── gradlew / gradle/wrapper/       # Gradle 包装
```

---

## 使用步骤（全程只用终端）

### 1. 放入你的 HTML 应用

把你的前端小工具（`index.html` + css/js/图片）打包成 zip，
命名为 `html_app.zip`，放到：

```
app/src/main/assets/html_app.zip
```

> `index.html` 必须在 zip 的根目录。仓库里已带一个示例页面，
> 没有自己的应用时可以直接用。

### 2. 一键推送到 Gitee

```bash
cd html-micro-app
bash init_repo.sh
```

脚本会自动：初始化 git → 用 gitee CLI 建仓 → 提交 → 推送 → 触发云打包。

### 3. 等构建完成（约 3~8 分钟）

查看进度：`https://gitee.com/<你的用户名>/html-micro-app/pipeline`

### 4. 拿 APK 下载地址

```bash
bash get_apk.sh
```

输出就是可直接在手机浏览器打开的下载链接。

---

## 自定义

| 想改什么 | 改哪里 |
|---------|--------|
| 应用名称（含桌面图标首字） | `app/src/main/res/values/strings.xml` → `app_name` |
| 包名 | `app/build.gradle.kts` → `applicationId`，并同步 `AndroidManifest.xml` |
| 图标配色 | `IconGenerator.kt` → `PALETTE` |
| 仓库名 | `init_repo.sh` / `get_apk.sh` → `REPO_NAME` |
| Android SDK 版本 | `ci/build_and_publish.sh` → `ANDROID_COMPILE_SDK` |

---

## 在 HTML 里调用本地存储

```html
<script>
  // 保存
  LocalStore.save("todos", JSON.stringify([{ text: "买菜", done: false }]));

  // 读取（返回 JSON 字符串：{"ok":true,"value":"..."}）
  const r = JSON.parse(LocalStore.load("todos"));
  const todos = r.value ? JSON.parse(r.value) : [];

  // 删除 / 清空
  LocalStore.remove("todos");
  LocalStore.clear();
</script>
```

数据存在应用私有目录（SharedPreferences），卸载应用才会清除。

---

## 常见问题

**Q：流水线没触发？**
打开仓库 → 流水线 → 找到「HTML微应用容器 · 云打包」→ 点「立即执行」。

**Q：提示找不到 `execute@shell` 步骤？**
Gitee Go 有多个版本，步骤参数写法不同。打开 `.workflow/config.yml`，
按文件里的注释把 `inputs: / script:` 换成 `script: |` 或 `commands:` 即可。

**Q：构建成功但 Release 是空的？**
说明没配 `CI_TOKEN`。到 流水线 → 设置 → 全局变量 添加
`CI_TOKEN` = 你的 Gitee 私人令牌（需 projects 权限），
令牌在 <https://gitee.com/profile/personal_access_tokens> 生成。

**Q：Gitee Go 用不了 / 想换 GitHub？**
```bash
bash init_repo.sh github   # 建 GitHub 仓库（备选流水线 .github/workflows/build.yml）
```
