#!/bin/bash
# AMS Remote Support installer for macOS.
# Usage: sudo ./install.sh <ams-url> <agent-key>
# Example: sudo ./install.sh https://ams.example.com abc123def456
#
# Installs:
#   /usr/local/lib/CLT/              — agent + desktop binaries + ConnectionInfo.json
#   /Library/LaunchDaemons/com.ams.clt-agent.plist   — agent daemon (boot)
#   /Applications/AMS Remote Support.app             — IT Remote Support window
#   ~/Library/LaunchAgents/com.ams.remote-support.plist — auto-start at login
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "This installer needs root. Run: sudo $0 $*"
    exit 1
fi

AMS_URL="${1:-}"
AGENT_KEY="${2:-}"
INSTALL_DIR="/usr/local/lib/CLT"

if [ -z "$AMS_URL" ] || [ -z "$AGENT_KEY" ]; then
    echo "Usage: sudo $0 <ams-url> <agent-key>"
    echo "Example: sudo $0 https://ams.example.com abc123def456"
    exit 1
fi

# Strip trailing slash.
AMS_URL="${AMS_URL%/}"

echo "Installing AMS Remote Support..."
echo "  Server: $AMS_URL"
echo "  Install dir: $INSTALL_DIR"

# ── 1. Register this computer with AMS ──

HOSTNAME=$(scutil --get ComputerName 2>/dev/null || hostname)
SERIAL=$(ioreg -l | grep IOPlatformSerialNumber | awk '{print $NF}' | tr -d '"' 2>/dev/null || echo "")
MODEL=$(sysctl -n hw.model 2>/dev/null || echo "Mac")
ARCH=$(uname -m)

echo "  Registering: $HOSTNAME ($MODEL, $SERIAL, $ARCH)"

REGISTER_BODY="{\"hostname\":\"$HOSTNAME\",\"serial\":\"$SERIAL\",\"model\":\"$MODEL\",\"platform\":\"macOS\",\"architecture\":\"$ARCH\"}"

REGISTER_RESPONSE=$(curl -s -X POST "$AMS_URL/api/v1/agent/register" \
    -H "Content-Type: application/json" \
    -H "X-Agent-Key: $AGENT_KEY" \
    -d "$REGISTER_BODY" \
    --max-time 15 || echo "{}")

DEVICE_ID=$(echo "$REGISTER_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('deviceId',''))" 2>/dev/null || echo "")

if [ -z "$DEVICE_ID" ]; then
    echo "  Registration failed — check the AMS URL and agent key."
    echo "  Response: $REGISTER_RESPONSE"
    exit 1
fi

echo "  Device ID: $DEVICE_ID"

# ── 2. Write ConnectionInfo.json ──

mkdir -p "$INSTALL_DIR"
cat > "$INSTALL_DIR/ConnectionInfo.json" << CEOF
{
  "DeviceID": "$DEVICE_ID",
  "Host": "$AMS_URL/remote",
  "OrganizationID": "$AGENT_KEY",
  "ServerVerificationToken": ""
}
CEOF

chmod 644 "$INSTALL_DIR/ConnectionInfo.json"

# ── 3. Copy binaries ──

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
AGENT_BIN="$SCRIPT_DIR/../build/Agent"
DESKTOP_BIN="$SCRIPT_DIR/../build/Desktop"
SUPPORT_APP="$SCRIPT_DIR/../build/AMS Remote Support.app"

if [ -d "$AGENT_BIN" ]; then
    echo "  Installing agent..."
    cp -R "$AGENT_BIN/"* "$INSTALL_DIR/"
    chmod +x "$INSTALL_DIR/CLT-Agent"
fi

if [ -d "$DESKTOP_BIN" ]; then
    echo "  Installing desktop..."
    cp -R "$DESKTOP_BIN/"* "$INSTALL_DIR/"
    chmod +x "$INSTALL_DIR/CLT-Desktop"
fi

if [ -d "$SUPPORT_APP" ]; then
    echo "  Installing IT Remote Support app..."
    rm -rf "/Applications/AMS Remote Support.app"
    cp -R "$SUPPORT_APP" "/Applications/"
fi

# ── 4. Install launch daemons ──

PLIST_DIR="$SCRIPT_DIR/../LaunchDaemons"

echo "  Installing LaunchDaemon..."
cp "$PLIST_DIR/com.ams.clt-agent.plist" /Library/LaunchDaemons/
chmod 644 /Library/LaunchDaemons/com.ams.clt-agent.plist
launchctl load /Library/LaunchDaemons/com.ams.clt-agent.plist 2>/dev/null || true

# Install the LaunchAgent for the console user (not root).
CONSOLE_USER=$(scutil <<< "show State:/Users/ConsoleUser" | awk '/Name :/ { print $3 }')
if [ -n "$CONSOLE_USER" ] && [ "$CONSOLE_USER" != "loginwindow" ]; then
    CONSOLE_HOME=$(dscl . -read "/Users/$CONSOLE_USER" NFSHomeDirectory | awk '{print $2}')
    mkdir -p "$CONSOLE_HOME/Library/LaunchAgents"
    cp "$PLIST_DIR/com.ams.remote-support.plist" "$CONSOLE_HOME/Library/LaunchAgents/"
    chown "$CONSOLE_USER" "$CONSOLE_HOME/Library/LaunchAgents/com.ams.remote-support.plist"

    CONSOLE_UID=$(id -u "$CONSOLE_USER")
    launchctl asuser "$CONSOLE_UID" launchctl load "$CONSOLE_HOME/Library/LaunchAgents/com.ams.remote-support.plist" 2>/dev/null || true
fi

echo ""
echo "Done! AMS Remote Support is installed."
echo "  Agent: running as a system daemon"
echo "  IT Remote Support: in Applications (will start at next login)"
echo ""
echo "The computer needs Screen Recording and Accessibility permissions for remote control."
echo "Open System Settings → Privacy & Security to grant them when prompted."
