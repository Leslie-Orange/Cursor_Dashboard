import AppKit
import Combine
import Foundation
import SQLite3
import SwiftUI

struct QuotaWindow {
    let remaining: Double?
    let used: Double?
    let resetAt: TimeInterval?
    let windowMinutes: Double?
    let badge: String
    let title: String
    let detail: String?
    let remainingAmountCents: Double?
    let limitAmountCents: Double?
    let currencyCode: String?
    let schedule: String?

    init(remaining: Double?, used: Double?, resetAt: TimeInterval?, windowMinutes: Double?,
         badge: String, title: String, detail: String?, remainingAmountCents: Double? = nil,
         limitAmountCents: Double? = nil, currencyCode: String? = nil,
         schedule: String? = nil) {
        self.remaining = remaining
        self.used = used
        self.resetAt = resetAt
        self.windowMinutes = windowMinutes
        self.badge = badge
        self.title = title
        self.detail = detail
        self.remainingAmountCents = remainingAmountCents
        self.limitAmountCents = limitAmountCents
        self.currencyCode = currencyCode
        self.schedule = schedule
    }
}

struct QuotaSnapshot {
    let planType: String?
    let planNote: String?
    let primary: QuotaWindow
    let secondary: QuotaWindow
    let sampledAt: Date
    let sourceName: String

    init(planType: String?, planNote: String? = nil, primary: QuotaWindow,
         secondary: QuotaWindow, sampledAt: Date, sourceName: String) {
        self.planType = planType
        self.planNote = planNote
        self.primary = primary
        self.secondary = secondary
        self.sampledAt = sampledAt
        self.sourceName = sourceName
    }
}

enum JSONValue {
    static func number(_ value: Any?) -> Double? {
        if let number = value as? NSNumber {
            return number.doubleValue
        }
        if let string = value as? String {
            return Double(string)
        }
        return nil
    }

    static func string(_ value: Any?) -> String? {
        if let string = value as? String, !string.isEmpty {
            return string
        }
        return nil
    }

    static func dictionary(_ value: Any?) -> [String: Any]? {
        value as? [String: Any]
    }

    static func value(_ dictionary: [String: Any]?, names: [String]) -> Any? {
        guard let dictionary else { return nil }
        for name in names {
            if let value = dictionary[name], !(value is NSNull) {
                return value
            }
        }
        return nil
    }

    static func nested(_ root: [String: Any]?, path: [String]) -> Any? {
        var current: Any? = root
        for key in path {
            current = dictionary(current)?[key]
        }
        return current
    }

    static func percent(_ value: Double?) -> Double? {
        guard let value, value.isFinite else { return nil }
        let scaled = value <= 1.0000001 ? value * 100 : value
        return min(100, max(0, scaled))
    }

    static func date(_ value: Any?) -> Date? {
        if let number = number(value) {
            let interval = number > 10_000_000_000 ? number / 1000 : number
            if interval > 1_000_000 {
                return Date(timeIntervalSince1970: interval)
            }
        }
        guard let string = string(value) else { return nil }
        if let number = Double(string) {
            return date(number)
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = formatter.date(from: string) {
            return date
        }
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: string)
    }
}

enum PlanCatalog {
    enum Model {
        case unknown
        case weekly
        case legacyDual
        case legacyCursor
        case pooled
    }

