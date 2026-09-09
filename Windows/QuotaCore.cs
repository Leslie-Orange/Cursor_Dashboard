using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class QuotaWindow
{
    public double? Remaining;
    public double? Used;
    public double? ResetAt;
    public double? WindowMinutes;
    public string Badge;
    public string Title;
    public string Detail;
}

internal sealed class QuotaSnapshot
{
    public string PlanType;
    public QuotaWindow Primary;
    public QuotaWindow Secondary;
    public DateTime SampledAt;
    public string SourceName;
}

internal static class JsonUtil
{
    public static double? Number(object value)
    {
        if (value == null || value is DBNull)
        {
            return null;
        }
        if (value is string)
        {
            double parsed;
            if (double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }
        try
        {
            if (value is bool)
            {
                return null;
            }
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static string String(object value)
    {
        string text = value as string;
        if (text != null && text.Length > 0)
        {
            return text;
        }
        return null;
    }

    public static Dictionary<string, object> Dictionary(object value)
    {
        return value as Dictionary<string, object>;
    }

    public static object Value(Dictionary<string, object> dictionary, params string[] names)
    {
        if (dictionary == null)
        {
            return null;
        }
        for (int i = 0; i < names.Length; i++)
        {
            object value;
            if (dictionary.TryGetValue(names[i], out value) && value != null && !(value is DBNull))
            {
                return value;
            }
        }
        return null;
    }

    public static object Nested(Dictionary<string, object> root, params string[] path)
    {
        object current = root;
        for (int i = 0; i < path.Length; i++)
        {
            Dictionary<string, object> dict = Dictionary(current);
            if (dict == null)
            {
                return null;
            }
            if (!dict.TryGetValue(path[i], out current))
            {
                return null;
            }
        }
        return current;
    }

    public static double? Percent(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }
        double scaled = value.Value <= 1.0000001 ? value.Value * 100.0 : value.Value;
        if (scaled < 0) scaled = 0;
        if (scaled > 100) scaled = 100;
        return scaled;
    }

    public static DateTime? Date(object value)
    {
        double? number = Number(value);
        if (number.HasValue)
        {
            double interval = number.Value > 10000000000.0 ? number.Value / 1000.0 : number.Value;
            if (interval > 1000000.0)
            {
                return Unix.FromSeconds(interval);
            }
        }

        string text = String(value);
        if (text == null)
        {
            return null;
        }

        double parsed;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return Date(parsed);
        }

        DateTime date;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
        {
            return date.ToUniversalTime();
        }
        return null;
    }
}

internal static class Unix
{
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime FromSeconds(double seconds)
    {
        return Epoch.AddSeconds(seconds);
    }

