# AMS Remote Support for macOS — Build & Install Guide

> Build on a Mac, install on any Mac. AMS server needs no changes — the macOS
> endpoints (`/remote/Content/CLT-Mac-*.zip`, `/remote/api/AgentUpdate/DownloadPackage/osx-*`)
> are already in place.

---

## 1. Prerequisites (build machine)

| What | Version | Check |
|------|---------|-------|
| macOS | 12 Monterey or later | `sw_vers` |
| Xcode Command Line Tools | any recent | `xcode-select -p` (install: `xcode-select --install`) |
| .NET SDK | **10.0** | `dotnet --version` |
| Swift | 5.9+ (ships with Xcode CLT) | `swift --version` |

Install .NET 10 SDK for macOS:

```bash
# ARM (Apple Silicon)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0

# Intel
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --arch x64
```

Add to PATH if the installer didn't:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

---

## 2. Source layout

```
scripts/RemoteMac/
├── RemoteMac.sln              ← .NET solution (Agent + Desktop + Shared)
├── Shared/                    ← Protocol DTOs, ConnectionInfo — shared by Agent & Desktop
│   ├── Shared.csproj
│   ├── Protocol.cs
│   └── ConnectionInfo.cs
├── Agent/                     ← CLT-Agent: background daemon, connects to AMS via SignalR
│   ├── Agent.csproj
│   ├── Program.cs
│   ├── AgentConnection.cs
│   ├── MacDeviceInfo.cs
│   └── DesktopLauncher.cs
├── Desktop/                   ← CLT-Desktop: screen capture + input relay
│   ├── Desktop.csproj
│   ├── Program.cs
│   ├── CoreGraphics.cs        ← P/Invoke bindings for CoreGraphics
│   ├── ScreenCapture.cs
│   ├── InputRelay.cs
│   └── DesktopConnection.cs
├── SupportApp/                ← IT Remote Support window (Swift / AppKit)
│   ├── Package.swift
│   └── Sources/
│       ├── main.swift
│       ├── AppDelegate.swift
│       ├── SupportWindow.swift
│       ├── AmsClient.swift
│       └── PasswordGenerator.swift
├── LaunchDaemons/             ← launchd plist files
│   ├── com.ams.clt-agent.plist
│   └── com.ams.remote-support.plist
├── Installer/
│   ├── install.sh
│   └── uninstall.sh
└── build.sh                   ← One-command build
```

---

## 3. Build

### 3a. One-command build (recommended)

```bash
cd scripts/RemoteMac
chmod +x build.sh
./build.sh osx-arm64       # Apple Silicon (M1/M2/M3/M4)
# or
./build.sh osx-x64         # Intel Mac
```

Output: `scripts/RemoteMac/build/dist/CLT-Mac-arm64.zip` (or `-x64.zip`).

### 3b. Build each component separately

**Agent (.NET)**

```bash
cd scripts/RemoteMac
dotnet publish Agent/Agent.csproj -c Release -r osx-arm64 --self-contained -o build/Agent
```

Produces `build/Agent/CLT-Agent` (~60 MB self-contained binary).

**Desktop (.NET)**

```bash
dotnet publish Desktop/Desktop.csproj -c Release -r osx-arm64 --self-contained -o build/Desktop
```

Produces `build/Desktop/CLT-Desktop` (~60 MB self-contained binary).

**IT Remote Support (Swift)**

```bash
cd scripts/RemoteMac/SupportApp
swift build -c release
```

Produces `.build/release/AMS-Remote-Support`.

Then create the .app bundle:

```bash
APP="scripts/RemoteMac/build/AMS Remote Support.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/AMS-Remote-Support "$APP/Contents/MacOS/"
```

And write `"$APP/Contents/Info.plist"` — `build.sh` does this automatically.

### 3c. Universal binary (both architectures)

Build twice and use `lipo`:

```bash
./build.sh osx-arm64
mv build/dist/CLT-Mac-arm64.zip build/dist/CLT-Mac-arm64.zip.bak

./build.sh osx-x64
# Now you have both zips.
```

