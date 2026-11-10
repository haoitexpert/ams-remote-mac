#!/bin/bash
# Builds everything on one Mac (.NET 10 SDK + Xcode command-line tools), the same output as the GitHub workflow:
#   build/dist/CLT-Mac-arm64.zip, build/dist/CLT-Mac-x64.zip   the client packages AMS serves to Macs
#   build/dist/AMS-Remote-Setup.pkg                            the installer stub AMS exports with its address and key
# Usage: ./build.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BUILD="$ROOT/build"
rm -rf "$BUILD"
mkdir -p "$BUILD/dist"

echo "=== IT Remote Support (universal) ==="
(cd "$ROOT/SupportApp" && swift build -c release --arch arm64 --arch x86_64)
BIN="$(cd "$ROOT/SupportApp" && swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"
APP="$BUILD/AMS Remote Support.app"
mkdir -p "$APP/Contents/MacOS"
cp "$BIN/AMS-Remote-Support" "$APP/Contents/MacOS/"
cp "$ROOT/SupportApp/Info.plist" "$APP/Contents/"

for ARCH in arm64 x64; do
    echo "=== CLT-Agent + CLT-Desktop ($ARCH) ==="
    OUT="$BUILD/dist/CLT-Mac-$ARCH"
    dotnet publish "$ROOT/Agent/Agent.csproj" -c Release -r "osx-$ARCH" -o "$BUILD/net-$ARCH"
    dotnet publish "$ROOT/Desktop/Desktop.csproj" -c Release -r "osx-$ARCH" -o "$BUILD/net-$ARCH"
    HELPER="$OUT/AMS Remote Control.app/Contents"
    mkdir -p "$HELPER/MacOS"
    cp "$BUILD/net-$ARCH/CLT-Agent" "$OUT/"
    cp "$BUILD/net-$ARCH/CLT-Desktop" "$HELPER/MacOS/"
    cp "$ROOT/Desktop/Info.plist" "$HELPER/"
    cp -R "$APP" "$OUT/"
    cp "$ROOT/LaunchDaemons/"*.plist "$ROOT/Installer/uninstall.sh" "$OUT/"
    (cd "$OUT" && zip -qry "../CLT-Mac-$ARCH.zip" .)
done

echo "=== AMS-Remote-Setup.pkg ==="
mkdir -p "$BUILD/pkg-scripts"
cp "$ROOT/pkg/postinstall" "$ROOT/Installer/install.sh" "$ROOT/Installer/uninstall.sh" "$BUILD/pkg-scripts/"
chmod 755 "$BUILD/pkg-scripts/"*
pkgbuild --nopayload --scripts "$BUILD/pkg-scripts" --identifier com.ams.remote-setup --version 1.0 \
    "$BUILD/dist/AMS-Remote-Setup.pkg"

echo ""
echo "Done: $BUILD/dist"
echo "  CLT-Mac-*.zip          → upload on AMS: IT Assets → Settings → Remote Access → Client package"
echo "  AMS-Remote-Setup.pkg   → AMS repo: Services/RemoteControl/Assets/, then deploy AMS"
