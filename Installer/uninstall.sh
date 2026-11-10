#!/bin/bash
# Uninstalls AMS Remote Support from macOS.
# Usage: sudo ./uninstall.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "This needs root. Run: sudo $0"
    exit 1
fi

echo "Uninstalling AMS Remote Support..."

# Stop and unload the agent daemon.
launchctl unload /Library/LaunchDaemons/com.ams.clt-agent.plist 2>/dev/null || true
rm -f /Library/LaunchDaemons/com.ams.clt-agent.plist

# Unload the LaunchAgent for the console user.
CONSOLE_USER=$(scutil <<< "show State:/Users/ConsoleUser" | awk '/Name :/ { print $3 }')
if [ -n "$CONSOLE_USER" ] && [ "$CONSOLE_USER" != "loginwindow" ]; then
    CONSOLE_HOME=$(dscl . -read "/Users/$CONSOLE_USER" NFSHomeDirectory | awk '{print $2}')
    CONSOLE_UID=$(id -u "$CONSOLE_USER")
    launchctl asuser "$CONSOLE_UID" launchctl unload "$CONSOLE_HOME/Library/LaunchAgents/com.ams.remote-support.plist" 2>/dev/null || true
    rm -f "$CONSOLE_HOME/Library/LaunchAgents/com.ams.remote-support.plist"
fi

# Kill running processes.
pkill -f CLT-Agent 2>/dev/null || true
pkill -f CLT-Desktop 2>/dev/null || true
pkill -f "AMS-Remote-Support" 2>/dev/null || true

# Remove files.
rm -rf /usr/local/lib/CLT
rm -rf "/Applications/AMS Remote Support.app"
rm -f /var/log/clt-agent.log

echo "Done. AMS Remote Support has been removed."
