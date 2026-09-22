using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class QuotaWindow
{
    public double? Remaining;
    public double? Used;
    public double? ResetAt;
    public double? WindowMinutes;
    public string Badge;
    public string Title;
}

internal sealed class QuotaSnapshot
{
    public string PlanType;
    public QuotaWindow Primary;
    public QuotaWindow Secondary;
    public DateTime SampledAt;
    public string SourceName;
}

internal static class JsonValue
{
    public static Dictionary<string, object> Map(object value)
    {
        return value as Dictionary<string, object>;
    }

    public static object Get(Dictionary<string, object> map, params string[] names)
    {
        if (map == null || names == null)
        {
            return null;
        }
        for (int i = 0; i < names.Length; i++)
        {
            object value;
            if (map.TryGetValue(names[i], out value) && value != null)
            {
                return value;
            }
        }
        return null;
    }

    public static Dictionary<string, object> Nested(Dictionary<string, object> root, params string[] path)
    {
        object current = root;
        for (int i = 0; path != null && i < path.Length; i++)
        {
            Dictionary<string, object> map = Map(current);
            if (map == null || !map.TryGetValue(path[i], out current) || current == null)
            {
                return null;
            }
        }
        return Map(current);
    }

    public static double? Number(object value)
    {
        if (value == null)
        {
            return null;
        }
        if (value is int)
        {
            return (int)value;
        }
        if (value is long)
        {
            return (long)value;
        }
        if (value is double)
        {
            return (double)value;
        }
        if (value is float)
        {
            return (float)value;
        }
        if (value is decimal)
        {
            return (double)(decimal)value;
        }
        string text = value as string;
        if (text != null)
        {
            double parsed;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    public static string Text(object value)
    {
        string text = value as string;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return text;
    }

    public static double? Percent(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }
        double scaled = value.Value <= 1.0000001 ? value.Value * 100.0 : value.Value;
        if (double.IsNaN(scaled) || double.IsInfinity(scaled))
        {
            return null;
        }
        return Math.Min(100.0, Math.Max(0.0, scaled));
    }

    public static double? UnixSeconds(object value)
    {
        double? number = Number(value);
        if (number.HasValue)
        {
            double interval = number.Value > 10000000000.0 ? number.Value / 1000.0 : number.Value;
            if (interval > 1000000.0)
            {
                return interval;
            }
        }
        string text = Text(value);
        if (text == null)
        {
            return null;
        }
        double parsed;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return UnixSeconds(parsed);
        }
        DateTime date;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
        {
            return (date.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
        return null;
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
        Dictionary<string, object> planUsage = JsonValue.Map(JsonValue.Get(dashboard, "planUsage"));
        if (planUsage == null)
        {
            planUsage = JsonValue.Nested(summary, "individualUsage", "plan");
        }
        Dictionary<string, object> summaryPlan = JsonValue.Nested(summary, "individualUsage", "plan");

        double? autoUsed = FirstPercent(
            JsonValue.Get(planUsage, "autoPercentUsed"),
            JsonValue.Get(summaryPlan, "autoPercentUsed"),
            JsonValue.Get(summary, "autoModelSelectedDisplayMessage"));
        double? otherUsed = FirstPercent(
            JsonValue.Get(planUsage, "apiPercentUsed"),
            JsonValue.Get(summaryPlan, "apiPercentUsed"),
            JsonValue.Get(summary, "namedModelSelectedDisplayMessage"));
        double? totalUsed = FirstPercent(
            JsonValue.Get(planUsage, "totalPercentUsed"),
            JsonValue.Get(summaryPlan, "totalPercentUsed"));

        double? centsLimit = JsonValue.Number(JsonValue.Get(planUsage, "limit"));
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonValue.Number(JsonValue.Get(JsonValue.Nested(planInfo, "planInfo"), "includedAmountCents"));
        }
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonValue.Number(JsonValue.Get(planInfo, "includedAmountCents"));
        }
        if (!centsLimit.HasValue)
        {
            centsLimit = JsonValue.Number(JsonValue.Get(summaryPlan, "limit"));
        }

        double? includedSpend = JsonValue.Number(JsonValue.Get(planUsage, "includedSpend"));
        if (!includedSpend.HasValue)
        {
            includedSpend = JsonValue.Number(JsonValue.Get(summaryPlan, "used"));
        }
        double? centsRemaining = JsonValue.Number(JsonValue.Get(planUsage, "remaining"));
        if (!centsRemaining.HasValue)
        {
            centsRemaining = JsonValue.Number(JsonValue.Get(summaryPlan, "remaining"));
        }
        if (!centsRemaining.HasValue && includedSpend.HasValue && centsLimit.HasValue)
        {
            centsRemaining = Math.Max(0, centsLimit.Value - includedSpend.Value);
        }

        double? includedUsed = null;
        if (centsLimit.HasValue && centsLimit.Value > 0)
        {
            if (centsRemaining.HasValue)
            {
                includedUsed = JsonValue.Percent(1.0 - centsRemaining.Value / centsLimit.Value);
            }
            else if (includedSpend.HasValue)
            {
                includedUsed = JsonValue.Percent(includedSpend.Value / centsLimit.Value);
            }
        }

        double? builtinUsed = autoUsed ?? includedUsed ?? totalUsed;
        double? builtinRemaining = null;
        if (builtinUsed.HasValue)
        {
            builtinRemaining = Math.Min(100, Math.Max(0, 100 - builtinUsed.Value));
        }
        if (!builtinRemaining.HasValue && centsLimit.HasValue && centsLimit.Value > 0 && centsRemaining.HasValue)
        {
            builtinRemaining = JsonValue.Percent(centsRemaining.Value / centsLimit.Value);
        }

        double bucketUsed;
        double bucketLimit;
        bool hasBucket = RequestBucket(authUsage, out bucketUsed, out bucketLimit)
            || RequestBucket(summaryPlan, out bucketUsed, out bucketLimit);
        if (!builtinRemaining.HasValue && hasBucket && bucketLimit > 0)
        {
            builtinRemaining = Math.Min(100, Math.Max(0, (bucketLimit - bucketUsed) / bucketLimit * 100));
        }

        double? cycleStart = FirstDate(
            JsonValue.Get(dashboard, "billingCycleStart"),
            JsonValue.Get(summary, "billingCycleStart"),
            JsonValue.Get(authUsage, "startOfMonth"));
        double? cycleEnd = FirstDate(
            JsonValue.Get(dashboard, "billingCycleEnd"),
            JsonValue.Get(summary, "billingCycleEnd"),
            JsonValue.Get(JsonValue.Nested(planInfo, "planInfo"), "billingCycleEnd"),
            JsonValue.Get(planInfo, "billingCycleEnd"));
        if (!cycleEnd.HasValue && cycleStart.HasValue)
        {
            cycleEnd = cycleStart.Value + 30 * 24 * 60 * 60;
        }

        double? windowMinutes = 30 * 24 * 60;
        if (cycleStart.HasValue && cycleEnd.HasValue)
        {
            windowMinutes = Math.Max(1, (cycleEnd.Value - cycleStart.Value) / 60.0);
        }

        string planType = JsonValue.Text(JsonValue.Get(JsonValue.Nested(planInfo, "planInfo"), "planName"));
        if (planType == null)
        {
            planType = JsonValue.Text(JsonValue.Get(planInfo, "planName", "planType"));
        }
        if (planType == null)
        {
            planType = JsonValue.Text(JsonValue.Get(summary, "membershipType"));
        }
        if (planType == null)
        {
            planType = JsonValue.Text(JsonValue.Get(stripe, "membershipType", "individualMembershipType"));
        }
        if (planType == null)
        {
            planType = localPlan;
        }

        if (!builtinRemaining.HasValue && !otherUsed.HasValue && !hasBucket)
        {
            return null;
        }

        QuotaSnapshot snapshot = new QuotaSnapshot();
        snapshot.PlanType = planType;
        snapshot.SampledAt = sampledAt;
        snapshot.SourceName = sourceName;
        snapshot.Primary = new QuotaWindow();
        snapshot.Primary.Remaining = builtinRemaining;
        snapshot.Primary.Used = builtinUsed ?? (builtinRemaining.HasValue ? (double?)(100 - builtinRemaining.Value) : null);
        snapshot.Primary.ResetAt = cycleEnd;
        snapshot.Primary.WindowMinutes = windowMinutes;
        snapshot.Primary.Badge = "内置";
        snapshot.Primary.Title = "内置模型剩余";
        snapshot.Secondary = new QuotaWindow();
        snapshot.Secondary.ResetAt = cycleEnd;
        snapshot.Secondary.WindowMinutes = windowMinutes;
        snapshot.Secondary.Badge = "其他";
        snapshot.Secondary.Title = "其他模型剩余";
        if (otherUsed.HasValue)
        {
            snapshot.Secondary.Remaining = Math.Min(100, Math.Max(0, 100 - otherUsed.Value));
            snapshot.Secondary.Used = otherUsed;
        }
        else if (hasBucket && bucketLimit > 0)
        {
            double remaining = Math.Min(100, Math.Max(0, (bucketLimit - bucketUsed) / bucketLimit * 100));
            snapshot.Secondary.Remaining = remaining;
            snapshot.Secondary.Used = 100 - remaining;
        }
        return snapshot;
    }