Most Macs in use are Apple Silicon; build `osx-arm64` first, add `osx-x64` if
you have Intel Macs in the fleet.

---

## 4. Deploy the package to AMS

Copy the zip to the AMS server's `RemoteClient/` directory:

```bash
# From the Mac
scp build/dist/CLT-Mac-arm64.zip user@ams-server:/path/to/ams/RemoteClient/
```

Or upload via the admin UI: **IT Assets → Settings → Remote Access → Upload
macOS package**.

AMS serves it at:
- `/remote/Content/CLT-Mac-arm64.zip` (download)
- `/remote/api/AgentUpdate/DownloadPackage/osx-arm64` (agent self-update)

---

## 5. Install on a target MacBook

### 5a. Automated install (install.sh)

Copy and extract the zip on the target Mac:

```bash
# Get the package from AMS
curl -o CLT-Mac-arm64.zip https://ams.example.com/remote/Content/CLT-Mac-arm64.zip
unzip CLT-Mac-arm64.zip
cd CLT-Mac-arm64

# Install (needs sudo)
sudo ./install.sh https://ams.example.com your-agent-key
```

The agent key is on the AMS web UI: **IT Assets → Settings → Remote Access →
Agent Key**.

What `install.sh` does:

1. **Registers** the Mac with AMS (`POST /api/v1/agent/register`) — hostname,
   serial number, model, architecture
2. **Writes** `/usr/local/lib/CLT/ConnectionInfo.json` with the device ID and
   server address
3. **Copies** `CLT-Agent` and `CLT-Desktop` to `/usr/local/lib/CLT/`
4. **Copies** `AMS Remote Support.app` to `/Applications/`
5. **Installs** the LaunchDaemon (`com.ams.clt-agent.plist`) — agent starts at
   boot, runs as root
6. **Installs** the LaunchAgent (`com.ams.remote-support.plist`) — IT Remote
   Support window starts at login for the current user

### 5b. Manual install

If you prefer to install step by step:

```bash
# 1. Create the install directory
sudo mkdir -p /usr/local/lib/CLT

# 2. Copy binaries
sudo cp CLT-Agent CLT-Desktop /usr/local/lib/CLT/
sudo chmod +x /usr/local/lib/CLT/CLT-Agent /usr/local/lib/CLT/CLT-Desktop

# 3. Write ConnectionInfo.json (replace the values)
sudo tee /usr/local/lib/CLT/ConnectionInfo.json > /dev/null << 'EOF'
{
  "DeviceID": "<device-id-from-ams>",
  "Host": "https://ams.example.com/remote",
  "OrganizationID": "<agent-key>",
  "ServerVerificationToken": ""
}
EOF

# 4. Install the app
cp -R "AMS Remote Support.app" /Applications/

# 5. Install the agent daemon
sudo cp com.ams.clt-agent.plist /Library/LaunchDaemons/
sudo chmod 644 /Library/LaunchDaemons/com.ams.clt-agent.plist
sudo launchctl load /Library/LaunchDaemons/com.ams.clt-agent.plist

# 6. Install the user LaunchAgent
cp com.ams.remote-support.plist ~/Library/LaunchAgents/
launchctl load ~/Library/LaunchAgents/com.ams.remote-support.plist
```

---

## 6. macOS permissions

The Mac **will prompt** the user to grant permissions the first time. These
are required for remote control to work:

| Permission | What needs it | Where to grant |
|------------|---------------|----------------|
| **Screen Recording** | CLT-Desktop (screen capture) | System Settings → Privacy & Security → Screen Recording → CLT-Desktop ✓ |
| **Accessibility** | CLT-Desktop (keyboard/mouse input) | System Settings → Privacy & Security → Accessibility → CLT-Desktop ✓ |

To pre-grant via MDM (Jamf, Mosyle, Kandji…), deploy a TCC profile:

