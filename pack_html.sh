#!/usr/bin/env bash
# =============================================================================
#  把 html_app_src/ 打包成 app/src/main/assets/html_app.zip
#  用法: bash pack_html.sh
# =============================================================================
set -euo pipefail

SRC="html_app_src"
OUT="app/src/main/assets/html_app.zip"

[ -d "$SRC" ] || { echo "找不到 $SRC 目录"; exit 1; }
[ -f "$SRC/index.html" ] || { echo "$SRC/index.html 不存在（zip 根目录必须有 index.html）"; exit 1; }

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"

# -j 去掉目录层级，保证 index.html 在 zip 根目录
if command -v zip >/dev/null 2>&1; then
    (cd "$SRC" && zip -q -r -j "../$OUT" .)
else
    # 没有 zip 命令时用 Python
    python -c "
import os, zipfile
src = '$SRC'
out = '$OUT'
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(src):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, src))
" 2>/dev/null || { echo "打包失败：需要 zip 或 python"; exit 1; }
fi

echo "✔ 已生成 $OUT ($(du -h "$OUT" | cut -f1))"
echo "  接下来推送代码即可触发云打包：git add -A && git commit -m 'update html app' && git push"