    static func key(_ planType: String?) -> String {
        (planType ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()
            .replacingOccurrences(of: "_", with: "")
            .replacingOccurrences(of: "-", with: "")
            .replacingOccurrences(of: " ", with: "")
    }

    static func model(_ planType: String?, legacyFlag: Bool) -> Model {
        let key = key(planType)
        if key.isEmpty { return .unknown }
        if key == "hobby" || key == "start" || key == "cursorstart" { return .legacyCursor }
        if isLegacyDualName(key) { return .legacyDual }
        if key == "enterprise" { return .pooled }
        if key == "business" { return .legacyDual }
        if key == "ultra" || key == "team" || key == "teams" {
            return legacyFlag ? .legacyDual : .weekly
        }
        if isWeeklyName(key) { return .weekly }
        return .unknown
    }

    static func display(_ planType: String?) -> String? {
        switch key(planType) {
        case "free", "freetrial": return "Free"
        case "plus": return "Plus"
        case "premium": return "Premium"
        case "super": return "Super"
        case "ultra", "ultralegacy", "legacyultra": return "Ultra"
        case "hobby": return "Hobby"
        case "start", "cursorstart": return "Start"
        case "pro": return "Pro"
        case "proplus", "pro+": return "Pro+"
        case "business": return "Business"
        case "enterprise": return "Enterprise"
        case "team", "teams": return "Teams"
        case "teamsstandard", "teamstandard": return "Teams Standard"
        case "teamspremium", "teampremium": return "Teams Premium"
        default: return nil
        }
    }

    static func note(_ planType: String?, model: Model) -> String? {
        let key = key(planType)
        if model == .unknown { return nil }
        if key == "plus" { return "基础 · 每周" }
        if key == "premium" { return "4× Plus · 每周" }
        if key == "super" { return "15× Plus · 每周" }
        if key == "ultra" && model == .weekly { return "50× Plus · 每周" }
        if key == "free" || key == "freetrial" || key == "hobby" { return "有限" }
        if key == "start" || key == "cursorstart" { return "仅内置 · 每月" }
        if key == "teamspremium" || key == "teampremium" { return "5× Standard · 每月" }
        if key == "team" || key == "teams" { return model == .weekly ? "按席位 · 每周" : "月度双池" }
        if key == "enterprise" { return "共享池" }
        if model == .legacyDual || model == .legacyCursor {
            return model == .legacyCursor ? "仅内置 · 每月" : "月度双池"
        }
        return nil
    }

    private static func isLegacyDualName(_ key: String) -> Bool {
        ["pro", "proplus", "pro+", "teamsstandard", "teamstandard", "teamspremium", "teampremium", "ultralegacy", "legacyultra"].contains(key)
    }

    private static func isWeeklyName(_ key: String) -> Bool {
        ["free", "freetrial", "plus", "premium", "super", "ultra", "team", "teams"].contains(key)
    }
}

enum QuotaParser {
    static func snapshot(
        dashboard: [String: Any]?,
        planInfo: [String: Any]?,
        summary: [String: Any]?,
        authUsage: [String: Any]?,
        stripe: [String: Any]?,
        localPlan: String?,
        sampledAt: Date,
        sourceName: String,
        creditBalance: [String: Any]? = nil,
        prepaidBilling: [String: Any]? = nil
    ) -> QuotaSnapshot? {
        let dashboardPlan = JSONValue.dictionary(JSONValue.value(dashboard, names: ["planUsage"]))
        let planUsage = dashboardPlan
            ?? JSONValue.dictionary(JSONValue.nested(summary, path: ["individualUsage", "plan"]))
        let summaryPlan = JSONValue.dictionary(JSONValue.nested(summary, path: ["individualUsage", "plan"]))

        let autoUsedPercent = firstPoints([
            JSONValue.value(planUsage, names: ["autoPercentUsed"]),
            JSONValue.value(summaryPlan, names: ["autoPercentUsed"]),
            JSONValue.value(dashboard, names: ["autoModelSelectedDisplayMessage"]),
            JSONValue.value(summary, names: ["autoModelSelectedDisplayMessage"])
        ])
        let otherUsedPercent = firstPoints([
            JSONValue.value(planUsage, names: ["apiPercentUsed"]),
            JSONValue.value(summaryPlan, names: ["apiPercentUsed"]),
            JSONValue.value(dashboard, names: ["namedModelSelectedDisplayMessage"]),
            JSONValue.value(summary, names: ["namedModelSelectedDisplayMessage"])
        ])
        let totalUsedPercent = firstPoints([
            JSONValue.value(planUsage, names: ["totalPercentUsed"]),
            JSONValue.value(summaryPlan, names: ["totalPercentUsed"])
        ])

        let centsLimit = JSONValue.number(JSONValue.value(planUsage, names: ["limit"]))
            ?? JSONValue.number(JSONValue.nested(planInfo, path: ["planInfo", "includedAmountCents"]))
            ?? JSONValue.number(JSONValue.value(planInfo, names: ["includedAmountCents"]))
            ?? JSONValue.number(JSONValue.value(summaryPlan, names: ["limit"]))
        let includedSpend = JSONValue.number(JSONValue.value(planUsage, names: ["includedSpend"]))
            ?? JSONValue.number(JSONValue.value(summaryPlan, names: ["used"]))
        let centsRemaining = JSONValue.number(JSONValue.value(planUsage, names: ["remaining"]))
            ?? JSONValue.number(JSONValue.value(summaryPlan, names: ["remaining"]))
            ?? combinedRemaining(used: includedSpend, limit: centsLimit)

        let allowance: (used: Double, remaining: Double)?
        if let centsLimit, centsLimit > 0, let includedSpend {
            let used = clampPercent(includedSpend / centsLimit * 100)
            allowance = (used, clampPercent(100 - used))
        } else if let centsLimit, centsLimit > 0, let centsRemaining {
            let remaining = clampPercent(centsRemaining / centsLimit * 100)
            allowance = (clampPercent(100 - remaining), remaining)
        } else if let messageUsed = firstPoints([
            JSONValue.value(dashboard, names: ["displayMessage"]),
            JSONValue.value(summary, names: ["displayMessage"])
        ]) {
            allowance = (messageUsed, clampPercent(100 - messageUsed))
        } else {
            allowance = nil
        }

        let builtinUsedPercent = autoUsedPercent ?? totalUsedPercent
        var builtinRemaining = builtinUsedPercent.map { clampPercent(100 - $0) }

        let requestBucket = requestBucket(from: authUsage) ?? requestBucket(from: summaryPlan)
        if builtinRemaining == nil, let bucket = requestBucket, bucket.limit > 0 {
            builtinRemaining = min(100, max(0, (bucket.limit - bucket.used) / bucket.limit * 100))
        }

        let cycleStart = firstDate([
            JSONValue.value(dashboard, names: ["billingCycleStart"]),
            JSONValue.value(summary, names: ["billingCycleStart"]),
            JSONValue.value(authUsage, names: ["startOfMonth"])
        ])
        var cycleEnd = firstDate([
            JSONValue.value(dashboard, names: ["billingCycleEnd"]),
            JSONValue.value(summary, names: ["billingCycleEnd"]),
            JSONValue.nested(planInfo, path: ["planInfo", "billingCycleEnd"]),
            JSONValue.value(planInfo, names: ["billingCycleEnd"])
        ])
        if cycleEnd == nil, let cycleStart {
            cycleEnd = cycleStart.addingTimeInterval(30 * 24 * 60 * 60)
        }
        let plan = JSONValue.dictionary(planInfo?["planInfo"]) ?? planInfo
        let periodValue = plan?["includedUsagePeriod"]
        let periodName = JSONValue.string(periodValue)?.uppercased() ?? ""
        let isWeekly = JSONValue.number(periodValue) == 2 || periodName.contains("WEEKLY")
        let includedReset = JSONValue.date(plan?["includedUsageResetsAt"])
        let resetAt = isWeekly ? includedReset?.timeIntervalSince1970
            : (includedReset ?? cycleEnd)?.timeIntervalSince1970
        let windowMinutes: Double?
        if isWeekly {
            windowMinutes = 7 * 24 * 60
        } else if let cycleStart, let cycleEnd {
            windowMinutes = max(1, cycleEnd.timeIntervalSince(cycleStart) / 60)
        } else {
            windowMinutes = 30 * 24 * 60
        }

        let planType = JSONValue.string(JSONValue.nested(planInfo, path: ["planInfo", "planName"]))
            ?? JSONValue.string(JSONValue.value(planInfo, names: ["planName", "planType"]))
            ?? JSONValue.string(JSONValue.value(summary, names: ["membershipType"]))
            ?? JSONValue.string(JSONValue.value(stripe, names: ["membershipType", "individualMembershipType"]))
            ?? localPlan

        let onDemand = JSONValue.dictionary(JSONValue.nested(summary, path: ["individualUsage", "onDemand"]))
        let onDemandEnabled = onDemand?["enabled"] as? Bool ?? false
        let onDemandUsed = JSONValue.number(JSONValue.value(onDemand, names: ["used"]))

        let amounts = extraAmounts(creditBalance: creditBalance, prepaidBilling: prepaidBilling)
        // The website summary can still describe a legacy billing month for weekly plans.
        // Keep the current-period percentage separate so a stale monthly amount cannot override it.
        let weeklyUsedPercent = firstPoints([
            dashboardPlan?["totalPercentUsed"]
        ])
        let quotaIsAllowance = !isWeekly && allowance != nil
        let primaryRemaining = isWeekly
            ? weeklyUsedPercent.map { clampPercent(100 - $0) }
            : allowance?.remaining ?? builtinRemaining
        guard primaryRemaining != nil || otherUsedPercent != nil || requestBucket != nil || amounts != nil else {
            return nil
        }

        let totalDetail = quotaIsAllowance
            ? amountDetail(
                used: includedSpend,
                remaining: centsRemaining,
                limit: centsLimit,
                request: nil,
                onDemandEnabled: onDemandEnabled,
                onDemandUsed: onDemandUsed
            )
            : amountDetail(
                used: includedSpend,
                remaining: centsRemaining,
                limit: centsLimit,
                request: requestBucket,
                onDemandEnabled: onDemandEnabled,
                onDemandUsed: onDemandUsed
            )

        let primary = QuotaWindow(
            remaining: primaryRemaining,
            used: isWeekly ? weeklyUsedPercent
                : allowance?.used ?? builtinUsedPercent ?? builtinRemaining.map { 100 - $0 },
            resetAt: resetAt,
            windowMinutes: windowMinutes,
            badge: isWeekly ? "内置" : (quotaIsAllowance ? "额度" : "内置"),
            title: isWeekly ? "周限额" : (quotaIsAllowance ? "套餐剩余" : "内置模型剩余"),
            detail: isWeekly ? (primaryRemaining.map { percentDetail(used: 100 - $0, label: "已用") }) : totalDetail
        )

        let secondary: QuotaWindow
        if let amounts {
            let remainingPercent = amounts.total.flatMap { total in
                guard total > 0, let remaining = amounts.remaining else { return nil as Double? }
                return clampPercent(remaining / total * 100)
            }
            secondary = QuotaWindow(
                remaining: remainingPercent,
                used: remainingPercent.map { 100 - $0 },
                resetAt: nil, windowMinutes: nil,
                badge: "其他", title: "额外额度池",
                detail: amounts.used.map { String(format: "已用 $%.2f", $0 / 100) },
                remainingAmountCents: amounts.remaining,
                limitAmountCents: amounts.total, currencyCode: "USD"
            )
        } else if isWeekly {
            secondary = QuotaWindow(
                remaining: nil, used: nil, resetAt: nil, windowMinutes: nil,
                badge: "其他", title: "额外额度池", detail: "金额暂不可用"
            )
        } else if let otherUsedPercent {
            secondary = QuotaWindow(
                remaining: min(100, max(0, 100 - otherUsedPercent)),
                used: otherUsedPercent,
                resetAt: resetAt,
                windowMinutes: windowMinutes,
                badge: "其他",
                title: "其他模型剩余",
                detail: percentDetail(used: otherUsedPercent, label: "已用"),
                remainingAmountCents: legacyOtherRemaining(planUsage, summaryPlan),
                limitAmountCents: JSONValue.number(planUsage?["apiLimit"]) ?? JSONValue.number(summaryPlan?["apiLimit"]),
                currencyCode: "USD"
            )
        } else if let bucket = requestBucket, bucket.limit > 0 {
            let remaining = min(100, max(0, (bucket.limit - bucket.used) / bucket.limit * 100))
            secondary = QuotaWindow(
                remaining: remaining,
                used: 100 - remaining,
                resetAt: resetAt,
                windowMinutes: windowMinutes,
                badge: "其他",
                title: "其他模型剩余",
                detail: String(format: "%.0f / %.0f 次", bucket.used, bucket.limit)
            )
        } else {
            secondary = QuotaWindow(
                remaining: nil,
                used: nil,
                resetAt: resetAt,
                windowMinutes: windowMinutes,
                badge: "其他",
                title: "其他模型剩余",
                detail: nil
            )
        }

        return QuotaSnapshot(
            planType: planType,
            planNote: PlanCatalog.note(planType, model: PlanCatalog.model(
                planType, legacyFlag: legacyFlag(summary: summary, planInfo: planInfo, stripe: stripe)
            )),
            primary: primary,
            secondary: secondary,
            sampledAt: sampledAt,
            sourceName: sourceName
        )
    }

    private static func extraAmounts(
        creditBalance: [String: Any]?, prepaidBilling: [String: Any]?
    ) -> (remaining: Double?, total: Double?, used: Double?)? {
        func amount(_ object: [String: Any]?, _ key: String) -> Double? {
            guard let value = JSONValue.number(object?[key]), value.isFinite else { return nil }
            return value
        }
        let grantTotal = amount(creditBalance, "totalCents")
        let grantRemaining = amount(creditBalance, "creditBalanceCents")
        let hasGrants = creditBalance?["hasCreditGrants"] as? Bool == true
            || (grantTotal ?? 0) > 0
        if hasGrants, grantRemaining != nil || grantTotal != nil {
            return (grantRemaining, grantTotal, amount(creditBalance, "usedCents"))
        }
        let available = amount(prepaidBilling, "availableCents")
        let funded = amount(prepaidBilling, "fundedCents")
        if available != nil || funded != nil {
            return (available, funded, amount(prepaidBilling, "spentCents"))
        }
        return nil
    }