    public static double ToSeconds(DateTime date)
    {
        return (date.ToUniversalTime() - Epoch).TotalSeconds;
    }
}

internal static class QuotaParser
{
    public static QuotaSnapshot Snapshot(
        Dictionary<string, object> dashboard,
        Dictionary<string, object> planInfo,
        Dictionary<string, object> summary,
        Dictionary<string, object> authUsage,
        Dictionary<string, object> stripe,
        string localPlan,
        DateTime sampledAt,
        string sourceName)
    {
        Dictionary<string, object> planUsage = JsonUtil.Dictionary(JsonUtil.Value(dashboard, "planUsage"));
        if (planUsage == null)
        {
            planUsage = JsonUtil.Dictionary(JsonUtil.Nested(summary, "individualUsage", "plan"));
        }
        Dictionary<string, object> summaryPlan = JsonUtil.Dictionary(JsonUtil.Nested(summary, "individualUsage", "plan"));

        double? autoUsedPercent = FirstPercent(
            JsonUtil.Value(planUsage, "autoPercentUsed"),
            JsonUtil.Value(summaryPlan, "autoPercentUsed"),
            PercentFromMessage(JsonUtil.Value(summary, "autoModelSelectedDisplayMessage")));
        double? otherUsedPercent = FirstPercent(
            JsonUtil.Value(planUsage, "apiPercentUsed"),
            JsonUtil.Value(summaryPlan, "apiPercentUsed"),
            PercentFromMessage(JsonUtil.Value(summary, "namedModelSelectedDisplayMessage")));
        double? totalUsedPercent = FirstPercent(
            JsonUtil.Value(planUsage, "totalPercentUsed"),
            JsonUtil.Value(summaryPlan, "totalPercentUsed"));

        double? centsLimit = JsonUtil.Number(JsonUtil.Value(planUsage, "limit"));
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonUtil.Number(JsonUtil.Nested(planInfo, "planInfo", "includedAmountCents"));
        }
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonUtil.Number(JsonUtil.Value(planInfo, "includedAmountCents"));
        }
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonUtil.Number(JsonUtil.Value(summaryPlan, "limit"));
        }

        double? includedSpend = JsonUtil.Number(JsonUtil.Value(planUsage, "includedSpend"));
        if (!includedSpend.HasValue)
        {
            includedSpend = JsonUtil.Number(JsonUtil.Value(summaryPlan, "used"));
        }

        double? centsRemaining = JsonUtil.Number(JsonUtil.Value(planUsage, "remaining"));
        if (!centsRemaining.HasValue)
        {
            centsRemaining = JsonUtil.Number(JsonUtil.Value(summaryPlan, "remaining"));
        }
        if (!centsRemaining.HasValue)
        {
            centsRemaining = CombinedRemaining(includedSpend, centsLimit);
        }

        double? includedUsedPercent = null;
        if (centsLimit.HasValue && centsLimit.Value > 0)
        {
            if (centsRemaining.HasValue)
            {
                includedUsedPercent = JsonUtil.Percent(1.0 - centsRemaining.Value / centsLimit.Value);
            }
            else if (includedSpend.HasValue)
            {
                includedUsedPercent = JsonUtil.Percent(includedSpend.Value / centsLimit.Value);
            }
        }

        double? builtinUsedPercent = autoUsedPercent;
        if (!builtinUsedPercent.HasValue)
        {
            builtinUsedPercent = includedUsedPercent;
        }
        if (!builtinUsedPercent.HasValue)
        {
            builtinUsedPercent = totalUsedPercent;
        }

        double? builtinRemaining = null;
        if (builtinUsedPercent.HasValue)
        {
            double remain = 100.0 - builtinUsedPercent.Value;
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            builtinRemaining = remain;
        }
        if (!builtinRemaining.HasValue && centsLimit.HasValue && centsLimit.Value > 0 && centsRemaining.HasValue)
        {
            builtinRemaining = JsonUtil.Percent(centsRemaining.Value / centsLimit.Value);
        }

        RequestBucket requestBucket = RequestBucketFrom(authUsage);
        if (requestBucket == null)
        {
            requestBucket = RequestBucketFrom(summaryPlan);
        }
        if (!builtinRemaining.HasValue && requestBucket != null && requestBucket.Limit > 0)
        {
            double remain = (requestBucket.Limit - requestBucket.Used) / requestBucket.Limit * 100.0;
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            builtinRemaining = remain;
        }

        DateTime? cycleStart = FirstDate(
            JsonUtil.Value(dashboard, "billingCycleStart"),
            JsonUtil.Value(summary, "billingCycleStart"),
            JsonUtil.Value(authUsage, "startOfMonth"));
        DateTime? cycleEnd = FirstDate(
            JsonUtil.Value(dashboard, "billingCycleEnd"),
            JsonUtil.Value(summary, "billingCycleEnd"),
            JsonUtil.Nested(planInfo, "planInfo", "billingCycleEnd"),
            JsonUtil.Value(planInfo, "billingCycleEnd"));
        if (!cycleEnd.HasValue && cycleStart.HasValue)
        {
            cycleEnd = cycleStart.Value.AddDays(30);
        }

        double? resetAt = null;
        if (cycleEnd.HasValue)
        {
            resetAt = Unix.ToSeconds(cycleEnd.Value);
        }

        double? windowMinutes;
        if (cycleStart.HasValue && cycleEnd.HasValue)
        {
            windowMinutes = Math.Max(1, (cycleEnd.Value - cycleStart.Value).TotalMinutes);
        }
        else
        {
            windowMinutes = 30.0 * 24.0 * 60.0;
        }

        string planType = JsonUtil.String(JsonUtil.Nested(planInfo, "planInfo", "planName"));
        if (planType == null)
        {
            planType = JsonUtil.String(JsonUtil.Value(planInfo, "planName", "planType"));
        }
        if (planType == null)
        {
            planType = JsonUtil.String(JsonUtil.Value(summary, "membershipType"));
        }
        if (planType == null)
        {
            planType = JsonUtil.String(JsonUtil.Value(stripe, "membershipType", "individualMembershipType"));
        }
        if (planType == null)
        {
            planType = localPlan;
        }

        Dictionary<string, object> onDemand = JsonUtil.Dictionary(JsonUtil.Nested(summary, "individualUsage", "onDemand"));
        bool onDemandEnabled = false;
        if (onDemand != null && onDemand.ContainsKey("enabled") && onDemand["enabled"] is bool)
        {
            onDemandEnabled = (bool)onDemand["enabled"];
        }
        double? onDemandUsed = JsonUtil.Number(JsonUtil.Value(onDemand, "used"));

        if (!builtinRemaining.HasValue && !otherUsedPercent.HasValue && requestBucket == null)
        {
            return null;
        }

        string totalDetail = AmountDetail(includedSpend, centsRemaining, centsLimit, requestBucket, onDemandEnabled, onDemandUsed);

        QuotaWindow primary = new QuotaWindow();
        primary.Remaining = builtinRemaining;
        if (builtinUsedPercent.HasValue)
        {
            primary.Used = builtinUsedPercent;
        }
        else if (builtinRemaining.HasValue)
        {
            primary.Used = 100.0 - builtinRemaining.Value;
        }
        primary.ResetAt = resetAt;
        primary.WindowMinutes = windowMinutes;
        primary.Badge = "内置";
        primary.Title = "内置模型剩余";
        primary.Detail = totalDetail;

        QuotaWindow secondary = new QuotaWindow();
        if (otherUsedPercent.HasValue)
        {
            double remain = 100.0 - otherUsedPercent.Value;
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            secondary.Remaining = remain;
            secondary.Used = otherUsedPercent;
            secondary.ResetAt = resetAt;
            secondary.WindowMinutes = windowMinutes;
            secondary.Badge = "其他";
            secondary.Title = "其他模型剩余";
            secondary.Detail = PercentDetail(otherUsedPercent.Value, "已用");
        }
        else if (requestBucket != null && requestBucket.Limit > 0)
        {
            double remain = (requestBucket.Limit - requestBucket.Used) / requestBucket.Limit * 100.0;
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            secondary.Remaining = remain;
            secondary.Used = 100.0 - remain;
            secondary.ResetAt = resetAt;
            secondary.WindowMinutes = windowMinutes;
            secondary.Badge = "其他";
            secondary.Title = "其他模型剩余";
            secondary.Detail = string.Format(CultureInfo.InvariantCulture, "{0:0} / {1:0} 次", requestBucket.Used, requestBucket.Limit);
        }
        else
        {
            secondary.Remaining = null;
            secondary.Used = null;
            secondary.ResetAt = resetAt;
            secondary.WindowMinutes = windowMinutes;
            secondary.Badge = "其他";
            secondary.Title = "其他模型剩余";
            secondary.Detail = null;
        }

        QuotaSnapshot snapshot = new QuotaSnapshot();
        snapshot.PlanType = planType;
        snapshot.Primary = primary;
        snapshot.Secondary = secondary;
        snapshot.SampledAt = sampledAt;
        snapshot.SourceName = sourceName;
        return snapshot;
    }

    private static DateTime? FirstDate(params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            DateTime? date = JsonUtil.Date(values[i]);
            if (date.HasValue)
            {
                return date;
            }
        }
        return null;
    }

    private static double? FirstPercent(params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            double? percent = JsonUtil.Percent(JsonUtil.Number(values[i]));
            if (percent.HasValue)
            {
                return percent;
            }
            percent = PercentFromMessage(values[i]);
            if (percent.HasValue)
            {
                return percent;
            }
        }
        return null;
    }

    private static double? PercentFromMessage(object value)
    {
        string text = JsonUtil.String(value);
        if (text == null)
        {
            return null;
        }
        Match match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        if (!match.Success)
        {
            return null;
        }
        double number;
        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return null;
        }
        return JsonUtil.Percent(number);
    }

    private static double? CombinedRemaining(double? used, double? limit)
    {
        if (!used.HasValue || !limit.HasValue)
        {
            return null;
        }
        return Math.Max(0, limit.Value - used.Value);
    }

    private static string PercentDetail(double used, string label)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0} {1:0}%", label, used);
    }

    private static string AmountDetail(
        double? used,
        double? remaining,
        double? limit,
        RequestBucket request,
        bool onDemandEnabled,
        double? onDemandUsed)
    {
        List<string> parts = new List<string>();
        if (limit.HasValue && limit.Value > 0 && LooksLikeCents(limit.Value))
        {
            double usedValue = used.HasValue ? used.Value : (limit.Value - (remaining.HasValue ? remaining.Value : limit.Value));
            parts.Add(string.Format(CultureInfo.InvariantCulture, "${0:0.00} / ${1:0.00}", usedValue / 100.0, limit.Value / 100.0));
        }
        else if (request != null && request.Limit > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} / {1:0} 次", request.Used, request.Limit));
        }
        if (onDemandEnabled && onDemandUsed.HasValue && onDemandUsed.Value > 0)
        {
            string display = LooksLikeCents(onDemandUsed.Value)
                ? string.Format(CultureInfo.InvariantCulture, "按量 ${0:0.00}", onDemandUsed.Value / 100.0)
                : string.Format(CultureInfo.InvariantCulture, "按量 {0:0}", onDemandUsed.Value);
            parts.Add(display);
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
    }

    private static bool LooksLikeCents(double value)
    {
        return value >= 100;
    }

    private sealed class RequestBucket
    {
        public string Name;
        public double Used;
        public double Limit;
    }

    private static RequestBucket RequestBucketFrom(Dictionary<string, object> root)
    {
        if (root == null)
        {
            return null;
        }

        string[] preferred = new string[] { "gpt-4", "gpt-4o", "default", "composer" };
        List<RequestBucket> found = new List<RequestBucket>();
        Walk(root, "", delegate(string key, Dictionary<string, object> obj)
        {
            double? used = JsonUtil.Number(JsonUtil.Value(obj, "numRequests", "used", "requests"));
            double? limit = JsonUtil.Number(JsonUtil.Value(obj, "maxRequestUsage", "limit", "maxRequests", "requestLimit"));
            if (used.HasValue && limit.HasValue && limit.Value > 0)
            {
                RequestBucket bucket = new RequestBucket();
                bucket.Name = key;
                bucket.Used = used.Value;
                bucket.Limit = limit.Value;
                found.Add(bucket);
            }
        });

        for (int i = 0; i < preferred.Length; i++)
        {
            string name = preferred[i];
            for (int j = 0; j < found.Count; j++)
            {
                if (found[j].Name == name || found[j].Name.EndsWith("." + name, StringComparison.Ordinal))
                {
                    found[j].Name = ShortBucketName(found[j].Name);
                    return found[j];
                }
            }
        }

        if (found.Count > 0)
        {
            found[0].Name = ShortBucketName(found[0].Name);
            return found[0];
        }

        double? usedRoot = JsonUtil.Number(JsonUtil.Value(root, "used"));
        double? limitRoot = JsonUtil.Number(JsonUtil.Value(root, "limit"));
        if (usedRoot.HasValue && limitRoot.HasValue && limitRoot.Value > 0)
        {
            RequestBucket bucket = new RequestBucket();
            bucket.Name = "请求";
            bucket.Used = usedRoot.Value;
            bucket.Limit = limitRoot.Value;
            return bucket;
        }
        return null;
    }

    private static string ShortBucketName(string key)
    {
        string last = key;
        int dot = key.LastIndexOf('.');
        if (dot >= 0 && dot < key.Length - 1)
        {
            last = key.Substring(dot + 1);
        }
        if (last.Length <= 6)
        {
            return last;
        }
        return last.Substring(0, 6);
    }

    private delegate void WalkVisitor(string key, Dictionary<string, object> obj);

    private static void Walk(Dictionary<string, object> obj, string prefix, WalkVisitor visit)
    {
        visit(prefix.Length == 0 ? "root" : prefix, obj);
        foreach (KeyValuePair<string, object> pair in obj)
        {
            Dictionary<string, object> nested = JsonUtil.Dictionary(pair.Value);
            if (nested == null)
            {
                continue;
            }
            string next = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
            Walk(nested, next, visit);
        }
    }
}

