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
    let schedule: String?
}

struct QuotaSnapshot {
    let planType: String?
    let planNote: String?
    let primary: QuotaWindow
    let secondary: QuotaWindow
    let sampledAt: Date
    let sourceName: String
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

    static func allowance(_ planType: String?, model: Model) -> String? {
        guard model == .weekly else { return nil }
        switch key(planType) {
        case "plus": return "基础"
        case "premium": return "4× Plus"
        case "super": return "15× Plus"
        case "ultra": return "50× Plus"
        case "team", "teams": return "按席位"
        case "free", "freetrial": return "有限"
        default: return nil
        }
    }

    static func includesOtherPool(_ model: Model) -> Bool {
        model == .unknown || model == .legacyDual
    }

    static func thirdPartyPrepaid(_ planType: String?, model: Model) -> Bool {
        if model == .pooled { return true }
        guard model == .weekly else { return false }
        let key = key(planType)
        return key != "plus" && key != "free" && key != "freetrial"
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
        sourceName: String
    ) -> QuotaSnapshot? {
        let planUsage = JSONValue.dictionary(JSONValue.value(dashboard, names: ["planUsage"]))
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
        let resetAt = cycleEnd?.timeIntervalSince1970
        let windowMinutes: Double?
        if let cycleStart, let cycleEnd {
            windowMinutes = max(1, cycleEnd.timeIntervalSince(cycleStart) / 60)
        } else {
            windowMinutes = 30 * 24 * 60
        }

        let planType = JSONValue.string(JSONValue.nested(planInfo, path: ["planInfo", "planName"]))
            ?? JSONValue.string(JSONValue.value(planInfo, names: ["planName", "planType"]))
            ?? JSONValue.string(JSONValue.value(summary, names: ["membershipType"]))
            ?? JSONValue.string(JSONValue.value(stripe, names: ["membershipType", "individualMembershipType"]))
            ?? localPlan

        // Included usage follows the plan. Current X Subscription plans have one
        // weekly Cursor-model pool. Legacy Pro / Pro+ / Ultra keep two monthly pools.
        let quotaIsAllowance = autoUsedPercent == nil && allowance != nil
        let primaryRemaining = allowance?.remaining ?? builtinRemaining
        guard primaryRemaining != nil || otherUsedPercent != nil || requestBucket != nil else {
            return nil
        }

        let legacyFlag = legacyFlag(summary: summary, planInfo: planInfo, stripe: stripe)
        let planModel = PlanCatalog.model(planType, legacyFlag: legacyFlag)
        let planKey = PlanCatalog.key(planType)
        let allowanceDetail = quotaIsAllowance
            ? amountDetail(
                used: includedSpend,
                remaining: centsRemaining,
                limit: centsLimit,
                request: nil,
                onDemandEnabled: false,
                onDemandUsed: nil
            )
            : nil
        let primaryBadge: String
        let primaryTitle: String
        if quotaIsAllowance {
            primaryBadge = "额度"
            primaryTitle = "套餐剩余"
        } else if planModel == .weekly && (planKey == "free" || planKey == "freetrial") {
            primaryBadge = "有限"
            primaryTitle = "有限额度"
        } else if planModel == .weekly {
            primaryBadge = "本周"
            primaryTitle = "Cursor 模型剩余"
        } else if planModel == .pooled {
            primaryBadge = "共享"
            primaryTitle = "共享额度剩余"
        } else {
            primaryBadge = "内置"
            primaryTitle = "内置模型剩余"
        }
        let primarySchedule = planModel == .weekly
            ? ((planKey == "free" || planKey == "freetrial") ? "有限额度" : "每周重置")
            : nil
        let primaryDetail = (!quotaIsAllowance && planModel == .weekly)
            ? PlanCatalog.allowance(planType, model: planModel)
            : allowanceDetail
        let primary = QuotaWindow(
            remaining: primaryRemaining,
            used: quotaIsAllowance ? allowance?.used : (builtinUsedPercent ?? builtinRemaining.map { 100 - $0 }),
            resetAt: planModel == .weekly ? nil : resetAt,
            windowMinutes: planModel == .weekly ? nil : windowMinutes,
            badge: primaryBadge,
            title: primaryTitle,
            detail: primaryDetail,
            schedule: primarySchedule
        )
        let secondary = secondaryWindow(
            planType: planType,
            planModel: planModel,
            otherUsedPercent: otherUsedPercent,
            requestBucket: requestBucket,
            resetAt: resetAt,
            windowMinutes: windowMinutes,
            summary: summary,
            dashboard: dashboard,
            planInfo: planInfo
        )

        return QuotaSnapshot(
            planType: planType,
            planNote: PlanCatalog.note(planType, model: planModel),
            primary: primary,
            secondary: secondary,
            sampledAt: sampledAt,
            sourceName: sourceName
        )
    }