    private static func legacyOtherRemaining(_ plan: [String: Any]?, _ summary: [String: Any]?) -> Double? {
        let total = JSONValue.number(plan?["apiLimit"]) ?? JSONValue.number(summary?["apiLimit"])
        let spent = JSONValue.number(plan?["apiSpend"]) ?? JSONValue.number(summary?["apiSpend"])
        guard let total, let spent, total.isFinite, spent.isFinite else { return nil }
        return total - spent
    }

    private static func legacyFlag(summary: [String: Any]?, planInfo: [String: Any]?, stripe: [String: Any]?) -> Bool {
        if isTrue(JSONValue.value(summary, names: ["isLegacyPlan", "legacyPlan"])) { return true }
        if isTrue(JSONValue.value(planInfo, names: ["isLegacyPlan", "legacyPlan"])) { return true }
        if isTrue(JSONValue.nested(planInfo, path: ["planInfo", "isLegacyPlan"])) { return true }
        if isTrue(JSONValue.value(stripe, names: ["isLegacyPlan", "legacyPlan"])) { return true }
        let pricing = JSONValue.string(JSONValue.value(summary, names: ["pricingModel", "usageAccounting"]))
            ?? JSONValue.string(JSONValue.value(stripe, names: ["pricingModel", "usageAccounting"]))
        return pricing?.lowercased().contains("legacy") == true
    }

    private static func isTrue(_ value: Any?) -> Bool {
        if let flag = value as? Bool { return flag }
        return JSONValue.string(value)?.lowercased() == "true"
    }

    private static func money(_ cents: Double) -> String {
        let dollars = max(0, cents) / 100
        if abs(dollars - dollars.rounded()) < 0.001 {
            return String(format: "$%.0f", dollars.rounded())
        }
        return String(format: "$%.2f", dollars)
    }

    private static func firstDate(_ values: [Any?]) -> Date? {
        for value in values {
            if let date = JSONValue.date(value) {
                return date
            }
        }
        return nil
    }

    private static func firstPoints(_ values: [Any?]) -> Double? {
        for value in values {
            if let number = JSONValue.number(value) {
                return clampPercent(number)
            }
            if let percent = percentFromMessage(value) {
                return percent
            }
        }
        return nil
    }

    private static func clampPercent(_ value: Double) -> Double {
        guard value.isFinite else { return 0 }
        return min(100, max(0, value))
    }

    private static func percentFromMessage(_ value: Any?) -> Double? {
        guard let string = JSONValue.string(value) else { return nil }
        guard let regex = try? NSRegularExpression(pattern: #"(\d+(?:\.\d+)?)\s*%"#) else { return nil }
        let range = NSRange(string.startIndex..<string.endIndex, in: string)
        guard let match = regex.firstMatch(in: string, range: range),
              let numberRange = Range(match.range(at: 1), in: string),
              let number = Double(string[numberRange]) else {
            return nil
        }
        return clampPercent(number)
    }

    private static func combinedRemaining(used: Double?, limit: Double?) -> Double? {
        guard let used, let limit else { return nil }
        return max(0, limit - used)
    }

    private static func percentDetail(used: Double, label: String) -> String {
        String(format: "%@ %.0f%%", label, used)
    }

    private static func amountDetail(
        used: Double?,
        remaining: Double?,
        limit: Double?,
        request: (name: String, used: Double, limit: Double)?,
        onDemandEnabled: Bool,
        onDemandUsed: Double?
    ) -> String? {
        var parts: [String] = []
        if let limit, limit > 0, looksLikeCents(limit) {
            let usedValue = used ?? (limit - (remaining ?? limit))
            parts.append(money(usedValue) + " / " + money(limit))
        } else if let request, request.limit > 0 {
            parts.append(String(format: "%.0f / %.0f 次", request.used, request.limit))
        }
        if onDemandEnabled, let onDemandUsed, onDemandUsed > 0 {
            let display = looksLikeCents(onDemandUsed)
                ? "按量 " + money(onDemandUsed)
                : String(format: "按量 %.0f", onDemandUsed)
            parts.append(display)
        }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    private static func looksLikeCents(_ value: Double) -> Bool {
        value >= 100
    }

    private static func requestBucket(from root: [String: Any]?) -> (name: String, used: Double, limit: Double)? {
        guard let root else { return nil }
        let preferred = ["gpt-4", "gpt-4o", "default", "composer"]
        var found: [(String, Double, Double)] = []
        walk(root, prefix: "") { key, object in
            let used = JSONValue.number(JSONValue.value(object, names: ["numRequests", "used", "requests"]))
            let limit = JSONValue.number(JSONValue.value(object, names: ["maxRequestUsage", "limit", "maxRequests", "requestLimit"]))
            if let used, let limit, limit > 0 {
                found.append((key, used, limit))
            }
        }
        for name in preferred {
            if let match = found.first(where: { $0.0 == name || $0.0.hasSuffix("." + name) }) {
                return (shortBucketName(match.0), match.1, match.2)
            }
        }
        if let first = found.first {
            return (shortBucketName(first.0), first.1, first.2)
        }
        let used = JSONValue.number(JSONValue.value(root, names: ["used"]))
        let limit = JSONValue.number(JSONValue.value(root, names: ["limit"]))
        if let used, let limit, limit > 0 {
            return ("请求", used, limit)
        }
        return nil
    }

    private static func shortBucketName(_ key: String) -> String {
        let last = key.split(separator: ".").last.map(String.init) ?? key
        if last.count <= 6 { return last }
        return String(last.prefix(6))
    }

    private static func walk(_ object: [String: Any], prefix: String, visit: (String, [String: Any]) -> Void) {
        visit(prefix.isEmpty ? "root" : prefix, object)
        for (key, value) in object {
            guard let nested = JSONValue.dictionary(value) else { continue }
            let next = prefix.isEmpty ? key : "\(prefix).\(key)"
            walk(nested, prefix: next, visit: visit)
        }
    }
}

enum SessionStore {
    static func load() -> (token: String, source: String, localPlan: String?)? {
        let localPlan = sqliteValue(key: "cursorAuth/stripeMembershipType")
        if let raw = ProcessInfo.processInfo.environment["CURSOR_SESSION_TOKEN"], let token = normalize(raw) {
            return (token, "env", localPlan)
        }
        if let raw = readFile(configURL()), let token = normalize(raw) {
            return (token, "config", localPlan)
        }
        if let raw = keychainToken(), let token = normalize(raw) {
            return (token, "keychain", localPlan)
        }
        if let raw = sqliteValue(key: "cursorAuth/accessToken"), let token = normalize(raw) {
            return (token, "cursor-app", localPlan)
        }
        return nil
    }

    static func cookieValue(from token: String) -> String {
        if token.contains("::") || token.contains("%3A%3A") {
            return token.replacingOccurrences(of: "%3A%3A", with: "::")
        }
        if let sub = jwtSubject(token) {
            return "\(sub)::\(token)"
        }
        return token
    }

    static func bearerToken(from token: String) -> String {
        if token.contains("%3A%3A") {
            return token.components(separatedBy: "%3A%3A").last ?? token
        }
        if token.contains("::") {
            return token.components(separatedBy: "::").last ?? token
        }
        return token
    }

    static func configURL() -> URL {
        URL(fileURLWithPath: NSHomeDirectory())
            .appendingPathComponent("Library/Application Support/CursorQuotaPet/session-token")
    }

    private static func normalize(_ raw: String) -> String? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !trimmed.hasPrefix("#") else { return nil }
        return trimmed
    }

    private static func readFile(_ url: URL) -> String? {
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return nil }
        for line in text.split(whereSeparator: \.isNewline) {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if !trimmed.isEmpty && !trimmed.hasPrefix("#") {
                return trimmed
            }
        }
        return nil
    }

    private static func keychainToken() -> String? {
        let process = Process()
        let pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["find-generic-password", "-s", "cursor-access-token", "-w"]
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
            process.waitUntilExit()
        } catch {
            return nil
        }
        guard process.terminationStatus == 0 else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        return String(data: data, encoding: .utf8)
    }

