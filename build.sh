#!/bin/bash
# Builds all macOS remote support components.
# Run on a Mac with .NET 10 SDK and Xcode command-line tools installed.
# Usage: ./build.sh [osx-arm64|osx-x64]
set -euo pipefail

PLATFORM="${1:-osx-arm64}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"
CONFIGURATION="Release"

echo "Building AMS Remote Support for $PLATFORM..."
echo "Output: $BUILD_DIR"
echo ""

# ── 1. .NET Agent ──
echo "=== Building CLT Agent ==="
dotnet publish "$SCRIPT_DIR/Agent/Agent.csproj" \
    -c "$CONFIGURATION" -r "$PLATFORM" --self-contained \
    -o "$BUILD_DIR/Agent"
echo "  → $BUILD_DIR/Agent/CLT-Agent"

# ── 2. .NET Desktop ──
echo "=== Building CLT Desktop ==="
dotnet publish "$SCRIPT_DIR/Desktop/Desktop.csproj" \
    -c "$CONFIGURATION" -r "$PLATFORM" --self-contained \
    -o "$BUILD_DIR/Desktop"
echo "  → $BUILD_DIR/Desktop/CLT-Desktop"

# ── 3. Swift Support App ──
echo "=== Building IT Remote Support App ==="
cd "$SCRIPT_DIR/SupportApp"
swift build -c release
APP_DIR="$BUILD_DIR/AMS Remote Support.app"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp ".build/release/AMS-Remote-Support" "$APP_DIR/Contents/MacOS/"

cat > "$APP_DIR/Contents/Info.plist" << 'PEOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>AMS-Remote-Support</string>
    <key>CFBundleIdentifier</key>
    <string>com.ams.remote-support</string>
    <key>CFBundleName</key>
    <string>AMS Remote Support</string>
    <key>CFBundleDisplayName</key>
    <string>IT Remote Support</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>
PEOF
echo "  → $APP_DIR"

cd "$SCRIPT_DIR"

# ── 4. Package for distribution ──
echo ""
echo "=== Creating distribution package ==="
DIST_DIR="$BUILD_DIR/dist"
DIST_NAME="CLT-Mac-$(echo "$PLATFORM" | sed 's/osx-//')"
mkdir -p "$DIST_DIR/$DIST_NAME"

cp -R "$BUILD_DIR/Agent/"* "$DIST_DIR/$DIST_NAME/"
cp -R "$BUILD_DIR/Desktop/"* "$DIST_DIR/$DIST_NAME/"
cp -R "$APP_DIR" "$DIST_DIR/$DIST_NAME/"
cp -R "$SCRIPT_DIR/Installer/"* "$DIST_DIR/$DIST_NAME/"
cp -R "$SCRIPT_DIR/LaunchDaemons" "$DIST_DIR/$DIST_NAME/"

cd "$BUILD_DIR/dist"
zip -r "$DIST_DIR/$DIST_NAME.zip" "$DIST_NAME/"
echo "  → $DIST_DIR/$DIST_NAME.zip"

echo ""
echo "Build complete!"
echo "To install on a Mac:"
echo "  1. Copy $DIST_NAME.zip to the Mac"
echo "  2. unzip $DIST_NAME.zip"
echo "  3. cd $DIST_NAME"
echo "  4. sudo ./install.sh <ams-url> <agent-key>"