    private static double? FirstDate(params object[] values)
    {
        if (values == null)
        {
            return null;
        }
        for (int i = 0; i < values.Length; i++)
        {
            double? date = JsonValue.UnixSeconds(values[i]);
            if (date.HasValue)
            {
                return date;
            }
        }
        return null;
    }

    private static double? FirstPercent(params object[] values)
    {
        if (values == null)
        {
            return null;
        }
        for (int i = 0; i < values.Length; i++)
        {
            double? number = JsonValue.Percent(JsonValue.Number(values[i]));
            if (number.HasValue)
            {
                return number;
            }
            double? fromText = PercentFromMessage(values[i]);
            if (fromText.HasValue)
            {
                return fromText;
            }
        }
        return null;
    }

    private static double? PercentFromMessage(object value)
    {
        string text = JsonValue.Text(value);
        if (text == null)
        {
            return null;
        }
        int mark = text.IndexOf('%');
        if (mark <= 0)
        {
            return null;
        }
        int start = mark - 1;
        while (start >= 0)
        {
            char ch = text[start];
            if ((ch >= '0' && ch <= '9') || ch == '.')
            {
                start--;
                continue;
            }
            break;
        }
        start++;
        if (start >= mark)
        {
            return null;
        }
        double parsed;
        if (!double.TryParse(text.Substring(start, mark - start), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return null;
        }
        return JsonValue.Percent(parsed);
    }

    private static bool RequestBucket(Dictionary<string, object> root, out double used, out double limit)
    {
        used = 0;
        limit = 0;
        if (root == null)
        {
            return false;
        }
        List<string> names = new List<string>();
        List<double> usedValues = new List<double>();
        List<double> limitValues = new List<double>();
        Walk(root, "", names, usedValues, limitValues);
        string[] preferred = new string[] { "gpt-4", "gpt-4o", "default", "composer" };
        for (int p = 0; p < preferred.Length; p++)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == preferred[p] || names[i].EndsWith("." + preferred[p], StringComparison.Ordinal))
                {
                    used = usedValues[i];
                    limit = limitValues[i];
                    return true;
                }
            }
        }
        if (names.Count > 0)
        {
            used = usedValues[0];
            limit = limitValues[0];
            return true;
        }
        return false;
    }

    private static void Walk(Dictionary<string, object> map, string prefix, List<string> names, List<double> usedValues, List<double> limitValues)
    {
        double? used = JsonValue.Number(JsonValue.Get(map, "numRequests", "requests"));
        double? limit = JsonValue.Number(JsonValue.Get(map, "maxRequestUsage", "maxRequests", "requestLimit"));
        if (used.HasValue && limit.HasValue && limit.Value > 0 && prefix.Length > 0)
        {
            names.Add(prefix);
            usedValues.Add(used.Value);
            limitValues.Add(limit.Value);
        }
        foreach (KeyValuePair<string, object> pair in map)
        {
            Dictionary<string, object> child = JsonValue.Map(pair.Value);
            if (child == null)
            {
                continue;
            }
            string next = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
            Walk(child, next, names, usedValues, limitValues);
        }
    }
}