    private static func sqliteValue(key: String) -> String? {
        let path = NSHomeDirectory() + "/Library/Application Support/Cursor/User/globalStorage/state.vscdb"
        guard FileManager.default.fileExists(atPath: path) else { return nil }
        if let value = querySQLite(path: path, key: key) {
            return value
        }
        if let value = sqliteCLI(path: path, key: key) {
            return value
        }
        let temporary = FileManager.default.temporaryDirectory
            .appendingPathComponent("cursor-quota-\(UUID().uuidString).vscdb")
        defer { try? FileManager.default.removeItem(at: temporary) }
        do {
            try FileManager.default.copyItem(atPath: path, toPath: temporary.path)
            return querySQLite(path: temporary.path, key: key) ?? sqliteCLI(path: temporary.path, key: key)
        } catch {
            return nil
        }
    }

    private static func sqliteCLI(path: String, key: String) -> String? {
        let process = Process()
        let pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/sqlite3")
        process.arguments = [
            "-readonly", "-noheader", "-batch", path,
            "SELECT value FROM ItemTable WHERE key='\(key.replacingOccurrences(of: "'", with: "''"))' LIMIT 1;"
        ]
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
            process.waitUntilExit()
        } catch {
            return nil
        }
        guard process.terminationStatus == 0 else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        let value = String(data: data, encoding: .utf8)?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return (value?.isEmpty == false) ? value : nil
    }

    private static func querySQLite(path: String, key: String) -> String? {
        var db: OpaquePointer?
        let uri = URL(fileURLWithPath: path).absoluteString + "?mode=ro"
        let flags = SQLITE_OPEN_READONLY | SQLITE_OPEN_URI | SQLITE_OPEN_NOMUTEX
        guard sqlite3_open_v2(uri, &db, flags, nil) == SQLITE_OK else {
            sqlite3_close(db)
            return nil
        }
        defer { sqlite3_close(db) }

        let sql = "SELECT value FROM ItemTable WHERE key = ? LIMIT 1;"
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &statement, nil) == SQLITE_OK else { return nil }
        defer { sqlite3_finalize(statement) }
        let SQLITE_TRANSIENT = unsafeBitCast(OpaquePointer(bitPattern: -1), to: sqlite3_destructor_type.self)
        _ = key.withCString { pointer in
            sqlite3_bind_text(statement, 1, pointer, -1, SQLITE_TRANSIENT)
        }
        guard sqlite3_step(statement) == SQLITE_ROW else { return nil }
        guard let pointer = sqlite3_column_text(statement, 0) else { return nil }
        let value = String(cString: pointer).trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    private static func jwtSubject(_ token: String) -> String? {
        let parts = token.split(separator: ".")
        guard parts.count >= 2 else { return nil }
        var payload = String(parts[1])
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        let remainder = payload.count % 4
        if remainder > 0 {
            payload.append(String(repeating: "=", count: 4 - remainder))
        }
        guard let data = Data(base64Encoded: payload),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        return JSONValue.string(object["sub"])
    }
}

enum SnapshotCache {
    static func url() -> URL {
        URL(fileURLWithPath: NSHomeDirectory())
            .appendingPathComponent("Library/Caches/com.local.cursor.quotapet/last-snapshot.json")
    }

    static func load() -> QuotaSnapshot? {
        guard let data = try? Data(contentsOf: url()) else { return nil }
        return decode(data)
    }

    static func decode(_ data: Data) -> QuotaSnapshot? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        let sampledAt = JSONValue.date(object["sampledAt"]) ?? .distantPast
        let resetAt = JSONValue.number(object["resetAt"])
        let windowMinutes = JSONValue.number(object["windowMinutes"])
        // 1.5.0 could cache a monthly fallback as a weekly percentage. Preserve the extra
        // balance but wait for a fresh weekly sample before displaying that primary value.
        let legacyWeekly = windowMinutes == 7 * 24 * 60
            && (JSONValue.number(object["formatVersion"]) ?? 0) < 3
        let primary = QuotaWindow(
            remaining: legacyWeekly ? nil : JSONValue.number(object["totalRemain"]),
            used: legacyWeekly ? nil : JSONValue.number(object["totalUsed"]),
            resetAt: resetAt,
            windowMinutes: windowMinutes,
            badge: remappedBadge(JSONValue.string(object["primaryBadge"]), fallback: "内置"),
            title: JSONValue.string(object["primaryTitle"]) ?? "内置模型剩余",
            detail: legacyWeekly ? nil : JSONValue.string(object["primaryDetail"]),
            schedule: JSONValue.string(object["primarySchedule"])
        )
        let secondary = QuotaWindow(
            remaining: JSONValue.number(object["secondaryRemain"]),
            used: JSONValue.number(object["secondaryUsed"]),
            resetAt: object.keys.contains("secondaryResetAt") ? JSONValue.number(object["secondaryResetAt"])
                : (JSONValue.string(object["secondarySchedule"]) == nil ? resetAt : nil),
            windowMinutes: object.keys.contains("secondaryWindowMinutes") ? JSONValue.number(object["secondaryWindowMinutes"])
                : (JSONValue.string(object["secondarySchedule"]) == nil ? windowMinutes : nil),
            badge: remappedBadge(JSONValue.string(object["secondaryBadge"]), fallback: "其他"),
            title: JSONValue.string(object["secondaryTitle"]) ?? "其他模型剩余",
            detail: JSONValue.string(object["secondaryDetail"]),
            remainingAmountCents: JSONValue.number(object["secondaryRemainingAmountCents"]),
            limitAmountCents: JSONValue.number(object["secondaryLimitAmountCents"]),
            currencyCode: JSONValue.string(object["secondaryCurrencyCode"]),
            schedule: JSONValue.string(object["secondarySchedule"])
        )
        guard primary.remaining != nil || secondary.remaining != nil
            || secondary.remainingAmountCents != nil || secondary.limitAmountCents != nil
            || secondary.detail != nil else { return nil }
        return QuotaSnapshot(
            planType: JSONValue.string(object["planType"]),
            planNote: JSONValue.string(object["planNote"]),
            primary: primary,
            secondary: secondary,
            sampledAt: sampledAt,
            sourceName: "cache"
        )
    }

    static func encode(_ snapshot: QuotaSnapshot) -> Data? {
        let object: [String: Any] = [
            "formatVersion": 3,
            "planType": snapshot.planType as Any,
            "planNote": snapshot.planNote ?? NSNull(),
            "totalRemain": snapshot.primary.remaining as Any,
            "totalUsed": snapshot.primary.used as Any,
            "primaryBadge": snapshot.primary.badge,
            "primaryTitle": snapshot.primary.title,
            "primaryDetail": snapshot.primary.detail as Any,
            "primarySchedule": snapshot.primary.schedule ?? NSNull(),
            "secondaryRemain": snapshot.secondary.remaining as Any,
            "secondaryUsed": snapshot.secondary.used as Any,
            "secondaryBadge": snapshot.secondary.badge,
            "secondaryTitle": snapshot.secondary.title,
            "secondaryDetail": snapshot.secondary.detail as Any,
            "secondaryRemainingAmountCents": snapshot.secondary.remainingAmountCents as Any,
            "secondaryLimitAmountCents": snapshot.secondary.limitAmountCents as Any,
            "secondaryCurrencyCode": snapshot.secondary.currencyCode as Any,
            "secondaryResetAt": snapshot.secondary.resetAt.map { NSNumber(value: $0) } ?? NSNull(),
            "secondaryWindowMinutes": snapshot.secondary.windowMinutes.map { NSNumber(value: $0) } ?? NSNull(),
            "secondarySchedule": snapshot.secondary.schedule ?? NSNull(),
            "resetAt": snapshot.primary.resetAt as Any,
            "windowMinutes": snapshot.primary.windowMinutes as Any,
            "sampledAt": ISO8601DateFormatter().string(from: snapshot.sampledAt),
            "sourceName": snapshot.sourceName
        ]
        return try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted])
    }

    static func save(_ snapshot: QuotaSnapshot) {
        guard let data = encode(snapshot) else { return }
        let file = url()
        try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: file, options: .atomic)
    }

    private static func remappedBadge(_ value: String?, fallback: String) -> String {
        switch value {
        case "总额": return "内置"
        case "API": return "其他"
        case "Auto": return "内置"
        case let value?: return value
        default: return fallback
        }
    }
}

final class CursorUsageClient {
    var onSnapshot: ((QuotaSnapshot) -> Void)?
    var onError: ((String) -> Void)?

    private let queue = DispatchQueue(label: "com.local.cursor.quotapet.usage")
    private var inflight = false
    private var lastError = ""

    var currentError: String {
        queue.sync { lastError }
    }

    func refresh() {
        queue.async { [weak self] in
            self?.refreshOnQueue()
        }
    }

