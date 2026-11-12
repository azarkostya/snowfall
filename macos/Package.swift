// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Snowfall",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "Snowfall", path: "Sources/Snowfall")
    ]
)