internal static class QuotaFormatter
{
    public static string Percent(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value))
        {
            return "—";
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0}%", Math.Round(value.Value));
    }

    public static string ShortNumber(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value))
        {
            return "—";
        }
        return Math.Round(value.Value).ToString(CultureInfo.InvariantCulture);
    }

    public static string PlanTitle(string planType)
    {
        if (string.IsNullOrEmpty(planType))
        {
            return "Cursor 额度";
        }
        string raw = planType.Trim();
        if (raw.Length == 0)
        {
            return "Cursor 额度";
        }
        string key = raw.ToLowerInvariant().Replace("_", "");
        string mapped = raw;
        if (key == "pro") mapped = "Pro";
        else if (key == "proplus" || key == "pro+") mapped = "Pro+";
        else if (key == "ultra") mapped = "Ultra";
        else if (key == "business") mapped = "Business";
        else if (key == "enterprise") mapped = "Enterprise";
        else if (key == "team") mapped = "Team";
        else if (key == "free" || key == "hobby") mapped = "Free";
        else mapped = Capitalize(raw);
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

    public static string ResetAt(double? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return null;
        }
        DateTime date = Unix(timestamp.Value).ToLocalTime();
        return "将于 " + date.ToString("MM-dd  HH:mm", CultureInfo.InvariantCulture) + " 重置";
    }

    public static string Reset(double? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "重置时间未知";
        }
        double seconds = timestamp.Value - Now();
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
            return "约 " + days.ToString(CultureInfo.InvariantCulture)
                + " 天 " + hours.ToString(CultureInfo.InvariantCulture)
                + " 小时 " + rest.ToString(CultureInfo.InvariantCulture) + " 分钟后重置";
        }
        return "约 " + hours.ToString(CultureInfo.InvariantCulture)
            + " 小时 " + rest.ToString(CultureInfo.InvariantCulture) + " 分钟后重置";
    }

    public static string BurnRatePerDay(double? used, double? remaining, double? resetAt, double? windowMinutes)
    {
        double? usedPercent = used;
        if (!usedPercent.HasValue && remaining.HasValue)
        {
            usedPercent = Math.Min(100, Math.Max(0, 100 - remaining.Value));
        }
        if (!usedPercent.HasValue || double.IsNaN(usedPercent.Value))
        {
            return "—";
        }
        if (!windowMinutes.HasValue || windowMinutes.Value <= 0 || !resetAt.HasValue)
        {
            return "—";
        }
        double remainingSeconds = resetAt.Value - Now();
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
        if (Math.Abs(tenths - Math.Round(tenths)) < 0.001)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}%/天", tenths);
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0}%/天", tenths);
    }

    public static string Footer(QuotaSnapshot snapshot, string connectionError)
    {
        double ageMinutes = 0;
        if (snapshot != null)
        {
            ageMinutes = Math.Max(0, (DateTime.UtcNow - snapshot.SampledAt.ToUniversalTime()).TotalMinutes);
        }
        string stamp = snapshot == null
            ? ""
            : snapshot.SampledAt.ToLocalTime().ToString(ageMinutes >= 10 ? "MM-dd HH:mm" : "HH:mm", CultureInfo.InvariantCulture);
        string source = snapshot != null && snapshot.SourceName == "cursor-api" ? "实时" : "快照";
        string stale = ageMinutes >= 10 ? " · 可能过期" : "";
        string brief = ShortError(connectionError);
        string error = snapshot != null && snapshot.SourceName == "cursor-api" || brief.Length == 0 ? "" : " · " + brief;
        if (snapshot == null)
        {
            return brief.Length == 0 ? "正在读取 Cursor 登录态…" : brief;
        }
        return source + " " + stamp + stale + error;
    }

    public static string ShortError(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return "";
        }
        string oneLine = error.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (oneLine.Length > 22)
        {
            return oneLine.Substring(0, 22);
        }
        return oneLine;
    }

    public static string StatusBadge(QuotaSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return "WAIT";
        }
        return snapshot.SourceName == "cursor-api" ? "LIVE" : "SNAPSHOT";
    }

    private static string Capitalize(string raw)
    {
        string[] parts = raw.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }
            parts[i] = char.ToUpperInvariant(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1) : "");
        }
        return string.Join(" ", parts);
    }

    private static double Now()
    {
        return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    private static DateTime Unix(double seconds)
    {
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
    }
}