    private func refreshOnQueue() {
        guard !inflight else { return }
        inflight = true
        defer { inflight = false }

        guard let session = SessionStore.load() else {
            emitError("未找到 Cursor 登录态，请先在 Cursor 中登录")
            return
        }

        let bearer = SessionStore.bearerToken(from: session.token)
        let cookie = SessionStore.cookieValue(from: session.token)
        let group = DispatchGroup()
        var dashboard: [String: Any]?
        var planInfo: [String: Any]?
        var summary: [String: Any]?
        var authUsage: [String: Any]?
        var stripe: [String: Any]?
        var creditBalance: [String: Any]?
        var prepaidBilling: [String: Any]?
        var errors: [String] = []

        fetch(
            group: group,
            method: "POST",
            url: "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage",
            bearer: bearer,
            cookie: nil,
            connectRPC: true,
            body: [:]
        ) { json, error in
            dashboard = json
            if let error { errors.append(error) }
        }
        fetch(
            group: group,
            method: "POST",
            url: "https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo",
            bearer: bearer,
            cookie: nil,
            connectRPC: true,
            body: [:]
        ) { json, error in
            planInfo = json
            if let error { errors.append(error) }
        }
        fetch(
            group: group,
            method: "GET",
            url: "https://cursor.com/api/usage-summary",
            bearer: nil,
            cookie: cookie,
            connectRPC: false,
            body: nil
        ) { json, error in
            summary = json
            if let error { errors.append(error) }
        }
        fetch(
            group: group,
            method: "GET",
            url: "https://api2.cursor.sh/auth/usage",
            bearer: bearer,
            cookie: nil,
            connectRPC: false,
            body: nil
        ) { json, error in
            authUsage = json
            if let error { errors.append(error) }
        }
        fetch(
            group: group,
            method: "GET",
            url: "https://api2.cursor.sh/auth/full_stripe_profile",
            bearer: bearer,
            cookie: nil,
            connectRPC: false,
            body: nil
        ) { json, error in
            stripe = json
            if let error { errors.append(error) }
        }

        fetch(
            group: group, method: "POST",
            url: "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCreditGrantsBalance",
            bearer: bearer, cookie: nil, connectRPC: true, body: [:]
        ) { json, _ in creditBalance = json }
        fetch(
            group: group, method: "POST",
            url: "https://api2.cursor.sh/aiserver.v1.DashboardService/GetUserPrepaidBilling",
            bearer: bearer, cookie: nil, connectRPC: true, body: [:]
        ) { json, _ in prepaidBilling = json }

        _ = group.wait(timeout: .now() + 18)

        if let snapshot = QuotaParser.snapshot(
            dashboard: dashboard,
            planInfo: planInfo,
            summary: summary,
            authUsage: authUsage,
            stripe: stripe,
            localPlan: session.localPlan,
            sampledAt: Date(),
            sourceName: "cursor-api",
            creditBalance: creditBalance,
            prepaidBilling: prepaidBilling
        ) {
            lastError = ""
            SnapshotCache.save(snapshot)
            onSnapshot?(snapshot)
            return
        }

        let message = friendlyError(errors.first ?? "额度接口没有返回数据")
        emitError(message)
    }

    private func fetch(
        group: DispatchGroup,
        method: String,
        url urlString: String,
        bearer: String?,
        cookie: String?,
        connectRPC: Bool,
        body: [String: Any]?,
        completion: @escaping ([String: Any]?, String?) -> Void
    ) {
        guard let url = URL(string: urlString) else { return }
        group.enter()
        var request = URLRequest(url: url, timeoutInterval: 15)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("CursorQuotaPet/1.0", forHTTPHeaderField: "User-Agent")
        if connectRPC {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue("1", forHTTPHeaderField: "Connect-Protocol-Version")
        }
        if let bearer {
            request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization")
        }
        if let cookie {
            request.setValue("WorkosCursorSessionToken=\(cookie)", forHTTPHeaderField: "Cookie")
            request.setValue("https://cursor.com", forHTTPHeaderField: "Origin")
        }
        if method == "POST" {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue("https://cursor.com", forHTTPHeaderField: "Origin")
            request.httpBody = try? JSONSerialization.data(withJSONObject: body ?? [:])
        }

        URLSession.shared.dataTask(with: request) { data, response, error in
            defer { group.leave() }
            if let error {
                completion(nil, error.localizedDescription)
                return
            }
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard let data, !data.isEmpty else {
                completion(nil, status >= 400 ? "HTTP \(status)" : "空响应")
                return
            }
            if status == 401 || status == 403 {
                completion(nil, "会话已过期，请重新登录 Cursor")
                return
            }
            if status >= 400 {
                completion(nil, "HTTP \(status)")
                return
            }
            let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
            completion(object, object == nil ? "响应不是 JSON" : nil)
        }.resume()
    }

    private func friendlyError(_ message: String) -> String {
        let lower = message.lowercased()
        if lower.contains("401") || lower.contains("403") || lower.contains("not_authenticated") || message.contains("过期") {
            return "会话已过期，请重新登录 Cursor"
        }
        if lower.contains("offline") || lower.contains("network") || lower.contains("internet") || lower.contains("timed out") {
            return "网络不可用，稍后重试"
        }
        return message
    }

    private func emitError(_ message: String) {
        lastError = message
        onError?(message)
    }
}

enum QuotaFormatter {
    static func percent(_ value: Double?) -> String {
        guard let value else { return "—" }
        return String(format: "%.0f%%", value)
    }

    static func planTitle(_ planType: String?) -> String {
        let raw = planType?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !raw.isEmpty else { return "Cursor 额度" }
        return "\(PlanCatalog.display(raw) ?? raw.capitalized) 额度"
    }

    static func caption(minutes: Double?, fallback: String) -> String {
        guard let minutes else { return fallback }
        let rounded = Int(minutes.rounded())
        guard rounded > 0 else { return fallback }
        if rounded % 1440 == 0 { return "\(rounded / 1440) 天窗口剩余" }
        if rounded % 60 == 0 { return "\(rounded / 60) 小时窗口剩余" }
        return fallback
    }

    private static let resetAtFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "MM-dd  HH:mm"
        return formatter
    }()

    static func resetAt(_ timestamp: TimeInterval?) -> String? {
        guard let timestamp else { return nil }
        return "将于 \(resetAtFormatter.string(from: Date(timeIntervalSince1970: timestamp))) 重置"
    }

    static func reset(_ timestamp: TimeInterval?, schedule: String? = nil) -> String {
        if timestamp == nil, let schedule, !schedule.isEmpty { return schedule }
        guard let timestamp else { return "重置时间未知" }
        let seconds = timestamp - Date().timeIntervalSince1970
        if seconds <= 0 { return "窗口已到点，等待刷新" }
        let minutes = Int(ceil(seconds / 60))
        if minutes < 60 { return "约 \(minutes) 分钟后重置" }
        let days = minutes / 1440
        let hours = (minutes % 1440) / 60
        let rest = minutes % 60
        if days > 0 { return "约 \(days) 天 \(hours) 小时 \(rest) 分钟后重置" }
        return "约 \(hours) 小时 \(rest) 分钟后重置"
    }

    static func shortError(_ error: String) -> String {
        if error.isEmpty { return "" }
        let oneLine = error.replacingOccurrences(of: "\n", with: " ").trimmingCharacters(in: .whitespacesAndNewlines)
        return String(oneLine.prefix(22))
    }

    static func burnRatePerDay(
        used: Double?,
        remaining: Double?,
        resetAt: TimeInterval?,
        windowMinutes: Double?
    ) -> String {
        let usedPercent = used ?? remaining.map { min(100, max(0, 100 - $0)) }
        guard let usedPercent, usedPercent.isFinite else { return "—" }
        guard let windowMinutes, windowMinutes > 0, let resetAt else { return "—" }

        let remainingSeconds = resetAt - Date().timeIntervalSince1970
        let elapsedDays = max(0, windowMinutes * 60 - remainingSeconds) / 86_400
        if usedPercent <= 0.0001 {
            return "0%/天"
        }

        let rate = usedPercent / max(elapsedDays, 1.0 / 24.0)
        if rate < 0.05 {
            return String(format: "%.2f%%/天", rate)
        }
        let tenths = (rate * 10).rounded() / 10
        if tenths == tenths.rounded() {
            return String(format: "%.0f%%/天", tenths)
        }
        return String(format: "%.1f%%/天", tenths)
    }
}

enum PreviewScenario: String, CaseIterable {
    case normal, warning, critical, empty, snapshot, error

