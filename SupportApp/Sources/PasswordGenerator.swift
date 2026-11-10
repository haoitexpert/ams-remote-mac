// Password generator — same algorithm as the Windows version (scripts/RemoteSupport/SupportForm.cs).
// 9 characters from an alphabet that avoids confusable pairs (0/O, 1/I/L).
import Foundation
import Security

enum PasswordGenerator {
    private static let alphabet = Array("23456789ABCDEFGHJKMNPQRSTUVWXYZ")
    private static let length = 9

    /// Generates a 9-character password with at least one digit and one letter.
    static func generate() -> String {
        var bytes = [UInt8](repeating: 0, count: 1)
        while true {
            var chars = [Character]()
            for _ in 0..<length {
                repeat {
                    _ = SecRandomCopyBytes(kSecRandomDefault, 1, &bytes)
                } while bytes[0] >= 248  // bias rejection: 248 = 8 * 31
                chars.append(alphabet[Int(bytes[0]) % alphabet.count])
            }
            let password = String(chars)
            if password.contains(where: \.isNumber) && password.contains(where: \.isLetter) {
                return password
            }
        }
    }

    /// "K7M2QX9PA" → "K7M 2QX 9PA" — easier to read out.
    static func spaced(_ password: String) -> String {
        let s = Array(password)
        guard s.count == 9 else { return password }
        return "\(String(s[0..<3])) \(String(s[3..<6])) \(String(s[6..<9]))"
    }
}
