#!/bin/bash
# Attaches Mac build(s) to an EXISTING GitHub release as downloadable assets (does not create a
# new release or tag -- Windows' release.ps1 owns versioning/tagging; this just gives Mac users
# something real to download from the same release page).
#
# Usage: ./release-mac.sh [tag] [arm64|x64|both]
#   tag defaults to the latest release (e.g. v1.1.17); arch defaults to "both".
#
# Requires `gh` authenticated with push access to kingsupreme89/Bandroom-v1.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="kingsupreme89/Bandroom-v1"

TAG="${1:-}"
if [ -z "$TAG" ]; then
  TAG="$(gh release list --repo "$REPO" --limit 1 --json tagName -q '.[0].tagName')"
fi
ARCH_ARG="${2:-both}"
echo "==> Target release: $TAG"

publish_and_upload() {
  local rid="$1" label="$2"
  echo "==> Building Bandroom.app ($rid)..."
  "$SCRIPT_DIR/publish-mac.sh" "$rid"

  local app_dir="$SCRIPT_DIR/bin/publish/$rid/Bandroom.app"
  local zip_name="Bandroom-mac-$label-$TAG.zip"
  local zip_path="$SCRIPT_DIR/bin/publish/$rid/$zip_name"

  echo "==> Zipping ($label, ditto preserves the app bundle + code signature)..."
  rm -f "$zip_path"
  ditto -c -k --sequesterRsrc --keepParent "$app_dir" "$zip_path"
  echo "    $(du -h "$zip_path" | cut -f1)  $zip_path"

  echo "==> Uploading $zip_name to $TAG..."
  gh release upload "$TAG" "$zip_path" --repo "$REPO" --clobber
}

case "$ARCH_ARG" in
  arm64) publish_and_upload osx-arm64 "apple-silicon" ;;
  x64)   publish_and_upload osx-x64 "intel" ;;
  both)
    publish_and_upload osx-arm64 "apple-silicon"
    publish_and_upload osx-x64 "intel"
    ;;
  *) echo "Unknown arch '$ARCH_ARG' -- expected arm64, x64, or both" >&2; exit 1 ;;
esac

echo ""
echo "==> Done. Mac users can download from:"
echo "    https://github.com/$REPO/releases/tag/$TAG"
echo ""
echo "    Note: these builds are only ad-hoc signed (not notarized with a real Apple Developer ID),"
echo "    so first launch will need a right-click > Open (or a Gatekeeper override) instead of a"
echo "    plain double-click -- see docs for a notarized release if that friction matters."
