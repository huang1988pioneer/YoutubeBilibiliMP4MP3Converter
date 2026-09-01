#!/bin/zsh
set -euo pipefail

dotnet build -p:UsedAvaloniaProducts=

APP_DIR="YoutubeOrBilibiliMP3Converter.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RES_DIR="$APP_DIR/Contents/Resources"

mkdir -p "$MACOS_DIR" "$RES_DIR"
cp -R bin/Debug/net10.0/. "$MACOS_DIR/"
cp Info.plist "$APP_DIR/Contents/Info.plist"
cp Assets/AppIcon.icns "$RES_DIR/AppIcon.icns"
chmod +x "$MACOS_DIR/YoutubeOrBilibiliMP3Converter"
touch "$APP_DIR"

open -n "$APP_DIR"