internal static class SqliteKv
{
    public static bool TryRead(string path, string key, out string value, out string error)
    {
        value = null;
        error = null;
        try
        {
            byte[] db = Load(path);
            if (db == null || db.Length < 100 || Encoding.ASCII.GetString(db, 0, 15) != "SQLite format 3")
            {
                error = "不是可读的 SQLite 数据库";
                return false;
            }
            string treeError;
            string found = Find(db, key, out treeError);
            if (!string.IsNullOrEmpty(found))
            {
                value = found.Trim();
                return true;
            }
            string guess = Heuristic(db, key);
            if (!string.IsNullOrEmpty(guess))
            {
                value = guess.Trim();
                return true;
            }
            if (treeError != null)
            {
                error = treeError;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static byte[] Load(string path)
    {
        byte[] db = ReadFile(path);
        string walPath = path + "-wal";
        if (!File.Exists(walPath))
        {
            return db;
        }
        try
        {
            return ApplyWal(db, ReadFile(walPath));
        }
        catch
        {
            return db;
        }
    }

    private static byte[] ReadFile(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] data = new byte[stream.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }
            if (offset == data.Length)
            {
                return data;
            }
            byte[] exact = new byte[offset];
            Buffer.BlockCopy(data, 0, exact, 0, offset);
            return exact;
        }
    }

    private static byte[] ApplyWal(byte[] db, byte[] wal)
    {
        if (wal == null || wal.Length < 32 || db == null || db.Length < 100)
        {
            return db;
        }
        int pageSize = PageSize(db);
        int walPageSize = (int)Be32(wal, 8);
        if (walPageSize == 1)
        {
            walPageSize = 65536;
        }
        if (walPageSize != pageSize || pageSize < 512)
        {
            return db;
        }
        int frameSize = 24 + pageSize;
        int lastCommit = -1;
        int count = 0;
        for (int offset = 32; offset + frameSize <= wal.Length; offset += frameSize)
        {
            if (!SameSalt(wal, 16, wal, offset + 8))
            {
                break;
            }
            if (Be32(wal, offset + 4) != 0)
            {
                lastCommit = count;
            }
            count++;
        }
        if (lastCommit < 0)
        {
            return db;
        }
        byte[] image = db;
        int index = 0;
        for (int offset = 32; index <= lastCommit && offset + frameSize <= wal.Length; offset += frameSize, index++)
        {
            int pageNo = (int)Be32(wal, offset);
            if (pageNo <= 0)
            {
                continue;
            }
            int need = pageNo * pageSize;
            if (image.Length < need)
            {
                byte[] grown = new byte[need];
                Buffer.BlockCopy(image, 0, grown, 0, image.Length);
                image = grown;
            }
            Buffer.BlockCopy(wal, offset + 24, image, (pageNo - 1) * pageSize, pageSize);
        }
        return image;
    }

    private static bool SameSalt(byte[] left, int leftOffset, byte[] right, int rightOffset)
    {
        for (int i = 0; i < 8; i++)
        {
            if (left[leftOffset + i] != right[rightOffset + i])
            {
                return false;
            }
        }
        return true;
    }

    private static string Find(byte[] db, string key, out string error)
    {
        error = null;
        int root = 0;
        List<string> types = new List<string>();
        Visit(db, 1, delegate(object[] columns)
        {
            if (columns == null || columns.Length < 4)
            {
                return;
            }
            string type = columns[0] as string;
            string name = columns.Length > 1 ? columns[1] as string : null;
            string table = columns.Length > 2 ? columns[2] as string : null;
            if (type != null)
            {
                types.Add(type);
            }
            if (type == "table" && (name == "ItemTable" || table == "ItemTable"))
            {
                long page = ToLong(columns[3]);
                if (page > 0)
                {
                    root = (int)page;
                }
            }
        });
        if (root <= 0)
        {
            error = "找不到 ItemTable";
            return null;
        }
        string found = null;
        Visit(db, root, delegate(object[] columns)
        {
            if (found != null || columns == null || columns.Length < 2)
            {
                return;
            }
            string rowKey = columns[0] as string;
            if (rowKey != key)
            {
                return;
            }
            found = Coerce(columns[1]);
        });
        return found;
    }

    private delegate void RowVisitor(object[] columns);

    private static void Visit(byte[] db, int pageNumber, RowVisitor visitor)
    {
        Dictionary<int, bool> seen = new Dictionary<int, bool>();
        VisitPage(db, pageNumber, visitor, seen, 0);
    }

    private static void VisitPage(byte[] db, int pageNumber, RowVisitor visitor, Dictionary<int, bool> seen, int depth)
    {
        if (pageNumber <= 0 || depth > 64 || seen.ContainsKey(pageNumber))
        {
            return;
        }
        int pageSize = PageSize(db);
        int pageBase = (pageNumber - 1) * pageSize;
        if (pageBase < 0 || pageBase + 12 >= db.Length)
        {
            return;
        }
        seen[pageNumber] = true;
        int header = pageNumber == 1 ? pageBase + 100 : pageBase;
        if (header + 8 >= db.Length)
        {
            return;
        }
        byte kind = db[header];
        bool interior = kind == 0x05 || kind == 0x02;
        bool table = kind == 0x0d || kind == 0x05;
        if (!table)
        {
            return;
        }
        int cells = Be16(db, header + 3);
        int pointer = header + (interior ? 12 : 8);
        int right = interior ? (int)Be32(db, header + 8) : 0;
        if (interior)
        {
            VisitPage(db, right, visitor, seen, depth + 1);
        }
        for (int i = 0; i < cells; i++)
        {
            int pointerOffset = pointer + i * 2;
            if (pointerOffset + 2 > db.Length)
            {
                break;
            }
            int cell = pageBase + Be16(db, pointerOffset);
            if (cell < pageBase || cell >= pageBase + pageSize || cell >= db.Length)
            {
                continue;
            }
            int cursor = cell;
            if (interior)
            {
                if (cursor + 4 > db.Length)
                {
                    continue;
                }
                int child = (int)Be32(db, cursor);
                cursor += 4;
                ReadVarint(db, ref cursor);
                VisitPage(db, child, visitor, seen, depth + 1);
                continue;
            }
            long payloadSize = ReadVarint(db, ref cursor);
            ReadVarint(db, ref cursor);
            if (payloadSize <= 0 || payloadSize > 8 * 1024 * 1024)
            {
                continue;
            }
            byte[] payload = ReadPayload(db, pageSize, pageBase, cursor, (int)payloadSize);
            if (payload == null)
            {
                continue;
            }
            object[] columns = ParseRecord(payload);
            if (columns != null)
            {
                visitor(columns);
            }
        }
    }

    private static byte[] ReadPayload(byte[] db, int pageSize, int pageBase, int payloadOffset, int payloadSize)
    {
        int reserved = db.Length > 20 ? db[20] : 0;
        int usable = pageSize - reserved;
        int maxLocal = usable - 35;
        int minLocal = ((usable - 12) * 32 / 255) - 23;
        int local = payloadSize;
        if (payloadSize > maxLocal)
        {
            int surplus = minLocal + ((payloadSize - minLocal) % (usable - 4));
            local = surplus <= maxLocal ? surplus : minLocal;
        }
        if (local < 0 || payloadOffset + local > db.Length)
        {
            return null;
        }
        byte[] payload = new byte[payloadSize];
        Buffer.BlockCopy(db, payloadOffset, payload, 0, Math.Min(local, payloadSize));
        if (payloadSize <= local)
        {
            return payload;
        }
        if (payloadOffset + local + 4 > db.Length)
        {
            return payload;
        }
        int overflow = (int)Be32(db, payloadOffset + local);
        int copied = local;
        int guard = 0;
        while (overflow > 0 && copied < payloadSize && guard < 10000)
        {
            int overflowOffset = (overflow - 1) * pageSize;
            if (overflowOffset < 0 || overflowOffset + 4 >= db.Length)
            {
                break;
            }
            int next = (int)Be32(db, overflowOffset);
            int take = Math.Min(usable - 4, payloadSize - copied);
            if (take > 0 && overflowOffset + 4 + take <= db.Length)
            {
                Buffer.BlockCopy(db, overflowOffset + 4, payload, copied, take);
                copied += take;
            }
            overflow = next;
            guard++;
        }
        return payload;
    }

    private static object[] ParseRecord(byte[] payload)
    {
        int cursor = 0;
        long headerSize = ReadVarint(payload, ref cursor);
        if (headerSize <= 0 || headerSize > payload.Length)
        {
            return null;
        }
        int headerEnd = (int)headerSize;
        List<long> types = new List<long>();
        while (cursor < headerEnd && cursor < payload.Length)
        {
            types.Add(ReadVarint(payload, ref cursor));
        }
        int body = headerEnd;
        object[] values = new object[types.Count];
        for (int i = 0; i < types.Count; i++)
        {
            int size = SerialSize(types[i]);
            if (size < 0 || body + size > payload.Length)
            {
                values[i] = null;
                break;
            }
            values[i] = ReadSerial(payload, body, types[i], size);
            body += size;
        }
        return values;
    }

    private static int SerialSize(long type)
    {
        if (type == 0) return 0;
        if (type == 1) return 1;
        if (type == 2) return 2;
        if (type == 3) return 3;
        if (type == 4) return 4;
        if (type == 5) return 6;
        if (type == 6 || type == 7) return 8;
        if (type == 8 || type == 9) return 0;
        if (type >= 12)
        {
            return (int)((type - 12) / 2);
        }
        return 0;
    }

    private static object ReadSerial(byte[] data, int offset, long type, int size)
    {
        if (type == 0) return null;
        if (type == 8) return (long)0;
        if (type == 9) return (long)1;
        if (type >= 1 && type <= 6)
        {
            return ReadSigned(data, offset, size);
        }
        if (type == 7)
        {
            return null;
        }
        if (type >= 12)
        {
            return Encoding.UTF8.GetString(data, offset, size);
        }
        return null;
    }

    private static long ReadSigned(byte[] data, int offset, int size)
    {
        long value = 0;
        for (int i = 0; i < size; i++)
        {
            value = (value << 8) | data[offset + i];
        }
        int bits = size * 8;
        if (bits > 0 && bits < 64 && ((value >> (bits - 1)) & 1) == 1)
        {
            value |= -1L << bits;
        }
        return value;
    }

    private static long ReadVarint(byte[] data, ref int offset)
    {
        long result = 0;
        for (int i = 0; i < 9 && offset < data.Length; i++)
        {
            byte b = data[offset++];
            if (i == 8)
            {
                result = (result << 8) | b;
                break;
            }
            result = (result << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }
        return result;
    }

    private static string Coerce(object value)
    {
        if (value == null)
        {
            return null;
        }
        string text = value as string;
        if (text != null)
        {
            return text;
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static long ToLong(object value)
    {
        if (value is long)
        {
            return (long)value;
        }
        if (value is int)
        {
            return (int)value;
        }
        string text = value as string;
        long parsed;
        if (text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }
        return 0;
    }

    private static string Heuristic(byte[] db, string key)
    {
        byte[] needle = Encoding.UTF8.GetBytes(key);
        int from = 0;
        while (from >= 0 && from < db.Length)
        {
            int index = IndexOf(db, needle, from);
            if (index < 0)
            {
                return null;
            }
            int start = index + needle.Length;
            string value = ReadToken(db, start);
            if (Plausible(key, value))
            {
                return value;
            }
            string nearby = ReadToken(db, start, 48);
            if (Plausible(key, nearby))
            {
                return nearby;
            }
            from = index + needle.Length;
        }
        return null;
    }

    private static string ReadToken(byte[] data, int start)
    {
        return ReadToken(data, start, 0);
    }

    private static string ReadToken(byte[] data, int start, int scan)
    {
        int begin = start;
        int limit = Math.Min(data.Length, start + scan);
        if (scan > 0)
        {
            begin = -1;
            for (int i = start; i < limit - 2; i++)
            {
                if (data[i] == (byte)'e' && data[i + 1] == (byte)'y' && data[i + 2] == (byte)'J')
                {
                    begin = i;
                    break;
                }
            }
            if (begin < 0)
            {
                return null;
            }
        }
        if (begin >= data.Length)
        {
            return null;
        }
        int end = begin;
        while (end < data.Length && end - begin < 8192)
        {
            byte b = data[end];
            bool ok = (b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'0' && b <= (byte)'9')
                || b == (byte)'.' || b == (byte)'_' || b == (byte)'-'
                || b == (byte)':' || b == (byte)'%' || b == (byte)'+';
            if (!ok)
            {
                break;
            }
            end++;
        }
        if (end <= begin)
        {
            return null;
        }
        return Encoding.UTF8.GetString(data, begin, end - begin);
    }

    private static bool Plausible(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        if (key.IndexOf("accessToken", StringComparison.Ordinal) >= 0 || key.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return value.Length > 20 && (value.IndexOf("eyJ", StringComparison.Ordinal) >= 0 || value.IndexOf("::", StringComparison.Ordinal) >= 0);
        }
        return value.Length >= 2 && value.Length <= 40;
    }

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        for (int i = start; i + needle.Length <= data.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static int PageSize(byte[] db)
    {
        int size = Be16(db, 16);
        return size == 1 ? 65536 : size;
    }

    private static int Be16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static long Be32(byte[] data, int offset)
    {
        return ((long)data[offset] << 24)
            | ((long)data[offset + 1] << 16)
            | ((long)data[offset + 2] << 8)
            | data[offset + 3];
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
        string env = Normalize(Environment.GetEnvironmentVariable("CURSOR_SESSION_TOKEN"));
        if (env != null)
        {
            return Make(env, "env", localPlan);
        }
        string file = ReadFile(ConfigPath());
        if (file != null)
        {
            return Make(file, "config", localPlan);
        }
        string app = SqliteValue("cursorAuth/accessToken");
        if (app != null)
        {
            return Make(app, "cursor-app", localPlan);
        }
        return null;
    }

    public static string CookieValue(string token)
    {
        if (token.IndexOf("::", StringComparison.Ordinal) >= 0 || token.IndexOf("%3A%3A", StringComparison.Ordinal) >= 0)
        {
            return token.Replace("%3A%3A", "::");
        }
        string subject = JwtSubject(token);
        if (subject != null)
        {
            return subject + "::" + token;
        }
        return token;
    }

    public static string BearerToken(string token)
    {
        int encoded = token.LastIndexOf("%3A%3A", StringComparison.Ordinal);
        if (encoded >= 0)
        {
            return token.Substring(encoded + "%3A%3A".Length);
        }
        int split = token.LastIndexOf("::", StringComparison.Ordinal);
        if (split >= 0)
        {
            return token.Substring(split + 2);
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
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return null;
        }
        return trimmed;
    }

    private static string ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string token = Normalize(lines[i]);
            if (token != null)
            {
                return token;
            }
        }
        return null;
    }

    private static string SqliteValue(string key)
    {
        string[] roots = new string[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        for (int i = 0; i < roots.Length; i++)
        {
            string path = Path.Combine(roots[i], "Cursor", "User", "globalStorage", "state.vscdb");
            if (!File.Exists(path))
            {
                continue;
            }
            string value;
            string error;
            if (SqliteKv.TryRead(path, key, out value, out error) && !string.IsNullOrEmpty(value))
            {
                return value.Trim();
            }
        }
        return null;
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
            payload = payload + new string('=', 4 - remainder);
        }
        try
        {
            byte[] data = Convert.FromBase64String(payload);
            object parsed = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(data));
            return JsonValue.Text(JsonValue.Get(JsonValue.Map(parsed), "sub"));
        }
        catch
        {
            return null;
        }
    }
}

internal static class SnapshotCache
{
    public static QuotaSnapshot Load()
    {
        try
        {
            if (!File.Exists(PathName()))
            {
                return null;
            }
            object parsed = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(PathName(), Encoding.UTF8));
            Dictionary<string, object> map = JsonValue.Map(parsed);
            if (map == null)
            {
                return null;
            }
            QuotaSnapshot snapshot = new QuotaSnapshot();
            snapshot.PlanType = JsonValue.Text(JsonValue.Get(map, "planType"));
            snapshot.SourceName = "cache";
            double? sampled = JsonValue.UnixSeconds(JsonValue.Get(map, "sampledAt"));
            snapshot.SampledAt = sampled.HasValue
                ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sampled.Value)
                : DateTime.UtcNow;
            double? resetAt = JsonValue.Number(JsonValue.Get(map, "resetAt"));
            double? windowMinutes = JsonValue.Number(JsonValue.Get(map, "windowMinutes"));
            snapshot.Primary = Window(
                JsonValue.Number(JsonValue.Get(map, "totalRemain")),
                JsonValue.Number(JsonValue.Get(map, "totalUsed")),
                resetAt,
                windowMinutes,
                Remap(JsonValue.Text(JsonValue.Get(map, "primaryBadge")), "内置"),
                JsonValue.Text(JsonValue.Get(map, "primaryTitle")) ?? "内置模型剩余");
            snapshot.Secondary = Window(
                JsonValue.Number(JsonValue.Get(map, "secondaryRemain")),
                JsonValue.Number(JsonValue.Get(map, "secondaryUsed")),
                resetAt,
                windowMinutes,
                Remap(JsonValue.Text(JsonValue.Get(map, "secondaryBadge")), "其他"),
                JsonValue.Text(JsonValue.Get(map, "secondaryTitle")) ?? "其他模型剩余");
            if (!snapshot.Primary.Remaining.HasValue && !snapshot.Secondary.Remaining.HasValue)
            {
                return null;
            }
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(QuotaSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }
        try
        {
            string path = PathName();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            double sampled = (snapshot.SampledAt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"planType\": ").Append(JsonString(snapshot.PlanType)).Append(",\n");
            json.Append("  \"totalRemain\": ").Append(JsonNumber(snapshot.Primary.Remaining)).Append(",\n");
            json.Append("  \"totalUsed\": ").Append(JsonNumber(snapshot.Primary.Used)).Append(",\n");
            json.Append("  \"primaryBadge\": ").Append(JsonString(snapshot.Primary.Badge)).Append(",\n");
            json.Append("  \"primaryTitle\": ").Append(JsonString(snapshot.Primary.Title)).Append(",\n");
            json.Append("  \"secondaryRemain\": ").Append(JsonNumber(snapshot.Secondary.Remaining)).Append(",\n");
            json.Append("  \"secondaryUsed\": ").Append(JsonNumber(snapshot.Secondary.Used)).Append(",\n");
            json.Append("  \"secondaryBadge\": ").Append(JsonString(snapshot.Secondary.Badge)).Append(",\n");
            json.Append("  \"secondaryTitle\": ").Append(JsonString(snapshot.Secondary.Title)).Append(",\n");
            json.Append("  \"resetAt\": ").Append(JsonNumber(snapshot.Primary.ResetAt)).Append(",\n");
            json.Append("  \"windowMinutes\": ").Append(JsonNumber(snapshot.Primary.WindowMinutes)).Append(",\n");
            json.Append("  \"sampledAt\": ").Append(JsonNumber(sampled)).Append("\n");
            json.Append("}\n");
            File.WriteAllText(path, json.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static QuotaWindow Window(double? remaining, double? used, double? resetAt, double? windowMinutes, string badge, string title)
    {
        QuotaWindow window = new QuotaWindow();
        window.Remaining = remaining;
        window.Used = used;
        window.ResetAt = resetAt;
        window.WindowMinutes = windowMinutes;
        window.Badge = badge;
        window.Title = title;
        return window;
    }

    private static string Remap(string value, string fallback)
    {
        if (value == "总额" || value == "Auto") return "内置";
        if (value == "API") return "其他";
        if (!string.IsNullOrEmpty(value)) return value;
        return fallback;
    }

    private static string PathName()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorQuotaPet", "last-snapshot.json");
    }

    private static string JsonString(string value)
    {
        if (value == null)
        {
            return "null";
        }
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string JsonNumber(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return "null";
        }
        return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

internal sealed class CursorUsageClient
{
    public string CurrentError = "";
    private bool _inflight;

    public QuotaSnapshot RefreshBlocking()
    {
        if (_inflight)
        {
            return null;
        }
        _inflight = true;
        try
        {
            return RefreshOnQueue();
        }
        finally
        {
            _inflight = false;
        }
    }

    public void RefreshAsync(Control ui, Action<QuotaSnapshot, string> completed)
    {
        if (_inflight)
        {
            return;
        }
        _inflight = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            QuotaSnapshot snapshot = null;
            string error = "";
            try
            {
                snapshot = RefreshOnQueue();
                error = CurrentError;
            }
            catch (Exception ex)
            {
                error = Friendly(ex.Message);
                CurrentError = error;
            }
            finally
            {
                _inflight = false;
            }
            QuotaSnapshot captured = snapshot;
            string capturedError = error;
            try
            {
                ui.BeginInvoke((Action)delegate { completed(captured, capturedError); });
            }
            catch
            {
            }
        });
    }

    private QuotaSnapshot RefreshOnQueue()
    {
        SessionInfo session = SessionStore.Load();
        if (session == null)
        {
            CurrentError = "未找到 Cursor 登录态，请先在 Cursor 中登录";
            return null;
        }
        string bearer = SessionStore.BearerToken(session.Token);
        string cookie = SessionStore.CookieValue(session.Token);
        FetchSlot[] slots = new FetchSlot[]
        {
            Slot("POST", "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage", bearer, null, true),
            Slot("POST", "https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo", bearer, null, true),
            Slot("GET", "https://cursor.com/api/usage-summary", null, cookie, false),
            Slot("GET", "https://api2.cursor.sh/auth/usage", bearer, null, false),
            Slot("GET", "https://api2.cursor.sh/auth/full_stripe_profile", bearer, null, false)
        };
        CountdownEvent done = new CountdownEvent(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            FetchSlot slot = slots[i];
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                FetchSlot item = (FetchSlot)state;
                try
                {
                    Fetch(item);
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message;
                }
                finally
                {
                    done.Signal();
                }
            }, slot);
        }
        done.Wait(18000);
        QuotaSnapshot snapshot = QuotaParser.Snapshot(
            slots[0].Json,
            slots[1].Json,
            slots[2].Json,
            slots[3].Json,
            slots[4].Json,
            session.LocalPlan,
            DateTime.UtcNow,
            "cursor-api");
        if (snapshot != null)
        {
            CurrentError = "";
            SnapshotCache.Save(snapshot);
            return snapshot;
        }
        CurrentError = Friendly(FirstError(slots));
        return null;
    }

    private static FetchSlot Slot(string method, string url, string bearer, string cookie, bool connect)
    {
        FetchSlot slot = new FetchSlot();
        slot.Method = method;
        slot.Url = url;
        slot.Bearer = bearer;
        slot.Cookie = cookie;
        slot.Connect = connect;
        return slot;
    }

    private static void Fetch(FetchSlot slot)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(slot.Url);
        request.Method = slot.Method;
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;
        request.Accept = "application/json";
        request.UserAgent = "CursorQuotaPet/1.4";
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        if (slot.Connect || slot.Method == "POST")
        {
            request.ContentType = "application/json";
            request.Headers["Connect-Protocol-Version"] = "1";
            request.Headers["Origin"] = "https://cursor.com";
        }
        if (!string.IsNullOrEmpty(slot.Bearer))
        {
            request.Headers["Authorization"] = "Bearer " + slot.Bearer;
        }
        if (!string.IsNullOrEmpty(slot.Cookie))
        {
            request.Headers["Cookie"] = "WorkosCursorSessionToken=" + slot.Cookie;
            request.Headers["Origin"] = "https://cursor.com";
        }
        if (slot.Method == "POST")
        {
            byte[] body = Encoding.UTF8.GetBytes("{}");
            request.ContentLength = body.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }
        }
        HttpWebResponse response = null;
        try
        {
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    slot.Error = ex.Message;
                    return;
                }
            }
            int status = (int)response.StatusCode;
            string text;
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }
            if (status == 401 || status == 403)
            {
                slot.Error = "会话已过期，请重新登录 Cursor";
                return;
            }
            if (status >= 400)
            {
                slot.Error = "HTTP " + status.ToString(CultureInfo.InvariantCulture);
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                slot.Error = "空响应";
                return;
            }
            object parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);
            slot.Json = JsonValue.Map(parsed);
            if (slot.Json == null)
            {
                slot.Error = "响应不是 JSON";
            }
        }
        finally
        {
            if (response != null)
            {
                response.Close();
            }
        }
    }

    private static string FirstError(FetchSlot[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!string.IsNullOrEmpty(slots[i].Error))
            {
                return slots[i].Error;
            }
        }
        return "额度接口没有返回数据";
    }

    private static string Friendly(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "额度接口没有返回数据";
        }
        string lower = message.ToLowerInvariant();
        if (lower.IndexOf("401", StringComparison.Ordinal) >= 0
            || lower.IndexOf("403", StringComparison.Ordinal) >= 0
            || message.IndexOf("过期", StringComparison.Ordinal) >= 0)
        {
            return "会话已过期，请重新登录 Cursor";
        }
        if (lower.IndexOf("timed out", StringComparison.Ordinal) >= 0
            || lower.IndexOf("timeout", StringComparison.Ordinal) >= 0
            || lower.IndexOf("network", StringComparison.Ordinal) >= 0
            || message.IndexOf("无法", StringComparison.Ordinal) >= 0)
        {
            return "网络不可用，稍后重试";
        }
        return message;
    }

    private sealed class FetchSlot
    {
        public string Method;
        public string Url;
        public string Bearer;
        public string Cookie;
        public bool Connect;
        public Dictionary<string, object> Json;
        public string Error;
    }
}

