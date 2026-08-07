#!/bin/zsh
set -euo pipefail

dotnet build -p:UsedAvaloniaProducts=

APP_DIR="YoutubeOrBilibiliMP3Converter.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"

mkdir -p "$MACOS_DIR"
cp -R bin/Debug/net10.0/. "$MACOS_DIR/"
cp Info.plist "$APP_DIR/Contents/Info.plist"
chmod +x "$MACOS_DIR/YoutubeOrBilibiliMP3Converter"

open -n "$APP_DIR"