    func sample(at now: Date = Date()) -> QuotaSnapshot? {
        guard self != .empty else { return nil }
        let values: (Double, Double)
        switch self {
        case .warning: values = (30, 10)
        case .critical: values = (0, 0)
        default: values = (76, 45)
        }
        let reset = now.addingTimeInterval(3 * 86400 + 9 * 3600).timeIntervalSince1970
        let cached = self == .snapshot || self == .error
        return QuotaSnapshot(
            planType: "premium",
            primary: QuotaWindow(remaining: values.0, used: 100 - values.0, resetAt: reset,
                                 windowMinutes: 10080, badge: "内置", title: "内置模型剩余",
                                 detail: "已用 \(Int(100 - values.0))%"),
            secondary: QuotaWindow(remaining: values.1, used: 100 - values.1, resetAt: nil,
                                   windowMinutes: nil, badge: "额外", title: "额外额度池",
                                   detail: String(format: "已用 $%.2f", (100 - values.1) / 5),
                                   remainingAmountCents: values.1 * 20,
                                   limitAmountCents: 2000, currencyCode: "USD"),
            sampledAt: cached ? now.addingTimeInterval(-2700) : now,
            sourceName: cached ? "cache" : "cursor-api"
        )
    }
}

@MainActor
final class QuotaModel: ObservableObject {
    @Published private(set) var snapshot: QuotaSnapshot?
    @Published private(set) var fallbackSnapshot: QuotaSnapshot?
    @Published private(set) var connectionError = ""
    @Published private(set) var footer = "正在读取 Cursor 登录态…"

    private let client = CursorUsageClient()
    private let previewScenario: PreviewScenario?
    private let uiSeconds: TimeInterval
    private let fetchSeconds: TimeInterval
    private var uiTimer: Timer?
    private var fetchTimer: Timer?
    private var lastFetch = Date.distantPast

    init(uiSeconds: TimeInterval = 1, fetchSeconds: TimeInterval = 30, previewScenario: PreviewScenario? = nil) {
        self.previewScenario = previewScenario
        self.uiSeconds = uiSeconds
        self.fetchSeconds = fetchSeconds
        client.onSnapshot = { [weak self] snapshot in
            Task { @MainActor in
                self?.snapshot = snapshot
                self?.connectionError = ""
                self?.updateFooter(snapshot)
            }
        }
        client.onError = { [weak self] message in
            Task { @MainActor in self?.connectionError = message }
        }
    }

    func start() {
        if previewScenario != nil {
            refreshPreview()
            uiTimer = Timer.scheduledTimer(withTimeInterval: uiSeconds, repeats: true) { [weak self] _ in
                Task { @MainActor in self?.tick() }
            }
            return
        }
        fallbackSnapshot = SnapshotCache.load()
        refresh(forceNetwork: true)
        uiTimer = Timer.scheduledTimer(withTimeInterval: uiSeconds, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        fetchTimer = Timer.scheduledTimer(withTimeInterval: fetchSeconds, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh(forceNetwork: true) }
        }
    }

    func refresh(forceNetwork: Bool = true) {
        if previewScenario != nil {
            refreshPreview()
            return
        }
        fallbackSnapshot = SnapshotCache.load()
        if forceNetwork || Date().timeIntervalSince(lastFetch) >= fetchSeconds {
            lastFetch = Date()
            client.refresh()
        }
        let display = snapshot ?? fallbackSnapshot
        if let display {
            updateFooter(display)
        } else if !connectionError.isEmpty {
            footer = connectionError
        } else {
            footer = "正在读取 Cursor 登录态…"
        }
    }

    private func refreshPreview() {
        guard let previewScenario else { return }
        let sample = previewScenario.sample()
        if previewScenario == .snapshot || previewScenario == .error {
            snapshot = nil
            fallbackSnapshot = sample
        } else {
            snapshot = sample
            fallbackSnapshot = nil
        }
        connectionError = previewScenario == .error
            ? "网络不可用，正在显示上次成功数据。请检查网络后重试。" : ""
        if let sample {
            updateFooter(sample)
        } else {
            footer = "等待额度数据 · 离线预览"
        }
    }

    func stop() {
        uiTimer?.invalidate()
        fetchTimer?.invalidate()
        uiTimer = nil
        fetchTimer = nil
    }

    private func tick() {
        if let display = snapshot ?? fallbackSnapshot {
            updateFooter(display)
        } else if !connectionError.isEmpty {
            footer = connectionError
        }
    }

    private func updateFooter(_ value: QuotaSnapshot) {
        let ageMinutes = max(0, Date().timeIntervalSince(value.sampledAt) / 60)
        let formatter = DateFormatter()
        formatter.dateFormat = ageMinutes >= 10 ? "MM-dd HH:mm" : "HH:mm"
        let stale = ageMinutes >= 10 ? " · 可能过期" : ""
        footer = "更新于 \(formatter.string(from: value.sampledAt))\(stale)"
    }
}

private final class QuotaDetailPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    init(contentRect: NSRect) {
        super.init(
            contentRect: contentRect,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        isReleasedWhenClosed = false
        acceptsMouseMovedEvents = true
        becomesKeyOnlyIfNeeded = false
        isFloatingPanel = true
        level = .popUpMenu
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        animationBehavior = .utilityWindow
        hidesOnDeactivate = false
    }

    override func becomeKey() {
        super.becomeKey()
        guard let contentView else { return }
        invalidateCursorRects(for: contentView)
    }
}

@MainActor
final class QuotaDetailPanelController {
    private let panel: QuotaDetailPanel
    private let hostingController: NSHostingController<QuotaView>

    var isShown: Bool { panel.isVisible }

    var window: NSWindow { panel }

    private var activateGeneration = 0

    init(model: QuotaModel, refresh: @escaping () -> Void, dismiss: @escaping () -> Void) {
        hostingController = NSHostingController(rootView: QuotaView(
            model: model,
            refresh: refresh,
            dismiss: dismiss
        ))
        hostingController.view.wantsLayer = true
        hostingController.view.layer?.isOpaque = false
        hostingController.view.layer?.backgroundColor = NSColor.clear.cgColor
        hostingController.view.layer?.masksToBounds = false

        panel = QuotaDetailPanel(
            contentRect: NSRect(
                x: 0,
                y: 0,
                width: GlassTheme.windowWidth,
                height: 360
            )
        )
        panel.contentViewController = hostingController
        panel.contentView?.wantsLayer = true
        panel.contentView?.layer?.isOpaque = false
        panel.contentView?.layer?.backgroundColor = NSColor.clear.cgColor
        panel.contentView?.layer?.masksToBounds = false
        panel.title = "Cursor 额度详情"
        panel.setAccessibilityLabel("Cursor 额度详情")
        resizeToContent()
    }

    private func resizeToContent() {
        hostingController.view.invalidateIntrinsicContentSize()
        hostingController.view.layoutSubtreeIfNeeded()
        let measured = hostingController.view.fittingSize
        let height = max(300, ceil(measured.height))
        guard height.isFinite, height < 1200 else { return }
        if abs(panel.frame.height - height) > 0.5 {
            panel.setContentSize(NSSize(width: GlassTheme.windowWidth, height: height))
        }
    }

    func refreshLayout(relativeTo statusView: NSView) {
        guard isShown else { return }
        resizeToContent()
        position(relativeTo: statusView)
    }

    func show(relativeTo statusView: NSView) {
        resizeToContent()
        position(relativeTo: statusView)
        panel.orderFrontRegardless()
        scheduleActivate()
    }

    func scheduleActivate() {
        activateGeneration += 1
        let generation = activateGeneration
        for delay in [0.0, 0.05, 0.12] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                self?.activateNow(generation: generation)
            }
        }
    }

    private func activateNow(generation: Int) {
        guard activateGeneration == generation, panel.isVisible else { return }
        if !panel.isKeyWindow {
            NSApp.activate(ignoringOtherApps: true)
            panel.makeKeyAndOrderFront(nil)
        }
        if let contentView = panel.contentView {
            panel.invalidateCursorRects(for: contentView)
        }
    }

    func close() {
        activateGeneration += 1
        NSCursor.arrow.set()
        panel.orderOut(nil)
    }

    func toggle(relativeTo statusView: NSView) {
        if isShown {
            close()
        } else {
            show(relativeTo: statusView)
        }
    }

    private func position(relativeTo statusView: NSView) {
        guard let statusWindow = statusView.window else { return }
        let statusRect = statusWindow.convertToScreen(statusView.convert(statusView.bounds, to: nil))
        let size = NSSize(width: GlassTheme.windowWidth, height: panel.frame.height)
        let topAnchor = min(statusRect.minY, statusWindow.frame.minY)
        var origin = NSPoint(
            x: statusRect.midX - size.width / 2,
            y: topAnchor - size.height + GlassTheme.shadowPadding
        )

        if let visible = (statusWindow.screen ?? NSScreen.main)?.visibleFrame {
            origin.x = min(max(origin.x, visible.minX + 8), visible.maxX - size.width - 8)
            if origin.y < visible.minY + 8 {
                origin.y = max(statusRect.maxY, statusWindow.frame.maxY) - GlassTheme.shadowPadding
            }
            origin.y = min(max(origin.y, visible.minY + 8 - GlassTheme.shadowPadding),
                           visible.maxY - size.height + GlassTheme.shadowPadding)
        }

        panel.setFrame(NSRect(origin: origin, size: size), display: true)
    }
}