internal sealed class QuotaModel
{
    public readonly Control Ui;
    public QuotaSnapshot Snapshot;
    public QuotaSnapshot Fallback;
    public string ConnectionError = "";
    public string FooterText = "正在读取 Cursor 登录态…";
    public event Action Changed;

    private readonly CursorUsageClient _client = new CursorUsageClient();
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _fetchTimer;
    private string _iconA = "";
    private string _iconB = "";
    public bool IconChanged;

    public QuotaModel(Control ui)
    {
        Ui = ui;
        _uiTimer = new System.Windows.Forms.Timer();
        _uiTimer.Interval = 1000;
        _uiTimer.Tick += delegate { Publish(false); };
        _fetchTimer = new System.Windows.Forms.Timer();
        _fetchTimer.Interval = 30000;
        _fetchTimer.Tick += delegate { Refresh(); };
    }

    public QuotaSnapshot Display
    {
        get { return Snapshot ?? Fallback; }
    }

    public void Start()
    {
        Fallback = SnapshotCache.Load();
        Publish(true);
        Refresh();
        _uiTimer.Start();
        _fetchTimer.Start();
    }

    public void Stop()
    {
        _uiTimer.Stop();
        _fetchTimer.Stop();
    }

    public void Refresh()
    {
        Fallback = SnapshotCache.Load();
        Publish(true);
        _client.RefreshAsync(Ui, delegate(QuotaSnapshot snapshot, string error)
        {
            if (snapshot != null)
            {
                Snapshot = snapshot;
                ConnectionError = "";
            }
            else if (!string.IsNullOrEmpty(error))
            {
                ConnectionError = error;
            }
            Publish(true);
        });
    }

    private void Publish(bool allowIcon)
    {
        QuotaSnapshot display = Display;
        FooterText = display == null && !string.IsNullOrEmpty(ConnectionError)
            ? ConnectionError
            : QuotaFormatter.Footer(display, ConnectionError);
        string nextA = QuotaFormatter.ShortNumber(display != null && display.Primary != null ? display.Primary.Remaining : (double?)null);
        string nextB = QuotaFormatter.ShortNumber(display != null && display.Secondary != null ? display.Secondary.Remaining : (double?)null);
        IconChanged = allowIcon && (nextA != _iconA || nextB != _iconB);
        _iconA = nextA;
        _iconB = nextB;
        Action changed = Changed;
        if (changed != null)
        {
            changed();
        }
    }
}