internal sealed class SessionInfo
{
    public string Token;
    public string Source;
    public string LocalPlan;
}

internal static class SessionStore
{
    public static SessionInfo Load()
    {
        string localPlan = SqliteValue("cursorAuth/stripeMembershipType");
        string raw = Environment.GetEnvironmentVariable("CURSOR_SESSION_TOKEN");
        string token = Normalize(raw);
        if (token != null)
        {
            return Make(token, "env", localPlan);
        }

        token = Normalize(ReadFile(ConfigPath()));
        if (token != null)
        {
            return Make(token, "config", localPlan);
        }

        token = Normalize(SqliteValue("cursorAuth/accessToken"));
        if (token != null)
        {
            return Make(token, "cursor-app", localPlan);
        }

        return null;
    }

    public static string CookieValue(string token)
    {
        if (token.Contains("::") || token.Contains("%3A%3A"))
        {
            return token.Replace("%3A%3A", "::");
        }
        string sub = JwtSubject(token);
        if (sub != null)
        {
            return sub + "::" + token;
        }
        return token;
    }

    public static string BearerToken(string token)
    {
        if (token.Contains("%3A%3A"))
        {
            string[] parts = token.Split(new string[] { "%3A%3A" }, StringSplitOptions.None);
            return parts[parts.Length - 1];
        }
        if (token.Contains("::"))
        {
            string[] parts = token.Split(new string[] { "::" }, StringSplitOptions.None);
            return parts[parts.Length - 1];
        }
        return token;
    }

