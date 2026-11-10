// The IT Remote Support window for macOS — equivalent of SupportForm.cs (WinForms).
// Shows: computer ID, password (refreshable), status, and a REMOTE banner when IT connects.
import AppKit

class SupportWindow: NSWindow, NSWindowDelegate {
    private let ams: AmsClient?
    private var secret: String = ""
    private var liveWith: String? = nil
    private var leaving = false
    private var busy = false
    private var timer: Timer?

    // UI elements
    private let idLabel = NSTextField(labelWithString: "")
    private let passwordLabel = NSTextField(labelWithString: "--- --- ---")
    private let statusDot = NSTextField(labelWithString: "●")
    private let statusLabel = NSTextField(wrappingLabelWithString: "")
    private let remoteBanner = NSView()

    // AMS colours matching the Windows form (theme.css / assetit.css).
    private static let primary  = NSColor(red: 6/255,   green: 118/255, blue: 237/255, alpha: 1)
    private static let ink      = NSColor(red: 23/255,  green: 43/255,  blue: 77/255,  alpha: 1)
    private static let muted    = NSColor(red: 98/255,  green: 111/255, blue: 134/255, alpha: 1)
    private static let rule     = NSColor(red: 223/255, green: 225/255, blue: 230/255, alpha: 1)
    private static let soft     = NSColor(red: 247/255, green: 248/255, blue: 249/255, alpha: 1)
    private static let good     = NSColor(red: 33/255,  green: 110/255, blue: 78/255,  alpha: 1)
    private static let warn     = NSColor(red: 151/255, green: 79/255,  blue: 12/255,  alpha: 1)
    private static let bad      = NSColor(red: 174/255, green: 46/255,  blue: 36/255,  alpha: 1)
    private static let live     = NSColor(red: 222/255, green: 53/255,  blue: 11/255,  alpha: 1)

    init() {
        ams = AmsClient.load()

        let rect = NSRect(x: 0, y: 0, width: 440, height: 340)
        super.init(contentRect: rect, styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)

        title = "IT Remote Support"
        isReleasedWhenClosed = false
        delegate = self
        center()

        setupUI()

        if let ams = ams {
            secret = PasswordGenerator.generate()
            passwordLabel.stringValue = PasswordGenerator.spaced(secret)
            idLabel.stringValue = Host.current().localizedName ?? ProcessInfo.processInfo.hostName
            setStatus(color: Self.muted, text: "Connecting to AMS...")
            startPolling()
        } else {
            idLabel.stringValue = ProcessInfo.processInfo.hostName
            passwordLabel.stringValue = "--- --- ---"
            setStatus(color: Self.bad, text: "The remote client is not installed on this computer. Ask IT to install it.")
        }
    }

    private func setupUI() {
        guard let content = contentView else { return }
        content.wantsLayer = true
        content.layer?.backgroundColor = NSColor.white.cgColor

        // Title
        let titleLabel = NSTextField(labelWithString: "IT Remote Support")
        titleLabel.font = .systemFont(ofSize: 18, weight: .semibold)
        titleLabel.textColor = Self.ink
        titleLabel.frame = NSRect(x: 70, y: 280, width: 300, height: 24)

        let subtitleLabel = NSTextField(labelWithString: "Lets IT see and control this computer to help you.")
        subtitleLabel.font = .systemFont(ofSize: 12)
        subtitleLabel.textColor = Self.muted
        subtitleLabel.frame = NSRect(x: 70, y: 260, width: 350, height: 16)

        // Divider
        let divider = NSBox()
        divider.boxType = .separator
        divider.frame = NSRect(x: 0, y: 254, width: 440, height: 1)

        // Your ID
        let idCaption = Self.caption("Your ID", x: 20, y: 230)
        idLabel.font = .systemFont(ofSize: 16, weight: .semibold)
        idLabel.textColor = Self.ink
        idLabel.frame = NSRect(x: 20, y: 205, width: 300, height: 22)

        let copyIdBtn = Self.iconButton(title: "Copy ID", x: 388, y: 205, target: self, action: #selector(copyId))

        // Password
        let pwCaption = Self.caption("Password", x: 20, y: 180)
        passwordLabel.font = .monospacedSystemFont(ofSize: 24, weight: .semibold)
        passwordLabel.textColor = Self.ink
        passwordLabel.frame = NSRect(x: 20, y: 148, width: 300, height: 30)

        let renewBtn = Self.iconButton(title: "New", x: 352, y: 155, target: self, action: #selector(newPassword))
        let copyPwBtn = Self.iconButton(title: "Copy", x: 388, y: 155, target: self, action: #selector(copyPassword))

        // Status
        statusDot.font = .systemFont(ofSize: 12)
        statusDot.frame = NSRect(x: 20, y: 110, width: 16, height: 16)
        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.textColor = Self.ink
        statusLabel.frame = NSRect(x: 38, y: 80, width: 382, height: 45)
        statusLabel.maximumNumberOfLines = 3

        // Footer
        let footer = NSView(frame: NSRect(x: 0, y: 0, width: 440, height: 56))
        footer.wantsLayer = true
        footer.layer?.backgroundColor = Self.soft.cgColor

        let footerDivider = NSBox()
        footerDivider.boxType = .separator
        footerDivider.frame = NSRect(x: 0, y: 55, width: 440, height: 1)

        let footerNote = NSTextField(wrappingLabelWithString: "IT can connect only while this window is open.\nClose it to stop remote access.")
        footerNote.font = .systemFont(ofSize: 11)
        footerNote.textColor = Self.muted
        footerNote.frame = NSRect(x: 20, y: 10, width: 300, height: 36)

        let closeBtn = NSButton(title: "Close", target: self, action: #selector(closeWindow))
        closeBtn.frame = NSRect(x: 336, y: 13, width: 84, height: 30)
        closeBtn.bezelStyle = .rounded

        footer.addSubview(footerDivider)
        footer.addSubview(footerNote)
        footer.addSubview(closeBtn)

        // REMOTE banner (hidden until IT connects)
        remoteBanner.frame = NSRect(x: 0, y: 56, width: 440, height: 24)
        remoteBanner.wantsLayer = true
        remoteBanner.layer?.backgroundColor = Self.live.cgColor
        remoteBanner.isHidden = true
        let bannerLabel = NSTextField(labelWithString: "● REMOTE")
        bannerLabel.font = .systemFont(ofSize: 11, weight: .bold)
        bannerLabel.textColor = .white
        bannerLabel.frame = NSRect(x: 10, y: 3, width: 200, height: 18)
        remoteBanner.addSubview(bannerLabel)

        for v: NSView in [titleLabel, subtitleLabel, divider, idCaption, idLabel, copyIdBtn,
                          pwCaption, passwordLabel, renewBtn, copyPwBtn,
                          statusDot, statusLabel, footer, remoteBanner] {
            content.addSubview(v)
        }
    }

    // MARK: - Actions

    @objc private func copyId() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(idLabel.stringValue, forType: .string)
    }

    @objc private func copyPassword() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(secret, forType: .string)
    }

    @objc private func newPassword() {
        secret = PasswordGenerator.generate()
        passwordLabel.stringValue = PasswordGenerator.spaced(secret)
        setStatus(color: Self.muted, text: "Connecting to AMS...")
        checkInNow()
    }

    @objc private func closeWindow() {
        close()
    }

    // MARK: - Polling

    private func startPolling() {
        checkInNow()
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.checkInNow()
        }
    }

    private func checkInNow() {
        guard !busy, !leaving, let ams = ams else { return }
        busy = true

        let pw = secret
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let result = ams.checkIn(password: pw)
            DispatchQueue.main.async { [weak self] in
                guard let self = self, !self.leaving, pw == self.secret else {
                    self?.busy = false
                    return
                }
                self.busy = false
                self.handleCheckIn(result)
            }
        }
    }

