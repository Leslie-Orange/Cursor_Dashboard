import AppKit
import SwiftUI

/// The small amount of sizing shared with the panel controller. The panel height
/// comes from the hosting view's fitting size because the footer can wrap.
enum GlassTheme {
    static let panelWidth: CGFloat = 386
    static let shadowPadding: CGFloat = 18
    static let windowWidth: CGFloat = panelWidth + shadowPadding * 2
}

enum QuotaPresentation {
    static func badge(_ window: QuotaWindow?, secondary: Bool) -> String {
        if secondary { return "额外" }
        guard let window else { return "内置" }
        if window.badge == "内置" || window.badge == "额度" {
            return window.badge
        }
        return window.title.contains("套餐") ? "额度" : "内置"
    }

    static func cardBadge(window: QuotaWindow? = nil, secondary: Bool) -> String {
        if secondary { return "额外额度池" }
        guard let minutes = window?.windowMinutes, minutes.isFinite, minutes > 10_080 else {
            return "周限额"
        }
        return "月限额"
    }

    static func value(_ window: QuotaWindow?, secondary: Bool) -> String {
        guard secondary else { return QuotaFormatter.percent(window?.remaining) }
        let remaining = QuotaAmountFormatter.string(
            cents: window?.remainingAmountCents,
            currencyCode: window?.currencyCode
        ) ?? "—"
        let total = QuotaAmountFormatter.string(
            cents: window?.limitAmountCents,
            currencyCode: window?.currencyCode
        ) ?? "—"
        return "\(remaining) / \(total)"
    }
}

private enum QuotaAmountFormatter {
    static func string(cents: Double?, currencyCode: String?) -> String? {
        guard let cents, cents.isFinite else { return nil }
        let code = currencyCode?.trimmingCharacters(in: .whitespacesAndNewlines)
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: "en_US")
        formatter.numberStyle = .currency
        formatter.currencyCode = (code?.isEmpty == false ? code?.uppercased() : nil) ?? "USD"
        formatter.minimumFractionDigits = 2
        formatter.maximumFractionDigits = 2
        return formatter.string(from: NSNumber(value: cents / 100))
    }
}

private enum QuotaPalette {
    static let normal = Color.primary
    static let warning = Color(nsColor: .systemOrange)
    static let danger = Color(nsColor: .systemRed)

    static func emphasis(remaining: Double?) -> Color {
        guard let remaining, remaining.isFinite else { return .secondary }
        if remaining <= 10 { return danger }
        if remaining <= 30 { return warning }
        return normal
    }
}

private enum QuotaLayout {
    static let panelRadius: CGFloat = 29
    static let cardRadius: CGFloat = 18
    static let panelInset: CGFloat = 16

    static var panelShape: RoundedRectangle {
        RoundedRectangle(cornerRadius: panelRadius, style: .continuous)
    }

    static var cardShape: RoundedRectangle {
        RoundedRectangle(cornerRadius: cardRadius, style: .continuous)
    }
}

private final class MaskedGlassView: NSVisualEffectView {
    var cornerRadius: CGFloat = QuotaLayout.panelRadius {
        didSet { applyMask() }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        applyMask()
    }

    override func layout() {
        super.layout()
        applyMask()
    }

    private func applyMask() {
        wantsLayer = true
        layer?.cornerRadius = cornerRadius
        layer?.cornerCurve = .continuous
        layer?.masksToBounds = true
        layer?.backgroundColor = NSColor.clear.cgColor
        layer?.borderWidth = 0
    }
}

private struct VisualBlur: NSViewRepresentable {
    var material: NSVisualEffectView.Material = .hudWindow
    var cornerRadius: CGFloat = QuotaLayout.panelRadius

    func makeNSView(context: Context) -> MaskedGlassView {
        let view = MaskedGlassView()
        view.material = material
        view.blendingMode = .behindWindow
        view.state = .active
        view.isEmphasized = true
        view.cornerRadius = cornerRadius
        return view
    }

    func updateNSView(_ nsView: MaskedGlassView, context: Context) {
        nsView.material = material
        nsView.state = .active
        nsView.cornerRadius = cornerRadius
    }
}

private struct GlassPanel<Content: View>: View {
    let reduceTransparency: Bool
    @ViewBuilder var content: Content

