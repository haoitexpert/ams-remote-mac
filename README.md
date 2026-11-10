# AMS Remote Support for macOS

The macOS client of AMS remote control: IT sees and controls a Mac from the asset page, the same way as the Windows CLT client.

| Component | Language | What it does |
|-----------|----------|-------------|
| **CLT-Agent** | .NET 10 | Root daemon: stays connected to AMS (`/remote/hubs/service`), starts IT Remote Control when IT opens a session |
| **IT Remote Control** (CLT-Desktop) | .NET 10 | `AMS Remote Control.app`, run as the signed-in user: streams the screen (CoreGraphics) and applies mouse/keyboard (CGEvent); the app Screen Recording and Accessibility are granted to |
| **IT Remote Support** | Swift / AppKit | The window with the computer's ID and password the user reads to IT, and the two buttons that open those permissions; nobody connects while it is closed |
| **AMS-Remote-Setup.pkg** | pkgbuild | The installer AMS exports with its address and agent key appended, like AMS-Remote-Setup.exe |

## Build

**Actions → Build macOS Remote Support → Run workflow** (or push to `main`). The run's artifacts:

- `CLT-Mac-arm64.zip`, `CLT-Mac-x64.zip` — upload each on AMS: IT Assets → Settings → Remote Access → Client package.
- `AMS-Remote-Setup-pkg.zip` — unzip; put `AMS-Remote-Setup.pkg` in the AMS repo at `Services/RemoteControl/Assets/` and deploy AMS.

On a Mac with .NET 10 and the Xcode command-line tools, `./build.sh` makes the same files in `build/dist`.

## Install on a Mac

AMS → IT Assets → Settings → Remote Access → **Export macOS installer**, send the .pkg to the user, they open it. See [BUILD.md](BUILD.md).

Manual (testing): `sudo Installer/install.sh https://ams.example.com <agent-key>`. Remove: `sudo /usr/local/lib/CLT/uninstall.sh`.

## License

Internal use only.