```xml
<!-- PPPC profile for MDM -->
<key>Services</key>
<dict>
    <key>ScreenCapture</key>
    <array>
        <dict>
            <key>Identifier</key>
            <string>/usr/local/lib/CLT/CLT-Desktop</string>
            <key>IdentifierType</key>
            <string>path</string>
            <key>Allowed</key>
            <true/>
        </dict>
    </array>
    <key>Accessibility</key>
    <array>
        <dict>
            <key>Identifier</key>
            <string>/usr/local/lib/CLT/CLT-Desktop</string>
            <key>IdentifierType</key>
            <string>path</string>
            <key>Allowed</key>
            <true/>
        </dict>
    </array>
</dict>
```

---

## 7. Verify the installation

### Agent is running?

```bash
# Check the daemon
sudo launchctl list | grep com.ams.clt-agent

# Check the process
ps aux | grep CLT-Agent

# Check the log
tail -f /var/log/clt-agent.log
```

The agent logs `Connected to AMS` on success. It sends a heartbeat every 5
minutes.

### IT Remote Support window?

Open **AMS Remote Support** from Launchpad or `/Applications/`. It shows:
- **Computer ID** — the device ID registered in AMS
- **Password** — a 9-character code (e.g., `K7M 2QX 9PA`) that IT reads to
  start a session

### AMS sees the Mac?

In the AMS web UI: **IT Assets → Computers** — the Mac appears with its
hostname, macOS version, and "Online" status.

### Remote control works?

1. Open the **IT Remote Support** window on the Mac
2. In AMS, go to the computer → **Remote** → enter the password
3. The Mac's screen appears in AMS; keyboard and mouse work

---

## 8. How it works (architecture)

```
┌─────────────────────────────────────────────────────┐
│                    AMS Server                       │
│  ┌───────────┐  ┌──────────────┐  ┌─────────────┐  │
│  │ AgentHub  │  │  DesktopHub  │  │  ViewerHub  │  │
│  │ /hubs/    │  │  /hubs/      │  │  /hubs/     │  │
│  │ service   │  │  desktop     │  │  viewer     │  │
│  └─────▲─────┘  └──────▲───────┘  └──────▲──────┘  │
│        │               │                 │          │
└────────┼───────────────┼─────────────────┼──────────┘
         │               │                 │
    SignalR          SignalR           WebSocket
   MessagePack      MessagePack
         │               │                 │
┌────────┴────────┐ ┌────┴──────────┐  ┌───┴──────────┐
│   CLT-Agent     │ │ CLT-Desktop   │  │  Browser     │
│   (launchd      │ │ (launched per │  │  (IT user    │
│    daemon)      │ │  session)     │  │   in AMS)    │
│                 │ │               │  │              │
│ • DeviceCame-   │ │ • Screen      │  │ • Sees the   │
│   Online        │ │   capture     │  │   Mac screen │
│ • Heartbeat     │ │   (CG API)   │  │ • Sends      │
│   every 5 min   │ │ • Input       │  │   keyboard   │
│ • Launches      │ │   relay       │  │   + mouse    │
│   Desktop when  │ │   (CGEvent)  │  │   input      │
│   IT connects   │ │ • JPEG stream │  │              │
└─────────────────┘ └───────────────┘  └──────────────┘

┌─────────────────────────┐
│  IT Remote Support      │
│  (Swift / AppKit)       │
│                         │
│  • Shows password       │
│  • Polls AMS every 3s   │
│  • "REMOTE" bar when    │
│    IT is connected      │
│  • Menu bar icon        │
└─────────────────────────┘
```

- **CLT-Agent** runs as a system daemon (root). It connects to AMS via SignalR
  (`/remote/hubs/service`), reports the Mac (OS, CPU, RAM, storage, MAC
  addresses), and handles `RemoteControl` callbacks by launching CLT-Desktop
  via `launchctl asuser` in the console user's GUI session.

- **CLT-Desktop** runs per session, in the user's GUI session (needed for
  Screen Recording). It connects to `/remote/hubs/desktop`, sends the screen
  as a JPEG stream (tile-based, ~30 FPS), and relays keyboard/mouse input
  from the viewer via CoreGraphics CGEvent API.

- **IT Remote Support** (Swift app) shows the computer ID and a 9-character
  password. IT reads the password, enters it in AMS, and the session starts.
  The password is only valid while the window is open. AMS stores only its
  hash.

---

## 9. Uninstall

