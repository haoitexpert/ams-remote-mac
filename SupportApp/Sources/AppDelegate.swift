// IT Remote Support for macOS — application delegate.
import AppKit

class AppDelegate: NSObject, NSApplicationDelegate {
    var window: SupportWindow?
    var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Listen for "reveal" from a second-launch attempt.
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(reveal), name: .init("com.ams.remote-support.reveal"), object: nil)

        window = SupportWindow()
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        // Menu-bar icon for easy access.
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem?.button {
            button.image = NSImage(systemSymbolName: "desktopcomputer", accessibilityDescription: "IT Remote Support")
            button.action = #selector(reveal)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return false  // Keep running in menu bar.
    }

    @objc func reveal() {
        window?.reopen()
    }

    func applicationWillTerminate(_ notification: Notification) {
        window?.closeSession()
    }
}
