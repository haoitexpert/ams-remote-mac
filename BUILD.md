# AMS Remote Support for macOS — build and install

The same flow as Windows: IT exports an installer from AMS, the user opens it, the Mac shows up in AMS. Nothing on the Mac
needs a command line.

---

## 1. Build (GitHub Actions)

**Actions → Build macOS Remote Support → Run workflow** (it also runs on every push to `main`).

| Job | Runner | Makes |
|-----|--------|-------|
| `dotnet` | `ubuntu-latest` | CLT-Agent, CLT-Desktop for `osx-arm64` and `osx-x64` (cross-compiled, single file, self-contained) |
| `mac` | `macos-15-intel` | IT Remote Support.app (universal: arm64 + x86_64) and `AMS-Remote-Setup.pkg` |
| `package` | `ubuntu-latest` | `CLT-Mac-arm64` and `CLT-Mac-x64` artifacts: everything a Mac installs |

Only the Swift app and the .pkg need a Mac. The .NET binaries are built on Linux, so they carry no signature;
`install.sh` signs them ad hoc on the Mac (`codesign --sign -`), which Apple silicon requires before it runs any code.

One arm64 package covers every Apple silicon Mac (M1 to M5); the x64 package is for Intel Macs.

On a Mac with .NET 10 and the Xcode command-line tools, `./build.sh` makes the same files in `build/dist`.

## 2. Put it on AMS (once per build)

1. Download the run's artifacts.
2. **Client packages** — on AMS, IT Assets → Settings → Remote Access → Client package: upload `CLT-Mac-arm64.zip` on the
   *macOS Apple silicon* row and `CLT-Mac-x64.zip` on the *macOS Intel* row, as downloaded (no need to unzip).
3. **Installer** — unzip `AMS-Remote-Setup-pkg.zip`, put `AMS-Remote-Setup.pkg` in the AMS repository at
   `Services/RemoteControl/Assets/AMS-Remote-Setup.pkg`, commit and deploy AMS. It changes only when `pkg/` or
   `Installer/` change; a new client build needs only step 2.

## 3. Install on a Mac

1. AMS → IT Assets → Settings → Remote Access → **Export macOS installer**. The file carries this server's address and the
   agent key (appended to the .pkg exactly like `AMS-Remote-Setup.exe`); a new key needs a new export.
2. Send `AMS-Remote-Setup.pkg` to the user (or put it on a share). They double-click it and click through Installer,
   which asks for an administrator password.
3. The package is not signed with an Apple Developer ID, so macOS stops it the first time:
   - macOS 15 and later: System Settings → Privacy & Security → *"AMS-Remote-Setup.pkg" was blocked* → **Open Anyway**.
   - macOS 14 and earlier: right-click the .pkg → **Open** → Open.

What the installer does (`pkg/postinstall` → `Installer/install.sh`):

1. Reads the AMS address and agent key from the end of the .pkg.
2. Downloads `CLT-Mac-arm64.zip` or `CLT-Mac-x64.zip` from AMS (`/remote/Content/…`) for this Mac's processor.
3. Registers the Mac (`POST /api/v1/agent/register`): its own device ID (kept across reinstalls), computer name, serial
   number and model — AMS links it to the asset by serial, else computer name = asset tag.
4. Stops whatever an earlier install left running, then installs:
   - `/usr/local/lib/CLT/` — CLT-Agent, `AMS Remote Control.app` (CLT-Desktop, shown as "IT Remote Control"),
     ConnectionInfo.json, uninstall.sh
   - `/Applications/AMS Remote Support.app`
   - `/Library/LaunchDaemons/com.ams.clt-agent.plist` — the agent, as root, from boot
   - `/Library/LaunchAgents/com.ams.remote-support.plist` — IT Remote Support at every user's login (menu bar)
5. Starts both. Its output is in `/var/log/install.log`.

Running it again (a new key, a new server, a new client) replaces everything and keeps the device ID.

Testing without the .pkg: `sudo Installer/install.sh https://ams.example.com <agent-key> [unzipped-package-dir]`.

## 4. macOS permissions

Once per Mac, the user turns on two switches for **IT Remote Control** (CLT-Desktop, wrapped as
`AMS Remote Control.app` so macOS shows it by name):

| Permission | Without it |
|------------|-----------|
| Screen Recording | IT sees only the desktop background (the viewer says so) |
| Accessibility | IT sees the screen but mouse and keyboard do nothing (the viewer says so) |