```bash
curl -o uninstall.sh https://ams.example.com/path/to/uninstall.sh
sudo ./uninstall.sh
```

Or if you have the zip extracted:

```bash
sudo ./uninstall.sh
```

This stops all services, removes `/usr/local/lib/CLT`,
`/Applications/AMS Remote Support.app`, the LaunchDaemon, the LaunchAgent,
and the log file.

---

## 10. Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Agent not connecting | Wrong AMS URL or agent key | Check `/usr/local/lib/CLT/ConnectionInfo.json` |
| Agent connects but Desktop doesn't launch | `launchctl asuser` fails — no console user | Someone must be logged in at the Mac's GUI |
| Screen is black in AMS | Screen Recording not granted | System Settings → Privacy & Security → Screen Recording → add CLT-Desktop |
| Mouse/keyboard doesn't work | Accessibility not granted | System Settings → Privacy & Security → Accessibility → add CLT-Desktop |
| IT Remote Support doesn't open | LaunchAgent not loaded | `launchctl load ~/Library/LaunchAgents/com.ams.remote-support.plist` |
| "Registration failed" on install | AMS unreachable or key wrong | Verify `curl https://ams.example.com/remote/api/branding/test` returns JSON |
| Agent keeps restarting | Crash loop | Check `/var/log/clt-agent.log` for errors |
| Permission prompt doesn't appear | macOS cached a previous denial | Remove CLT-Desktop from the relevant Privacy list, then re-add it |
| `dotnet publish` fails on build | .NET 10 SDK not installed | `dotnet --list-sdks` — need 10.0.x |
| `swift build` fails | Xcode CLT too old | `xcode-select --install` or update Xcode |

### Resetting permissions (if macOS cached a denial)

```bash
# Reset Screen Recording permission for CLT-Desktop
tccutil reset ScreenCapture /usr/local/lib/CLT/CLT-Desktop

# Reset Accessibility permission
tccutil reset Accessibility /usr/local/lib/CLT/CLT-Desktop
```

Then launch CLT-Desktop again — macOS will prompt fresh.

### Checking the agent log

```bash
tail -100 /var/log/clt-agent.log
```

The agent logs: connection status, heartbeat sends, and desktop launch
requests.

---

## 11. CI build (GitHub Actions)

A workflow at `.github/workflows/build-remote-mac.yml` builds both architectures
on GitHub's macOS runners — no local Mac needed.

**Trigger:**
- **Automatic** — push to `main` that touches `scripts/RemoteMac/**`
- **Manual** — Actions tab → "Build macOS Remote Support" → Run workflow →
  pick `both` / `arm64` / `x64`

**Runners:**
| Architecture | Runner | Hardware |
|-------------|--------|----------|
| arm64 | `macos-14` | Apple Silicon (M1) |
| x64 | `macos-13` | Intel |

**Output:** download the zip from the workflow run's **Artifacts** section
(kept 30 days).

**Usage:** GitHub gives 3,000 minutes/month for private repos (macOS
runners use a 10× multiplier, so ~300 effective minutes). Each build takes
~5 minutes per architecture → ~10 minutes per run → ~100 minutes billed →
can run ~30 times/month.

To deploy the artifact to AMS after build:

```bash
# Download from GitHub Actions (needs gh CLI)
gh run download <run-id> -n CLT-Mac-arm64

# Copy to AMS server
scp CLT-Mac-arm64.zip user@ams-server:/path/to/ams/RemoteClient/
```

---

## 12. Supported macOS versions

| macOS version | Architecture | Status |
|---------------|-------------|--------|
| 15 Sequoia | ARM (M1+) | Supported |
| 14 Sonoma | ARM (M1+) | Supported |
| 13 Ventura | ARM / Intel | Supported |
| 12 Monterey | ARM / Intel | Supported (minimum) |
| 11 Big Sur | Intel | Not tested |
| 10.15 and older | Intel | Not supported |

The Swift app targets macOS 12+. The .NET 10 runtime supports macOS 12+.
CoreGraphics screen capture APIs are available since macOS 10.8 but the
permission model (TCC) changed significantly in macOS 10.15+.
