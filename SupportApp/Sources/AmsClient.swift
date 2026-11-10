// Talks to AMS — equivalent of scripts/RemoteSupport/AmsClient.cs (Windows).
// Reads ConnectionInfo.json, checks in with password, tells AMS when the window closes.
import Foundation

enum CheckInState {
    case ok(id: String, connectedBy: String?, agentOnline: Bool)
    case newPasswordNeeded
    case refused(message: String)
    case unreachable
}

class AmsClient {
    let amsUrl: String
    let deviceId: String
    private let agentKey: String

    private init(amsUrl: String, agentKey: String, deviceId: String) {
        self.amsUrl = amsUrl
        self.agentKey = agentKey
        self.deviceId = deviceId
    }

    /// Reads ConnectionInfo.json from the installed CLT agent.
    static func load() -> AmsClient? {
        let paths = [
            Bundle.main.bundlePath + "/ConnectionInfo.json",
            "/usr/local/lib/CLT/ConnectionInfo.json",
            "/usr/local/lib/Remotely/ConnectionInfo.json",
        ]

        for path in paths {
            guard FileManager.default.fileExists(atPath: path),
                  let data = try? Data(contentsOf: URL(fileURLWithPath: path)),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  var host = json["Host"] as? String, !host.isEmpty,
                  let key = json["OrganizationID"] as? String, !key.isEmpty,
                  let id = json["DeviceID"] as? String, !id.isEmpty
            else { continue }

            host = host.trimmingCharacters(in: .whitespacesAndNewlines)
            if host.hasSuffix("/remote") { host = String(host.dropLast("/remote".count)) }
            host = host.trimmingCharacters(in: CharacterSet(charactersIn: "/"))

            return AmsClient(amsUrl: host, agentKey: key.trimmingCharacters(in: .whitespaces),
                             deviceId: id.trimmingCharacters(in: .whitespaces))
        }
        return nil
    }

    /// Check in with AMS — sends the password, gets back state and identity.
    func checkIn(password: String) -> CheckInState {
        let body = "{\"deviceId\":\"\(jsonEscape(deviceId))\",\"password\":\"\(jsonEscape(password))\"}"
        guard let (status, responseBody) = post(path: "/api/v1/agent/support", body: body, timeout: 8) else {
            return .unreachable
        }

        if status == 200 {
            let json = (try? JSONSerialization.jsonObject(with: Data(responseBody.utf8)) as? [String: Any]) ?? [:]
            let id = json["id"] as? String ?? ""
            let connectedBy = json["connectedBy"] as? String
            let online = json["online"] as? Bool ?? false
            return .ok(id: id, connectedBy: (connectedBy?.isEmpty ?? true) ? nil : connectedBy, agentOnline: online)
        }
        if status == 409 {
            return .newPasswordNeeded
        }

        let json = (try? JSONSerialization.jsonObject(with: Data(responseBody.utf8)) as? [String: Any]) ?? [:]
        let error = json["error"] as? String ?? "AMS refused this computer (HTTP \(status)). Ask IT for help."
        return .refused(message: error)
    }

    /// The window closed — invalidate the password and end any session.
    func close() {
        let body = "{\"deviceId\":\"\(jsonEscape(deviceId))\"}"
        _ = post(path: "/api/v1/agent/support/close", body: body, timeout: 3)
    }

    private func post(path: String, body: String, timeout: TimeInterval) -> (Int, String)? {
        guard let url = URL(string: amsUrl + path) else { return nil }
        var request = URLRequest(url: url, timeoutInterval: timeout)
        request.httpMethod = "POST"
        request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue(agentKey, forHTTPHeaderField: "X-Agent-Key")
        request.httpBody = body.data(using: .utf8)

        let semaphore = DispatchSemaphore(value: 0)
        var result: (Int, String)?

        URLSession.shared.dataTask(with: request) { data, response, error in
            defer { semaphore.signal() }
            guard error == nil, let http = response as? HTTPURLResponse else { return }
            let text = data.flatMap { String(data: $0, encoding: .utf8) } ?? ""
            result = (http.statusCode, text)
        }.resume()

        semaphore.wait()
        return result
    }

    private func jsonEscape(_ s: String) -> String {
        s.replacingOccurrences(of: "\\", with: "\\\\")
          .replacingOccurrences(of: "\"", with: "\\\"")
    }
}
