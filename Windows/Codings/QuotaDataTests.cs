using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Security.Cryptography;

internal static class QuotaDataTests
{
    public static void Run()
    {
        TestCredentialCodec();
        Dictionary<string, object> dashboard = Map("{\"planUsage\":{\"autoPercentUsed\":99,\"totalPercentUsed\":12.5,\"includedSpend\":2000,\"limit\":2000},\"billingCycleEnd\":\"2026-11-30T00:00:00Z\"}");
        Dictionary<string, object> plan = Map("{\"planInfo\":{\"planName\":\"pro\",\"includedUsagePeriod\":2,\"includedUsageResetsAt\":\"2026-11-02T00:00:00Z\"}}");
        Dictionary<string, object> summary = Map("{\"individualUsage\":{\"plan\":{\"used\":2000,\"limit\":2000,\"totalPercentUsed\":100,\"apiPercentUsed\":99}}}");
        QuotaSnapshot weekly = Parse(dashboard, plan, summary, null, null);
        Equal(weekly.Primary.Remaining, 87.5, "weekly current-period total overrides monthly amount and auto");
        Equal(weekly.Primary.WindowMinutes, 10080, "weekly window");
        Equal(weekly.Primary.ResetAt, JsonValue.UnixSeconds("2026-11-02T00:00:00Z").Value, "included reset overrides month");
        Check(weekly.Primary.Title == "周限额" && !weekly.Secondary.Remaining.HasValue, "weekly title and no legacy API pool");

        var namedWeekly = Map("{\"includedUsagePeriod\":\"INCLUDED_USAGE_PERIOD_WEEKLY\"}");
        Equal(Parse(dashboard, namedWeekly, summary, null, null).Primary.Remaining, 87.5, "weekly enum string");
        var noCurrent = Parse(null, plan, summary, null, null);
        Check(noCurrent != null && !noCurrent.Primary.Remaining.HasValue, "missing weekly sample must not use monthly summary");
        Check(!CursorUsageClient.HasPrimaryData(noCurrent) && noCurrent.SourceName == "partial", "missing primary retains partial snapshot status");
        Check(CursorUsageClient.ShouldTryAnotherCandidate(noCurrent, true), "partial current-period unauthorized must try next credential");
        Check(!CursorUsageClient.ShouldTryAnotherCandidate(noCurrent, false), "partial without authorization failure does not retry credentials");
        Check(QuotaFormatter.StatusBadge(noCurrent) == "部分数据" && QuotaFormatter.Footer(noCurrent, "主额度暂不可用").Contains("部分数据"), "partial status never labeled live");
        Check(CursorUsageClient.HasPrimaryData(weekly) && !CursorUsageClient.ShouldTryAnotherCandidate(weekly, true), "complete primary ends candidate fallback even if optional endpoint unauthorized");
        Check(CursorUsageClient.CandidateBudgetMilliseconds(20000, 4) == 5000, "slow optional endpoint cannot consume other candidates budget");
        Check(CursorUsageClient.CandidateBudgetMilliseconds(12000, 3) == 4000, "remaining candidates share remaining time");
        Check(CursorUsageClient.CandidateBudgetMilliseconds(6000, 1) == 6000, "last candidate can use remaining budget");
        Check(!Parse(dashboard, namedWeekly, null, null, null).Primary.ResetAt.HasValue, "weekly reset must not fall back to monthly date");
        Equal(Parse(Map("{\"planUsage\":{\"totalPercentUsed\":100}}"), plan,
            Map("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":0,\"remaining\":2000,\"limit\":2000}}}"), null, null).Primary.Remaining,
            0, "exhausted weekly current response overrides unused monthly summary");
        Check(Parse(null, null, null, null, null) == null, "empty endpoints rejected");

        var grant = Map("{\"hasCreditGrants\":true,\"totalCents\":2000,\"creditBalanceCents\":1500,\"usedCents\":500}");
        var prepaid = Map("{\"availableCents\":700,\"fundedCents\":1000,\"spentCents\":300}");
        var credited = Parse(dashboard, plan, summary, grant, prepaid);
        Equal(credited.Secondary.Remaining, 75, "grant priority over prepaid");
        Equal(credited.Secondary.RemainingAmountCents, 1500, "grant balance cents");
        Equal(credited.Secondary.LimitAmountCents, 2000, "grant total cents");
        Check(credited.Secondary.Detail == "已用 $5.00" && !credited.Secondary.ResetAt.HasValue, "extra amount detail and no monthly reset");
        var funded = Parse(dashboard, plan, null, Map("{\"hasCreditGrants\":false,\"totalCents\":0,\"creditBalanceCents\":0}"), prepaid);
        Equal(funded.Secondary.Remaining, 70, "prepaid fallback");
        var zero = Parse(null, null, null, Map("{\"hasCreditGrants\":true,\"totalCents\":2000,\"creditBalanceCents\":0}"), prepaid);
        Equal(zero.Secondary.Remaining, 0, "exhausted grant remains authoritative");
        var unknownTotal = Parse(null, null, null, null, Map("{\"availableCents\":700}"));
        Check(unknownTotal != null && !unknownTotal.Secondary.Remaining.HasValue, "amount-only response valid without invented percentage");
        Check(!CursorUsageClient.HasPrimaryData(unknownTotal) && unknownTotal.SourceName == "partial", "extra-only response is partial");
        Equal(unknownTotal.Secondary.RemainingAmountCents, 700, "extra-only partial preserves balance");
        var emptyFund = Parse(null, null, null, null, Map("{\"availableCents\":0,\"fundedCents\":0}"));
        Check(emptyFund != null && emptyFund.Secondary.CurrencyCode == "USD" && !emptyFund.Secondary.Remaining.HasValue, "zero total preserves amount without division by zero");
        Equal(emptyFund.Secondary.RemainingAmountCents, 0, "zero prepaid balance");

        var legacy = Parse(Map("{\"planUsage\":{\"autoPercentUsed\":4,\"includedSpend\":500,\"limit\":2000,\"apiPercentUsed\":0.7,\"apiLimit\":1000,\"apiSpend\":7}}"), null, null, null, null);
        Equal(legacy.Primary.Remaining, 75, "monthly amount authority matches Mac");
        Equal(legacy.Secondary.Remaining, 99.3, "percentage points below one");
        Equal(legacy.Secondary.RemainingAmountCents, 993, "legacy other amounts");

        Check(SnapshotCache.Parse("{\"quotaFormatVersion\":2,\"totalRemain\":90}") == null, "old semantic cache rejected");
        Check(SnapshotCache.Parse("{\"totalRemain\":90}") == null, "unversioned cache rejected");
        Check(SnapshotCache.Parse("{\"quotaFormatVersion\":1,\"totalRemain\":90}") == null, "v1 cache rejected");
        Check(SnapshotCache.Parse("{malformed json") == null, "malformed cache rejected");
        var cache = SnapshotCache.Parse("{\"quotaFormatVersion\":3,\"totalRemain\":80,\"resetAt\":1800000000,\"windowMinutes\":10080,\"secondaryRemainingAmountCents\":1250,\"secondaryLimitAmountCents\":2000,\"secondaryCurrencyCode\":\"USD\",\"secondaryResetAt\":null,\"secondaryWindowMinutes\":null}");
        Equal(cache.Secondary.RemainingAmountCents, 1250, "cache amounts");
        Check(!cache.Secondary.ResetAt.HasValue && cache.Secondary.CurrencyCode == "USD", "cache extra reset independent");
        var amountCache = SnapshotCache.Parse("{\"quotaFormatVersion\":3,\"secondaryRemainingAmountCents\":1250}");
        Check(amountCache != null, "amount-only cache valid");
        Check(QuotaFormatter.Footer(weekly, "网络不可用").Contains("快照") && QuotaFormatter.Footer(weekly, "网络不可用").Contains("网络"), "live refresh failure visible over previous sample");
        Check(SessionStore.BearerToken("fixture-user%3a%3afixture-token") == "fixture-token", "lowercase encoded session separator");
        Check(SessionStore.CookieValue("fixture-user%3a%3afixture-token") == "fixture-user::fixture-token", "encoded cookie separator");
        List<SessionInfo> candidates = new List<SessionInfo>();
        MethodInfo add = typeof(SessionStore).GetMethod("AddCandidate", BindingFlags.NonPublic | BindingFlags.Static);
        add.Invoke(null, new object[] { candidates, "fixture-user::fixture-token", "config", null });
        add.Invoke(null, new object[] { candidates, "fixture-token", "cursor-app", null });
        add.Invoke(null, new object[] { candidates, "fixture-second", "auth.json", null });
        Check(candidates.Count == 2 && candidates[0].Source == "config" && candidates[1].Source == "auth.json", "candidate dedup preserves source priority and alternatives");
        add.Invoke(null, new object[] { candidates, CursorCredentialCodec.Prefix + "invalid", "cursor-app", null });
        Check(candidates.Count == 2, "undecodable encrypted credential never becomes bearer candidate");
    }

