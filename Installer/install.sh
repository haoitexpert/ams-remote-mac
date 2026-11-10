#!/bin/bash
# Installs, or installs again, AMS Remote Support on this Mac.
#   sudo ./install.sh <ams-url> <agent-key> [package-dir]
# Without package-dir it downloads CLT-Mac-<arch>.zip from AMS, like the Windows installer does. AMS-Remote-Setup.pkg runs
# this script (pkg/postinstall) with the address and key AMS put in the .pkg when IT exported it.
set -euo pipefail

AMS_URL="${1:-}"
AGENT_KEY="${2:-}"
PACKAGE_DIR="${3:-}"

INSTALL_DIR=/usr/local/lib/CLT
APP="/Applications/AMS Remote Support.app"
DAEMON_LABEL=com.ams.clt-agent
AGENT_LABEL=com.ams.remote-support
DAEMON_PLIST="/Library/LaunchDaemons/$DAEMON_LABEL.plist"
AGENT_PLIST="/Library/LaunchAgents/$AGENT_LABEL.plist"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

fail() { echo "ERROR: $*" >&2; exit 1; }
json() { local s=${1//\\/\\\\}; s=${s//\"/\\\"}; printf '"%s"' "$s"; }
first() { find "$1" -name "$2" -type "$3" 2>/dev/null | head -n 1 || true; }
bootstrap() {
    # A job that was just booted out can take a moment to go; bootstrap fails until it has.
    for _ in 1 2 3 4 5; do
        launchctl bootstrap "$1" "$2" 2>/dev/null && return 0
        sleep 1
    done
    return 1
}

[ "$(id -u)" -eq 0 ] || fail "This installer needs administrator rights. Run: sudo $0 <ams-url> <agent-key>"
[ -n "$AMS_URL" ] && [ -n "$AGENT_KEY" ] || fail "Usage: sudo $0 <ams-url> <agent-key> [package-dir]"
AMS_URL="${AMS_URL%/}"
case "$(uname -m)" in
    arm64) ARCH=arm64 ;;
    *) ARCH=x64 ;;
esac

echo "AMS Remote Support for macOS"
echo "  Server: $AMS_URL"
echo "  Mac:    $ARCH"

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# ── 1. The client package ──
if [ -z "$PACKAGE_DIR" ]; then
    echo "Downloading CLT-Mac-$ARCH.zip from AMS..."
    curl -fsSL --max-time 900 -o "$WORK/package.zip" "$AMS_URL/remote/Content/CLT-Mac-$ARCH.zip" \
        || fail "Could not download CLT-Mac-$ARCH.zip from $AMS_URL. Check that this Mac reaches AMS, and that the macOS package is uploaded (IT Assets → Settings → Remote Access)."
    ditto -x -k "$WORK/package.zip" "$WORK/package" || fail "CLT-Mac-$ARCH.zip from AMS is not a valid zip."
    PACKAGE_DIR="$WORK/package"
fi

AGENT_BIN=$(first "$PACKAGE_DIR" CLT-Agent f)
HELPER_SRC=$(first "$PACKAGE_DIR" "AMS Remote Control.app" d)
APP_SRC=$(first "$PACKAGE_DIR" "AMS Remote Support.app" d)
DAEMON_SRC=$(first "$PACKAGE_DIR" "$DAEMON_LABEL.plist" f)
AGENT_SRC=$(first "$PACKAGE_DIR" "$AGENT_LABEL.plist" f)
UNINSTALL_SRC=$(first "$PACKAGE_DIR" uninstall.sh f)
[ -n "$UNINSTALL_SRC" ] || UNINSTALL_SRC="$SCRIPT_DIR/uninstall.sh"
for part in "$AGENT_BIN" "$HELPER_SRC" "$APP_SRC" "$DAEMON_SRC" "$AGENT_SRC" "$UNINSTALL_SRC"; do
    [ -n "$part" ] && [ -e "$part" ] || fail "The package is incomplete (it needs CLT-Agent, AMS Remote Control.app, AMS Remote Support.app, both launchd plists and uninstall.sh). Rebuild it with the ams-remote-mac workflow."
done

# ── 2. Register this Mac with AMS ──
# The device ID is the client's own: kept across reinstalls so AMS keeps seeing the same computer.
DEVICE_ID=""
[ -f "$INSTALL_DIR/ConnectionInfo.json" ] && DEVICE_ID=$(plutil -extract DeviceID raw -o - "$INSTALL_DIR/ConnectionInfo.json" 2>/dev/null || true)
[ -n "$DEVICE_ID" ] || DEVICE_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')

