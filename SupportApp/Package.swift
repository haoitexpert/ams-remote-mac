// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AMS-Remote-Support",
    platforms: [.macOS(.v12)],
    targets: [
        .executableTarget(
            name: "AMS-Remote-Support",
            path: "Sources"
        ),
    ]
)