    private static void TestCredentialCodec()
    {
        string plain = "fixture-header.fixture-payload.fixture-signature";
        Check(CursorCredentialCodec.Decode(plain) == plain, "legacy plaintext credential preserved");
        Check(CursorCredentialCodec.Decode(CursorCredentialCodec.Prefix + "invalid") == null, "malformed encrypted wrapper rejected");
        Check(CursorCredentialCodec.Decode("cursor-safe-storage:v2:fixture") == null, "unsupported wrapper version never sent as bearer");
        Check(CursorCredentialCodec.Decode(CursorCredentialCodec.Prefix + Convert.ToBase64String(new byte[40])) == null, "unsupported encrypted payload rejected");
        // NIST AES-GCM: zero 128-bit key, zero 96-bit IV, zero 128-bit plaintext.
        byte[] payload = Hex("7631300000000000000000000000000388dace60b6a392f328c2b971b2fe78ab6e47d42cec13bdf53a67b21257bddf");
        byte[] decrypted = CursorCredentialCodec.DecryptPayload(payload, new byte[16]);
        Check(decrypted.Length == 16, "AES-GCM fixture length");
        foreach (byte value in decrypted) Check(value == 0, "AES-GCM fixture plaintext");
        payload[payload.Length - 1] ^= 1;
        bool rejected = false;
        try { CursorCredentialCodec.DecryptPayload(payload, new byte[16]); }
        catch (CryptographicException) { rejected = true; }
        Check(rejected, "AES-GCM authentication rejects tampered tag");
    }

    private static byte[] Hex(string text)
    {
        byte[] bytes = new byte[text.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static QuotaSnapshot Parse(Dictionary<string, object> dashboard, Dictionary<string, object> plan,
        Dictionary<string, object> summary, Dictionary<string, object> credit, Dictionary<string, object> prepaid)
    {
        return QuotaParser.Snapshot(dashboard, plan, summary, null, null, null, DateTime.UtcNow, "cursor-api", credit, prepaid);
    }
    private static Dictionary<string, object> Map(string json)
    {
        return JsonValue.Map(new JavaScriptSerializer().DeserializeObject(json));
    }
    private static void Equal(double? actual, double expected, string name)
    {
        Check(actual.HasValue && Math.Abs(actual.Value - expected) < 0.000001, name);
    }
    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception("quota-data: " + name);
    }
}
