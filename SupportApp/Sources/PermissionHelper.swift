// Screen Recording and Accessibility belong to "IT Remote Control" (/usr/local/lib/CLT/AMS Remote Control.app), the app that
// shows the screen to IT and moves the mouse — not to this window. macOS lists an app on those pages of System Settings only
// after the app itself has asked, so a button first makes IT Remote Control ask, then opens the right page, where the user
// only has to turn the switch on. IT Remote Control writes what is allowed to statusURL.
import AppKit

struct PermissionState {
    let screenRecording: Bool
    let accessibility: Bool
    var allGranted: Bool { screenRecording && accessibility }
}

enum Permission {
    case screenRecording
    case accessibility

    var requestArgument: String { self == .screenRecording ? "screen" : "accessibility" }
    var settingsPane: String { self == .screenRecording ? "Privacy_ScreenCapture" : "Privacy_Accessibility" }
}

enum PermissionHelper {
    static let appURL = URL(fileURLWithPath: "/usr/local/lib/CLT/AMS Remote Control.app")

    static var statusURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/AMS Remote Support/permissions.json")
    }

    static var installed: Bool { FileManager.default.fileExists(atPath: appURL.path) }

    /// Starts IT Remote Control in the background to look at its permissions; it rewrites statusURL and quits.
    static func refresh() {
        run(["--check-permissions"])
    }

    /// Makes IT Remote Control ask macOS (which lists it in System Settings), then opens that page.
    static func request(_ permission: Permission) {
        run(["--request-permissions", permission.requestArgument])
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(permission.settingsPane)") {
                NSWorkspace.shared.open(url)
            }
        }
    }

    static func read() -> PermissionState? {
        guard let data = try? Data(contentsOf: statusURL),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }
        return PermissionState(screenRecording: json["screenRecording"] as? Bool ?? false,
                               accessibility: json["accessibility"] as? Bool ?? false)
    }

    private static func run(_ arguments: [String]) {
        guard installed else { return }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.arguments = arguments
        configuration.activates = false
        configuration.createsNewApplicationInstance = true
        configuration.addsToRecentItems = false
        NSWorkspace.shared.openApplication(at: appURL, configuration: configuration, completionHandler: nil)
    }
}
