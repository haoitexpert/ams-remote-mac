#!/bin/bash
# Removes AMS Remote Support from this Mac.
#   sudo /usr/local/lib/CLT/uninstall.sh
# AMS runs it too (Remove on the computer in AMS → the agent's UninstallAgent). The agent daemon is unloaded LAST: that ends
# the agent and everything it started, this script included, so every other step must come before it.
set -uo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "This needs administrator rights. Run: sudo $0"
    exit 1
fi

DAEMON_LABEL=com.ams.clt-agent
AGENT_LABEL=com.ams.remote-support

echo "Uninstalling AMS Remote Support..."

# IT Remote Support, for whoever is signed in, then for every user at their next login.
CONSOLE_UID=$(stat -f%u /dev/console)
if [ "$CONSOLE_UID" -ne 0 ]; then
    launchctl bootout "gui/$CONSOLE_UID/$AGENT_LABEL" 2>/dev/null || true
fi
pkill -x AMS-Remote-Support 2>/dev/null || true
pkill -x CLT-Desktop 2>/dev/null || true
rm -f "/Library/LaunchAgents/$AGENT_LABEL.plist"
# Installs before 2026-11 put the LaunchAgent in the user's own Library. Each user also has IT Remote Control's log and
# the permission status IT Remote Support reads.
for home in /Users/*; do
    rm -f "$home/Library/LaunchAgents/$AGENT_LABEL.plist" "$home/Library/Logs/IT Remote Control.log" 2>/dev/null || true
    rm -rf "$home/Library/Application Support/AMS Remote Support" 2>/dev/null || true
done
# What the user allowed for IT Remote Control goes with it.
tccutil reset ScreenCapture com.ams.remote-control >/dev/null 2>&1 || true
tccutil reset Accessibility com.ams.remote-control >/dev/null 2>&1 || true

rm -rf "/Applications/AMS Remote Support.app"
rm -rf /usr/local/lib/CLT
rm -f /var/log/clt-agent.log
rm -f "/Library/LaunchDaemons/$DAEMON_LABEL.plist"

echo "Done. AMS Remote Support has been removed."
launchctl bootout "system/$DAEMON_LABEL" 2>/dev/null || true