final class QuotaStatusView: NSView {
    var onLeftClick: (() -> Void)?
    var onLeftMouseUp: (() -> Void)?
    var onRightClick: (() -> Void)?

    private enum Metrics {
        static let height: CGFloat = 22
        static let iconSize: CGFloat = 20
        static let iconLeading: CGFloat = 3
        static let iconTextGap: CGFloat = 4
        static let trailing: CGFloat = 5
        static let lineHeight: CGFloat = 11
        static let textInset: CGFloat = 2
    }

    private let iconView = NSImageView()
    private let primaryLabel = NSTextField(labelWithString: "内置 —")
    private let secondaryLabel = NSTextField(labelWithString: "额外 —")
    private var isPressed = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        setAccessibilityRole(.button)
        setAccessibilityLabel("Cursor 剩余额度")

        iconView.image = CursorBrand.markImage(size: 16)
        iconView.imageScaling = .scaleProportionallyUpOrDown
        iconView.contentTintColor = .labelColor
        addSubview(iconView)

        for label in [primaryLabel, secondaryLabel] {
            label.font = NSFont.monospacedDigitSystemFont(ofSize: 10, weight: .medium)
            label.textColor = .secondaryLabelColor
            label.alignment = .left
            label.lineBreakMode = .byClipping
            label.cell?.wraps = false
            label.cell?.isScrollable = false
            label.cell?.truncatesLastVisibleLine = false
            label.cell?.usesSingleLineMode = true
            label.backgroundColor = .clear
            label.drawsBackground = false
            label.isBordered = false
            label.isBezeled = false
            label.isEditable = false
            label.isSelectable = false
            addSubview(label)
        }
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        return !isHidden && bounds.contains(local) ? self : nil
    }

    override func accessibilityPerformPress() -> Bool {
        onLeftClick?()
        return true
    }

    override func layout() {
        super.layout()
        iconView.frame = NSRect(x: Metrics.iconLeading + 2, y: 3, width: 16, height: 16)
        let textX = Metrics.iconLeading + Metrics.iconSize + Metrics.iconTextGap
        let textWidth = max(24, bounds.width - textX - Metrics.trailing)
        primaryLabel.frame = NSRect(x: textX, y: 0, width: textWidth, height: Metrics.lineHeight)
        secondaryLabel.frame = NSRect(x: textX, y: 11, width: textWidth, height: Metrics.lineHeight)
    }

    func preferredWidth() -> CGFloat {
        let textWidth = max(
            measuredTextWidth(primaryLabel.stringValue),
            measuredTextWidth(secondaryLabel.stringValue),
            measuredTextWidth("内置 100%"),
            measuredTextWidth("额外 100%")
        )
        return ceil(
            Metrics.iconLeading
            + Metrics.iconSize
            + Metrics.iconTextGap
            + textWidth
            + Metrics.textInset
            + Metrics.trailing
        )
    }

    private func measuredTextWidth(_ text: String) -> CGFloat {
        let font = primaryLabel.font ?? NSFont.monospacedDigitSystemFont(ofSize: 10, weight: .medium)
        let attributed = NSAttributedString(string: text, attributes: [.font: font])
        let line = CTLineCreateWithAttributedString(attributed)
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var leading: CGFloat = 0
        let width = CGFloat(CTLineGetTypographicBounds(line, &ascent, &descent, &leading))
        let boundsWidth = attributed.boundingRect(
            with: NSSize(width: CGFloat.greatestFiniteMagnitude, height: Metrics.lineHeight),
            options: [.usesLineFragmentOrigin, .usesFontLeading]
        ).width
        return ceil(max(width, boundsWidth))
    }

    func update(snapshot: QuotaSnapshot?, connectionError: String = "") {
        let primary = snapshot?.primary
        let secondary = snapshot?.secondary
        primaryLabel.stringValue = "\(QuotaPresentation.badge(primary, secondary: false)) \(QuotaFormatter.percent(primary?.remaining))"
        secondaryLabel.stringValue = "额外 \(QuotaFormatter.percent(secondary?.remaining))"
        primaryLabel.textColor = statusColor(primary?.remaining)
        secondaryLabel.textColor = statusColor(secondary?.remaining)
        let source: String
        if snapshot?.sourceName == "cursor-api" && connectionError.isEmpty {
            source = "实时"
        } else if snapshot == nil {
            source = "等待数据"
        } else {
            source = "快照"
        }
        toolTip = "Cursor 额度 · \(source)\n\(primaryLabel.stringValue)\n\(secondaryLabel.stringValue)"
        setAccessibilityValue("\(primaryLabel.stringValue)，\(secondaryLabel.stringValue)，\(source)")
        needsLayout = true
        needsDisplay = true
    }

    private func statusColor(_ remaining: Double?) -> NSColor {
        guard let remaining else { return .secondaryLabelColor }
        if remaining <= 10 { return NSColor(red: 0.94, green: 0.29, blue: 0.30, alpha: 1) }
        if remaining <= 30 { return NSColor(red: 0.96, green: 0.60, blue: 0.06, alpha: 1) }
        return .secondaryLabelColor
    }

    override func draw(_ dirtyRect: NSRect) {
        if isPressed {
            NSColor.labelColor.withAlphaComponent(0.12).setFill()
            NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 5, yRadius: 5).fill()
        }
    }

    override func mouseDown(with event: NSEvent) {
        isPressed = true
        needsDisplay = true
        onLeftClick?()
    }

    override func mouseUp(with event: NSEvent) {
        isPressed = false
        needsDisplay = true
        onLeftMouseUp?()
    }

    override func rightMouseDown(with event: NSEvent) {
        onRightClick?()
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let model: QuotaModel
    private let previewScenario: PreviewScenario?

    init(previewScenario: PreviewScenario? = nil) {
        self.previewScenario = previewScenario
        self.model = QuotaModel(previewScenario: previewScenario)
        super.init()
    }
    private var detailPanel: QuotaDetailPanelController!
    private var statusItem: NSStatusItem!
    private var statusView: QuotaStatusView!
    private let statusMenu = NSMenu()
    private var cancellables = Set<AnyCancellable>()
    private var localClickMonitor: Any?
    private var globalClickMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildDetailPanel()
        buildStatusItem()
        model.$snapshot
            .combineLatest(model.$fallbackSnapshot)
            .receive(on: RunLoop.main)
            .sink { [weak self] _, _ in self?.updateStatusItem() }
            .store(in: &cancellables)
        model.objectWillChange
            .sink { [weak self] in
                DispatchQueue.main.async {
                    guard let self, let statusView = self.statusView else { return }
                    self.updateStatusItem()
                    self.detailPanel.refreshLayout(relativeTo: statusView)
                }
            }
            .store(in: &cancellables)
        model.start()
        updateStatusItem()
        NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {
                self?.closePopover()
                return nil
            }
            return event
        }
        installPopoverDismissMonitors()
        if previewScenario != nil || CommandLine.arguments.contains("--show-details") {
            DispatchQueue.main.async { [weak self] in self?.showPopover() }
        }
    }

    func applicationDidResignActive(_ notification: Notification) {
        closePopover()
    }

    func applicationWillTerminate(_ notification: Notification) {
        removePopoverDismissMonitors()
        model.stop()
    }

    private func buildDetailPanel() {
        detailPanel = QuotaDetailPanelController(
            model: model,
            refresh: { [weak self] in self?.refreshQuota() },
            dismiss: { [weak self] in self?.closePopover() }
        )
    }

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusView = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 96, height: 22))
        statusView.onLeftClick = { [weak self] in self?.togglePopover() }
        statusView.onLeftMouseUp = { [weak self] in self?.detailPanel.scheduleActivate() }
        statusView.onRightClick = { [weak self] in self?.showStatusMenu() }
        statusItem.length = statusView.preferredWidth()
        if let button = statusItem.button {
            button.title = ""
            button.image = nil
            button.target = self
            button.action = #selector(statusButtonPressed)
            button.setAccessibilityLabel("Cursor 剩余额度")
            button.addSubview(statusView)
        }

        let show = NSMenuItem(title: "显示额度", action: #selector(showPopover), keyEquivalent: "")
        let refresh = NSMenuItem(title: "立即刷新", action: #selector(refreshQuota), keyEquivalent: "")
        let dashboard = NSMenuItem(title: "打开 Cursor 用量页", action: #selector(openDashboard), keyEquivalent: "")
        let quit = NSMenuItem(title: "退出", action: #selector(quitApp), keyEquivalent: "")
        [show, refresh, dashboard, NSMenuItem.separator(), quit].forEach { item in
            item.target = self
            statusMenu.addItem(item)
        }
    }

    private func showStatusMenu() {
        closePopover()
        statusMenu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }

    private func installPopoverDismissMonitors() {
        let mouseDownMask: NSEvent.EventTypeMask = [.leftMouseDown, .rightMouseDown, .otherMouseDown]

        localClickMonitor = NSEvent.addLocalMonitorForEvents(matching: mouseDownMask) { [weak self] event in
            self?.dismissPopoverForLocalClick()
            return event
        }

        globalClickMonitor = NSEvent.addGlobalMonitorForEvents(matching: mouseDownMask) { [weak self] _ in
            DispatchQueue.main.async {
                self?.dismissPopoverForGlobalClick()
            }
        }
    }

    private func removePopoverDismissMonitors() {
        if let localClickMonitor {
            NSEvent.removeMonitor(localClickMonitor)
            self.localClickMonitor = nil
        }
        if let globalClickMonitor {
            NSEvent.removeMonitor(globalClickMonitor)
            self.globalClickMonitor = nil
        }
    }

    private func dismissPopoverForLocalClick() {
        guard detailPanel.isShown else { return }
        let clickLocation = NSEvent.mouseLocation
        if isClickInDetailCard(clickLocation) || isClickInStatusItem(clickLocation) {
            return
        }
        closePopover()
    }

    private func dismissPopoverForGlobalClick() {
        guard detailPanel.isShown else { return }
        if isClickInStatusItem(NSEvent.mouseLocation) { return }
        closePopover()
    }

    private func isClickInDetailCard(_ clickLocation: NSPoint) -> Bool {
        let frame = detailPanel.window.frame
        return frame.insetBy(dx: GlassTheme.shadowPadding, dy: GlassTheme.shadowPadding).contains(clickLocation)
    }

    private func isClickInStatusItem(_ clickLocation: NSPoint) -> Bool {
        guard let statusWindow = statusView.window else { return false }
        let statusRect = statusView.convert(statusView.bounds, to: nil)
        return statusWindow.convertToScreen(statusRect).contains(clickLocation)
    }

    private func updateStatusItem() {
        guard let statusView else { return }
        statusView.update(snapshot: model.snapshot ?? model.fallbackSnapshot, connectionError: model.connectionError)
        statusItem.button?.setAccessibilityValue(statusView.accessibilityValue())
        let width = statusView.preferredWidth()
        statusView.frame = NSRect(x: 0, y: 0, width: width, height: 22)
        statusItem.length = width
        statusView.needsLayout = true
        statusView.layoutSubtreeIfNeeded()
    }

    @objc private func statusButtonPressed() {
        togglePopover()
    }

    @objc private func showPopover() {
        guard let statusView else { return }
        if !detailPanel.isShown {
            detailPanel.show(relativeTo: statusView)
            previewEvent("shown")
        }
    }

    @objc private func closePopover() {
        guard detailPanel?.isShown == true else { return }
        detailPanel.close()
        previewEvent("closed")
    }

    private func togglePopover() {
        guard let statusView else { return }
        detailPanel.toggle(relativeTo: statusView)
        previewEvent(detailPanel.isShown ? "shown" : "closed")
    }

    private func previewEvent(_ name: String) {
        guard previewScenario != nil else { return }
        print("Preview event: \(name)")
        fflush(stdout)
    }

    @objc private func refreshQuota() {
        model.refresh(forceNetwork: true)
        previewEvent("refreshed")
    }

    @objc private func openDashboard() {
        if let url = URL(string: "https://cursor.com/dashboard/usage") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func quitApp() {
        NSApp.terminate(nil)
    }
}

private func addAmountProbeFields(_ snapshot: QuotaSnapshot, to result: inout [String: Any]) {
    if let balance = snapshot.secondary.remainingAmountCents { result["SecondaryBalanceCents"] = balance }
    if let total = snapshot.secondary.limitAmountCents { result["SecondaryPoolCents"] = total }
    if let currency = snapshot.secondary.currencyCode { result["SecondaryCurrency"] = currency }
    if let minutes = snapshot.primary.windowMinutes { result["PrimaryWindowMinutes"] = minutes }
    if let reset = snapshot.primary.resetAt { result["PrimaryResetAt"] = ISO8601DateFormatter().string(from: Date(timeIntervalSince1970: reset)) }
}

func runProbe() -> Int32 {
    let client = CursorUsageClient()
    let semaphore = DispatchSemaphore(value: 0)
    var snapshot: QuotaSnapshot?
    var errorMessage = ""
    client.onSnapshot = {
        snapshot = $0
        semaphore.signal()
    }
    client.onError = {
        errorMessage = $0
        semaphore.signal()
    }
    client.refresh()
    _ = semaphore.wait(timeout: .now() + 20)

    if let snapshot {
        var result: [String: Any] = [
            "Status": "ok",
            "PlanType": snapshot.planType as Any,
            "PlanNote": snapshot.planNote ?? NSNull(),
            "PrimaryBadge": snapshot.primary.badge,
            "IncludedRemain": snapshot.primary.remaining as Any,
            "OtherRemain": snapshot.secondary.remaining as Any,
            "SecondaryBadge": snapshot.secondary.badge,
            "SampledAt": ISO8601DateFormatter().string(from: snapshot.sampledAt),
            "SourceName": snapshot.sourceName
        ]
        addAmountProbeFields(snapshot, to: &result)
        if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]),
           let text = String(data: data, encoding: .utf8) {
            print(text)
        }
        return 0
    }

    if let fallback = SnapshotCache.load() {
        var result: [String: Any] = [
            "Status": "ok",
            "PlanType": fallback.planType as Any,
            "PlanNote": fallback.planNote ?? NSNull(),
            "PrimaryBadge": fallback.primary.badge,
            "IncludedRemain": fallback.primary.remaining as Any,
            "SecondaryBadge": fallback.secondary.badge,
            "SecondaryRemain": fallback.secondary.remaining as Any,
            "SampledAt": ISO8601DateFormatter().string(from: fallback.sampledAt),
            "SourceName": fallback.sourceName
        ]
        addAmountProbeFields(fallback, to: &result)
        if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]),
           let text = String(data: data, encoding: .utf8) {
            print(text)
        }
        return 0
    }

    let message = errorMessage.isEmpty ? client.currentError : errorMessage
    let result = [
        "Status": "unavailable",
        "Message": message.isEmpty ? "没有找到可读取的 Cursor 额度" : message
    ]
    if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]),
       let text = String(data: data, encoding: .utf8) {
        print(text)
    }
    return 1
}

