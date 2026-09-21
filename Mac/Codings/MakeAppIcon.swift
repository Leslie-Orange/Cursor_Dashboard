import AppKit
import Foundation

let outputURL = URL(fileURLWithPath: CommandLine.arguments.dropFirst().first ?? "AppIcon.icns")
let workDir = FileManager.default.temporaryDirectory.appendingPathComponent("CursorQuotaPet.iconset")
try? FileManager.default.removeItem(at: workDir)
try FileManager.default.createDirectory(at: workDir, withIntermediateDirectories: true)

func drawIcon(size: CGFloat) -> NSImage {
    let image = NSImage(size: NSSize(width: size, height: size))
    image.lockFocus()

    let rect = NSRect(x: 0, y: 0, width: size, height: size)
    let radius = size * 0.223
    let path = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    path.addClip()

    let gradient = NSGradient(colors: [
        NSColor(red: 0.27, green: 0.49, blue: 0.98, alpha: 1),
        NSColor(red: 0.38, green: 0.29, blue: 0.90, alpha: 1)
    ])
    gradient?.draw(in: rect, angle: -45)

    let highlight = NSBezierPath(ovalIn: NSRect(
        x: size * 0.12,
        y: size * 0.52,
        width: size * 0.62,
        height: size * 0.46
    ))
    NSColor.white.withAlphaComponent(0.18).setFill()
    highlight.fill()

    let config = NSImage.SymbolConfiguration(pointSize: size * 0.48, weight: .semibold)
    if let symbol = NSImage(systemSymbolName: "gauge.medium", accessibilityDescription: nil)?
        .withSymbolConfiguration(config) {
        let symbolSize = symbol.size
        let symbolRect = NSRect(
            x: (size - symbolSize.width) / 2,
            y: (size - symbolSize.height) / 2 - size * 0.02,
            width: symbolSize.width,
            height: symbolSize.height
        )
        symbol.isTemplate = true
        NSGraphicsContext.current?.cgContext.setFillColor(NSColor.white.cgColor)
        symbol.draw(in: symbolRect, from: .zero, operation: .sourceOver, fraction: 1)
    }

    image.unlockFocus()
    return image
}

func pngData(from image: NSImage, pixelSize: Int) -> Data {
    let scaleSize = NSSize(width: pixelSize, height: pixelSize)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: pixelSize,
        pixelsHigh: pixelSize,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    )!
    bitmap.size = scaleSize
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    image.draw(in: NSRect(origin: .zero, size: scaleSize))
    NSGraphicsContext.restoreGraphicsState()
    return bitmap.representation(using: .png, properties: [:])!
}

let variants: [(String, Int)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
    ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256),
    ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512),
    ("icon_512x512@2x.png", 1024)
]

for (name, pixels) in variants {
    let rendered = drawIcon(size: CGFloat(pixels))
    let data = pngData(from: rendered, pixelSize: pixels)
    try data.write(to: workDir.appendingPathComponent(name))
}

let process = Process()
process.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
process.arguments = ["-c", "icns", "-o", outputURL.path, workDir.path]
try process.run()
process.waitUntilExit()
guard process.terminationStatus == 0 else {
    fputs("iconutil 失败\n", stderr)
    exit(1)
}
try? FileManager.default.removeItem(at: workDir)
print(outputURL.path)
