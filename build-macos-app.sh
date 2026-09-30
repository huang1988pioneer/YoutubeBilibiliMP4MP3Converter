#!/bin/zsh
set -euo pipefail

# Use the user-local .NET SDK when dotnet is not on PATH (e.g. ~/.dotnet).
if ! command -v dotnet >/dev/null 2>&1; then
  for dir in "$HOME/.dotnet" /usr/local/share/dotnet; do
    if [ -x "$dir/dotnet" ]; then
      export DOTNET_ROOT="$dir"
      export PATH="$dir:$PATH"
      break
    fi
  done
fi
command -v dotnet >/dev/null 2>&1 || { echo "找不到 dotnet，請先安裝 .NET 8 SDK"; exit 1; }

dotnet build -p:UsedAvaloniaProducts=

APP_DIR="YoutubeOrBilibiliMP3Converter.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RES_DIR="$APP_DIR/Contents/Resources"

mkdir -p "$MACOS_DIR" "$RES_DIR"
cp -R bin/Debug/net8.0/. "$MACOS_DIR/"
cp Info.plist "$APP_DIR/Contents/Info.plist"
cp Assets/AppIcon.icns "$RES_DIR/AppIcon.icns"
chmod +x "$MACOS_DIR/YoutubeOrBilibiliMP3Converter"
touch "$APP_DIR"

open -n "$APP_DIR"
