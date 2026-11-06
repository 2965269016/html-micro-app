#!/usr/bin/env bash
# =============================================================================
#  HTML 微应用容器 · 云构建与发布脚本
#  适用于 Gitee Go（Gitee 流水线），也可直接在任何有网络的 Linux 上运行。
#
#  流程：
#    1. 安装 JDK 17 + Android SDK
#    2. 执行 gradle assembleDebug
#    3. 将 APK 上传到 Gitee Release（需要 CI_TOKEN）
#    4. 打印最终下载地址
#
#  环境变量：
#    CI_TOKEN    Gitee 私人令牌（需 projects 权限）。未设置则只构建、不发布。
#    CI          由 CI 平台自动置位（本脚本自动检测）
# =============================================================================
set -uo pipefail

# ------------------------------- 可配置项 ------------------------------------
APK_REL_PATH="app/build/outputs/apk/debug/app-debug.apk"
ANDROID_COMPILE_SDK="34"
ANDROID_BUILD_TOOLS="34.0.0"
CMDLINE_TOOLS_URL="https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip"
GRADLE_VERSION="8.4"
GRADLE_DIST_URL="https://services.gradle.org/distributions/gradle-${GRADLE_VERSION}-bin.zip"
GITEE_API="${GITEE_API:-https://gitee.com/api/v5}"
# -----------------------------------------------------------------------------

REPO_DIR="$(pwd)"
export ANDROID_HOME="${ANDROID_HOME:-/opt/android-sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"

