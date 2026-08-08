#!/usr/bin/env bash
# Catches the exact bug class that broke .team-picker-grid and .bandroom-songs-grid:
# an element is given id="foo" (no class="foo"), then style.css targets ".foo" as a
# CSS class selector. The rule silently never matches -- the element renders unstyled/
# unlaid-out with no error anywhere. Run this before every release.
set -euo pipefail
cd "$(dirname "$0")/.."

HTML="wwwroot/index.html"
CSS="wwwroot/style.css"

# Elements that have id="X" but NOT class="X" on the same tag.
id_only=$(grep -oE '<[a-zA-Z][^>]*\bid="[a-zA-Z0-9_-]+"[^>]*>' "$HTML" | while read -r tag; do
  id=$(echo "$tag" | grep -oE 'id="[a-zA-Z0-9_-]+"' | sed 's/id="//;s/"//')
  if ! echo "$tag" | grep -oE 'class="[^"]*"' | grep -qw "$id"; then
    echo "$id"
  fi
done | sort -u)

# CSS class selectors (".foo") that appear WITHOUT a corresponding "#foo" selector anywhere.
found=0
for id in $id_only; do
  if grep -qE "\.${id}([^a-zA-Z0-9_-]|$)" "$CSS" && ! grep -qE "#${id}([^a-zA-Z0-9_-]|$)" "$CSS"; then
    echo "MISMATCH: id=\"$id\" exists in $HTML with no class=\"$id\", but style.css only has a .$id class selector (never matches). Use #$id instead."
    found=1
  fi
done

if [ "$found" -eq 1 ]; then
  echo ""
  echo "Found id-vs-class CSS selector mismatches -- these render silently broken with no console error. Fix before shipping."
  exit 1
fi
echo "No id-vs-class CSS selector mismatches found."