    private func handleCheckIn(_ result: CheckInState) {
        switch result {
        case .ok(let id, let connectedBy, let agentOnline):
            if !id.isEmpty { idLabel.stringValue = id }
            if let who = connectedBy {
                showLive(who: who)
            } else {
                endLive()
                if !agentOnline {
                    setStatus(color: Self.warn, text: "This computer's remote service is not connected to AMS, so IT cannot get in yet. Restart the computer, or ask IT for help.")
                } else {
                    setStatus(color: Self.good, text: "Ready. Give your ID and password to IT.")
                }
            }

        case .newPasswordNeeded:
            secret = PasswordGenerator.generate()
            passwordLabel.stringValue = PasswordGenerator.spaced(secret)
            setStatus(color: Self.warn, text: "Someone typed a wrong password 5 times, so this is a new one. Give it only to IT.")
            checkInNow()

        case .refused(let message):
            setStatus(color: Self.bad, text: message)

        case .unreachable:
            setStatus(color: Self.bad, text: "Cannot reach AMS at \(ams?.amsUrl ?? "?"). Check the network connection — retrying.")
        }
    }

    private func showLive(who: String) {
        liveWith = who
        title = "● Remote session — IT Remote Support"
        remoteBanner.isHidden = false
        setStatus(color: Self.live, text: "\(who) from IT is connected and can see and control this computer. Close this window to end the session.")
    }

    private func endLive() {
        guard liveWith != nil else { return }
        liveWith = nil
        title = "IT Remote Support"
        remoteBanner.isHidden = true
    }

    private func setStatus(color: NSColor, text: String) {
        statusDot.textColor = color
        statusLabel.stringValue = text
    }

    // MARK: - Close

    func closeSession() {
        leaving = true
        timer?.invalidate()
        ams?.close()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if let who = liveWith {
            let alert = NSAlert()
            alert.messageText = "End the session?"
            alert.informativeText = "\(who) from IT is connected and helping you.\n\nClose IT Remote Support and end the session?"
            alert.addButton(withTitle: "End session")
            alert.addButton(withTitle: "Cancel")
            alert.alertStyle = .warning
            if alert.runModal() != .alertFirstButtonReturn { return false }
        }

        leaving = true
        timer?.invalidate()
        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.ams?.close()
            DispatchQueue.main.async {
                NSApp.hide(nil)
            }
        }
        return true
    }

    // MARK: - Helpers

    private static func caption(_ text: String, x: CGFloat, y: CGFloat) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 11)
        label.textColor = muted
        label.frame = NSRect(x: x, y: y, width: 100, height: 14)
        return label
    }

    private static func iconButton(title: String, x: CGFloat, y: CGFloat, target: AnyObject, action: Selector) -> NSButton {
        let btn = NSButton(title: title, target: target, action: action)
        btn.frame = NSRect(x: x, y: y, width: 32, height: 24)
        btn.bezelStyle = .inline
        btn.font = .systemFont(ofSize: 10)
        return btn
    }
}
