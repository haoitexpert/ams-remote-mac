# AMS Remote Support for macOS

macOS agent for [AMS](https://github.com/haoitexpert) IT remote control. Connects a MacBook to AMS so IT can view the screen and control keyboard/mouse — same as the Windows CLT client.

## Components

| Component | Language | What it does |
|-----------|----------|-------------|
| **CLT-Agent** | .NET 10 | Background daemon — connects to AMS via SignalR, reports the Mac, launches Desktop when IT connects |
| **CLT-Desktop** | .NET 10 | Screen capture (CoreGraphics) + input relay (CGEvent) — runs per remote session |
| **IT Remote Support** | Swift / AppKit | Window showing the computer ID + 9-char password IT reads to start a session |

## Quick start

See [BUILD.md](BUILD.md) for the full build and install guide.

```bash
# Build (on a Mac with .NET 10 + Xcode CLT)
./build.sh osx-arm64

# Install on a target Mac
sudo ./Installer/install.sh https://ams.example.com <agent-key>
```

Or let GitHub Actions build it: **Actions → Build macOS Remote Support → Run workflow**.

## License

Internal use only.
