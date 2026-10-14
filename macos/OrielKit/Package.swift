// swift-tools-version: 6.2
import PackageDescription

/// Oriel's logic with no UI: providers, wallpaper pipeline, storage, scheduling. The macOS twin of Oriel.Core.
let package = Package(
    name: "OrielKit",
    platforms: [.macOS(.v26)],
    products: [
        .library(name: "OrielKit", targets: ["OrielKit"]),
    ],
    targets: [
        .target(name: "OrielKit"),
        .testTarget(name: "OrielKitTests", dependencies: ["OrielKit"]),
    ]
)