    public static string ConfigPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CursorQuotaPet", "session-token");
    }

    private static SessionInfo Make(string token, string source, string localPlan)
    {
        SessionInfo info = new SessionInfo();
        info.Token = token;
        info.Source = source;
        info.LocalPlan = localPlan;
        return info;
    }

    private static string Normalize(string raw)
    {
        if (raw == null)
        {
            return null;
        }
        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }
        return trimmed;
    }

    private static string ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        return trimmed;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string SqliteValue(string key)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cursor", "User", "globalStorage", "state.vscdb");
        return SqliteKv.Read(path, key);
    }

    private static string JwtSubject(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        int remainder = payload.Length % 4;
        if (remainder > 0)
        {
            payload += new string('=', 4 - remainder);
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(payload);
            string json = Encoding.UTF8.GetString(bytes);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> obj = serializer.DeserializeObject(json) as Dictionary<string, object>;
            return JsonUtil.String(JsonUtil.Value(obj, "sub"));
        }
        catch
        {
            return null;
        }
    }
}

internal static class SnapshotCache
{
    public static string PathName()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "com.local.cursor.quotapet",
            "last-snapshot.json");
    }

    public static QuotaSnapshot Load()
    {
        try
        {
            string path = PathName();
            if (!File.Exists(path))
            {
                return null;
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> obj = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (obj == null)
            {
                return null;
            }

            DateTime? sampledAt = JsonUtil.Date(JsonUtil.Value(obj, "sampledAt"));
            double? resetAt = JsonUtil.Number(JsonUtil.Value(obj, "resetAt"));
            double? windowMinutes = JsonUtil.Number(JsonUtil.Value(obj, "windowMinutes"));

            QuotaWindow primary = new QuotaWindow();
            primary.Remaining = JsonUtil.Number(JsonUtil.Value(obj, "totalRemain"));
            primary.Used = JsonUtil.Number(JsonUtil.Value(obj, "totalUsed"));
            primary.ResetAt = resetAt;
            primary.WindowMinutes = windowMinutes;
            primary.Badge = RemappedBadge(JsonUtil.String(JsonUtil.Value(obj, "primaryBadge")), "内置");
            string primaryTitle = JsonUtil.String(JsonUtil.Value(obj, "primaryTitle"));
            primary.Title = primaryTitle != null ? primaryTitle : "内置模型剩余";
            primary.Detail = JsonUtil.String(JsonUtil.Value(obj, "primaryDetail"));

            QuotaWindow secondary = new QuotaWindow();
            secondary.Remaining = JsonUtil.Number(JsonUtil.Value(obj, "secondaryRemain"));
            secondary.Used = JsonUtil.Number(JsonUtil.Value(obj, "secondaryUsed"));
            secondary.ResetAt = resetAt;
            secondary.WindowMinutes = windowMinutes;
            secondary.Badge = RemappedBadge(JsonUtil.String(JsonUtil.Value(obj, "secondaryBadge")), "其他");
            string secondaryTitle = JsonUtil.String(JsonUtil.Value(obj, "secondaryTitle"));
            secondary.Title = secondaryTitle != null ? secondaryTitle : "其他模型剩余";
            secondary.Detail = JsonUtil.String(JsonUtil.Value(obj, "secondaryDetail"));

            if (!primary.Remaining.HasValue && !secondary.Remaining.HasValue)
            {
                return null;
            }

            QuotaSnapshot snapshot = new QuotaSnapshot();
            snapshot.PlanType = JsonUtil.String(JsonUtil.Value(obj, "planType"));
            snapshot.Primary = primary;
            snapshot.Secondary = secondary;
            snapshot.SampledAt = sampledAt.HasValue ? sampledAt.Value : DateTime.MinValue;
            snapshot.SourceName = "cache";
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(QuotaSnapshot snapshot)
    {
        try
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["planType"] = snapshot.PlanType;
            obj["totalRemain"] = snapshot.Primary.Remaining;
            obj["totalUsed"] = snapshot.Primary.Used;
            obj["primaryBadge"] = snapshot.Primary.Badge;
            obj["primaryTitle"] = snapshot.Primary.Title;
            obj["primaryDetail"] = snapshot.Primary.Detail;
            obj["secondaryRemain"] = snapshot.Secondary.Remaining;
            obj["secondaryUsed"] = snapshot.Secondary.Used;
            obj["secondaryBadge"] = snapshot.Secondary.Badge;
            obj["secondaryTitle"] = snapshot.Secondary.Title;
            obj["secondaryDetail"] = snapshot.Secondary.Detail;
            obj["resetAt"] = snapshot.Primary.ResetAt;
            obj["windowMinutes"] = snapshot.Primary.WindowMinutes;
            obj["sampledAt"] = snapshot.SampledAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            obj["sourceName"] = snapshot.SourceName;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(obj);
            string path = PathName();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string RemappedBadge(string value, string fallback)
    {
        if (value == "总额" || value == "Auto")
        {
            return "内置";
        }
        if (value == "API")
        {
            return "其他";
        }
        if (value != null && value.Length > 0)
        {
            return value;
        }
        return fallback;
    }
}

internal sealed class CursorUsageClient
{
    public Action<QuotaSnapshot> OnSnapshot;
    public Action<string> OnError;

    private readonly object _sync = new object();
    private bool _inflight;
    private string _lastError = "";

    public string CurrentError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public void Refresh()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            RefreshOnQueue();
        });
    }

    public QuotaSnapshot RefreshBlocking()
    {
        return RefreshOnQueue();
    }

    private QuotaSnapshot RefreshOnQueue()
    {
        lock (_sync)
        {
            if (_inflight)
            {
                return null;
            }
            _inflight = true;
        }

        try
        {
            SessionInfo session = SessionStore.Load();
            if (session == null)
            {
                EmitError("未找到 Cursor 登录态，请先在 Cursor 中登录");
                return null;
            }

            string bearer = SessionStore.BearerToken(session.Token);
            string cookie = SessionStore.CookieValue(session.Token);

            FetchState dashboard = new FetchState();
            FetchState planInfo = new FetchState();
            FetchState summary = new FetchState();
            FetchState authUsage = new FetchState();
            FetchState stripe = new FetchState();

            using (CountdownEvent pending = new CountdownEvent(5))
            {
                StartFetch(pending, dashboard, "POST", "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage", bearer, null, true);
                StartFetch(pending, planInfo, "POST", "https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo", bearer, null, true);
                StartFetch(pending, summary, "GET", "https://cursor.com/api/usage-summary", null, cookie, false);
                StartFetch(pending, authUsage, "GET", "https://api2.cursor.sh/auth/usage", bearer, null, false);
                StartFetch(pending, stripe, "GET", "https://api2.cursor.sh/auth/full_stripe_profile", bearer, null, false);
                pending.Wait(18000);
            }

            QuotaSnapshot snapshot = QuotaParser.Snapshot(
                dashboard.Json,
                planInfo.Json,
                summary.Json,
                authUsage.Json,
                stripe.Json,
                session.LocalPlan,
                DateTime.UtcNow,
                "cursor-api");

            if (snapshot != null)
            {
                lock (_sync)
                {
                    _lastError = "";
                }
                SnapshotCache.Save(snapshot);
                Action<QuotaSnapshot> handler = OnSnapshot;
                if (handler != null)
                {
                    handler(snapshot);
                }
                return snapshot;
            }

            string firstError = FirstError(dashboard, planInfo, summary, authUsage, stripe);
            string message = FriendlyError(firstError != null ? firstError : "额度接口没有返回数据");
            EmitError(message);
            return null;
        }
        finally
        {
            lock (_sync)
            {
                _inflight = false;
            }
        }
    }

    private sealed class FetchState
    {
        public Dictionary<string, object> Json;
        public string Error;
    }

    private static void StartFetch(CountdownEvent done, FetchState state, string method, string url, string bearer, string cookie, bool connectRpc)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                Fetch(state, method, url, bearer, cookie, connectRpc);
            }
            finally
            {
                done.Signal();
            }
        });
    }

    private static void Fetch(FetchState state, string method, string url, string bearer, string cookie, bool connectRpc)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Accept = "application/json";
            request.UserAgent = "CursorQuotaPet/1.0";
            if (connectRpc)
            {
                request.ContentType = "application/json";
                request.Headers["Connect-Protocol-Version"] = "1";
            }
            if (bearer != null)
            {
                request.Headers["Authorization"] = "Bearer " + bearer;
            }
            if (cookie != null)
            {
                request.Headers["Cookie"] = "WorkosCursorSessionToken=" + cookie;
                request.Headers["Origin"] = "https://cursor.com";
            }
            if (method == "POST")
            {
                request.ContentType = "application/json";
                request.Headers["Origin"] = "https://cursor.com";
                byte[] body = Encoding.UTF8.GetBytes("{}");
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                ParseResponse(state, response);
            }
        }
        catch (WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                int status = (int)response.StatusCode;
                if (status == 401 || status == 403)
                {
                    state.Error = "会话已过期，请重新登录 Cursor";
                    return;
                }
                state.Error = "HTTP " + status.ToString(CultureInfo.InvariantCulture);
                return;
            }
            state.Error = ex.Message;
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
        }
    }

    private static void ParseResponse(FetchState state, HttpWebResponse response)
    {
        int status = (int)response.StatusCode;
        string text;
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            text = reader.ReadToEnd();
        }
        if (string.IsNullOrEmpty(text))
        {
            state.Error = status >= 400 ? "HTTP " + status.ToString(CultureInfo.InvariantCulture) : "空响应";
            return;
        }
        if (status == 401 || status == 403)
        {
            state.Error = "会话已过期，请重新登录 Cursor";
            return;
        }
        if (status >= 400)
        {
            state.Error = "HTTP " + status.ToString(CultureInfo.InvariantCulture);
            return;
        }

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        serializer.MaxJsonLength = int.MaxValue;
        object parsed = serializer.DeserializeObject(text);
        Dictionary<string, object> json = parsed as Dictionary<string, object>;
        if (json == null)
        {
            state.Error = "响应不是 JSON";
            return;
        }
        state.Json = json;
    }

    private static string FirstError(params FetchState[] states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].Error != null && states[i].Error.Length > 0)
            {
                return states[i].Error;
            }
        }
        return null;
    }

    private static string FriendlyError(string message)
    {
        string lower = message.ToLowerInvariant();
        if (lower.Contains("401") || lower.Contains("403") || lower.Contains("not_authenticated") || message.Contains("过期"))
        {
            return "会话已过期，请重新登录 Cursor";
        }
        if (lower.Contains("offline") || lower.Contains("network") || lower.Contains("internet") || lower.Contains("timed out") || lower.Contains("timeout"))
        {
            return "网络不可用，稍后重试";
        }
        return message;
    }

    private void EmitError(string message)
    {
        lock (_sync)
        {
            _lastError = message;
        }
        Action<string> handler = OnError;
        if (handler != null)
        {
            handler(message);
        }
    }
}

