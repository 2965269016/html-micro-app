#!/usr/bin/env bash
# =============================================================================
#  一键建仓 + 推送脚本（Gitee）
#  用法:
#      bash init_repo.sh            # 默认推送到 Gitee
#      bash init_repo.sh github     # 切换到 GitHub（备选）
#
#  本脚本自动完成：
#      1. 检查/初始化 git 身份
#      2. 在 Gitee 创建远端仓库（用已登录的 gitee CLI）
#      3. 提交全部代码并推送
#      4. 提示后续如何拿到 APK 下载地址
# =============================================================================
set -euo pipefail

REPO_NAME="html-micro-app"
REPO_DESC="离线 HTML 微应用容器（Kotlin + WebView，Gitee 云打包）"
PLATFORM="${1:-gitee}"

GREEN='\033[1;32m'; YELLOW='\033[1;33m'; RED='\033[1;31m'; CYAN='\033[1;36m'; NC='\033[0m'
step() { printf '\n\033[1;36m========== [%s] %s ==========\033[0m\n' "$1" "$2"; }
ok()   { printf "${GREEN}  ✔ %s${NC}\n" "$*"; }
warn() { printf "${YELLOW}  ⚠ %s${NC}\n" "$*"; }
die()  { printf "${RED}  ✘ %s${NC}\n" "$*" >&2; exit 1; }

# -----------------------------------------------------------------------------
step "0/5" "环境检查"

git --version >/dev/null || die "未安装 git，请先安装 Git for Windows"

# git 身份
if ! git config --global user.email >/dev/null 2>&1; then
    read -rp "  请输入 Git 邮箱: " GIT_EMAIL
    git config --global user.email "$GIT_EMAIL"
fi
if ! git config --global user.name >/dev/null 2>&1; then
    read -rp "  请输入 Git 用户名: " GIT_NAME
    git config --global user.name "$GIT_NAME"
fi
ok "git 身份: $(git config --global user.name) <$(git config --global user.email)>"

if [ "$PLATFORM" != "gitee" ]; then
    warn "已选择 GitHub 模式（备选方案），Gitee 流水线不会被触发"
fi

# -----------------------------------------------------------------------------
step "1/5" "初始化本地仓库"

if [ ! -d .git ]; then
    git init -b main
    ok "已执行 git init -b main"
else
    ok "检测到已有 .git，跳过初始化"
fi

# -----------------------------------------------------------------------------
step "2/5" "在 Gitee 创建远端仓库"

if ! command -v gitee >/dev/null 2>&1; then
    die "未找到 gitee CLI。
  请先安装（任选其一）：
    · GitHub 下载: https://github.com/AndersHsueh/gitee-cli/releases
    · 或直接用网页创建仓库后，手动执行:
        git remote add origin https://gitee.com/<你的用户名>/${REPO_NAME}.git
        git push -u origin main"
fi

if ! gitee auth status >/dev/null 2>&1; then
    warn "gitee CLI 未登录，请先执行: gitee auth login"
    die "登录后重跑本脚本"
fi

GITEE_USER="$(gitee api GET /user --jq '.login' 2>/dev/null || true)"
[ -z "$GITEE_USER" ] && GITEE_USER="$(gitee auth status 2>/dev/null | grep -oE 'User: [^ ]+' | awk '{print $2}')"
[ -z "$GITEE_USER" ] && die "无法获取 Gitee 用户名"
ok "Gitee 账号: ${GITEE_USER}"

REMOTE_URL="https://gitee.com/${GITEE_USER}/${REPO_NAME}.git"

# 仓库已存在则跳过创建
if gitee api GET "/repos/${GITEE_USER}/${REPO_NAME}" >/dev/null 2>&1; then
    ok "仓库已存在，跳过创建"
else
    gitee repo create \
        --name "$REPO_NAME" \
        --description "$REPO_DESC" \
        --private \
        --auto-init=false >/dev/null 2>&1 \
        || die "Gitee 仓库创建失败，请检查网络或令牌权限（需要 projects 权限）"
    ok "仓库创建成功: ${REMOTE_URL}"
fi

# -----------------------------------------------------------------------------
step "3/5" "提交代码"

git add -A
if git diff --cached --quiet; then
    ok "没有变更，跳过提交"
else
    git commit -q -m "feat: 离线 HTML 微应用容器（WebView + 本地存储 + 桌面快捷方式）"
    ok "已提交"
fi

# -----------------------------------------------------------------------------
step "4/5" "推送到 Gitee"

git remote remove origin 2>/dev/null || true
git remote add origin "$REMOTE_URL"
git branch -M main
git push -u origin main --force-with-lease 2>/dev/null || git push -u origin main --force
ok "推送完成"

# -----------------------------------------------------------------------------
step "5/5" "完成"

cat <<EOF

$(printf "${GREEN}")╔══════════════════════════════════════════════════════════════╗$(printf "${NC}")
$(printf "${GREEN}")║  ✅ 代码已推送到 Gitee，云打包流水线已自动触发                ║$(printf "${NC}")
$(printf "${GREEN}")╚══════════════════════════════════════════════════════════════╝$(printf "${NC}")

  仓库地址   : ${REMOTE_URL}
  流水线页面 : https://gitee.com/${GITEE_USER}/${REPO_NAME}/pipeline
  构建日志   : 打开上面的流水线页面，点击最新一条记录即可查看实时日志

  ── 接下来只需两步 ──────────────────────────────────────────────

  第 1 步 · 等 3~8 分钟（首次构建要装 JDK + Android SDK，比较慢）

  第 2 步 · 运行下面这条命令，直接拿到 APK 下载地址：

        bash get_apk.sh

  ── 如果流水线没有自动触发 ──────────────────────────────────────

  打开 https://gitee.com/${GITEE_USER}/${REPO_NAME}/pipeline
  → 找到「HTML微应用容器 · 云打包」→ 点「立即执行」

  ── 如果不想自动发布 Release（可选）────────────────────────────

  在流水线页面 → 设置 → 全局变量 中添加：
      名称 CI_TOKEN / 值 你的 Gitee 私人令牌
  令牌获取: https://gitee.com/profile/personal_access_tokens
  （需勾选 projects 权限；不配也能构建，APK 会存在流水线制品里）

EOF