    @ViewBuilder
    var body: some View {
        if #available(macOS 26.0, *), !reduceTransparency {
            content
                .frame(width: GlassTheme.panelWidth)
                .fixedSize(horizontal: false, vertical: true)
                .glassEffect(.regular, in: QuotaLayout.panelShape)
                .containerShape(QuotaLayout.panelShape)
                .overlay {
                    QuotaLayout.panelShape.strokeBorder(.primary.opacity(0.10), lineWidth: 0.5)
                }
        } else {
            content
                .frame(width: GlassTheme.panelWidth)
                .fixedSize(horizontal: false, vertical: true)
                .background {
                    if reduceTransparency {
                        QuotaLayout.panelShape.fill(Color(nsColor: .windowBackgroundColor))
                    } else {
                        VisualBlur(material: .hudWindow, cornerRadius: QuotaLayout.panelRadius)
                            .clipShape(QuotaLayout.panelShape)
                    }
                }
                .clipShape(QuotaLayout.panelShape)
                .containerShape(QuotaLayout.panelShape)
                .overlay {
                    QuotaLayout.panelShape.strokeBorder(.primary.opacity(0.12), lineWidth: 0.5)
                }
                .shadow(color: Color.black.opacity(0.16), radius: 18, x: 0, y: 10)
        }
    }
}

private struct GlassInsetCard<Content: View>: View {
    let reduceTransparency: Bool
    @ViewBuilder var content: Content

    @ViewBuilder
    var body: some View {
        if #available(macOS 26.0, *), !reduceTransparency {
            content
                .background(.fill.quaternary, in: QuotaLayout.cardShape)
                .overlay {
                    QuotaLayout.cardShape.strokeBorder(.primary.opacity(0.055), lineWidth: 0.5)
                }
        } else {
            content
                .background {
                    QuotaLayout.cardShape.fill(
                        reduceTransparency
                            ? Color(nsColor: .underPageBackgroundColor)
                            : Color.primary.opacity(0.045)
                    )
                }
                .clipShape(QuotaLayout.cardShape)
                .overlay {
                    QuotaLayout.cardShape.strokeBorder(.primary.opacity(0.075), lineWidth: 0.5)
                }
        }
    }
}

private struct GlassControlGroup<Content: View>: View {
    @ViewBuilder var content: Content

    @ViewBuilder
    var body: some View {
        if #available(macOS 26.0, *) {
            GlassEffectContainer(spacing: 6) {
                HStack(spacing: 6) { content }
            }
        } else {
            HStack(spacing: 8) { content }
        }
    }
}

private struct GlassIconButton: View {
    let systemName: String
    let accessibilityLabel: String
    let reduceTransparency: Bool
    let reduceMotion: Bool
    let action: () -> Void

    private let size: CGFloat = 28

    var body: some View {
        Image(systemName: systemName)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .frame(width: size, height: size)
            .modifier(GlassCircleBackground(
                reduceTransparency: reduceTransparency,
                reduceMotion: reduceMotion
            ))
            .overlay {
                PointingHandHitArea(toolTip: accessibilityLabel, action: action)
                    .frame(width: size, height: size)
            }
            .accessibilityLabel(accessibilityLabel)
            .accessibilityAddTraits(.isButton)
            .accessibilityAction(.default, action)
            .help(accessibilityLabel)
    }
}

private struct PointingHandHitArea: NSViewRepresentable {
    let toolTip: String
    let action: () -> Void

    func makeNSView(context: Context) -> PointingHandHitView {
        let view = PointingHandHitView()
        view.setAccessibilityElement(false)
        view.toolTip = toolTip
        view.action = action
        return view
    }

    func updateNSView(_ view: PointingHandHitView, context: Context) {
        view.toolTip = toolTip
        view.action = action
    }
}

private final class PointingHandHitView: NSView {
    var action: (() -> Void)?
    private var installedTrackingAreas: [NSTrackingArea] = []
    private var isTrackingPress = false

