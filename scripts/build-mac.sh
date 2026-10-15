#!/usr/bin/env bash
# Build KanjiPop.app + zip for macOS. Run on a Mac (or the macOS CI runner).
# Usage: scripts/build-mac.sh osx-arm64|osx-x64 [version]
set -euo pipefail
RID="${1:-osx-arm64}"
VERSION="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/KanjiPop.Mac/KanjiPop.Mac.csproj)}"
cd "$(dirname "$0")/.."

[ -f data/dict.db ] || python3 tools/build_dict.py

PUB="dist/mac-$RID/publish"
APP="dist/mac-$RID/KanjiPop.app"
rm -rf "dist/mac-$RID"
dotnet publish src/KanjiPop.Mac/KanjiPop.Mac.csproj -c Release -r "$RID" --self-contained -o "$PUB"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
rm -f "$APP"/Contents/MacOS/*.pdb
chmod +x "$APP/Contents/MacOS/KanjiPop"

# icon
python3 - "$APP/Contents/Resources/KanjiPop.icns" <<'PY'
import sys
from PIL import Image
Image.open("src/KanjiPop.Mac/Assets/icon.png").convert("RGBA").save(sys.argv[1])
PY

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>KanjiPop</string>
  <key>CFBundleDisplayName</key><string>KanjiPop</string>
  <key>CFBundleIdentifier</key><string>com.kanjipop.app</string>
  <key>CFBundleExecutable</key><string>KanjiPop</string>
  <key>CFBundleIconFile</key><string>KanjiPop</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

# smoke test: dictionary + SQLite native library load and the lookup engine works
case "$(uname -m)/$RID" in
  arm64/osx-arm64|x86_64/osx-x64)
    OUT="$("$APP/Contents/MacOS/KanjiPop" --dump "食べました")"
    echo "$OUT"
    echo "$OUT" | grep -q "食べる" || { echo "smoke test failed"; exit 1; } ;;
  *) echo "skipping smoke test (other architecture)" ;;
esac

# ad-hoc signature (required for arm64; not a Developer ID signature)
codesign --force --deep --sign - "$APP"

cp README.md NOTICE.md "dist/mac-$RID/" 
ZIP="dist/KanjiPop-mac-${RID#osx-}.zip"
rm -f "$ZIP"
(cd "dist/mac-$RID" && ditto -c -k --keepParent KanjiPop.app "../KanjiPop-mac-${RID#osx-}.zip")
echo "Done: $ZIP"