@main
struct CursorQuotaPetMain {
    static func main() {
        let hasPreviewOptions = CommandLine.arguments.contains { $0.hasPrefix("--preview-") }
        if hasPreviewOptions && CommandLine.arguments.contains("--probe") {
            fputs("离线预览不能与 --probe 同时使用。\n", stderr)
            Foundation.exit(2)
        }
        if hasPreviewOptions && !CommandLine.arguments.contains(where: { $0 == "--preview-ui" || $0.hasPrefix("--preview-ui=") }) {
            fputs("预览选项需要配合 --preview-ui 使用。\n", stderr)
            Foundation.exit(2)
        }
        if CommandLine.arguments.contains("--probe") {
            Foundation.exit(runProbe())
        }

        let previewArgument = CommandLine.arguments.first { $0 == "--preview-ui" || $0.hasPrefix("--preview-ui=") }
        var previewScenario: PreviewScenario?
        if let previewArgument {
            let name = previewArgument.split(separator: "=", maxSplits: 1).dropFirst().first.map(String.init) ?? "normal"
            guard let scenario = PreviewScenario(rawValue: name) else {
                fputs("未知预览场景。可选：normal, warning, critical, empty, snapshot, error\n", stderr)
                Foundation.exit(2)
            }
            previewScenario = scenario
        }
        let application = NSApplication.shared
        if previewScenario != nil {
            if CommandLine.arguments.contains("--appearance=light") { application.appearance = NSAppearance(named: .aqua) }
            if CommandLine.arguments.contains("--appearance=dark") { application.appearance = NSAppearance(named: .darkAqua) }
        }
        let delegate = AppDelegate(previewScenario: previewScenario)
        application.delegate = delegate
        application.run()
    }
}