    override var acceptsFirstResponder: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        containsLocalPoint(convert(point, from: superview)) ? self : nil
    }

    private func containsLocalPoint(_ point: NSPoint) -> Bool {
        guard bounds.contains(point) else { return false }
        let dx = point.x - bounds.midX
        let dy = point.y - bounds.midY
        let radius = min(bounds.width, bounds.height) / 2
        return dx * dx + dy * dy <= radius * radius
    }

    override func resetCursorRects() {
        discardCursorRects()
        guard bounds.width > 1, bounds.height > 1 else { return }
        addCursorRect(bounds, cursor: .pointingHand)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        installedTrackingAreas.forEach(removeTrackingArea)
        let optionSets: [NSTrackingArea.Options] = [
            [.mouseEnteredAndExited, .mouseMoved, .cursorUpdate, .activeInKeyWindow, .inVisibleRect],
            [.mouseEnteredAndExited, .mouseMoved, .cursorUpdate, .activeInActiveApp, .inVisibleRect]
        ]
        installedTrackingAreas = optionSets.map { options in
            let area = NSTrackingArea(rect: .zero, options: options, owner: self, userInfo: nil)
            addTrackingArea(area)
            return area
        }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        updateTrackingAreas()
        window?.invalidateCursorRects(for: self)
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        window?.invalidateCursorRects(for: self)
    }

    override func cursorUpdate(with event: NSEvent) {
        NSCursor.pointingHand.set()
    }

    override func mouseEntered(with event: NSEvent) {
        NSCursor.pointingHand.set()
    }

    override func mouseMoved(with event: NSEvent) {
        NSCursor.pointingHand.set()
    }

    override func mouseExited(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        if !containsLocalPoint(local) {
            NSCursor.arrow.set()
        }
    }

    override func mouseDown(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        isTrackingPress = containsLocalPoint(local)
        (isTrackingPress ? NSCursor.pointingHand : NSCursor.arrow).set()
    }

    override func mouseDragged(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        (containsLocalPoint(local) ? NSCursor.pointingHand : NSCursor.arrow).set()
    }

    override func mouseUp(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        let shouldPerform = isTrackingPress && containsLocalPoint(local)
        isTrackingPress = false
        (containsLocalPoint(local) ? NSCursor.pointingHand : NSCursor.arrow).set()
        guard shouldPerform else { return }
        let action = action
        DispatchQueue.main.async {
            action?()
        }
    }
}

private struct GlassCircleBackground: ViewModifier {
    let reduceTransparency: Bool
    let reduceMotion: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if reduceTransparency {
            content
                .background(Color(nsColor: .controlBackgroundColor), in: Circle())
                .overlay {
                    Circle().strokeBorder(Color.primary.opacity(0.055), lineWidth: 0.5)
                }
        } else if #available(macOS 26.0, *) {
            if reduceMotion {
                content.glassEffect(.regular, in: Circle())
            } else {
                content.glassEffect(.regular.interactive(), in: Circle())
            }
        } else {
            content.background(.primary.opacity(0.08), in: Circle())
        }
    }
}

private struct QuotaStatusLabel: View {
    let title: String
    let tint: Color

    var body: some View {
        HStack(spacing: 5) {
            Circle()
                .fill(tint)
                .frame(width: 6, height: 6)
            Text(title)
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }
}

private struct QuotaProgressBar: View {
    let remaining: Double?
    let tint: Color
    let reduceMotion: Bool

    private var progress: CGFloat {
        guard let remaining, remaining.isFinite else { return 0 }
        return CGFloat(min(100, max(0, remaining)) / 100)
    }

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule()
                    .fill(Color.primary.opacity(0.085))
                if progress > 0 {
                    Capsule()
                        .fill(tint.opacity(0.78))
                        .frame(width: geometry.size.width * progress)
                }
            }
        }
        .frame(height: 7)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.35), value: progress)
        .accessibilityElement()
        .accessibilityLabel("剩余额度")
        .accessibilityValue(remaining.map { QuotaFormatter.percent($0) } ?? "未知")
    }
}

private struct QuotaCard: View {
    let window: QuotaWindow?
    let secondary: Bool
    let reduceTransparency: Bool
    let reduceMotion: Bool

    private var tint: Color {
        QuotaPalette.emphasis(remaining: window?.remaining)
    }

    private var detail: String {
        guard let window else { return "—" }
        if let detail = window.detail, !detail.isEmpty { return detail }
        return QuotaFormatter.burnRatePerDay(
            used: window.used,
            remaining: window.remaining,
            resetAt: window.resetAt,
            windowMinutes: window.windowMinutes
        )
    }

    private var resetLabel: String {
        if secondary, let window, window.resetAt == nil, window.windowMinutes == nil {
            return "余额实时更新"
        }
        return QuotaFormatter.reset(window?.resetAt)
    }

    var body: some View {
        GlassInsetCard(reduceTransparency: reduceTransparency) {
            VStack(alignment: .leading, spacing: 7) {
                HStack(alignment: .center, spacing: 8) {
                    Text(QuotaPresentation.cardBadge(window: window, secondary: secondary))
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 7)
                        .padding(.vertical, 3)
                        .background(Color.primary.opacity(0.055), in: Capsule())
                        .fixedSize(horizontal: true, vertical: false)

                    Spacer(minLength: 8)

                    Text(QuotaPresentation.value(window, secondary: secondary))
                        .font(.system(
                            size: secondary ? 23 : 27,
                            weight: .semibold,
                            design: .rounded
                        ))
                        .foregroundStyle(tint)
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(secondary ? 0.62 : 0.8)
                        .layoutPriority(1)
                        .accessibilityLabel(secondary ? "余额 / 额度池总额" : "剩余百分比")
                        .accessibilityValue(QuotaPresentation.value(window, secondary: secondary))
                }

                QuotaProgressBar(
                    remaining: window?.remaining,
                    tint: tint,
                    reduceMotion: reduceMotion
                )

                HStack(spacing: 8) {
                    Text(resetLabel)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)

                    Spacer(minLength: 4)

                    Text(detail)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.tertiary)
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
            .padding(.horizontal, 13)
            .padding(.vertical, 11)
        }
    }
}