internal static class QuotaFormatter
{
    public static string Percent(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0}%", value.Value);
    }

    public static string PlanTitle(string planType)
    {
        string raw = planType != null ? planType.Trim() : "";
        if (raw.Length == 0)
        {
            return "Cursor 额度";
        }
        string mapped;
        string key = raw.ToLowerInvariant().Replace("_", "");
        switch (key)
        {
            case "pro": mapped = "Pro"; break;
            case "proplus":
            case "pro+": mapped = "Pro+"; break;
            case "ultra": mapped = "Ultra"; break;
            case "business": mapped = "Business"; break;
            case "enterprise": mapped = "Enterprise"; break;
            case "team": mapped = "Team"; break;
            case "free":
            case "hobby": mapped = "Free"; break;
            default:
                mapped = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw);
                break;
        }
        return mapped + " 额度";
    }

    public static string Caption(double? minutes, string fallback)
    {
        if (!minutes.HasValue)
        {
            return fallback;
        }
        int rounded = (int)Math.Round(minutes.Value);
        if (rounded <= 0)
        {
            return fallback;
        }
        if (rounded % 1440 == 0)
        {
            return (rounded / 1440).ToString(CultureInfo.InvariantCulture) + " 天窗口剩余";
        }
        if (rounded % 60 == 0)
        {
            return (rounded / 60).ToString(CultureInfo.InvariantCulture) + " 小时窗口剩余";
        }
        return fallback;
    }

    public static string Reset(double? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "重置时间未知";
        }
        double seconds = timestamp.Value - Unix.ToSeconds(DateTime.UtcNow);
        if (seconds <= 0)
        {
            return "窗口已到点，等待刷新";
        }
        int minutes = (int)Math.Ceiling(seconds / 60.0);
        if (minutes < 60)
        {
            return "约 " + minutes.ToString(CultureInfo.InvariantCulture) + " 分钟后重置";
        }
        int days = minutes / 1440;
        int hours = (minutes % 1440) / 60;
        int rest = minutes % 60;
        if (days > 0)
        {
            return "约 " + days.ToString(CultureInfo.InvariantCulture) + " 天 " + hours.ToString(CultureInfo.InvariantCulture) + " 小时后重置";
        }
        return "约 " + hours.ToString(CultureInfo.InvariantCulture) + " 小时 " + rest.ToString(CultureInfo.InvariantCulture) + " 分钟后重置";
    }

    public static string ShortError(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return "";
        }
        string oneLine = error.Replace("\n", " ").Trim();
        if (oneLine.Length <= 22)
        {
            return oneLine;
        }
        return oneLine.Substring(0, 22);
    }

    public static string BurnRatePerDay(double? used, double? remaining, double? resetAt, double? windowMinutes)
    {
        double? usedPercent = used;
        if (!usedPercent.HasValue && remaining.HasValue)
        {
            double value = 100.0 - remaining.Value;
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            usedPercent = value;
        }
        if (!usedPercent.HasValue || double.IsNaN(usedPercent.Value) || double.IsInfinity(usedPercent.Value))
        {
            return "—";
        }
        if (!windowMinutes.HasValue || windowMinutes.Value <= 0 || !resetAt.HasValue)
        {
            return "—";
        }

        double remainingSeconds = resetAt.Value - Unix.ToSeconds(DateTime.UtcNow);
        double elapsedDays = Math.Max(0, windowMinutes.Value * 60.0 - remainingSeconds) / 86400.0;
        if (usedPercent.Value <= 0.0001)
        {
            return "0%/天";
        }

        double rate = usedPercent.Value / Math.Max(elapsedDays, 1.0 / 24.0);
        if (rate < 0.05)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}%/天", rate);
        }
        double tenths = Math.Round(rate * 10.0) / 10.0;
        if (tenths == Math.Round(tenths))
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}%/天", tenths);
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0}%/天", tenths);
    }
}