    private static func secondaryWindow(
        planType: String?,
        planModel: PlanCatalog.Model,
        otherUsedPercent: Double?,
        requestBucket: (name: String, used: Double, limit: Double)?,
        resetAt: TimeInterval?,
        windowMinutes: Double?,
        summary: [String: Any]?,
        dashboard: [String: Any]?,
        planInfo: [String: Any]?
    ) -> QuotaWindow {
        if PlanCatalog.includesOtherPool(planModel) {
            if let otherUsedPercent {
                return QuotaWindow(
                    remaining: min(100, max(0, 100 - otherUsedPercent)),
                    used: otherUsedPercent,
                    resetAt: resetAt,
                    windowMinutes: windowMinutes,
                    badge: "其他",
                    title: "其他模型剩余",
                    detail: nil,
                    schedule: nil
                )
            }
            if let bucket = requestBucket, bucket.limit > 0 {
                let remaining = min(100, max(0, (bucket.limit - bucket.used) / bucket.limit * 100))
                return QuotaWindow(
                    remaining: remaining,
                    used: 100 - remaining,
                    resetAt: resetAt,
                    windowMinutes: windowMinutes,
                    badge: "其他",
                    title: "其他模型剩余",
                    detail: nil,
                    schedule: nil
                )
            }
            return QuotaWindow(
                remaining: nil,
                used: nil,
                resetAt: resetAt,
                windowMinutes: windowMinutes,
                badge: "其他",
                title: "其他模型剩余",
                detail: nil,
                schedule: nil
            )
        }
        if PlanCatalog.thirdPartyPrepaid(planType, model: planModel) {
            return QuotaWindow(
                remaining: nil,
                used: nil,
                resetAt: nil,
                windowMinutes: nil,
                badge: "预付",
                title: "第三方预付",
                detail: prepaidDetail(summary: summary, dashboard: dashboard, planInfo: planInfo) ?? "按量",
                schedule: "用完为止"
            )
        }
        return QuotaWindow(
            remaining: nil,
            used: nil,
            resetAt: nil,
            windowMinutes: nil,
            badge: "第三方",
            title: "第三方模型",
            detail: "不含",
            schedule: nil
        )
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

    private static func prepaidDetail(summary: [String: Any]?, dashboard: [String: Any]?, planInfo: [String: Any]?) -> String? {
        let individual = JSONValue.dictionary(JSONValue.nested(summary, path: ["individualUsage"]))
        let prepaid = JSONValue.dictionary(JSONValue.nested(summary, path: ["prepaid"]))
        let cents = JSONValue.number(JSONValue.value(summary, names: ["prepaidCreditRemainingCents", "creditBalanceCents"]))
            ?? JSONValue.number(JSONValue.value(individual, names: ["prepaidCreditRemainingCents", "creditBalanceCents"]))
            ?? JSONValue.number(JSONValue.value(prepaid, names: ["remainingCents", "balanceCents"]))
            ?? JSONValue.number(JSONValue.value(dashboard, names: ["prepaidCreditRemainingCents", "creditBalanceCents"]))
            ?? JSONValue.number(JSONValue.value(planInfo, names: ["prepaidCreditRemainingCents", "creditBalanceCents"]))
        guard let cents else { return nil }
        return "余额 " + money(cents)
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
        guard let data = try? Data(contentsOf: url()),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        let sampledAt = JSONValue.date(object["sampledAt"]) ?? .distantPast
        let resetAt = JSONValue.number(object["resetAt"])
        let windowMinutes = JSONValue.number(object["windowMinutes"])
        let primary = QuotaWindow(
            remaining: JSONValue.number(object["totalRemain"]),
            used: JSONValue.number(object["totalUsed"]),
            resetAt: resetAt,
            windowMinutes: windowMinutes,
            badge: remappedBadge(JSONValue.string(object["primaryBadge"]), fallback: "内置"),
            title: JSONValue.string(object["primaryTitle"]) ?? "内置模型剩余",
            detail: JSONValue.string(object["primaryDetail"]),
            schedule: JSONValue.string(object["primarySchedule"])
        )
        let secondary = QuotaWindow(
            remaining: JSONValue.number(object["secondaryRemain"]),
            used: JSONValue.number(object["secondaryUsed"]),
            resetAt: JSONValue.string(object["secondarySchedule"]) == nil ? resetAt : nil,
            windowMinutes: JSONValue.string(object["secondarySchedule"]) == nil ? windowMinutes : nil,
            badge: remappedBadge(JSONValue.string(object["secondaryBadge"]), fallback: "其他"),
            title: JSONValue.string(object["secondaryTitle"]) ?? "其他模型剩余",
            detail: JSONValue.string(object["secondaryDetail"]),
            schedule: JSONValue.string(object["secondarySchedule"])
        )
        guard primary.remaining != nil || secondary.remaining != nil || secondary.detail != nil else { return nil }
        return QuotaSnapshot(
            planType: JSONValue.string(object["planType"]),
            planNote: JSONValue.string(object["planNote"]),
            primary: primary,
            secondary: secondary,
            sampledAt: sampledAt,
            sourceName: "cache"
        )
    }

    static func save(_ snapshot: QuotaSnapshot) {
        let object: [String: Any] = [
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
            "secondarySchedule": snapshot.secondary.schedule ?? NSNull(),
            "resetAt": snapshot.primary.resetAt as Any,
            "windowMinutes": snapshot.primary.windowMinutes as Any,
            "sampledAt": ISO8601DateFormatter().string(from: snapshot.sampledAt),
            "sourceName": snapshot.sourceName
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted]) else { return }
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

        _ = group.wait(timeout: .now() + 18)

        if let snapshot = QuotaParser.snapshot(
            dashboard: dashboard,
            planInfo: planInfo,
            summary: summary,
            authUsage: authUsage,
            stripe: stripe,
            localPlan: session.localPlan,
            sampledAt: Date(),
            sourceName: "cursor-api"
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

@MainActor
final class QuotaModel: ObservableObject {
    @Published private(set) var snapshot: QuotaSnapshot?
    @Published private(set) var fallbackSnapshot: QuotaSnapshot?
    @Published private(set) var connectionError = ""
    @Published private(set) var footer = "正在读取 Cursor 登录态…"

    private let client = CursorUsageClient()
    private let uiSeconds: TimeInterval
    private let fetchSeconds: TimeInterval
    private var uiTimer: Timer?
    private var fetchTimer: Timer?
    private var lastFetch = Date.distantPast

    init(uiSeconds: TimeInterval = 1, fetchSeconds: TimeInterval = 30) {
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
        let shortError = QuotaFormatter.shortError(connectionError)
        let error = value.sourceName == "cursor-api" || shortError.isEmpty ? "" : " · \(shortError)"
        footer = "更新于 \(formatter.string(from: value.sampledAt))\(stale)\(error)"
    }
}

private enum GlassTheme {
    static let mint = Color(red: 22 / 255, green: 148 / 255, blue: 108 / 255)
    static let slateMuted = Color.secondary
    static let warning = Color(red: 0.96, green: 0.60, blue: 0.06)
    static let danger = Color(red: 0.94, green: 0.29, blue: 0.30)
    static let panelWidth: CGFloat = 386
    static let panelHeight: CGFloat = 280
    static let panelRadius: CGFloat = 30
    static let panelInset: CGFloat = 14
    static let cardRadius: CGFloat = panelRadius - panelInset
    static let shadowPadding: CGFloat = 18
    static var windowWidth: CGFloat { panelWidth + shadowPadding * 2 }
    static var windowHeight: CGFloat { panelHeight + shadowPadding * 2 }

    static var panelShape: RoundedRectangle {
        RoundedRectangle(cornerRadius: panelRadius, style: .continuous)
    }

    static var cardShape: RoundedRectangle {
        RoundedRectangle(cornerRadius: cardRadius, style: .continuous)
    }

    static func emphasis(remaining: Double?) -> Color {
        guard let remaining else { return slateMuted.opacity(0.7) }
        if remaining <= 10 { return danger }
        if remaining <= 30 { return warning }
        return mint
    }
}

private final class MaskedGlassView: NSVisualEffectView {
    var cornerRadius: CGFloat = GlassTheme.panelRadius {
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
    var cornerRadius: CGFloat = GlassTheme.panelRadius

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
    @ViewBuilder var content: Content

    @ViewBuilder
    var body: some View {
        if #available(macOS 26.0, *) {
            modernBody
        } else {
            legacyBody
        }
    }

    @available(macOS 26.0, *)
    private var modernBody: some View {
        content
            .frame(width: GlassTheme.panelWidth, height: GlassTheme.panelHeight)
            .glassEffect(.regular, in: GlassTheme.panelShape)
            .containerShape(GlassTheme.panelShape)
    }

    private var legacyBody: some View {
        content
            .background {
                VisualBlur(material: .hudWindow, cornerRadius: GlassTheme.panelRadius)
                    .clipShape(GlassTheme.panelShape)
            }
            .clipShape(GlassTheme.panelShape)
            .containerShape(GlassTheme.panelShape)
            .overlay {
                GlassTheme.panelShape.strokeBorder(.primary.opacity(0.12), lineWidth: 0.5)
            }
            .shadow(color: Color.black.opacity(0.16), radius: 18, x: 0, y: 10)
    }
}

private struct GlassInsetCard<Content: View>: View {
    @ViewBuilder var content: Content

    @ViewBuilder
    var body: some View {
        if #available(macOS 26.0, *) {
            content
                .background(
                    .fill.quaternary,
                    in: ConcentricRectangle(corners: .concentric(minimum: 12), isUniform: true)
                )
        } else {
            content
                .background(GlassTheme.cardShape.fill(.primary.opacity(0.06)))
                .clipShape(GlassTheme.cardShape)
                .overlay {
                    GlassTheme.cardShape.strokeBorder(.primary.opacity(0.08), lineWidth: 0.5)
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
    let action: () -> Void

    private let size: CGFloat = 28

    var body: some View {
        Image(systemName: systemName)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .frame(width: size, height: size)
            .modifier(GlassCircleBackground())
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
        guard let window else { return }
        NSCursor.pointingHand.set()
        var inside = true
        while let next = window.nextEvent(matching: [.leftMouseUp, .leftMouseDragged]) {
            inside = containsLocalPoint(convert(next.locationInWindow, from: nil))
            if next.type == .leftMouseUp { break }
        }
        guard inside else { return }
        let action = action
        DispatchQueue.main.async {
            action?()
        }
    }
}

private struct GlassCircleBackground: ViewModifier {
    @ViewBuilder
    func body(content: Content) -> some View {
        if #available(macOS 26.0, *) {
            content.glassEffect(.regular.interactive(), in: Circle())
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

private struct QuotaRing: View {
    let label: String
    let progress: CGFloat
    let tint: Color

    private let size: CGFloat = 50
    private let lineWidth: CGFloat = 5.5

    var body: some View {
        ZStack {
            Circle()
                .stroke(.quaternary, lineWidth: lineWidth)

            Circle()
                .trim(from: 0, to: progress)
                .stroke(tint, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .scaleEffect(x: -1, y: 1)

            Text(label)
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.secondary)
                .minimumScaleFactor(0.7)
                .lineLimit(1)
        }
        .frame(width: size, height: size)
        .animation(.easeOut(duration: 0.4), value: progress)
    }
}

private struct QuotaCard: View {
    let window: QuotaWindow

    private var tint: Color {
        GlassTheme.emphasis(remaining: window.remaining)
    }

    private var progress: CGFloat {
        CGFloat(min(100, max(0, window.remaining ?? 0)) / 100)
    }

    var body: some View {
        HStack(spacing: 13) {
            QuotaRing(label: window.badge, progress: progress, tint: tint)

            VStack(alignment: .leading, spacing: 4) {
                Text(QuotaFormatter.caption(minutes: window.windowMinutes, fallback: window.title))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(.primary)
                    .lineLimit(1)
                Text(QuotaFormatter.reset(window.resetAt, schedule: window.schedule))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer(minLength: 6)

            VStack(alignment: .trailing, spacing: 2) {
                Text(QuotaFormatter.percent(window.remaining))
                    .font(.system(size: 24, weight: .semibold, design: .rounded))
                    .foregroundStyle(tint)
                    .monospacedDigit()
                    .contentTransition(.numericText())
                Text(
                    window.detail ?? QuotaFormatter.burnRatePerDay(
                        used: window.used,
                        remaining: window.remaining,
                        resetAt: window.resetAt,
                        windowMinutes: window.windowMinutes
                    )
                )
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(.tertiary)
                .lineLimit(1)
                .monospacedDigit()
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
    }
}

struct QuotaView: View {
    @ObservedObject var model: QuotaModel
    let refresh: () -> Void
    let dismiss: () -> Void

    private let placeholder = QuotaWindow(
        remaining: nil,
        used: nil,
        resetAt: nil,
        windowMinutes: nil,
        badge: "—",
        title: "等待额度数据",
        detail: nil,
        schedule: nil
    )

    private var currentSnapshot: QuotaSnapshot? {
        model.snapshot ?? model.fallbackSnapshot
    }

    private var statusTint: Color {
        currentSnapshot?.sourceName == "cursor-api"
            ? GlassTheme.mint
            : GlassTheme.warning
    }

    private var statusBadge: String {
        guard let currentSnapshot else { return "等待" }
        return currentSnapshot.sourceName == "cursor-api" ? "实时" : "快照"
    }

    var body: some View {
        GlassPanel {
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(QuotaFormatter.planTitle(currentSnapshot?.planType))
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundStyle(.primary)
                            .lineLimit(1)

                        Text(
                            currentSnapshot?.planNote
                                ?? QuotaFormatter.resetAt(
                                    currentSnapshot?.primary.resetAt ?? currentSnapshot?.secondary.resetAt
                                )
                                ?? "重置时间未知"
                        )
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .monospacedDigit()
                    }

                    Spacer(minLength: 6)

                    GlassControlGroup {
                        GlassIconButton(systemName: "arrow.clockwise", accessibilityLabel: "刷新额度", action: refresh)
                        GlassIconButton(systemName: "xmark", accessibilityLabel: "关闭详情", action: dismiss)
                    }
                }
                .padding(.leading, 20)
                .padding(.trailing, 16)
                .padding(.top, 16)
                .padding(.bottom, 12)

                GlassInsetCard {
                    VStack(spacing: 0) {
                        QuotaCard(window: currentSnapshot?.primary ?? placeholder)
                        Divider()
                            .padding(.leading, 77)
                            .padding(.trailing, 14)
                        QuotaCard(window: currentSnapshot?.secondary ?? placeholder)
                    }
                }
                .padding(.horizontal, GlassTheme.panelInset)

                Spacer(minLength: 0)

                HStack(spacing: 8) {
                    Text(model.footer)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .monospacedDigit()
                    Spacer(minLength: 5)
                    QuotaStatusLabel(title: statusBadge, tint: statusTint)
                }
                .padding(.leading, 20)
                .padding(.trailing, 16)
                .padding(.bottom, 14)
            }
            .frame(width: GlassTheme.panelWidth, height: GlassTheme.panelHeight)
        }
        .padding(GlassTheme.shadowPadding)
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
                height: GlassTheme.windowHeight
            )
        )
        panel.contentViewController = hostingController
        panel.contentView?.wantsLayer = true
        panel.contentView?.layer?.isOpaque = false
        panel.contentView?.layer?.backgroundColor = NSColor.clear.cgColor
        panel.contentView?.layer?.masksToBounds = false
        panel.setContentSize(NSSize(width: GlassTheme.windowWidth, height: GlassTheme.windowHeight))
    }

    func show(relativeTo statusView: NSView) {
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
        let size = NSSize(width: GlassTheme.windowWidth, height: GlassTheme.windowHeight)
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
        static let iconSize: CGFloat = 22
        static let iconLeading: CGFloat = 1
        static let iconTextGap: CGFloat = 2
        static let trailing: CGFloat = 5
        static let lineHeight: CGFloat = 11
        static let textInset: CGFloat = 2
    }

    private let iconView = NSImageView()
    private let primaryLabel = NSTextField(labelWithString: "内置 —")
    private let secondaryLabel = NSTextField(labelWithString: "其他 —")
    private var isPressed = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        setAccessibilityRole(.button)
        setAccessibilityLabel("Cursor 剩余额度")

        let symbol = NSImage(systemSymbolName: "gauge.medium", accessibilityDescription: "Cursor 额度")
        iconView.image = symbol?.withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 18, weight: .medium))
        iconView.imageScaling = .scaleProportionallyUpOrDown
        iconView.contentTintColor = .secondaryLabelColor
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

    override func layout() {
        super.layout()
        iconView.frame = NSRect(x: Metrics.iconLeading, y: 0, width: Metrics.iconSize, height: Metrics.iconSize)
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
            measuredTextWidth("其他 100%"),
            measuredTextWidth("本周 100%"),
            measuredTextWidth("第三方 不含"),
            measuredTextWidth("预付 按量")
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

    func update(snapshot: QuotaSnapshot?) {
        let primary = snapshot?.primary
        let secondary = snapshot?.secondary
        primaryLabel.stringValue = statusLine(primary, fallback: "内置")
        secondaryLabel.stringValue = statusLine(secondary, fallback: "其他")
        primaryLabel.textColor = statusColor(primary?.remaining)
        secondaryLabel.textColor = statusColor(secondary?.remaining)
        let source: String
        if snapshot?.sourceName == "cursor-api" {
            source = "实时"
        } else if snapshot == nil {
            source = "等待数据"
        } else {
            source = "快照"
        }
        toolTip = "Cursor 额度 · \(source)"
        needsLayout = true
        needsDisplay = true
    }

    private func statusLine(_ window: QuotaWindow?, fallback: String) -> String {
        let badge = window?.badge ?? fallback
        if window?.remaining == nil, let detail = window?.detail, !detail.isEmpty {
            return "\(badge) \(detail)"
        }
        return "\(badge) \(QuotaFormatter.percent(window?.remaining))"
    }

    private func statusColor(_ remaining: Double?) -> NSColor {
        guard let remaining else { return .secondaryLabelColor }
        if remaining <= 10 { return NSColor(red: 0.94, green: 0.29, blue: 0.30, alpha: 1) }
        if remaining <= 30 { return NSColor(red: 0.96, green: 0.60, blue: 0.06, alpha: 1) }
        return .secondaryLabelColor
    }

    override func draw(_ dirtyRect: NSRect) {
        if isPressed {
            NSColor.selectedMenuItemColor.withAlphaComponent(0.25).setFill()
            dirtyRect.fill()
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
    private let model = QuotaModel()
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
    }

    func applicationWillTerminate(_ notification: Notification) {
        removePopoverDismissMonitors()
        model.stop()
    }

    private func buildDetailPanel() {
        detailPanel = QuotaDetailPanelController(
            model: model,
            refresh: { [weak self] in self?.model.refresh(forceNetwork: true) },
            dismiss: { [weak self] in self?.closePopover() }
        )
    }

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusView = QuotaStatusView(frame: NSRect(x: 0, y: 0, width: 96, height: 22))
        statusView.onLeftClick = { [weak self] in self?.togglePopover() }
        statusView.onLeftMouseUp = { [weak self] in self?.detailPanel.scheduleActivate() }
        statusView.onRightClick = { [weak self] in self?.showStatusMenu() }
        statusItem.view = statusView
        statusItem.length = statusView.preferredWidth()

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
        statusView.update(snapshot: model.snapshot ?? model.fallbackSnapshot)
        let width = statusView.preferredWidth()
        statusView.frame = NSRect(x: 0, y: 0, width: width, height: 22)
        statusItem.length = width
        statusView.needsLayout = true
        statusView.layoutSubtreeIfNeeded()
    }

    @objc private func showPopover() {
        guard let statusView else { return }
        if !detailPanel.isShown {
            detailPanel.show(relativeTo: statusView)
        }
    }

    @objc private func closePopover() {
        detailPanel.close()
    }

    private func togglePopover() {
        guard let statusView else { return }
        detailPanel.toggle(relativeTo: statusView)
    }

    @objc private func refreshQuota() {
        model.refresh(forceNetwork: true)
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
        let result: [String: Any] = [
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
        if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]),
           let text = String(data: data, encoding: .utf8) {
            print(text)
        }
        return 0
    }

    if let fallback = SnapshotCache.load() {
        let result: [String: Any] = [
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
        if CommandLine.arguments.contains("--probe") {
            Foundation.exit(runProbe())
        }

        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        application.run()
    }
}
