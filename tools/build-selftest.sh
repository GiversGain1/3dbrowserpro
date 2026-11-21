#!/usr/bin/env bash
# Monta tools/_selfcheck.html embutindo os fontes da extensão (não precisa de Node).
set -e
cd "$(dirname "$0")/.."
{
  printf '<!doctype html>\n<html lang="pt-BR"><head><meta charset="utf-8"><title>3D Browser Pro - selfcheck</title></head><body style="background:#0b0b14;color:#eef2ff;font:12px/1.5 ui-monospace,monospace;padding:16px"><pre id="out">rodando...</pre>\n'
  for pair in formats:src/lib/formats.js settings:src/lib/settings.js hook:src/content/hook.js bridge:src/content/bridge.js popup:src/popup/popup.js options:src/options/options.js background:src/background.js; do
    name="${pair%%:*}"; path="${pair#*:}"
    printf '<script id="f-%s" type="text/plain">\n' "$name"
    cat "$path"
    printf '\n</script>\n'
  done
  printf '<script>\n'
  cat tools/selftest.js
  printf '\nwindow.__runAll().catch(console.error);\n</script>\n</body></html>\n'
} > tools/_selfcheck.html
echo "gerado tools/_selfcheck.html ($(wc -c < tools/_selfcheck.html) bytes)"