internal sealed class QuotaModel
{
    public event Action Changed;

    public QuotaSnapshot Snapshot;
    public QuotaSnapshot FallbackSnapshot;
    public string ConnectionError = "";
    public string Footer = "正在读取 Cursor 登录态…";

    public System.Windows.Forms.Control MarshalControl;

    private readonly CursorUsageClient _client = new CursorUsageClient();
    private readonly int _fetchSeconds;
    private System.Windows.Forms.Timer _uiTimer;
    private System.Windows.Forms.Timer _fetchTimer;
    private DateTime _lastFetch = DateTime.MinValue;

    public QuotaModel()
        : this(1, 30)
    {
    }

    public QuotaModel(int uiSeconds, int fetchSeconds)
    {
        _fetchSeconds = fetchSeconds;
        QuotaModel self = this;
        _client.OnSnapshot = delegate(QuotaSnapshot snapshot)
        {
            self.Post(delegate
            {
                self.Snapshot = snapshot;
                self.ConnectionError = "";
                self.UpdateFooter(snapshot);
                self.RaiseChanged();
            });
        };
        _client.OnError = delegate(string message)
        {
            self.Post(delegate
            {
                self.ConnectionError = message;
                self.RaiseChanged();
            });
        };
    }

    public QuotaSnapshot DisplaySnapshot
    {
        get { return Snapshot != null ? Snapshot : FallbackSnapshot; }
    }