struct QuotaView: View {
    @ObservedObject var model: QuotaModel
    let refresh: () -> Void
    let dismiss: () -> Void

    @Environment(\.accessibilityReduceTransparency) private var accessibilityReduceTransparency
    @Environment(\.accessibilityReduceMotion) private var accessibilityReduceMotion

    private var reduceTransparency: Bool {
        accessibilityReduceTransparency || ProcessInfo.processInfo.arguments.contains("--preview-reduce-transparency")
    }

    private var reduceMotion: Bool {
        accessibilityReduceMotion || ProcessInfo.processInfo.arguments.contains("--preview-reduce-motion")
    }

    private var currentSnapshot: QuotaSnapshot? {
        model.snapshot ?? model.fallbackSnapshot
    }

    private var statusBadge: String {
        guard currentSnapshot != nil else { return "等待" }
        if !model.connectionError.isEmpty { return "快照" }
        return model.snapshot?.sourceName == "cursor-api" ? "实时" : "快照"
    }

    private var statusTint: Color {
        guard currentSnapshot != nil else { return .secondary }
        if !model.connectionError.isEmpty || model.snapshot?.sourceName != "cursor-api" {
            return QuotaPalette.warning
        }
        return Color.secondary
    }

    var body: some View {
        GlassPanel(reduceTransparency: reduceTransparency) {
            VStack(spacing: 10) {
                header
                    .padding(.horizontal, QuotaLayout.panelInset)
                    .padding(.top, 15)

                VStack(spacing: 9) {
                    QuotaCard(
                        window: currentSnapshot?.primary,
                        secondary: false,
                        reduceTransparency: reduceTransparency,
                        reduceMotion: reduceMotion
                    )
                    QuotaCard(
                        window: currentSnapshot?.secondary,
                        secondary: true,
                        reduceTransparency: reduceTransparency,
                        reduceMotion: reduceMotion
                    )
                }
                .padding(.horizontal, QuotaLayout.panelInset)

                footer
                    .padding(.horizontal, QuotaLayout.panelInset)
                    .padding(.bottom, 13)
            }
            .frame(width: GlassTheme.panelWidth)
            .fixedSize(horizontal: false, vertical: true)
        }
        .padding(GlassTheme.shadowPadding)
        .fixedSize(horizontal: false, vertical: true)
    }

    private var header: some View {
        HStack(spacing: 9) {
            Image(nsImage: CursorBrand.markImage(size: 28, template: true))
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(width: 28, height: 28)
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 2) {
                Text(QuotaFormatter.planTitle(currentSnapshot?.planType))
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.primary)
                    .lineLimit(1)

                Text(
                    QuotaFormatter.resetAt(
                        currentSnapshot?.primary.resetAt ?? currentSnapshot?.secondary.resetAt
                    ) ?? "重置时间未知"
                )
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.82)
                .monospacedDigit()
            }
            .layoutPriority(1)

            Spacer(minLength: 5)

            GlassControlGroup {
                GlassIconButton(
                    systemName: "arrow.clockwise",
                    accessibilityLabel: "刷新额度",
                    reduceTransparency: reduceTransparency,
                    reduceMotion: reduceMotion,
                    action: refresh
                )
                GlassIconButton(
                    systemName: "xmark",
                    accessibilityLabel: "关闭详情",
                    reduceTransparency: reduceTransparency,
                    reduceMotion: reduceMotion,
                    action: dismiss
                )
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var footer: some View {
        HStack(alignment: .top, spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                if currentSnapshot != nil || model.connectionError.isEmpty {
                    Text(model.footer)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                        .fixedSize(horizontal: false, vertical: true)
                }

                if !model.connectionError.isEmpty {
                    Text(model.connectionError)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(QuotaPalette.warning)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .layoutPriority(1)

            Spacer(minLength: 3)
            QuotaStatusLabel(title: statusBadge, tint: statusTint)
                .padding(.top, 1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
