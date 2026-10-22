import AppKit
import CryptoKit
import Foundation
import SwiftUI

@main
struct AppearanceTests {
    private static var assertions = 0

    @MainActor
    static func main() {
        NSApplication.shared.setActivationPolicy(.prohibited)

        testMenuPresentationAndWidth()
        testQuotaParserAmountSourcesAndPeriods()
        testQuotaSnapshotEncoding()
        testStatusViewHitTestingAndCallbacks()
        testPreviewScenariosAndCacheIsolation()
        testHostingSizes()
        testBrandImages()
        renderStatusViewPreviewsIfRequested()

        print("Appearance tests passed: \(assertions) assertions")
    }

    @MainActor
    private static func testMenuPresentationAndWidth() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let widths = [100.0, 0.0, nil as Double?].map { value -> CGFloat in
            let view = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 100, height: 22))
            view.update(snapshot: makeSnapshot(primary: value, secondary: value, sampledAt: now))
            let width = view.preferredWidth()
            view.setFrameSize(NSSize(width: width, height: 22))
            view.layout()

            let labels = view.subviews.compactMap { $0 as? NSTextField }
            expect(labels.count == 2, "status item should contain two text labels")
            for label in labels {
                let measured = (label.stringValue as NSString).size(
                    withAttributes: [.font: label.font ?? NSFont.monospacedDigitSystemFont(ofSize: 10, weight: .medium)]
                ).width
                expect(label.frame.minX >= 0, "menu label starts inside the status item")
                expect(label.frame.maxX <= view.bounds.maxX + 0.5, "menu label stays inside the status item")
                expect(measured <= label.frame.width + 1, "menu label text fits without clipping: \(label.stringValue)")
            }
            return width
        }
        expect(widths.max()! - widths.min()! <= 0.5, "menu width remains stable for 100%, 0%, and missing data")

        let now2 = Date(timeIntervalSince1970: 1_800_000_000)
        let legacySecondary = QuotaWindow(
            remaining: 45,
            used: 55,
            resetAt: nil,
            windowMinutes: nil,
            badge: "其他",
            title: "其他模型剩余",
            detail: nil
        )
        let originalRemaining = legacySecondary.remaining
        expect(QuotaPresentation.badge(legacySecondary, secondary: true) == "额外", "secondary badge is relabeled as extra")
        let secondarySnapshot = makeSnapshot(primary: 76, secondary: originalRemaining, sampledAt: now2,
                                             secondaryTitle: legacySecondary.title)
        let secondaryView = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 100, height: 22))
        secondaryView.update(snapshot: secondarySnapshot)
        let secondaryText = secondaryView.subviews.compactMap { $0 as? NSTextField }.last?.stringValue
        expect(secondaryText == "额外 45%", "secondary label uses the extra name and keeps its value")
        expect(secondarySnapshot.secondary.remaining == originalRemaining, "secondary relabeling does not change the numeric value")

        let amountFallback = QuotaParser.snapshot(
            dashboard: ["planUsage": ["limit": 5_000.0, "includedSpend": 1_250.0, "apiPercentUsed": 5.0]],
            planInfo: ["planName": "pro"],
            summary: nil,
            authUsage: nil,
            stripe: nil,
            localPlan: nil,
            sampledAt: now2,
            sourceName: "test"
        )
        expect(amountFallback?.primary.remaining == 75, "amount fallback computes 75% remaining")
        expect(amountFallback?.primary.badge == "额度", "amount fallback retains the quota badge")
        expect(amountFallback.map { QuotaPresentation.badge($0.primary, secondary: false) } == "额度",
               "amount fallback displays the quota badge in the main row")
        let fallbackView = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 100, height: 22))
        fallbackView.update(snapshot: amountFallback)
        let fallbackText = fallbackView.subviews.compactMap { $0 as? NSTextField }.first?.stringValue
        expect(fallbackText == "额度 75%", "amount fallback displays the correct main-row value")
    }

    private static func testQuotaParserAmountSourcesAndPeriods() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let monthStart = now.addingTimeInterval(-14 * 86_400)
        let monthEnd = now.addingTimeInterval(16 * 86_400)
        let weekReset = now.addingTimeInterval(3 * 86_400 + 9 * 3_600)
        let planInfo: [String: Any] = [
            "planInfo": [
                "planName": "Premium",
                "includedUsagePeriod": 2,
                "includedUsageResetsAt": weekReset.timeIntervalSince1970,
                "billingCycleEnd": monthEnd.timeIntervalSince1970
            ]
        ]
        let weeklyDashboard: [String: Any] = [
            "billingCycleStart": monthStart.timeIntervalSince1970,
            "billingCycleEnd": monthEnd.timeIntervalSince1970,
            "planUsage": [
                "autoPercentUsed": 24.5,
                "apiPercentUsed": 38.0
            ]
        ]

        let weekly = parseFixture(dashboard: weeklyDashboard, planInfo: planInfo)
        expect(weekly?.primary.remaining == 75.5, "weekly primary quota keeps the included model percentage")
        expect(weekly?.primary.resetAt == weekReset.timeIntervalSince1970,
               "weekly reset uses includedUsageResetsAt ahead of the monthly billing end")
        expect(weekly?.primary.windowMinutes == 10_080, "weekly plan uses a seven-day window")
        expect(QuotaPresentation.cardBadge(window: weekly?.primary, secondary: false) == "周限额",
               "seven-day primary card is labeled as a weekly quota")
        expect(QuotaPresentation.cardBadge(secondary: true) == "额外额度池",
               "secondary card is labeled as the extra credit pool")

        var enumStringPlanInfo = planInfo
        enumStringPlanInfo["planInfo"] = [
            "planName": "Premium",
            "includedUsagePeriod": "WEEKLY",
            "includedUsageResetsAt": weekReset.timeIntervalSince1970,
            "billingCycleEnd": monthEnd.timeIntervalSince1970
        ]
        let weeklyStringEnum = parseFixture(dashboard: weeklyDashboard, planInfo: enumStringPlanInfo)
        expect(weeklyStringEnum?.primary.windowMinutes == 10_080,
               "weekly enum strings are recognized as the seven-day period")

        let grantBalance: [String: Any] = [
            "hasCreditGrants": true,
            "creditBalanceCents": 25,
            "totalCents": 125,
            "usedCents": 100
        ]
        let competingPrepaid: [String: Any] = [
            "availableCents": 900,
            "fundedCents": 2_000,
            "spentCents": 1_100
        ]
        let grantSnapshot = parseFixture(
            dashboard: weeklyDashboard,
            planInfo: planInfo,
            creditBalance: grantBalance,
            prepaidBilling: competingPrepaid
        )
        expect(grantSnapshot?.secondary.remainingAmountCents == 25,
               "credit grant balance is read directly in cents")
        expect(grantSnapshot?.secondary.limitAmountCents == 125,
               "credit grant total is read directly in cents")
        expect(grantSnapshot?.secondary.currencyCode == "USD", "credit pool uses USD currency formatting")
        expect(QuotaPresentation.value(grantSnapshot?.secondary, secondary: true) == "$0.25 / $1.25",
               "25 cents formats as $0.25 without percentage conversion")
        expect(grantSnapshot?.secondary.remaining == 20,
               "credit grant remaining percentage is computed from the actual cent amounts")

        let noGrantBalance: [String: Any] = [
            "hasCreditGrants": false,
            "creditBalanceCents": 0,
            "totalCents": 0,
            "usedCents": 0
        ]
        let prepaidSnapshot = parseFixture(
            dashboard: weeklyDashboard,
            planInfo: planInfo,
            creditBalance: noGrantBalance,
            prepaidBilling: [
                "eligible": true,
                "availableCents": 450,
                "fundedCents": 1_000,
                "spentCents": 550
            ]
        )
        expect(prepaidSnapshot?.secondary.remainingAmountCents == 450,
               "prepaid available cents are used when no credit grant is active")
        expect(prepaidSnapshot?.secondary.limitAmountCents == 1_000,
               "prepaid funded cents supply the displayed pool total")
        expect(QuotaPresentation.value(prepaidSnapshot?.secondary, secondary: true) == "$4.50 / $10.00",
               "prepaid cents format as balance over funded amount")

        let zeroSnapshot = parseFixture(
            dashboard: weeklyDashboard,
            planInfo: planInfo,
            creditBalance: [
                "hasCreditGrants": true,
                "creditBalanceCents": 0,
                "totalCents": 0,
                "usedCents": 0
            ]
        )
        expect(zeroSnapshot?.secondary.remainingAmountCents == 0
               && zeroSnapshot?.secondary.limitAmountCents == 0,
               "zero balance and zero pool remain valid amount values")
        expect(QuotaPresentation.value(zeroSnapshot?.secondary, secondary: true) == "$0.00 / $0.00",
               "zero cents format as zero instead of missing data")

        let unknownAmountSnapshot = parseFixture(dashboard: weeklyDashboard, planInfo: planInfo)
        expect(unknownAmountSnapshot?.secondary.remainingAmountCents == nil
               && unknownAmountSnapshot?.secondary.limitAmountCents == nil,
               "weekly api percent does not fabricate monetary amounts")
        expect(QuotaPresentation.value(unknownAmountSnapshot?.secondary, secondary: true) == "— / —",
               "unknown pool amounts display dashes")
        expect(unknownAmountSnapshot?.secondary.resetAt == nil
               && unknownAmountSnapshot?.secondary.windowMinutes == nil,
               "extra pool remains independent of the weekly reset")

        let monthlyPlanInfo: [String: Any] = [
            "planInfo": [
                "planName": "Pro",
                "includedUsagePeriod": 1,
                "billingCycleEnd": monthEnd.timeIntervalSince1970
            ]
        ]
        let monthly = parseFixture(
            dashboard: [
                "billingCycleStart": monthStart.timeIntervalSince1970,
                "billingCycleEnd": monthEnd.timeIntervalSince1970,
                "planUsage": [
                    "autoPercentUsed": 10,
                    "apiPercentUsed": 30,
                    "apiSpend": 350,
                    "apiLimit": 1_000
                ]
            ],
            planInfo: monthlyPlanInfo
        )
        expect(monthly?.primary.resetAt == monthEnd.timeIntervalSince1970,
               "legacy monthly primary reset continues to use billingCycleEnd")
        expect(monthly?.primary.windowMinutes == 30 * 24 * 60,
               "legacy monthly primary keeps its monthly window")
        expect(QuotaPresentation.cardBadge(window: monthly?.primary, secondary: false) == "月限额",
               "legacy monthly primary card is labeled as a monthly quota")
        expect(monthly?.secondary.remainingAmountCents == 650,
               "legacy apiLimit minus apiSpend remains a direct monetary fallback")
        expect(monthly?.secondary.limitAmountCents == 1_000,
               "legacy apiLimit is preserved as the amount pool total")
        expect(QuotaPresentation.value(monthly?.secondary, secondary: true) == "$6.50 / $10.00",
               "legacy apiSpend/apiLimit fields display as dollars")
    }

    private static func testQuotaSnapshotEncoding() {
        let cacheURL = SnapshotCache.url()
        let before = cacheFingerprint()
        let sampledAt = Date(timeIntervalSince1970: 1_800_000_000)
        let primaryReset = sampledAt.addingTimeInterval(3 * 86_400).timeIntervalSince1970
        let amountOnly = QuotaSnapshot(
            planType: "premium",
            primary: QuotaWindow(
                remaining: nil, used: nil, resetAt: primaryReset, windowMinutes: 10_080,
                badge: "内置", title: "周限额", detail: nil
            ),
            secondary: QuotaWindow(
                remaining: nil, used: nil, resetAt: nil, windowMinutes: nil,
                badge: "额外", title: "额外额度池", detail: nil,
                remainingAmountCents: 0, limitAmountCents: 0, currencyCode: "USD"
            ),
            sampledAt: sampledAt,
            sourceName: "cursor-api"
        )

        guard let encoded = SnapshotCache.encode(amountOnly) else {
            expect(false, "amount-only cache fixture encodes without a filesystem write")
            return
        }
        guard let decoded = SnapshotCache.decode(encoded) else {
            expect(false, "amount-only cache fixture decodes without a filesystem read")
            return
        }
        expect(decoded.primary.remaining == nil && decoded.secondary.remaining == nil,
               "amount-only cache retains missing percentage values")
        expect(decoded.secondary.remainingAmountCents == 0 && decoded.secondary.limitAmountCents == 0,
               "amount-only cache preserves zero balance and zero total")
        expect(decoded.secondary.resetAt == nil && decoded.secondary.windowMinutes == nil,
               "explicitly null secondary reset fields stay independent of the weekly primary")
        expect(decoded.primary.resetAt == primaryReset && decoded.primary.windowMinutes == 10_080,
               "weekly primary reset survives a pure encode/decode round trip")
        expect(cacheFingerprint() == before && !FileManager.default.fileExists(atPath: cacheURL.path) == (before == "absent"),
               "pure cache codec does not create or modify the real snapshot cache")

        let oldCacheJSON = """
        {
          "planType": "pro",
          "totalRemain": 70,
          "totalUsed": 30,
          "resetAt": 1801000000,
          "windowMinutes": 43200,
          "secondaryRemain": 55,
          "secondaryUsed": 45,
          "sampledAt": "2027-01-15T08:00:00Z"
        }
        """
        let oldCache = SnapshotCache.decode(Data(oldCacheJSON.utf8))
        expect(oldCache?.secondary.resetAt == oldCache?.primary.resetAt
               && oldCache?.secondary.windowMinutes == oldCache?.primary.windowMinutes,
               "legacy snapshots without independent secondary fields inherit the shared reset")
        expect(oldCache?.secondary.remainingAmountCents == nil
               && oldCache?.secondary.limitAmountCents == nil,
               "legacy snapshots decode without new amount fields")
    }

    private static func parseFixture(
        dashboard: [String: Any]?,
        planInfo: [String: Any]?,
        creditBalance: [String: Any]? = nil,
        prepaidBilling: [String: Any]? = nil
    ) -> QuotaSnapshot? {
        QuotaParser.snapshot(
            dashboard: dashboard,
            planInfo: planInfo,
            summary: nil,
            authUsage: nil,
            stripe: nil,
            localPlan: nil,
            sampledAt: Date(timeIntervalSince1970: 1_800_000_000),
            sourceName: "fixture",
            creditBalance: creditBalance,
            prepaidBilling: prepaidBilling
        )
    }

    @MainActor
    private static func testStatusViewHitTestingAndCallbacks() {
        let parent = NSView(frame: NSRect(x: 0, y: 0, width: 100, height: 22))
        let view = QuotaStatusView(frame: parent.bounds)
        parent.addSubview(view)
        view.update(snapshot: makeSnapshot(
            primary: 76,
            secondary: 45,
            sampledAt: Date(timeIntervalSince1970: 1_800_000_000)
        ))
        view.layoutSubtreeIfNeeded()

        for (name, point) in [
            ("icon", NSPoint(x: 10, y: 5)),
            ("primary label", NSPoint(x: 40, y: 5)),
            ("secondary label", NSPoint(x: 40, y: 16)),
            ("blank area", NSPoint(x: 24, y: 5))
        ] {
            expect(view.hitTest(point) === view, "status view receives hit tests over the \(name)")
        }

        var leftDownCount = 0
        var leftUpCount = 0
        var rightDownCount = 0
        view.onLeftClick = { leftDownCount += 1 }
        view.onLeftMouseUp = { leftUpCount += 1 }
        view.onRightClick = { rightDownCount += 1 }

        expect(view.accessibilityPerformPress(), "status view accepts an accessibility press")
        expect(leftDownCount == 1, "accessibility press invokes the toggle callback exactly once")
        leftDownCount = 0

        guard
            let leftDown = NSEvent.mouseEvent(with: .leftMouseDown, location: NSPoint(x: 40, y: 5),
                                              modifierFlags: [], timestamp: 1, windowNumber: 0,
                                              context: nil, eventNumber: 1, clickCount: 1, pressure: 1),
            let leftUp = NSEvent.mouseEvent(with: .leftMouseUp, location: NSPoint(x: 40, y: 5),
                                            modifierFlags: [], timestamp: 1.01, windowNumber: 0,
                                            context: nil, eventNumber: 2, clickCount: 1, pressure: 0),
            let rightDown = NSEvent.mouseEvent(with: .rightMouseDown, location: NSPoint(x: 40, y: 5),
                                               modifierFlags: [], timestamp: 2, windowNumber: 0,
                                               context: nil, eventNumber: 3, clickCount: 1, pressure: 1)
        else {
            fatalError("Could not create synthetic status-item mouse events")
        }

        view.mouseDown(with: leftDown)
        expect(leftDownCount == 1, "left mouse down invokes the toggle callback exactly once")
        view.mouseUp(with: leftUp)
        expect(leftUpCount == 1, "left mouse up invokes the activation callback exactly once")
        expect(leftDownCount == 1, "left mouse up does not repeat the toggle callback")
        view.rightMouseDown(with: rightDown)
        expect(rightDownCount == 1, "right mouse down invokes the menu callback exactly once")
        expect(leftDownCount == 1 && leftUpCount == 1, "right mouse down does not invoke left-click callbacks")
    }

    @MainActor
    private static func testPreviewScenariosAndCacheIsolation() {
        let expectedCases = Set(["normal", "warning", "critical", "empty", "snapshot", "error"])
        expect(Set(PreviewScenario.allCases.map(\.rawValue)) == expectedCases, "all six documented preview scenarios exist")

        let fixedDate = Date(timeIntervalSince1970: 1_800_000_000)
        for scenario in PreviewScenario.allCases {
            let sample = scenario.sample(at: fixedDate)
            if scenario == .empty {
                expect(sample == nil, "empty preview has no snapshot")
            } else if let sample {
                expect(isBounded(sample.primary.remaining), "\(scenario.rawValue) primary remaining is within 0...100")
                expect(isBounded(sample.secondary.remaining), "\(scenario.rawValue) secondary remaining is within 0...100")
                expect(sample.sampledAt <= fixedDate, "\(scenario.rawValue) sample timestamp is not in the future")
            } else {
                expect(false, "\(scenario.rawValue) preview supplies a sample")
            }

            let before = cacheFingerprint()
            let model = QuotaModel(uiSeconds: 3_600, fetchSeconds: 3_600, previewScenario: scenario)
            model.start()
            expect(cacheFingerprint() == before, "\(scenario.rawValue) preview start leaves snapshot cache unchanged")
            model.refresh(forceNetwork: true)
            expect(cacheFingerprint() == before, "\(scenario.rawValue) preview refresh leaves snapshot cache unchanged")

            if scenario == .empty {
                expect(model.snapshot == nil && model.fallbackSnapshot == nil, "empty preview model remains empty")
                expect(model.footer.contains("离线预览"), "empty preview footer identifies offline preview")
            } else if scenario == .snapshot || scenario == .error {
                expect(model.snapshot == nil && model.fallbackSnapshot != nil, "\(scenario.rawValue) uses fallback snapshot state")
                expect(model.fallbackSnapshot?.sourceName == "cache", "\(scenario.rawValue) preview is marked as cached")
            } else {
                expect(model.snapshot != nil && model.fallbackSnapshot == nil, "\(scenario.rawValue) uses current snapshot state")
                expect(model.snapshot?.sourceName == "cursor-api", "\(scenario.rawValue) preview is marked current")
            }
            expect((scenario == .error) == !model.connectionError.isEmpty,
                   "\(scenario.rawValue) connection error state matches the scenario")
            model.stop()
        }
    }

    @MainActor
    private static func testHostingSizes() {
        let normalSize = hostingSize(for: .normal)
        let errorSize = hostingSize(for: .error)
        for (name, size) in [("normal", normalSize), ("error", errorSize)] {
            expect(size.width > 0 && size.height > 0, "\(name) detail hosting fitting size is nonzero")
            expect(size.width <= 500, "\(name) detail hosting width is reasonable")
            expect(size.height >= 200 && size.height <= 500, "\(name) detail hosting height is reasonable")
        }
        expect(errorSize.height + 1 >= normalSize.height, "error footer does not collapse the detail height")
    }

    @MainActor
    private static func hostingSize(for scenario: PreviewScenario) -> NSSize {
        let model = QuotaModel(uiSeconds: 3_600, fetchSeconds: 3_600, previewScenario: scenario)
        model.start()
        let hosting = NSHostingView(rootView: QuotaView(model: model, refresh: {}, dismiss: {}))
        hosting.layoutSubtreeIfNeeded()
        let size = hosting.fittingSize
        model.stop()
        return size
    }

    @MainActor
    private static func testBrandImages() {
        for size in [16, 32] {
            let mark = CursorBrand.markImage(size: CGFloat(size), template: true)
            expect(mark.size == NSSize(width: size, height: size), "\(size)px menu mark has the requested dimensions")
            expect(mark.isTemplate, "\(size)px monochrome mark is a template image")
            let markBitmap = rasterize(mark, size: size)
            expect(alpha(markBitmap, x: 0, y: 0) <= 0.01, "\(size)px mark preserves transparent outer pixels")
            expect(maximumAlpha(markBitmap) > 0.9, "\(size)px mark has opaque content")

            let appIcon = CursorBrand.appIconImage(size: CGFloat(size))
            expect(appIcon.size == NSSize(width: size, height: size), "\(size)px app icon has the requested dimensions")
            expect(!appIcon.isTemplate, "\(size)px app icon remains a full-color image")
            let iconBitmap = rasterize(appIcon, size: size)
            expect(alpha(iconBitmap, x: 0, y: 0) <= 0.01, "\(size)px app icon preserves transparent corner pixels")
            expect(maximumAlpha(iconBitmap) > 0.9, "\(size)px app icon has opaque content")
        }
    }

    @MainActor
    private static func renderStatusViewPreviewsIfRequested() {
        guard let directoryPath = ProcessInfo.processInfo.environment["CURSOR_UI_PREVIEW_DIR"],
              !directoryPath.isEmpty else { return }

        do {
            let directory = URL(fileURLWithPath: directoryPath, isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let now = Date(timeIntervalSince1970: 1_800_000_000)
            let appearances: [(String, NSAppearance.Name)] = [
                ("light", .aqua),
                ("dark", .darkAqua)
            ]

            for (name, appearanceName) in appearances {
                guard let appearance = NSAppearance(named: appearanceName) else {
                    fatalError("Could not create the \(name) appearance for offscreen rendering")
                }

                let view = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 100, height: 22))
                view.appearance = appearance
                view.update(snapshot: makeSnapshot(primary: 76, secondary: 45, sampledAt: now))
                let width = view.preferredWidth()
                view.setFrameSize(NSSize(width: width, height: 22))
                view.layoutSubtreeIfNeeded()

                let pixelsWide = Int(ceil(width * 2))
                guard let bitmap = NSBitmapImageRep(
                    bitmapDataPlanes: nil,
                    pixelsWide: pixelsWide,
                    pixelsHigh: 44,
                    bitsPerSample: 8,
                    samplesPerPixel: 4,
                    hasAlpha: true,
                    isPlanar: false,
                    colorSpaceName: .deviceRGB,
                    bytesPerRow: 0,
                    bitsPerPixel: 0
                ) else {
                    fatalError("Could not allocate a 2x \(name) status-view image")
                }
                bitmap.size = NSSize(width: width, height: 22)

                guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
                    fatalError("Could not create a 2x \(name) status-view context")
                }
                appearance.performAsCurrentDrawingAppearance {
                    NSGraphicsContext.saveGraphicsState()
                    NSGraphicsContext.current = context
                    context.imageInterpolation = .high
                    NSColor.windowBackgroundColor.setFill()
                    view.bounds.fill()
                    view.displayIgnoringOpacity(view.bounds, in: context)
                    context.flushGraphics()
                    NSGraphicsContext.restoreGraphicsState()
                }

                let output = directory.appendingPathComponent("menubar-render-\(name).png")
                guard let data = bitmap.representation(using: .png, properties: [:]) else {
                    fatalError("Could not encode the \(name) status-view PNG")
                }
                try data.write(to: output, options: .atomic)
                print("Offscreen QuotaStatusView render (not a system menu bar screenshot): \(output.path)")
            }
        } catch {
            fatalError("Could not write optional status-view previews: \(error)")
        }
    }

    private static func makeSnapshot(
        primary: Double?,
        secondary: Double?,
        sampledAt: Date,
        primaryBadge: String = "内置",
        primaryTitle: String = "内置模型剩余",
        secondaryTitle: String = "其他模型剩余"
    ) -> QuotaSnapshot {
        let resetAt = sampledAt.addingTimeInterval(8 * 86_400).timeIntervalSince1970
        return QuotaSnapshot(
            planType: "pro",
            primary: QuotaWindow(remaining: primary, used: primary.map { 100 - $0 }, resetAt: resetAt,
                                 windowMinutes: 43_200, badge: primaryBadge, title: primaryTitle, detail: nil),
            secondary: QuotaWindow(remaining: secondary, used: secondary.map { 100 - $0 }, resetAt: resetAt,
                                   windowMinutes: 43_200, badge: "其他", title: secondaryTitle, detail: nil),
            sampledAt: sampledAt,
            sourceName: "cursor-api"
        )
    }

    private static func isBounded(_ value: Double?) -> Bool {
        guard let value, value.isFinite else { return false }
        return (0...100).contains(value)
    }

    private static func cacheFingerprint() -> String {
        let file = SnapshotCache.url()
        guard FileManager.default.fileExists(atPath: file.path) else { return "absent" }
        do {
            let data = try Data(contentsOf: file)
            let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            return "sha256:\(digest)"
        } catch {
            fatalError("Could not read snapshot cache for a read-only fingerprint: \(error)")
        }
    }

    private static func rasterize(_ image: NSImage, size: Int) -> NSBitmapImageRep {
        guard let bitmap = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: size,
            pixelsHigh: size,
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: size * 4,
            bitsPerPixel: 32
        ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
            fatalError("Could not allocate a \(size)px bitmap for image verification")
        }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        context.imageInterpolation = .none
        image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
        context.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        return bitmap
    }

    private static func alpha(_ bitmap: NSBitmapImageRep, x: Int, y: Int) -> CGFloat {
        guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB) else { return -1 }
        return color.alphaComponent
    }

    private static func maximumAlpha(_ bitmap: NSBitmapImageRep) -> CGFloat {
        var result: CGFloat = 0
        for y in 0..<bitmap.pixelsHigh {
            for x in 0..<bitmap.pixelsWide {
                result = max(result, alpha(bitmap, x: x, y: y))
            }
        }
        return result
    }

    private static func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
        assertions += 1
        guard condition() else {
            fputs("FAIL: \(message)\n", stderr)
            Foundation.exit(1)
        }
    }
}