The IT Remote Support window has a button for each, with a check mark once it is on. macOS lists an app on those pages
only after the app itself asked, so a button starts IT Remote Control with `--request-permissions` (macOS's own prompt
appears and the app is added to the list, switched off), then opens System Settings at that page; the user turns the
switch on. The window re-checks by starting it with `--check-permissions`, which writes
`~/Library/Application Support/AMS Remote Support/permissions.json`.

The agent starts IT Remote Control through LaunchServices (`open`) as the signed-in user, never as its own child: macOS
grants these permissions to the process responsible for the request, and only an app LaunchServices started is
responsible for itself.

The app is signed ad hoc, so macOS ties the permission to that exact build: after a new client package the user may have to
turn the switches on again. A Developer ID signature keeps them across updates and lets an MDM (PPPC profile) pre-grant
Accessibility; Screen Recording always needs the user's click.

## 5. Check an installed Mac

```bash
sudo launchctl print system/com.ams.clt-agent | head -20   # the agent is loaded and running
tail -50 /var/log/clt-agent.log                             # "Connected to AMS", session launches
tail -50 ~/Library/Logs/IT\ Remote\ Control.log             # the session process, as the signed-in user
cat /usr/local/lib/CLT/ConnectionInfo.json                  # device ID, server, key
```

In AMS: IT Assets → Settings → Remote Access → Computers lists the Mac within a minute, online.

## 6. Remove

- From AMS: the agent runs `/usr/local/lib/CLT/uninstall.sh` when AMS sends UninstallAgent.
- On the Mac: `sudo /usr/local/lib/CLT/uninstall.sh`.

It removes the app, `/usr/local/lib/CLT`, both launchd plists and the log, and unloads the agent last.

## 7. How a session works

```
IT Remote Support (user)          CLT-Agent (root daemon)              AMS
  shows ID + password ── POST /api/v1/agent/support every 3 s ──────▶  checks the password IT types
                                    ◀── RemoteControl(session, key) ──  /remote/hubs/service
                                    launchctl asuser <uid> sudo -u <user> open -n -g "AMS Remote Control.app" --args …
IT Remote Control (user's session) ── /remote/hubs/desktop ────────▶  ReceiveUnattendedSessionInfo
  ScreenData → viewer, then SendDesktopStream: frames of
  [int32 JPEG size][float32 x, y, w, h][uint64 time][JPEG], ≤ 60 KB a message
  ◀── SendDtoToClient: MessagePack DtoWrapper (MouseMove 8, MouseDown 9, MouseUp 10, MouseWheel 12, KeyDown 13, KeyUp 14,
      TextTransfer 19, KeyPress 20, SetKeyStatesUp 24) — the values in wwwroot/remote/viewer/src/Enums/DtoType.js
```

Each frame is the bounding box of the 64-pixel tiles that changed since the previous one, in Retina pixels; mouse
positions arrive as fractions of the screen and are posted in points.

## 8. Troubleshooting

| Symptom | Look at |
|---------|---------|
| Installer says "no AMS server in it" | The .pkg was not exported from AMS (or was re-saved by a tool that cut its end). Export again. |
| Installer fails downloading CLT-Mac-*.zip | The Mac cannot reach AMS at the exported address, or the macOS package is not uploaded. |
| Mac not in the Computers list | `/var/log/clt-agent.log`; the agent key in ConnectionInfo.json is the current one. |
| IT Remote Support says the remote service is not connected | `sudo launchctl kickstart -k system/com.ams.clt-agent` |
| Viewer waits forever | Nobody is signed in at the Mac (login window): the agent log says so. |
| Black / background-only screen | Screen Recording for IT Remote Control (button in the IT Remote Support window). |
| No mouse or keyboard | Accessibility for IT Remote Control (button in the IT Remote Support window). |
| A button opens System Settings but IT Remote Control is not in the list | `~/Library/Logs/IT Remote Control.log`; `/usr/local/lib/CLT/AMS Remote Control.app` exists. |

Reset a denied permission: `tccutil reset ScreenCapture com.ams.remote-control` (and `Accessibility`), then click the
button in IT Remote Support again.

## 9. Supported macOS

macOS 12 Monterey or later, Apple silicon and Intel (the Swift app and .NET 10 both need 12+).