    public void Start()
    {
        FallbackSnapshot = SnapshotCache.Load();
        Refresh(true);
        _uiTimer = new System.Windows.Forms.Timer();
        _uiTimer.Interval = 1000;
        _uiTimer.Tick += delegate { Tick(); };
        _uiTimer.Start();
        _fetchTimer = new System.Windows.Forms.Timer();
        _fetchTimer.Interval = _fetchSeconds * 1000;
        _fetchTimer.Tick += delegate { Refresh(true); };
        _fetchTimer.Start();
    }

    public void Refresh(bool forceNetwork)
    {
        FallbackSnapshot = SnapshotCache.Load();
        if (forceNetwork || (DateTime.UtcNow - _lastFetch).TotalSeconds >= _fetchSeconds)
        {
            _lastFetch = DateTime.UtcNow;
            _client.Refresh();
        }
        QuotaSnapshot display = DisplaySnapshot;
        if (display != null)
        {
            UpdateFooter(display);
        }
        else if (!string.IsNullOrEmpty(ConnectionError))
        {
            Footer = ConnectionError;
        }
        else
        {
            Footer = "正在读取 Cursor 登录态…";
        }
        RaiseChanged();
    }

    public void Stop()
    {
        if (_uiTimer != null)
        {
            _uiTimer.Stop();
            _uiTimer.Dispose();
            _uiTimer = null;
        }
        if (_fetchTimer != null)
        {
            _fetchTimer.Stop();
            _fetchTimer.Dispose();
            _fetchTimer = null;
        }
    }

