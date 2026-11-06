#!/usr/bin/env bash
# =============================================================================
#  获取最新构建的 APK 下载地址（Gitee Release）
#  用法: bash get_apk.sh
# =============================================================================
set -euo pipefail

REPO_NAME="html-micro-app"

CYAN='\033[1;36m'; GREEN='\033[1;32m'; RED='\033[1;31m'; YELLOW='\033[1;33m'; NC='\033[0m'

command -v gitee >/dev/null 2>&1 || { printf "${RED}未找到 gitee CLI${NC}\n"; exit 1; }

# 解析 owner/repo
if git remote get-url origin >/dev/null 2>&1; then
    SLUG="$(git remote get-url origin \
        | sed -E 's#\.git$##; s#^[a-zA-Z]+://##; s#^[^@/]*@##; s#^[^/:]+[:/]##')"
else
    USER="$(gitee api GET /user --jq '.login' 2>/dev/null || true)"
    [ -z "$USER" ] && { printf "${RED}无法确定仓库地址，请在项目目录内运行${NC}\n"; exit 1; }
    SLUG="${USER}/${REPO_NAME}"
fi

printf "${CYAN}查询仓库 ${SLUG} 的最新 Release ...${NC}\n\n"

RESP="$(gitee api GET "/repos/${SLUG}/releases?per_page=1&direction=desc" 2>/dev/null || echo "[]")"

URL="$(printf '%s' "$RESP" | grep -oE '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]+"' \
    | head -1 | sed -E 's#.*"browser_download_url"[[:space:]]*:[[:space:]]*"([^"]+)"#\1#')"
TAG="$(printf '%s' "$RESP" | grep -oE '"tag_name"[[:space:]]*:[[:space:]]*"[^"]+"' \
    | head -1 | sed -E 's#.*"tag_name"[[:space:]]*:[[:space:]]*"([^"]+)"#\1#')"

if [ -z "$URL" ]; then
    printf "${YELLOW}还没有已发布的 Release。${NC}\n\n"
    echo "可能原因：流水线还在跑 / 未配置 CI_TOKEN / 构建失败。"
    echo "请到流水线页面查看：https://gitee.com/${SLUG}/pipeline"
    echo
    echo "等流水线跑完后重试本命令即可。"
    exit 0
fi

cat <<EOF
$(printf "${GREEN}")╔══════════════════════════════════════════════════════════════╗$(printf "${NC}")
$(printf "${GREEN}")║  📱 APK 下载地址（手机浏览器打开 / 扫码即可安装）             ║$(printf "${NC}")
$(printf "${GREEN}")╚══════════════════════════════════════════════════════════════╝$(printf "${NC}")

  ${URL}

  版本标签 : ${TAG}
  发布页面 : https://gitee.com/${SLUG}/releases

  ── 手机安装步骤 ──────────────────────────────────────────────
  1. 手机浏览器打开上面的地址（或用微信扫一扫打开）
  2. 下载 app-debug.apk
  3. 如提示「禁止安装未知来源应用」→ 去设置里允许浏览器安装
  4. 安装完成后打开，点「添加到桌面」即可生成桌面快捷方式

EOF
