// IT Remote Support for macOS — entry point.
// Single-instance: a second launch reveals the existing window.
import AppKit

// Single-instance lock: if another copy is already running, tell it to show its window and exit.
let lockPath = NSTemporaryDirectory() + "AMS-Remote-Support.lock"
let lockFd = open(lockPath, O_WRONLY | O_CREAT, 0o644)
if lockFd < 0 || flock(lockFd, LOCK_EX | LOCK_NB) != 0 {
    // Another instance is running. Signal it via distributed notification.
    DistributedNotificationCenter.default().post(name: .init("com.ams.remote-support.reveal"), object: nil)
    exit(0)
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
