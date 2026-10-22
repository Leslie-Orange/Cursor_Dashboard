import AppKit

/// Shared, vector-drawn identity used by the menu bar and the app icon.
enum CursorBrand {
    private enum Palette {
        static let graphite = NSColor(srgbRed: 32 / 255, green: 34 / 255, blue: 33 / 255, alpha: 1)
        static let warmWhite = NSColor(srgbRed: 242 / 255, green: 241 / 255, blue: 236 / 255, alpha: 1)
        static let lightQuota = NSColor(srgbRed: 174 / 255, green: 177 / 255, blue: 172 / 255, alpha: 1)
        static let darkQuota = NSColor(srgbRed: 119 / 255, green: 123 / 255, blue: 119 / 255, alpha: 1)
    }

    /// Returns a transparent, square monochrome mark suitable for a status item.
    static func markImage(size: CGFloat, template: Bool = true) -> NSImage {
        let image = makeImage(size: size) { bounds in
            drawMark(in: bounds, monochrome: template)
        }
        image.isTemplate = template
        return image
    }

    /// Returns the graphite rounded-square app icon, with transparent outer padding.
    static func appIconImage(size: CGFloat) -> NSImage {
        makeImage(size: size) { bounds in
            let side = min(bounds.width, bounds.height)
            let inset = max(side * 0.045, side <= 32 ? 0.5 : 0)
            let background = bounds.insetBy(dx: inset, dy: inset)
            let backgroundPath = NSBezierPath(
                roundedRect: background,
                xRadius: background.width * 0.22,
                yRadius: background.height * 0.22
            )
            Palette.graphite.setFill()
            backgroundPath.fill()
            drawMark(in: bounds, monochrome: false)
        }
    }

    private static func makeImage(size: CGFloat, drawing: (NSRect) -> Void) -> NSImage {
        let imageSize = NSSize(width: size, height: size)
        let image = NSImage(size: imageSize)
        image.lockFocus()
        drawing(NSRect(origin: .zero, size: imageSize))
        image.unlockFocus()
        return image
    }

    private static func drawMark(in bounds: NSRect, monochrome: Bool) {
        let side = min(bounds.width, bounds.height)
        let origin = NSPoint(
            x: bounds.midX - side / 2,
            y: bounds.midY - side / 2
        )
        func point(_ x: CGFloat, _ y: CGFloat) -> NSPoint {
            NSPoint(x: origin.x + side * x, y: origin.y + side * y)
        }

        let top = point(0.5, 0.875)
        let upperLeft = point(0.205, 0.715)
        let center = point(0.5, 0.555)
        let upperRight = point(0.795, 0.715)
        let lowerLeft = point(0.205, 0.405)
        let bottom = point(0.5, 0.245)
        let lowerRight = point(0.795, 0.405)

        let cube = NSBezierPath()
        cube.move(to: top)
        cube.line(to: upperRight)
        cube.line(to: lowerRight)
        cube.line(to: bottom)
        cube.line(to: lowerLeft)
        cube.line(to: upperLeft)
        cube.close()
        Palette.warmWhite.setFill()
        cube.fill()

        let seam = NSBezierPath()
        seam.move(to: upperLeft)
        seam.line(to: center)
        seam.line(to: upperRight)
        seam.move(to: center)
        seam.line(to: bottom)
        seam.lineCapStyle = .butt
        seam.lineJoinStyle = .miter
        seam.lineWidth = max(side * 0.014, side <= 32 ? 0.8 : 0)
        if monochrome {
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current?.compositingOperation = .destinationOut
            NSColor.black.setStroke()
            seam.stroke()
            NSGraphicsContext.restoreGraphicsState()
        } else {
            Palette.graphite.setStroke()
            seam.stroke()
        }

        let upperBarHeight = max(side * 0.04, side <= 32 ? 1 : 0)
        let lowerBarHeight = max(side * 0.04, side <= 32 ? 1 : 0)
        let bars: [(CGFloat, CGFloat, CGFloat, CGFloat, NSColor)] = [
            (0.5, 0.20, 0.43, upperBarHeight, monochrome ? NSColor.black : Palette.lightQuota),
            (0.5, 0.10, 0.64, lowerBarHeight, monochrome ? NSColor.black : Palette.darkQuota)
        ]
        for (x, y, width, height, color) in bars {
            let barRect = NSRect(
                x: origin.x + side * (x - width / 2),
                y: origin.y + side * y - height / 2,
                width: side * width,
                height: height
            )
            let bar = NSBezierPath(roundedRect: barRect, xRadius: height / 2, yRadius: height / 2)
            color.setFill()
            bar.fill()
        }
    }
}