# 非 root 且有 sudo 时使用 sudo
SUDO=""
if [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1; then SUDO="sudo"; fi

log() { printf '\n\033[1;36m========== %s ==========\033[0m\n' "$*"; }
ok()  { printf '\033[1;32m  ✔ %s\033[0m\n' "$*"; }
err() { printf '\033[1;31m  ✘ %s\033[0m\n' "$*" >&2; }

# -----------------------------------------------------------------------------
log "[1/5] 安装系统依赖与 JDK 17"

if command -v apt-get >/dev/null 2>&1; then
    $SUDO apt-get update -qq >/dev/null 2>&1 || true
    $SUDO apt-get install -y -qq curl unzip zip file >/dev/null 2>&1 || true
    if ! javac -version >/dev/null 2>&1; then
        $SUDO apt-get install -y -qq openjdk-17-jdk-headless >/dev/null 2>&1 || true
    fi
fi

if javac -version >/dev/null 2>&1; then
    ok "JDK: $(javac -version 2>&1)"
else
    err "JDK 安装失败，请检查 CI 环境"
    exit 1
fi

# -----------------------------------------------------------------------------
log "[2/5] 安装 Android SDK (platforms;android-${ANDROID_COMPILE_SDK})"

mkdir -p "$ANDROID_HOME/cmdline-tools" 2>/dev/null || {
    export ANDROID_HOME="$HOME/android-sdk"
    export ANDROID_SDK_ROOT="$ANDROID_HOME"
    mkdir -p "$ANDROID_HOME/cmdline-tools"
}

if [ ! -x "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" ]; then
    echo "  下载 Android 命令行工具 ..."
    curl -fsSL -o /tmp/cmdtools.zip "$CMDLINE_TOOLS_URL" || { err "命令行工具下载失败"; exit 1; }
    rm -rf "$ANDROID_HOME/cmdline-tools/latest"
    unzip -q -o /tmp/cmdtools.zip -d "$ANDROID_HOME/cmdline-tools" || { err "解压失败"; exit 1; }
    mv "$ANDROID_HOME/cmdline-tools/cmdline-tools" "$ANDROID_HOME/cmdline-tools/latest"
fi
export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

echo "  接受许可协议 ..."
yes 2>/dev/null | sdkmanager --licenses >/dev/null 2>&1 || true

echo "  安装 platform-tools / platforms / build-tools ..."
sdkmanager "platform-tools" "platforms;android-${ANDROID_COMPILE_SDK}" "build-tools;${ANDROID_BUILD_TOOLS}" \
    >/dev/null 2>&1 || { err "Android SDK 组件安装失败"; exit 1; }

# AGP 通过 local.properties 或 ANDROID_HOME 定位 SDK，两者都写更稳
echo "sdk.dir=${ANDROID_HOME}" > "$REPO_DIR/local.properties"
ok "Android SDK 就绪: $ANDROID_HOME"

# -----------------------------------------------------------------------------
log "[3/5] 准备 Gradle ${GRADLE_VERSION}"

if [ -f "$REPO_DIR/gradle/wrapper/gradle-wrapper.jar" ] && [ -x "$REPO_DIR/gradlew" ]; then
    GRADLE_CMD="$REPO_DIR/gradlew"
else
    if ! command -v gradle >/dev/null 2>&1; then
        echo "  下载 Gradle ${GRADLE_VERSION} ..."
        mkdir -p "$HOME/gradle-dist"
        curl -fsSL -o /tmp/gradle.zip "$GRADLE_DIST_URL" || { err "Gradle 下载失败"; exit 1; }
        unzip -q -o /tmp/gradle.zip -d "$HOME/gradle-dist" || { err "Gradle 解压失败"; exit 1; }
        export PATH="$HOME/gradle-dist/gradle-${GRADLE_VERSION}/bin:$PATH"
    fi
    GRADLE_CMD="gradle"
fi
ok "构建命令: $GRADLE_CMD"

# -----------------------------------------------------------------------------
log "[4/5] 执行 Gradle 构建 (assembleDebug)"

cd "$REPO_DIR"
$GRADLE_CMD assembleDebug --no-daemon --console=plain --stacktrace
BUILD_RC=$?
if [ $BUILD_RC -ne 0 ] || [ ! -f "$APK_REL_PATH" ]; then
    err "构建失败（退出码 $BUILD_RC）"
    exit 1
fi
ok "APK 已生成: $(ls -la "$APK_REL_PATH" | awk '{print $5" bytes"}')"

# -----------------------------------------------------------------------------
log "[5/5] 发布到 Gitee Release"

# 从 git remote 解析 owner/repo（兼容 https 带令牌、ssh 两种形式）
ORIGIN_URL="$(git remote get-url origin 2>/dev/null || echo "")"
REPO_SLUG="$(printf '%s' "$ORIGIN_URL" \
    | sed -E 's#\.git$##; s#^[a-zA-Z]+://##; s#^[^@/]*@##; s#^[^/:]+[:/]##')"
OWNER="${REPO_SLUG%%/*}"
NAME="${REPO_SLUG##*/}"

if [ -z "$OWNER" ] || [ -z "$NAME" ] || [ "$OWNER" = "$NAME" ]; then
    err "无法解析仓库地址（origin=$ORIGIN_URL）"
    exit 1
fi
ok "目标仓库: ${OWNER}/${NAME}"

TOKEN="${CI_TOKEN:-}"
if [ -z "$TOKEN" ]; then
    err "未设置 CI_TOKEN —— 已跳过 Release 发布"
    echo "  APK 已保存为流水线制品，可在流水线页面下载。"
    echo "  如需自动发布，请在仓库流水线中配置全局变量 CI_TOKEN。"
    exit 0
fi

BRANCH="${CI_BRANCH:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo main)}"
TAG="build-$(date +%Y%m%d-%H%M%S)"

echo "  创建 Release: ${TAG} ..."
CREATE_RESP="$(curl -sS -X POST "${GITEE_API}/repos/${OWNER}/${NAME}/releases" \
    -H "Authorization: token ${TOKEN}" \
    --data-urlencode "tag_name=${TAG}" \
    --data-urlencode "target_commitish=${BRANCH}" \
    --data-urlencode "name=自动构建 ${TAG}" \
    --data-urlencode "body=由 Gitee 流水线自动构建于 $(date '+%Y-%m-%d %H:%M:%S')

分支: ${BRANCH}
提交: $(git rev-parse --short HEAD 2>/dev/null)

## 安装方式
手机浏览器打开本页 → 下载 app-debug.apk → 允许安装未知来源应用。")"

RELEASE_ID="$(printf '%s' "$CREATE_RESP" \
    | grep -oE '"id"[[:space:]]*:[[:space:]]*[0-9]+,[[:space:]]*"tag_name"' \
    | grep -oE '[0-9]+' | head -1)"

if [ -z "$RELEASE_ID" ]; then
    err "Release 创建失败，API 返回："
    printf '%s\n' "$CREATE_RESP" | head -20
    exit 1
fi
ok "Release 创建成功 (id=${RELEASE_ID})"

echo "  上传 APK ..."
UPLOAD_RESP="$(curl -sS -X POST \
    "${GITEE_API}/repos/${OWNER}/${NAME}/releases/${RELEASE_ID}/attach_files" \
    -H "Authorization: token ${TOKEN}" \
    -F "file=@${APK_REL_PATH}")"

DOWNLOAD_URL="$(printf '%s' "$UPLOAD_RESP" \
    | grep -oE '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]+"' \
    | head -1 | sed -E 's#.*"browser_download_url"[[:space:]]*:[[:space:]]*"([^"]+)"#\1#')"

if [ -z "$DOWNLOAD_URL" ]; then
    err "APK 上传失败，API 返回："
    printf '%s\n' "$UPLOAD_RESP" | head -20
    exit 1
fi

# -----------------------------------------------------------------------------
log "🎉 构建发布完成"
cat <<EOF

  ┌──────────────────────────────────────────────────────────────┐
  │  APK 直接下载地址（手机浏览器打开即可）                        │
  │                                                              │
  │  ${DOWNLOAD_URL}
  │                                                              │
  │  Release 页面:                                               │
  │  https://gitee.com/${OWNER}/${NAME}/releases
  └──────────────────────────────────────────────────────────────┘

EOF
