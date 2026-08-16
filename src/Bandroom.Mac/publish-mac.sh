#!/bin/bash
# Builds a real double-clickable Bandroom.app bundle from src/Bandroom.Mac, self-contained
# (no .NET runtime install required on the user's Mac). Run from anywhere; paths are relative
# to this script's location.
#
# Usage: ./publish-mac.sh [osx-arm64|osx-x64]   (defaults to osx-arm64, the arch this was built on)

set -euo pipefail

RID="${1:-osx-arm64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/Bandroom.Mac.csproj"
PUBLISH_DIR="$SCRIPT_DIR/bin/publish/$RID"
APP_NAME="Bandroom.app"
APP_DIR="$SCRIPT_DIR/bin/publish/$APP_NAME"
VERSION="$(grep -m1 -oE '"[0-9]+\.[0-9]+\.[0-9]+"' "$SCRIPT_DIR/../../appcast.xml" 2>/dev/null | tr -d '"' || echo "1.0.0")"

echo "==> Publishing self-contained build for $RID..."
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR"

echo "==> Assembling $APP_NAME..."
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

# Everything the published build produced (executable, dylibs, wwwroot/, Assets/, TeamLogos/,
# TeamBackgrounds/, Fonts/, the OCR bridge script) goes under MacOS/ as the working directory the
# app expects (matches AppContext.BaseDirectory-relative paths already used throughout the shared
# ConfigStore/etc. code).
cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/Bandroom.Mac"

# Songs/Default (~2.6GB) is NOT part of the real distributable: the actual Windows installer
# (BandroomSetup.exe) is only ~46MB -- that content is fetched on demand at runtime via
# DefaultSongPackService.cs / bridge.DownloadDefaultSongPack into ConfigStore.
# DownloadedDefaultSongsFolder, not shipped in the installer. The .csproj's Content include for
# it exists for local `dotnet run` development convenience, not for what should ship here; strip
# it from the packaged app so this build matches the real Windows distributable's footprint
# instead of silently ballooning to ~2.8GB (and likely exceeding GitHub's 2GB release-asset
# limit if ever uploaded as-is).
rm -rf "$APP_DIR/Contents/MacOS/Songs/Default"

cp "$SCRIPT_DIR/Resources/AppIcon.icns" "$APP_DIR/Contents/Resources/AppIcon.icns"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleDevelopmentRegion</key>
	<string>en</string>
	<key>CFBundleExecutable</key>
	<string>Bandroom.Mac</string>
	<key>CFBundleIconFile</key>
	<string>AppIcon</string>
	<key>CFBundleIdentifier</key>
	<string>com.bandroom.mac</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>CFBundleName</key>
	<string>Bandroom</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>$VERSION</string>
	<key>CFBundleVersion</key>
	<string>$VERSION</string>
	<key>LSMinimumSystemVersion</key>
	<string>12.0</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSMicrophoneUsageDescription</key>
	<string>Bandroom does not use the microphone.</string>
</dict>
</plist>
PLIST

# Ad-hoc sign so Gatekeeper doesn't refuse to launch it at all (no Developer ID needed for
# local/personal use; a real notarized release build would sign with a real Developer ID
# certificate and staple a notarization ticket here instead).
echo "==> Ad-hoc code signing..."
codesign --force --deep --sign - "$APP_DIR"

echo "==> Done: $APP_DIR"
echo "    Run it with: open \"$APP_DIR\""
