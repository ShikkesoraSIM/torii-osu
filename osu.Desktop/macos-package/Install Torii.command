#!/bin/bash
# Installs Torii into your Applications folder and opens it.
#
# Why this exists: Torii isn't signed with a paid Apple developer certificate, and
# macOS marks everything a browser downloads as "quarantined". Together that shows
# up as "Torii is damaged and can't be opened" the first time you try it. This
# script clears the quarantine flag (safe: you downloaded it yourself), gives the
# app a local signature so the system is happy launching it, and puts it where
# updates expect it. Nothing else on your system is touched, and no password is
# needed for a normal account.

set -e
cd "$(dirname "$0")"

APP="Torii.app"

if [ ! -d "$APP" ]; then
  echo "Torii.app isn't next to this script. Keep both files in the same folder and run it again."
  exit 1
fi

DEST="/Applications"
if [ ! -w "$DEST" ]; then
  DEST="$HOME/Applications"
  mkdir -p "$DEST"
fi

echo "Installing Torii to $DEST ..."

xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
codesign --force --deep --sign - "$APP" 2>/dev/null || true

rm -rf "$DEST/$APP"
cp -R "$APP" "$DEST/"

xattr -dr com.apple.quarantine "$DEST/$APP" 2>/dev/null || true

echo "Done. Opening Torii..."
open "$DEST/$APP"
