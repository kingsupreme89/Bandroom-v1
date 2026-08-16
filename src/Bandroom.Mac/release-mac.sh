#!/bin/bash
# Attaches a Mac build to an EXISTING GitHub release as a downloadable asset (does not create a
# new release or tag -- Windows' release.ps1 owns versioning/tagging; this just gives Mac users
# something real to download from the same release page, which doesn't exist today: every
# published release so far only has Windows Squirrel assets).
#
# Usage: ./release-mac.sh [tag]   (defaults to the latest release, e.g. v1.1.17)
#
# Requires `gh` authenticated with push access to kingsupreme89/Bandroom-v1.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="kingsupreme89/Bandroom-v1"

TAG="${1:-}"
if [ -z "$TAG" ]; then
  TAG="$(gh release list --repo "$REPO" --limit 1 --json tagName -q '.[0].tagName')"
fi
echo "==> Target release: $TAG"

echo "==> Building Bandroom.app..."
"$SCRIPT_DIR/publish-mac.sh" osx-arm64

APP_DIR="$SCRIPT_DIR/bin/publish/Bandroom.app"
ZIP_NAME="Bandroom-mac-$TAG.zip"
ZIP_PATH="$SCRIPT_DIR/bin/publish/$ZIP_NAME"

echo "==> Zipping (ditto, preserves the app bundle + code signature)..."
rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"
echo "    $(du -h "$ZIP_PATH" | cut -f1)  $ZIP_PATH"

echo "==> Uploading to $TAG as a release asset..."
gh release upload "$TAG" "$ZIP_PATH" --repo "$REPO" --clobber

echo ""
echo "==> Done. Mac users can now download $ZIP_NAME from:"
echo "    https://github.com/$REPO/releases/tag/$TAG"
echo ""
echo "    Note: this build is only ad-hoc signed (not notarized with a real Apple Developer ID),"
echo "    so first launch will need a right-click > Open (or a Gatekeeper override) instead of a"
echo "    plain double-click -- see docs for a notarized release if that friction matters."