COMPUTER_NAME=$(scutil --get ComputerName 2>/dev/null || hostname -s)
SERIAL=$(ioreg -rd1 -c IOPlatformExpertDevice | awk -F'"' '/IOPlatformSerialNumber/ { print $4 }' || true)
MODEL=$(sysctl -n hw.model 2>/dev/null || echo Mac)

echo "Registering $COMPUTER_NAME (serial ${SERIAL:-none}, $MODEL)..."
BODY="{\"deviceId\":$(json "$DEVICE_ID"),\"hostName\":$(json "$COMPUTER_NAME"),\"serialNumber\":$(json "$SERIAL"),\"model\":$(json "Apple $MODEL")}"
STATUS=$(curl -sS -o "$WORK/register.json" -w '%{http_code}' --max-time 30 -X POST "$AMS_URL/api/v1/agent/register" \
    -H 'Content-Type: application/json; charset=utf-8' -H "X-Agent-Key: $AGENT_KEY" --data-binary "$BODY" || true)
case "${STATUS:-000}" in
    200) echo "  $(plutil -extract message raw -o - "$WORK/register.json" 2>/dev/null || echo Registered.)" ;;
    401) fail "AMS refused this installer: the agent key was changed, or remote access is off. Export the installer again from AMS." ;;
    *) echo "  Warning: AMS did not get the serial number (HTTP ${STATUS:-000}). The computer still links by computer name = asset tag." ;;
esac

# ── 3. Stop what an earlier install left running ──
CONSOLE_UID=$(stat -f%u /dev/console)
launchctl bootout "system/$DAEMON_LABEL" 2>/dev/null || true
[ "$CONSOLE_UID" -ne 0 ] && { launchctl bootout "gui/$CONSOLE_UID/$AGENT_LABEL" 2>/dev/null || true; }
pkill -x CLT-Desktop 2>/dev/null || true
pkill -x AMS-Remote-Support 2>/dev/null || true
for home in /Users/*; do
    rm -f "$home/Library/LaunchAgents/$AGENT_LABEL.plist" 2>/dev/null || true
done

# ── 4. Files ──
echo "Installing..."
mkdir -p "$INSTALL_DIR"
install -m 755 "$AGENT_BIN" "$INSTALL_DIR/CLT-Agent"
install -m 755 "$UNINSTALL_SRC" "$INSTALL_DIR/uninstall.sh"
# IT Remote Control: CLT-Desktop as an app, so Screen Recording and Accessibility are granted to it by name.
HELPER="$INSTALL_DIR/AMS Remote Control.app"
rm -rf "$HELPER" "$INSTALL_DIR/CLT-Desktop"
ditto "$HELPER_SRC" "$HELPER"
chmod 755 "$HELPER/Contents/MacOS/"*
rm -rf "$APP"
ditto "$APP_SRC" "$APP"
chmod 755 "$APP/Contents/MacOS/"*

# Built on Linux, the .NET binaries carry no signature, and Apple silicon refuses to run code with none: sign them here
# (ad hoc, which is all macOS needs for code that was not downloaded by a browser).
codesign --force --sign - "$INSTALL_DIR/CLT-Agent"
codesign --force --sign - "$HELPER"
codesign --force --deep --sign - "$APP"
xattr -dr com.apple.quarantine "$INSTALL_DIR" "$APP" 2>/dev/null || true

cat > "$INSTALL_DIR/ConnectionInfo.json" << EOF
{
  "DeviceID": $(json "$DEVICE_ID"),
  "Host": $(json "$AMS_URL/remote"),
  "OrganizationID": $(json "$AGENT_KEY"),
  "ServerVerificationToken": ""
}
EOF
# IT Remote Support runs as the user and reads the address and key from here, as on Windows.
chmod 644 "$INSTALL_DIR/ConnectionInfo.json"

install -m 644 -o root -g wheel "$DAEMON_SRC" "$DAEMON_PLIST"
install -m 644 -o root -g wheel "$AGENT_SRC" "$AGENT_PLIST"

# ── 5. Start ──
bootstrap system "$DAEMON_PLIST" || fail "macOS would not start the agent (launchctl bootstrap system $DAEMON_PLIST)."
if [ "$CONSOLE_UID" -ne 0 ]; then
    bootstrap "gui/$CONSOLE_UID" "$AGENT_PLIST" || echo "  Warning: IT Remote Support starts at the next login."
fi

echo ""
echo "Done. AMS Remote Support is installed."
echo "  Agent:             running (log: /var/log/clt-agent.log)"
echo "  IT Remote Support: Applications, and the menu bar at every login"
echo ""
echo "Once per Mac, the user turns on Screen Recording and Accessibility for IT Remote Control: the two buttons in the"
echo "IT Remote Support window open the right page of System Settings."