    private void Tick()
    {
        QuotaSnapshot display = DisplaySnapshot;
        if (display != null)
        {
            UpdateFooter(display);
            RaiseChanged();
        }
        else if (!string.IsNullOrEmpty(ConnectionError))
        {
            Footer = ConnectionError;
            RaiseChanged();
        }
    }

    private void UpdateFooter(QuotaSnapshot value)
    {
        double ageMinutes = Math.Max(0, (DateTime.UtcNow - value.SampledAt.ToUniversalTime()).TotalMinutes);
        string format = ageMinutes >= 10 ? "MM-dd HH:mm" : "HH:mm";
        string source = value.SourceName == "cursor-api" ? "实时" : "快照";
        string stale = ageMinutes >= 10 ? " · 可能过期" : "";
        string shortError = QuotaFormatter.ShortError(ConnectionError);
        string error = (value.SourceName == "cursor-api" || shortError.Length == 0) ? "" : " · " + shortError;
        Footer = source + " " + value.SampledAt.ToLocalTime().ToString(format, CultureInfo.InvariantCulture) + stale + error;
    }

    private void Post(Action action)
    {
        System.Windows.Forms.Control control = MarshalControl;
        if (control != null && control.IsHandleCreated)
        {
            control.BeginInvoke(action);
            return;
        }
        action();
    }

    private void RaiseChanged()
    {
        Action handler = Changed;
        if (handler != null)
        {
            handler();
        }
    }
}

internal static class ProbeRunner
{
    public static int Run()
    {
        CursorUsageClient client = new CursorUsageClient();
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            QuotaSnapshot snapshot = null;
            string errorMessage = "";
            client.OnSnapshot = delegate(QuotaSnapshot value)
            {
                snapshot = value;
                done.Set();
            };
            client.OnError = delegate(string err)
            {
                errorMessage = err;
                done.Set();
            };
            client.Refresh();
            done.WaitOne(20000);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (snapshot != null)
            {
                result["Status"] = "ok";
                result["PlanType"] = snapshot.PlanType;
                result["IncludedRemain"] = snapshot.Primary.Remaining;
                result["OtherRemain"] = snapshot.Secondary.Remaining;
                result["SecondaryBadge"] = snapshot.Secondary.Badge;
                result["SampledAt"] = snapshot.SampledAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                result["SourceName"] = snapshot.SourceName;
                Console.WriteLine(Pretty(serializer.Serialize(result)));
                return 0;
            }

            QuotaSnapshot fallback = SnapshotCache.Load();
            if (fallback != null)
            {
                result["Status"] = "ok";
                result["PlanType"] = fallback.PlanType;
                result["IncludedRemain"] = fallback.Primary.Remaining;
                result["SecondaryBadge"] = fallback.Secondary.Badge;
                result["SecondaryRemain"] = fallback.Secondary.Remaining;
                result["SampledAt"] = fallback.SampledAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                result["SourceName"] = fallback.SourceName;
                Console.WriteLine(Pretty(serializer.Serialize(result)));
                return 0;
            }

            string message = errorMessage.Length > 0 ? errorMessage : client.CurrentError;
            result["Status"] = "unavailable";
            result["Message"] = message.Length > 0 ? message : "没有找到可读取的 Cursor 额度";
            Console.WriteLine(Pretty(serializer.Serialize(result)));
            return 1;
        }
    }

    private static string Pretty(string json)
    {
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object obj = serializer.DeserializeObject(json);
            StringBuilder builder = new StringBuilder();
            WritePretty(builder, obj, 0);
            return builder.ToString();
        }
        catch
        {
            return json;
        }
    }

    private static void WritePretty(StringBuilder builder, object value, int indent)
    {
        Dictionary<string, object> dict = value as Dictionary<string, object>;
        if (dict != null)
        {
            builder.Append("{\n");
            List<string> keys = new List<string>(dict.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                builder.Append(' ', indent + 2);
                builder.Append("\"").Append(keys[i]).Append("\" : ");
                WritePretty(builder, dict[keys[i]], indent + 2);
                if (i < keys.Count - 1)
                {
                    builder.Append(",");
                }
                builder.Append("\n");
            }
            builder.Append(' ', indent).Append("}");
            return;
        }

        if (value == null)
        {
            builder.Append("null");
            return;
        }
        if (value is string)
        {
            builder.Append("\"").Append(Escape((string)value)).Append("\"");
            return;
        }
        if (value is bool)
        {
            builder.Append(((bool)value) ? "true" : "false");
            return;
        }
        IList list = value as IList;
        if (list != null && !(value is string))
        {
            builder.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                WritePretty(builder, list[i], indent);
            }
            builder.Append("]");
            return;
        }
        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
