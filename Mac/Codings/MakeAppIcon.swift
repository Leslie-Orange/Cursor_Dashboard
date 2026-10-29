import AppKit
import Foundation

@main
struct MakeAppIcon {
    // ICNS stores each vector-rendered PNG as a typed chunk. The Retina entries
    // use ic11/ic12/ic13/ic14; the 1x entries use the matching legacy PNG types.
    private static let iconVariants: [(String, Int, String)] = [
        ("icon_16x16.png", 16, "icp4"),
        ("icon_16x16@2x.png", 32, "ic11"),
        ("icon_32x32.png", 32, "icp5"),
        ("icon_32x32@2x.png", 64, "ic12"),
        ("icon_128x128.png", 128, "ic07"),
        ("icon_128x128@2x.png", 256, "ic13"),
        ("icon_256x256.png", 256, "ic08"),
        ("icon_256x256@2x.png", 512, "ic14"),
        ("icon_512x512.png", 512, "ic09"),
        ("icon_512x512@2x.png", 1024, "ic10")
    ]

    static func main() throws {
        let codingDirectory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        let defaultIconDirectory = codingDirectory.appendingPathComponent("icon", isDirectory: true)
        let arguments = Array(CommandLine.arguments.dropFirst())
        let pngURL = arguments.indices.contains(0)
            ? URL(fileURLWithPath: arguments[0])
            : defaultIconDirectory.appendingPathComponent("app-icon.png")
        let icnsURL = arguments.indices.contains(1)
            ? URL(fileURLWithPath: arguments[1])
            : codingDirectory.appendingPathComponent("AppIcon.icns")
        let previewDirectory = arguments.indices.contains(2)
            ? URL(fileURLWithPath: arguments[2], isDirectory: true)
            : defaultIconDirectory

        try FileManager.default.createDirectory(
            at: pngURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try FileManager.default.createDirectory(
            at: icnsURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try FileManager.default.createDirectory(at: previewDirectory, withIntermediateDirectories: true)

        var renderedPNG: [Int: Data] = [:]
        func pngData(size: Int) throws -> Data {
            if let data = renderedPNG[size] { return data }
            let data = try makePNG(size: size)
            renderedPNG[size] = data
            return data
        }

        try pngData(size: 1024).write(to: pngURL)
        for size in [16, 32, 64, 128, 256, 512] {
            let previewURL = previewDirectory.appendingPathComponent("app-icon-\(size).png")
            try pngData(size: size).write(to: previewURL)
        }

        let chunks = try iconVariants.map { (name, size, type) in
            (type, try pngData(size: size))
        }
        var icns = Data("icns".utf8)
        let icnsLength = 8 + chunks.reduce(0) { $0 + 8 + $1.1.count }
        appendUInt32(UInt32(icnsLength), to: &icns)
        for (type, payload) in chunks {
            icns.append(contentsOf: type.utf8)
            appendUInt32(UInt32(payload.count + 8), to: &icns)
            icns.append(payload)
        }
        try icns.write(to: icnsURL)

        print("1024px PNG: \(pngURL.path)")
        print("ICNS: \(icnsURL.path)")
        print("Previews: \(previewDirectory.path)")
    }

    private static func appendUInt32(_ value: UInt32, to data: inout Data) {
        var bigEndianValue = value.bigEndian
        withUnsafeBytes(of: &bigEndianValue) { data.append(contentsOf: $0) }
    }

    private static func makePNG(size: Int) throws -> Data {
        let pixelSize = NSSize(width: size, height: size)
        let image = CursorBrand.appIconImage(size: CGFloat(size))
        guard let bitmap = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: size,
            pixelsHigh: size,
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ) else {
            throw IconGenerationError.bitmapCreationFailed(size)
        }

        bitmap.size = pixelSize
        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }
        guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
            throw IconGenerationError.bitmapContextFailed(size)
        }
        context.imageInterpolation = .high
        NSGraphicsContext.current = context
        image.draw(
            in: NSRect(origin: .zero, size: pixelSize),
            from: NSRect(origin: .zero, size: image.size),
            operation: .copy,
            fraction: 1
        )
        guard let data = bitmap.representation(using: .png, properties: [:]) else {
            throw IconGenerationError.pngEncodingFailed(size)
        }
        return data
    }
}

private enum IconGenerationError: Error, CustomStringConvertible {
    case bitmapCreationFailed(Int)
    case bitmapContextFailed(Int)
    case pngEncodingFailed(Int)

    var description: String {
        switch self {
        case .bitmapCreationFailed(let size): return "无法创建 \(size)px 位图上下文"
        case .bitmapContextFailed(let size): return "无法初始化 \(size)px 图形上下文"
        case .pngEncodingFailed(let size): return "无法编码 \(size)px PNG"
        }
    }
}
