#!/bin/zsh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

VERSION="$(python3 - <<'PY'
import re, pathlib
text = pathlib.Path("YoutubeOrBilibiliMP3Converter.csproj").read_text()
match = re.search(r"<Version>([^<]+)</Version>", text)
print(match.group(1) if match else "0.0.0")
PY
)"
NAME="YoutubeOrBilibiliMP3Converter"
DIST="$ROOT/dist"
STAGE="$ROOT/release-staging"

rm -rf "$DIST" "$STAGE"
mkdir -p "$DIST" "$STAGE"

write_usage() {
  local dest="$1"
  local platform="$2"
  cat > "$dest" <<EOF
影音轉換大師 ${VERSION}  (${platform})

第一次使用前請安裝 yt-dlp 與 ffmpeg：

  Windows:  winget install yt-dlp.yt-dlp Gyan.FFmpeg
  macOS:    brew install yt-dlp ffmpeg
  Linux:    請用發行版套件或官方二進位安裝 yt-dlp 與 ffmpeg

YouTube 若出現 HTTP 403，請先更新 yt-dlp。
EOF
}

publish_rid() {
  local rid="$1"
  local out="$STAGE/publish/$rid"
  echo "==> publish $rid"
  dotnet publish \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:UsedAvaloniaProducts= \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$out"
}

package_windows() {
  local rid="$1"
  local zip_name="${NAME}-v${VERSION}-${rid}.zip"
  local src="$STAGE/publish/$rid"
  local pack="$STAGE/pack/$rid"
  rm -rf "$pack"
  mkdir -p "$pack"
  cp -R "$src"/. "$pack/"
  write_usage "$pack/README.txt" "Windows"
  (
    cd "$pack"
    zip -qry "$DIST/$zip_name" .
  )
  echo "created $DIST/$zip_name"
}

package_macos() {
  local rid="$1"
  local tar_name="${NAME}-v${VERSION}-${rid}.tar.gz"
  local src="$STAGE/publish/$rid"
  local app="$STAGE/pack/${rid}/${NAME}.app"
  rm -rf "$STAGE/pack/$rid"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$src"/. "$app/Contents/MacOS/"
  cp "$ROOT/Info.plist" "$app/Contents/Info.plist"
  chmod +x "$app/Contents/MacOS/${NAME}"
  if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign - "$app" >/dev/null 2>&1 || true
  fi
  write_usage "$STAGE/pack/${rid}/README.txt" "macOS"
  tar -czf "$DIST/$tar_name" -C "$STAGE/pack/$rid" "${NAME}.app" README.txt
  echo "created $DIST/$tar_name"
}

package_linux() {
  local rid="$1"
  local tar_name="${NAME}-v${VERSION}-${rid}.tar.gz"
  local src="$STAGE/publish/$rid"
  local pack="$STAGE/pack/$rid/${NAME}"
  rm -rf "$STAGE/pack/$rid"
  mkdir -p "$pack"
  cp -R "$src"/. "$pack/"
  chmod +x "$pack/${NAME}"
  write_usage "$pack/README.txt" "Linux"
  tar -czf "$DIST/$tar_name" -C "$STAGE/pack/$rid" "${NAME}"
  echo "created $DIST/$tar_name"
}

# Match previous GitHub releases: win-x64, osx-arm64, osx-x64, linux-x64
publish_rid win-x64
publish_rid osx-arm64
publish_rid osx-x64
publish_rid linux-x64

package_windows win-x64
package_macos osx-arm64
package_macos osx-x64
package_linux linux-x64

(
  cd "$DIST"
  shasum -a 256 *.zip *.tar.gz > SHA256SUMS.txt
)

echo
echo "Release ${VERSION} artifacts:"
ls -lh "$DIST"
echo
cat "$DIST/SHA256SUMS.txt"
