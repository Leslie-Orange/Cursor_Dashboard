using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class CursorQuotaPetMain
{
    private const string MutexName = "Local\\CursorQuotaPet";
    private const string ShowEventName = "Local\\CursorQuotaPet.Show";

    [STAThread]
    private static int Main(string[] args)
    {
        if (Has(args, "--probe"))
        {
            BindConsole();
            Environment.Exit(RunProbe());
        }
        if (Has(args, "--self-test"))
        {
            BindConsole();
            Environment.Exit(RunSelfTest(args));
        }

        try
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol
                | (SecurityProtocolType)3072
                | (SecurityProtocolType)12288;
        }
        catch
        {
        }
        ServicePointManager.Expect100Continue = false;

        bool created;
        Mutex mutex = new Mutex(true, MutexName, out created);
        EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!created)
        {
            if (Has(args, "--show"))
            {
                showEvent.Set();
            }
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        QuotaApplicationContext context = new QuotaApplicationContext(Has(args, "--show"));
        Thread waiter = new Thread(delegate()
        {
            while (showEvent.WaitOne())
            {
                try
                {
                    context.ShowFromExternal();
                }
                catch
                {
                }
            }
        });
        waiter.IsBackground = true;
        waiter.Start();
        Application.Run(context);
        mutex.ReleaseMutex();
        return 0;
    }

    private static int RunProbe()
    {
        try
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
        }
        catch
        {
        }
        CursorUsageClient client = new CursorUsageClient();
        QuotaSnapshot snapshot = client.RefreshBlocking();
        if (snapshot == null)
        {
            snapshot = SnapshotCache.Load();
        }
        if (snapshot != null)
        {
            Console.WriteLine(ProbeOk(snapshot));
            return 0;
        }
        string message = client.CurrentError;
        if (string.IsNullOrEmpty(message))
        {
            message = "没有找到可读取的 Cursor 额度";
        }
        Console.WriteLine("{\n  \"Message\": \"" + Escape(message) + "\",\n  \"Status\": \"unavailable\"\n}");
        return 1;
    }

    private static string ProbeOk(QuotaSnapshot snapshot)
    {
        StringBuilder json = new StringBuilder();
        json.Append("{\n");
        json.Append("  \"IncludedRemain\": ").Append(JsonNumber(snapshot.Primary != null ? snapshot.Primary.Remaining : (double?)null)).Append(",\n");
        json.Append("  \"OtherRemain\": ").Append(JsonNumber(snapshot.Secondary != null ? snapshot.Secondary.Remaining : (double?)null)).Append(",\n");
        json.Append("  \"PlanNote\": ").Append(snapshot.PlanNote == null ? "null" : "\"" + Escape(snapshot.PlanNote) + "\"").Append(",\n");
        json.Append("  \"PlanType\": ").Append(snapshot.PlanType == null ? "null" : "\"" + Escape(snapshot.PlanType) + "\"").Append(",\n");
        json.Append("  \"PrimaryBadge\": \"").Append(Escape(snapshot.Primary != null ? snapshot.Primary.Badge : "内置")).Append("\",\n");
        json.Append("  \"SampledAt\": \"").Append(snapshot.SampledAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append("\",\n");
        json.Append("  \"SecondaryBadge\": \"").Append(Escape(snapshot.Secondary != null ? snapshot.Secondary.Badge : "其他")).Append("\",\n");
        json.Append("  \"SourceName\": \"").Append(Escape(snapshot.SourceName)).Append("\",\n");
        json.Append("  \"Status\": \"ok\"\n");
        json.Append("}\n");
        return json.ToString();
    }

    private static int RunSelfTest(string[] args)
    {
        try
        {
            TestParser();
            TestAnchor();
            TestDetailLayout();
            TestTrayTipLayout();
            TestDpiCursorSet();
            TestLocator();
            TestHover();
            TestSqlite();
            if (Has(args, "--preview"))
            {
                string path = Path.Combine(Path.GetTempPath(), "cursor-quota-preview.png");
                Screen screen = Screen.FromPoint(Cursor.Position);
                if (screen == null)
                {
                    screen = Screen.PrimaryScreen;
                }
                int dpi = NativeMethods.GetMonitorDpi(screen.Bounds, IntPtr.Zero);
                DetailLayout layout = DetailLayout.Create(screen.WorkingArea, dpi);
                Console.WriteLine("screen.bounds=" + FormatRectangle(screen.Bounds));
                Console.WriteLine("screen.workingArea=" + FormatRectangle(screen.WorkingArea));
                Console.WriteLine("screen.dpi=" + dpi.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("screen.windowSize=" + layout.WindowSize.Width.ToString(CultureInfo.InvariantCulture) + "x" + layout.WindowSize.Height.ToString(CultureInfo.InvariantCulture));
                SavePreview(path, layout);
                Console.WriteLine("preview=" + path);
            }
            Console.WriteLine("self-test=ok");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("self-test=fail " + ex.Message);
            return 1;
        }
    }

    private static void TestParser()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["autoPercentUsed"] = 26;
        planUsage["apiPercentUsed"] = 0;
        planUsage["limit"] = 2000;
        planUsage["remaining"] = 1500;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;
        dashboard["billingCycleStart"] = "2026-09-01T00:00:00Z";
        dashboard["billingCycleEnd"] = "2026-10-01T00:00:00Z";
        Dictionary<string, object> planInfo = new Dictionary<string, object>();
        Dictionary<string, object> nested = new Dictionary<string, object>();
        nested["planName"] = "pro";
        planInfo["planInfo"] = nested;
        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, planInfo, null, null, null, null, DateTime.UtcNow, "cursor-api");
        if (snapshot == null)
        {
            throw new Exception("parser returned null");
        }
        if (Math.Abs(snapshot.Primary.Remaining.Value - 74) > 0.1)
        {
            throw new Exception("allowance remain " + snapshot.Primary.Remaining.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (snapshot.Primary.Badge != "内置")
        {
            throw new Exception("allowance badge " + snapshot.Primary.Badge);
        }
        if (snapshot.Primary.Detail != null)
        {
            throw new Exception("allowance detail " + snapshot.Primary.Detail);
        }
        if (Math.Abs(snapshot.Secondary.Remaining.Value - 100) > 0.1)
        {
            throw new Exception("other remain");
        }
        TestLiveAllowance();
        TestSmallPoolPercent();
        TestOtherPercentPoints();
        TestActualUsageResponse();
        TestSummaryOnlyUsageResponse();
        TestUsagePercentWithoutAmountFields();
        TestAllowanceFallbackWithoutAutoPercent();
        TestSnapshotCache();
        TestCurrentPlans();
        if (QuotaFormatter.PlanTitle(snapshot.PlanType) != "Pro 额度")
        {
            throw new Exception("plan title " + QuotaFormatter.PlanTitle(snapshot.PlanType));
        }
        if (QuotaFormatter.Percent(10) != "10%")
        {
            throw new Exception("percent format");
        }
    }

    private static void TestSnapshotCache()
    {
        string oldCache = "{\"planType\":\"pro\",\"totalRemain\":0,\"totalUsed\":100,\"primaryBadge\":\"额度\",\"secondaryRemain\":100,\"secondaryUsed\":0}";
        if (SnapshotCache.Parse(oldCache) != null)
        {
            throw new Exception("unversioned old amount cache should be rejected");
        }

        string versionOneCache = "{\"quotaFormatVersion\":1,\"planType\":\"pro\",\"totalRemain\":0,\"totalUsed\":100,\"primaryBadge\":\"额度\",\"secondaryRemain\":100,\"secondaryUsed\":0}";
        if (SnapshotCache.Parse(versionOneCache) != null)
        {
            throw new Exception("version 1 cache should be rejected");
        }

        string versionTwoCache = "{\"quotaFormatVersion\":2,\"planType\":\"pro\",\"totalRemain\":95.7867,\"totalUsed\":4.2133,\"primaryBadge\":\"内置\",\"secondaryRemain\":94.7556,\"secondaryUsed\":5.2444,\"secondaryBadge\":\"其他\"}";
        QuotaSnapshot snapshot = SnapshotCache.Parse(versionTwoCache);
        if (snapshot == null || snapshot.Primary == null || snapshot.Secondary == null)
        {
            throw new Exception("version 2 cache was not parsed");
        }
        if (Math.Abs(snapshot.Primary.Remaining.Value - 95.7867) > 0.000001
            || Math.Abs(snapshot.Secondary.Remaining.Value - 94.7556) > 0.000001)
        {
            throw new Exception("version 2 cache quota values were not preserved");
        }
        if (snapshot.SourceName != "cache")
        {
            throw new Exception("version 2 cache source " + snapshot.SourceName);
        }

        if (SnapshotCache.Parse("{malformed json") != null)
        {
            throw new Exception("malformed cache json should be rejected");
        }
    }

    private static void TestCurrentPlans()
    {
        ExpectPlan("free", "Free 额度", "有限");
        ExpectPlan("plus", "Plus 额度", "基础 · 每周");
        ExpectPlan("premium", "Premium 额度", "4× Plus · 每周");
        ExpectPlan("super", "Super 额度", "15× Plus · 每周");
        ExpectPlan("ultra", "Ultra 额度", "50× Plus · 每周");
        ExpectPlan("pro", "Pro 额度", "月度双池");
        ExpectPlan("pro_plus", "Pro+ 额度", "月度双池");
        ExpectPlan("teams_premium", "Teams Premium 额度", "5× Standard · 每月");
        ExpectPlan("enterprise", "Enterprise 额度", "共享池");
        ExpectPlan("team", "Teams 额度", "按席位 · 每周");
        if (PlanCatalog.Note("ultra", PlanCatalog.Model("ultra", true)) != "月度双池")
        {
            throw new Exception("legacy ultra note");
        }

        QuotaSnapshot premium = PlanSnapshot("premium", 26, 40, false, null);
        AssertPrimaryQuota(premium, 26, 74, "本周", "4× Plus", "本周 74%", "premium weekly pool");
        ExpectText(premium.PlanNote, "4× Plus · 每周", "premium note");
        ExpectText(premium.Primary.Schedule, "每周重置", "premium schedule");
        if (premium.Primary.ResetAt.HasValue)
        {
            throw new Exception("premium must not use the monthly billing cycle as the usage reset");
        }
        ExpectText(premium.Secondary.Badge, "预付", "premium secondary");
        ExpectText(premium.Secondary.Detail, "按量", "premium prepaid");
        if (premium.Secondary.Remaining.HasValue)
        {
            throw new Exception("premium api percent was treated as an included other pool");
        }
        ExpectText(QuotaFormatter.Line(premium.Secondary, "其他"), "预付 按量", "premium line");

        QuotaSnapshot plus = PlanSnapshot("plus", 10, 80, false, null);
        AssertPrimaryQuota(plus, 10, 90, "本周", "基础", "本周 90%", "plus weekly pool");
        ExpectText(plus.Secondary.Detail, "不含", "plus third party");
        ExpectText(QuotaFormatter.Line(plus.Secondary, "其他"), "第三方 不含", "plus line");

        QuotaSnapshot super = PlanSnapshot("super", 15, 1, false, null);
        AssertPrimaryQuota(super, 15, 85, "本周", "15× Plus", "本周 85%", "super weekly pool");
        QuotaSnapshot ultra = PlanSnapshot("ultra", 20, 10, false, null);
        AssertPrimaryQuota(ultra, 20, 80, "本周", "50× Plus", "本周 80%", "ultra weekly pool");
        ExpectText(ultra.Secondary.Badge, "预付", "ultra secondary");

        QuotaSnapshot legacyUltra = PlanSnapshot("ultra", 20, 10, true, null);
        AssertPrimaryQuota(legacyUltra, 20, 80, "内置", null, "内置 80%", "legacy ultra");
        AssertOtherQuota(legacyUltra, 10, 90, "其他 90%", "legacy ultra other");
        ExpectText(legacyUltra.PlanNote, "月度双池", "legacy ultra note");

        QuotaSnapshot pro = PlanSnapshot("pro", 26, 40, false, null);
        AssertPrimaryQuota(pro, 26, 74, "内置", null, "内置 74%", "legacy pro");
        AssertOtherQuota(pro, 40, 60, "其他 60%", "legacy pro other");
        ExpectText(pro.PlanNote, "月度双池", "legacy pro note");
        if (!pro.Primary.ResetAt.HasValue)
        {
            throw new Exception("legacy pro lost its billing reset");
        }

        QuotaSnapshot teamsPremium = PlanSnapshot("teams_premium", 5, 6, false, null);
        AssertPrimaryQuota(teamsPremium, 5, 95, "内置", null, "内置 95%", "legacy teams premium");
        AssertOtherQuota(teamsPremium, 6, 94, "其他 94%", "legacy teams premium other");
        ExpectText(teamsPremium.PlanNote, "5× Standard · 每月", "legacy teams premium note");

        QuotaSnapshot free = PlanSnapshot("free", 50, null, false, null);
        AssertPrimaryQuota(free, 50, 50, "有限", "有限", "有限 50%", "free");
        ExpectText(free.Secondary.Detail, "不含", "free third party");

        QuotaSnapshot start = PlanSnapshot("start", 12, 9, false, null);
        AssertPrimaryQuota(start, 12, 88, "内置", null, "内置 88%", "start");
        ExpectText(start.Secondary.Detail, "不含", "start other pool");
        ExpectText(start.PlanNote, "仅内置 · 每月", "start note");

        QuotaSnapshot enterprise = PlanSnapshot("enterprise", 30, 4, false, null);
        AssertPrimaryQuota(enterprise, 30, 70, "共享", null, "共享 70%", "enterprise");
        ExpectText(enterprise.PlanNote, "共享池", "enterprise note");
        ExpectText(enterprise.Secondary.Badge, "预付", "enterprise prepaid");
        if (!enterprise.Primary.ResetAt.HasValue)
        {
            throw new Exception("enterprise lost its billing reset");
        }

        QuotaSnapshot credited = PlanSnapshot("premium", 26, null, false, 1250);
        ExpectText(credited.Secondary.Detail, "余额 $12.50", "prepaid balance");
        ExpectText(QuotaFormatter.Line(credited.Secondary, "其他"), "预付 余额 $12.50", "prepaid line");
    }

    private static void ExpectPlan(string plan, string title, string note)
    {
        string actualTitle = QuotaFormatter.PlanTitle(plan);
        if (actualTitle != title)
        {
            throw new Exception("title " + plan + " " + actualTitle);
        }
        string actualNote = PlanCatalog.Note(plan, PlanCatalog.Model(plan, false));
        if (actualNote != note)
        {
            throw new Exception("note " + plan + " " + actualNote);
        }
    }

    private static void ExpectText(string actual, string expected, string name)
    {
        if (actual != expected)
        {
            throw new Exception(name + " expected " + expected + " actual " + (actual ?? "null"));
        }
    }

    private static QuotaSnapshot PlanSnapshot(string membership, double autoUsed, double? apiUsed, bool legacy, double? creditCents)
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["autoPercentUsed"] = autoUsed;
        if (apiUsed.HasValue)
        {
            planUsage["apiPercentUsed"] = apiUsed.Value;
        }
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;
        dashboard["billingCycleStart"] = "2026-09-01T00:00:00Z";
        dashboard["billingCycleEnd"] = "2026-10-01T00:00:00Z";
        Dictionary<string, object> summary = new Dictionary<string, object>();
        summary["membershipType"] = membership;
        if (legacy)
        {
            summary["isLegacyPlan"] = true;
        }
        if (creditCents.HasValue)
        {
            summary["creditBalanceCents"] = creditCents.Value;
        }
        return QuotaParser.Snapshot(dashboard, null, summary, null, null, null, DateTime.UtcNow, "cursor-api");
    }

    private static void TestLiveAllowance()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["includedSpend"] = 508;
        planUsage["remaining"] = 1492;
        planUsage["limit"] = 2000;
        planUsage["autoPercentUsed"] = 1.1289;
        planUsage["apiPercentUsed"] = 0;
        planUsage["totalPercentUsed"] = 1.0751;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;
        dashboard["displayMessage"] = "You've used 25% of your included usage";
        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        if (snapshot == null)
        {
            throw new Exception("live allowance parser returned null");
        }
        AssertPrimaryQuota(snapshot, 1.1289, 98.8711, "内置", null, "内置 99%", "auto pool must take priority over live amount fields");
        if (Math.Abs(snapshot.Secondary.Remaining.Value - 100) > 0.1)
        {
            throw new Exception("live other remain");
        }
    }

    private static void TestSmallPoolPercent()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["autoPercentUsed"] = 0.7;
        planUsage["apiPercentUsed"] = 0;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;
        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        if (snapshot == null)
        {
            throw new Exception("small pool parser returned null");
        }
        if (snapshot.Primary.Badge != "内置")
        {
            throw new Exception("small pool badge " + snapshot.Primary.Badge);
        }
        if (Math.Abs(snapshot.Primary.Remaining.Value - 99.3) > 0.05)
        {
            throw new Exception("small pool remain " + snapshot.Primary.Remaining.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (Math.Abs(snapshot.Primary.Used.Value - 0.7) > 0.000001 || QuotaFormatter.Line(snapshot.Primary, "内置") != "内置 99%")
        {
            throw new Exception("small pool percentage-point display");
        }
    }

    private static void TestOtherPercentPoints()
    {
        double[] usedValues = new double[] { 0.6222222222222222, 0.7, 1, 0 };
        double[] remainingValues = new double[] { 99.37777777777778, 99.3, 99, 100 };
        string[] lines = new string[] { "其他 99%", "其他 99%", "其他 99%", "其他 100%" };
        for (int i = 0; i < usedValues.Length; i++)
        {
            Dictionary<string, object> planUsage = new Dictionary<string, object>();
            planUsage["apiPercentUsed"] = usedValues[i];
            Dictionary<string, object> dashboard = new Dictionary<string, object>();
            dashboard["planUsage"] = planUsage;
            QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
            AssertOtherQuota(snapshot, usedValues[i], remainingValues[i], lines[i], "dashboard apiPercentUsed " + usedValues[i].ToString(CultureInfo.InvariantCulture));
        }

        Dictionary<string, object> summaryPlan = new Dictionary<string, object>();
        summaryPlan["apiPercentUsed"] = 0.6222222222222222;
        Dictionary<string, object> individualUsage = new Dictionary<string, object>();
        individualUsage["plan"] = summaryPlan;
        Dictionary<string, object> summary = new Dictionary<string, object>();
        summary["individualUsage"] = individualUsage;
        QuotaSnapshot summarySnapshot = QuotaParser.Snapshot(null, null, summary, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertOtherQuota(summarySnapshot, 0.6222222222222222, 99.37777777777778, "其他 99%", "summary individualUsage.plan apiPercentUsed");

        Dictionary<string, object> messageDashboard = new Dictionary<string, object>();
        messageDashboard["namedModelSelectedDisplayMessage"] = "You've used 0.6% of named model usage";
        QuotaSnapshot messageSnapshot = QuotaParser.Snapshot(messageDashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertOtherQuota(messageSnapshot, 0.6, 99.4, "其他 99%", "namedModelSelectedDisplayMessage");
    }

    private static void AssertOtherQuota(QuotaSnapshot snapshot, double expectedUsed, double expectedRemaining, string expectedLine, string context)
    {
        if (snapshot == null || snapshot.Secondary == null || !snapshot.Secondary.Used.HasValue || !snapshot.Secondary.Remaining.HasValue)
        {
            throw new Exception(context + " did not produce other quota percentages");
        }
        if (Math.Abs(snapshot.Secondary.Used.Value - expectedUsed) > 0.000001)
        {
            throw new Exception(context + " used " + snapshot.Secondary.Used.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        if (Math.Abs(snapshot.Secondary.Remaining.Value - expectedRemaining) > 0.000001)
        {
            throw new Exception(context + " remain " + snapshot.Secondary.Remaining.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        string actualLine = QuotaFormatter.Line(snapshot.Secondary, "其他");
        if (actualLine != expectedLine)
        {
            throw new Exception(context + " display " + actualLine);
        }
    }

    private static void TestActualUsageResponse()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["totalSpend"] = 2014;
        planUsage["includedSpend"] = 2000;
        planUsage["bonusSpend"] = 14;
        planUsage["limit"] = 2000;
        planUsage["autoPercentUsed"] = 4.213333333333334;
        planUsage["apiPercentUsed"] = 5.2444444444444445;
        planUsage["totalPercentUsed"] = 4.262433862433863;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;

        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertPrimaryQuota(snapshot, 4.213333333333334, 95.78666666666666, "内置", null, "内置 96%", "current dashboard response primary");
        AssertOtherQuota(snapshot, 5.2444444444444445, 94.75555555555555, "其他 95%", "current dashboard response secondary");
    }

    private static void TestSummaryOnlyUsageResponse()
    {
        Dictionary<string, object> summaryPlan = new Dictionary<string, object>();
        summaryPlan["used"] = 2000;
        summaryPlan["limit"] = 2000;
        summaryPlan["remaining"] = 0;
        summaryPlan["autoPercentUsed"] = 4.213333333333334;
        summaryPlan["apiPercentUsed"] = 5.2444444444444445;
        summaryPlan["totalPercentUsed"] = 4.262433862433863;
        Dictionary<string, object> individualUsage = new Dictionary<string, object>();
        individualUsage["plan"] = summaryPlan;
        Dictionary<string, object> summary = new Dictionary<string, object>();
        summary["individualUsage"] = individualUsage;

        QuotaSnapshot snapshot = QuotaParser.Snapshot(null, null, summary, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertPrimaryQuota(snapshot, 4.213333333333334, 95.78666666666666, "内置", null, "内置 96%", "summary-only primary");
        AssertOtherQuota(snapshot, 5.2444444444444445, 94.75555555555555, "其他 95%", "summary-only secondary");
    }

    private static void TestUsagePercentWithoutAmountFields()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["autoPercentUsed"] = 4.213333333333334;
        planUsage["apiPercentUsed"] = 5.2444444444444445;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;

        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertPrimaryQuota(snapshot, 4.213333333333334, 95.78666666666666, "内置", null, "内置 96%", "percent-only dashboard response");
        AssertOtherQuota(snapshot, 5.2444444444444445, 94.75555555555555, "其他 95%", "percent-only dashboard response secondary");
    }

    private static void TestAllowanceFallbackWithoutAutoPercent()
    {
        Dictionary<string, object> planUsage = new Dictionary<string, object>();
        planUsage["includedSpend"] = 508;
        planUsage["remaining"] = 1492;
        planUsage["limit"] = 2000;
        planUsage["apiPercentUsed"] = 5.2444444444444445;
        planUsage["totalPercentUsed"] = 4.262433862433863;
        Dictionary<string, object> dashboard = new Dictionary<string, object>();
        dashboard["planUsage"] = planUsage;

        QuotaSnapshot snapshot = QuotaParser.Snapshot(dashboard, null, null, null, null, null, DateTime.UtcNow, "cursor-api");
        AssertPrimaryQuota(snapshot, 25.4, 74.6, "额度", "$5.08 / $20", "额度 75%", "amount fallback without autoPercentUsed");
        AssertOtherQuota(snapshot, 5.2444444444444445, 94.75555555555555, "其他 95%", "amount fallback secondary");
    }

    private static void AssertPrimaryQuota(QuotaSnapshot snapshot, double expectedUsed, double expectedRemaining, string expectedBadge, string expectedDetail, string expectedLine, string context)
    {
        if (snapshot == null || snapshot.Primary == null || !snapshot.Primary.Used.HasValue || !snapshot.Primary.Remaining.HasValue)
        {
            throw new Exception(context + " did not produce primary quota percentages");
        }
        if (Math.Abs(snapshot.Primary.Used.Value - expectedUsed) > 0.000001)
        {
            throw new Exception(context + " used " + snapshot.Primary.Used.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        if (Math.Abs(snapshot.Primary.Remaining.Value - expectedRemaining) > 0.000001)
        {
            throw new Exception(context + " remain " + snapshot.Primary.Remaining.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        if (snapshot.Primary.Badge != expectedBadge || snapshot.Primary.Detail != expectedDetail)
        {
            throw new Exception(context + " badge/detail " + snapshot.Primary.Badge + "/" + (snapshot.Primary.Detail ?? "null"));
        }
        string actualLine = QuotaFormatter.Line(snapshot.Primary, expectedBadge);
        if (actualLine != expectedLine)
        {
            throw new Exception(context + " display " + actualLine);
        }
    }

    private static void TestAnchor()
    {
        Rectangle screen = new Rectangle(0, 0, 1920, 1080);
        Rectangle icon = new Rectangle(1504, 1032, 32, 48);
        Size tip = new Size(100, 40);
        Point point = PopupAnchor.Above(icon, tip, 0, 0, tip, 8, screen);
        Expect(point.X, 1470, "tip x");
        Expect(point.Y, 984, "tip y");

        Size window = new Size(422, 328);
        Size content = new Size(386, 292);
        Point popup = PopupAnchor.Above(icon, window, 18, 18, content, 8, screen);
        Expect(popup.X, 1309, "popup x");
        Expect(popup.Y, 714, "popup y");

        Rectangle overflow = new Rectangle(1488, 890, 44, 44);
        Point aboveOverflow = PopupAnchor.Above(overflow, tip, 0, 0, tip, 8, screen);
        Expect(aboveOverflow.X, 1460, "overflow tip x");
        Expect(aboveOverflow.Y, 842, "overflow tip y");

        Point clamped = PopupAnchor.Above(new Rectangle(10, 900, 40, 40), new Size(200, 40), 0, 0, new Size(200, 40), 8, screen);
        Expect(clamped.X, 4, "clamp x");
        Expect(clamped.Y, 852, "clamp y");

        Point flipped = PopupAnchor.Above(new Rectangle(100, 4, 32, 32), new Size(80, 40), 0, 0, new Size(80, 40), 8, screen);
        Expect(flipped.Y, 44, "flip y");
    }

    private static void TestDetailLayout()
    {
        int[] dpis = new int[] { 96, 120, 144, 192, 288 };
        Rectangle roomyArea = new Rectangle(-2400, -200, 2560, 1440);
        QuotaSnapshot snapshot = CreatePreviewSnapshot();
        Size previousSize = Size.Empty;
        for (int i = 0; i < dpis.Length; i++)
        {
            int dpi = dpis[i];
            DetailLayout layout = DetailLayout.Create(roomyArea, dpi);
            double expectedScale = dpi / 96.0;
            if (Math.Abs(layout.Scale - expectedScale) > 0.001)
            {
                throw new Exception("detail scale at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            if (layout.WindowSize.Width <= 0 || layout.WindowSize.Height <= 0)
            {
                throw new Exception("detail window size at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            ExpectFloor(layout.WindowSize.Width, GlassTheme.PanelWidth + GlassTheme.Shadow * 2, layout.Scale, "detail width floor");
            ExpectFloor(layout.WindowSize.Height, GlassTheme.PanelHeight + GlassTheme.Shadow * 2, layout.Scale, "detail height floor");
            Rectangle sample = new Rectangle(13, 17, 211, 173);
            Rectangle pixels = layout.ToPixels(sample);
            ExpectFloor(pixels.Left, sample.Left, layout.Scale, "detail left floor");
            ExpectFloor(pixels.Top, sample.Top, layout.Scale, "detail top floor");
            ExpectFloor(pixels.Right, sample.Right, layout.Scale, "detail right floor");
            ExpectFloor(pixels.Bottom, sample.Bottom, layout.Scale, "detail bottom floor");

            DetailLayout repeated = DetailLayout.Create(roomyArea, dpi);
            if (repeated.Scale != layout.Scale || repeated.WindowSize != layout.WindowSize
                || repeated.PanelBounds != layout.PanelBounds || repeated.ToPixels(sample) != pixels)
            {
                throw new Exception("detail layout accumulated scale at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            if (previousSize != Size.Empty
                && (layout.WindowSize.Width < previousSize.Width || layout.WindowSize.Height < previousSize.Height))
            {
                throw new Exception("detail size did not adapt with dpi");
            }
            previousSize = layout.WindowSize;

            if (dpi == 192 && layout.WindowSize != new Size(844, 656))
            {
                throw new Exception("192 dpi detail size expected 844x656 actual "
                    + layout.WindowSize.Width.ToString(CultureInfo.InvariantCulture) + "x"
                    + layout.WindowSize.Height.ToString(CultureInfo.InvariantCulture));
            }
            TestDetailRender(snapshot, layout, dpi);
        }

        Rectangle compactArea = new Rectangle(-1280, -120, 480, 320);
        DetailLayout compact = DetailLayout.Create(compactArea, 192);
        if (compact.WindowSize.Width > compactArea.Width - 8 || compact.WindowSize.Height > compactArea.Height - 8)
        {
            throw new Exception("detail window exceeds compact working area");
        }
        if (compact.Scale >= 2.0f)
        {
            throw new Exception("detail scale was not capped for compact working area");
        }

        Rectangle secondaryArea = new Rectangle(-1920, -100, 1920, 1040);
        DetailLayout secondary = DetailLayout.Create(secondaryArea, 144);
        AssertDetailAnchor(secondaryArea, secondary, new Rectangle(-1000, -96, 32, 32), "top anchor");
        AssertDetailAnchor(secondaryArea, secondary, new Rectangle(-1916, 350, 32, 32), "left anchor");
        AssertDetailAnchor(secondaryArea, secondary, new Rectangle(-32, 350, 32, 32), "right anchor");
        AssertDetailAnchor(secondaryArea, secondary, new Rectangle(-1000, 908, 32, 32), "bottom anchor");
    }

    private static void TestDetailRender(QuotaSnapshot snapshot, DetailLayout layout, int dpi)
    {
        Rectangle refresh;
        Rectangle close;
        using (Bitmap bitmap = DetailPainter.Render(snapshot, "自检", layout, false, false, out refresh, out close))
        {
            if (bitmap.Size != layout.WindowSize)
            {
                throw new Exception("detail bitmap size at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            Rectangle expectedRefresh = layout.ToPixels(new Rectangle(
                GlassTheme.Shadow + GlassTheme.PanelWidth - 76,
                GlassTheme.Shadow + 18,
                28,
                28));
            Rectangle expectedClose = layout.ToPixels(new Rectangle(
                GlassTheme.Shadow + GlassTheme.PanelWidth - 42,
                GlassTheme.Shadow + 18,
                28,
                28));
            ExpectRectangle(refresh, expectedRefresh, "refresh hit area");
            ExpectRectangle(close, expectedClose, "close hit area");
            Rectangle bitmapBounds = new Rectangle(Point.Empty, bitmap.Size);
            if (!bitmapBounds.Contains(refresh) || !bitmapBounds.Contains(close) || refresh.IntersectsWith(close))
            {
                throw new Exception("detail button hit areas outside bitmap at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
        }
    }

    private static void TestTrayTipLayout()
    {
        int[] dpis = new int[] { 96, 120, 144, 192, 288 };
        Rectangle roomyArea = new Rectangle(-2400, -200, 3840, 2160);
        string line1 = "内置剩余 74%";
        string line2 = "其他额度 100%";
        TrayTipLayout layout96 = TrayTipLayout.Create(line1, line2, 96, roomyArea);
        if (layout96.WindowSize.Width <= 0 || layout96.WindowSize.Height <= 0)
        {
            throw new Exception("tray tip has no physical area at 96 dpi");
        }

        for (int i = 0; i < dpis.Length; i++)
        {
            int dpi = dpis[i];
            TrayTipLayout layout = TrayTipLayout.Create(line1, line2, dpi, roomyArea);
            double expectedScale = dpi / 96.0;
            if (Math.Abs(layout.Scale - expectedScale) > 0.001)
            {
                throw new Exception("tray tip scale at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            AssertTipFits(layout, roomyArea, "tray tip at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            TestTrayTipRender(line1, line2, layout, dpi);

            TrayTipLayout repeated = TrayTipLayout.Create(line1, line2, dpi, roomyArea);
            if (repeated.Scale != layout.Scale || repeated.LogicalSize != layout.LogicalSize || repeated.WindowSize != layout.WindowSize)
            {
                throw new Exception("tray tip layout accumulated scale at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
        }

        TrayTipLayout layout192 = TrayTipLayout.Create(line1, line2, 192, roomyArea);
        ExpectApproximatelyDouble(layout96.WindowSize, layout192.WindowSize, "tray tip 200 percent size");

        TrayTipLayout empty = TrayTipLayout.Create(null, "", 144, roomyArea);
        if (empty.WindowSize.Width <= 0 || empty.WindowSize.Height <= 0)
        {
            throw new Exception("empty tray tip has no physical area");
        }
        TestTrayTipRender(null, "", empty, 144);

        StringBuilder longTextBuilder = new StringBuilder();
        for (int i = 0; i < 96; i++)
        {
            longTextBuilder.Append("额度边界测试 ");
        }
        string longText = longTextBuilder.ToString();
        Rectangle compactArea = new Rectangle(-420, -100, 420, 180);
        TrayTipLayout compact = TrayTipLayout.Create(longText, longText, 288, compactArea);
        AssertTipFits(compact, compactArea, "long tray tip on compact display");
        if (compact.Scale >= 3.0f)
        {
            throw new Exception("long tray tip was not reduced for compact display");
        }
        TestTrayTipRender(longText, longText, compact, 288);

        Rectangle secondaryArea = new Rectangle(-1600, -120, 1600, 900);
        TrayTipLayout secondary = TrayTipLayout.Create(line1, line2, 192, secondaryArea);
        AssertTrayTipAnchor(secondaryArea, secondary, new Rectangle(-900, -116, 32, 32), "tray tip top anchor");
        AssertTrayTipAnchor(secondaryArea, secondary, new Rectangle(-1596, 200, 32, 32), "tray tip left anchor");
        AssertTrayTipAnchor(secondaryArea, secondary, new Rectangle(-32, 200, 32, 32), "tray tip right anchor");
        AssertTrayTipAnchor(secondaryArea, secondary, new Rectangle(-900, 748, 32, 32), "tray tip bottom anchor");
    }

    private static void TestTrayTipRender(string line1, string line2, TrayTipLayout layout, int dpi)
    {
        using (Bitmap bitmap = TrayTipPainter.Render(line1, line2, Color.White, Color.LightSkyBlue, layout))
        {
            if (bitmap.Size != layout.WindowSize)
            {
                throw new Exception("tray tip bitmap size at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                throw new Exception("tray tip bitmap has no physical pixels at " + dpi.ToString(CultureInfo.InvariantCulture) + " dpi");
            }
        }
    }

    private static void AssertTipFits(TrayTipLayout layout, Rectangle workingArea, string name)
    {
        if (layout.WindowSize.Width <= 0 || layout.WindowSize.Height <= 0
            || layout.WindowSize.Width > workingArea.Width - 8
            || layout.WindowSize.Height > workingArea.Height - 8)
        {
            throw new Exception(name + " exceeds working area: " + FormatRectangle(new Rectangle(Point.Empty, layout.WindowSize)));
        }
    }

    private static void AssertTrayTipAnchor(Rectangle workingArea, TrayTipLayout layout, Rectangle icon, string name)
    {
        Point location = PopupAnchor.Above(icon, layout.WindowSize, 0, 0, layout.WindowSize, GlassTheme.TipGap, workingArea);
        Rectangle window = new Rectangle(location, layout.WindowSize);
        if (!workingArea.Contains(window))
        {
            throw new Exception(name + " escaped working area: " + FormatRectangle(window));
        }
        if (location.X >= 0)
        {
            throw new Exception(name + " lost negative secondary-screen coordinates");
        }
    }

    private static void ExpectApproximatelyDouble(Size singleScale, Size doubleScale, string name)
    {
        if (Math.Abs(doubleScale.Width - singleScale.Width * 2) > 2
            || Math.Abs(doubleScale.Height - singleScale.Height * 2) > 2)
        {
            throw new Exception(name + " expected about "
                + (singleScale.Width * 2).ToString(CultureInfo.InvariantCulture) + "x"
                + (singleScale.Height * 2).ToString(CultureInfo.InvariantCulture) + " actual "
                + doubleScale.Width.ToString(CultureInfo.InvariantCulture) + "x"
                + doubleScale.Height.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void TestDpiCursorSet()
    {
        DpiCursorSet cursors = new DpiCursorSet();
        try
        {
            cursors.Update(96);
            if (cursors.Arrow == null || cursors.Hand == null)
            {
                throw new Exception("96 dpi cursor set is incomplete");
            }
            IntPtr arrow96Handle = cursors.Arrow.Handle;
            IntPtr hand96Handle = cursors.Hand.Handle;
            Size arrow96 = NativeMethods.GetCursorImageSize(arrow96Handle);
            Size hand96 = NativeMethods.GetCursorImageSize(hand96Handle);
            ExpectCursorSize(arrow96, NativeMethods.GetCursorSize(96), "96 dpi arrow cursor");
            ExpectCursorSize(hand96, NativeMethods.GetCursorSize(96), "96 dpi hand cursor");

            cursors.Update(96);
            if (cursors.Arrow.Handle != arrow96Handle || cursors.Hand.Handle != hand96Handle)
            {
                throw new Exception("same-dpi cursor update did not reuse resources");
            }

            cursors.Update(192);
            IntPtr arrow192Handle = cursors.Arrow.Handle;
            IntPtr hand192Handle = cursors.Hand.Handle;
            Size arrow192 = NativeMethods.GetCursorImageSize(arrow192Handle);
            Size hand192 = NativeMethods.GetCursorImageSize(hand192Handle);
            Size expected192 = NativeMethods.GetCursorSize(192);
            ExpectCursorSize(arrow192, expected192, "192 dpi arrow cursor");
            ExpectCursorSize(hand192, expected192, "192 dpi hand cursor");
            if (arrow192.Width < arrow96.Width || arrow192.Height < arrow96.Height
                || hand192.Width < hand96.Width || hand192.Height < hand96.Height)
            {
                throw new Exception("200 percent cursor image is smaller than 100 percent");
            }
            if (arrow192 != hand192)
            {
                throw new Exception("200 percent arrow and hand cursor sizes differ");
            }

            cursors.Update(192);
            if (cursors.Arrow.Handle != arrow192Handle || cursors.Hand.Handle != hand192Handle)
            {
                throw new Exception("same-dpi high-resolution cursor update did not reuse resources");
            }
        }
        finally
        {
            cursors.Dispose();
        }
    }

    private static void ExpectCursorSize(Size actual, Size expected, string name)
    {
        if (actual.Width <= 0 || actual.Height <= 0 || actual != expected)
        {
            throw new Exception(name + " expected " + expected.Width.ToString(CultureInfo.InvariantCulture) + "x"
                + expected.Height.ToString(CultureInfo.InvariantCulture) + " actual "
                + actual.Width.ToString(CultureInfo.InvariantCulture) + "x"
                + actual.Height.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AssertDetailAnchor(Rectangle workingArea, DetailLayout layout, Rectangle icon, string name)
    {
        Point location = PopupAnchor.Above(
            icon,
            layout.WindowSize,
            layout.Shadow,
            layout.Shadow,
            layout.PanelBounds.Size,
            layout.Gap,
            workingArea);
        Rectangle window = new Rectangle(location, layout.WindowSize);
        if (!workingArea.Contains(window))
        {
            throw new Exception(name + " escaped working area: " + FormatRectangle(window));
        }
    }

    private static void ExpectFloor(int actual, int logical, float scale, string name)
    {
        double boundary = logical * (double)scale;
        if (actual > boundary || boundary - actual >= 1.00001)
        {
            throw new Exception(name + " boundary " + boundary.ToString("0.###", CultureInfo.InvariantCulture)
                + " actual " + actual.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void ExpectRectangle(Rectangle actual, Rectangle expected, string name)
    {
        if (actual != expected)
        {
            throw new Exception(name + " expected " + FormatRectangle(expected) + " actual " + FormatRectangle(actual));
        }
    }

    private static void TestLocator()
    {
        Point mouse = new Point(1520, 920);
        Rectangle slot = new Rectangle(1504, 1032, 32, 48);
        Rectangle element = new Rectangle(1488, 890, 44, 44);
        bool anchored;
        Rectangle resolved = IconLocator.Resolve(mouse, slot, true, element, out anchored);
        if (!anchored || resolved != element)
        {
            throw new Exception("overflow icon was not used");
        }
        Point onSlot = new Point(1520, 1050);
        resolved = IconLocator.Resolve(onSlot, slot, true, element, out anchored);
        if (!anchored || resolved != slot)
        {
            throw new Exception("taskbar slot was not used");
        }
        resolved = IconLocator.Resolve(mouse, slot, false, Rectangle.Empty, out anchored);
        if (anchored)
        {
            throw new Exception("fallback was marked anchored");
        }
        Expect(resolved.X, 1504, "fallback x");
        Expect(resolved.Y, 896, "fallback y");
    }

    private static void TestHover()
    {
        Point over = new Point(1520, 1050);
        Rectangle slot = new Rectangle(1504, 1032, 32, 48);
        Rectangle tip = new Rectangle(1470, 980, 100, 44);
        if (!TrayHover.StillOver(over, over, slot, tip, false))
        {
            throw new Exception("hover on slot");
        }
        Point away = new Point(900, 500);
        if (TrayHover.StillOver(away, over, slot, tip, false))
        {
            throw new Exception("hover followed pointer");
        }
        if (!TrayHover.StillOver(over, over, Rectangle.Empty, tip, false))
        {
            throw new Exception("hover without slot");
        }
        if (TrayHover.StillOver(new Point(over.X + 40, over.Y), over, Rectangle.Empty, tip, false))
        {
            throw new Exception("left icon without slot");
        }
        Rectangle locked = TrayHover.Lock(over, slot, false, Rectangle.Empty);
        Point moved = new Point(over.X + 8, over.Y);
        if (TrayHover.Lock(moved, slot, false, Rectangle.Empty) != locked)
        {
            throw new Exception("tip anchor followed the pointer");
        }
        if (locked != slot)
        {
            throw new Exception("tip anchor left the tray icon");
        }
        Rectangle plateTip = new Rectangle(1400, 980, 120, 48);
        if (!ShellTipGuard.ShouldHide(new Rectangle(1448, 992, 24, 32), plateTip))
        {
            throw new Exception("shell plate inside tip");
        }
        if (ShellTipGuard.ShouldHide(new Rectangle(0, 1040, 1920, 40), plateTip))
        {
            throw new Exception("taskbar was treated as the shell plate");
        }
        if (ShellTipGuard.ShouldHide(new Rectangle(10, 10, 24, 32), plateTip))
        {
            throw new Exception("distant window was treated as the shell plate");
        }
    }

    private static void TestSqlite()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cursor",
            "User",
            "globalStorage",
            "state.vscdb");
        if (!File.Exists(path))
        {
            Console.WriteLine("sqlite=skipped");
            return;
        }
        string plan;
        string error;
        if (!SqliteKv.TryRead(path, "cursorAuth/stripeMembershipType", out plan, out error))
        {
            throw new Exception("sqlite " + error);
        }
        string token;
        if (!SqliteKv.TryRead(path, "cursorAuth/accessToken", out token, out error))
        {
            throw new Exception("sqlite token " + error);
        }
        Console.WriteLine("sqlite=ok plan=" + (plan ?? "") + " token=" + (string.IsNullOrEmpty(token) ? "no" : "yes"));
    }

    private static QuotaSnapshot CreatePreviewSnapshot()
    {
        QuotaWindow primary = new QuotaWindow();
        primary.Badge = "内置";
        primary.Title = "内置模型剩余";
        primary.Remaining = 74;
        primary.Used = 26;
        primary.WindowMinutes = 30 * 24 * 60;
        primary.ResetAt = (DateTime.UtcNow.AddDays(12) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        QuotaWindow secondary = new QuotaWindow();
        secondary.Badge = "其他";
        secondary.Title = "其他模型剩余";
        secondary.Remaining = 100;
        secondary.Used = 0;
        secondary.WindowMinutes = primary.WindowMinutes;
        secondary.ResetAt = primary.ResetAt;
        QuotaSnapshot snapshot = new QuotaSnapshot();
        snapshot.PlanType = "pro";
        snapshot.Primary = primary;
        snapshot.Secondary = secondary;
        snapshot.SampledAt = DateTime.Now;
        snapshot.SourceName = "cursor-api";
        return snapshot;
    }

    private static void SavePreview(string path, DetailLayout layout)
    {
        Rectangle refresh;
        Rectangle close;
        using (Bitmap bitmap = DetailPainter.Render(CreatePreviewSnapshot(), "实时预览", layout, false, false, out refresh, out close))
        {
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static string FormatRectangle(Rectangle rect)
    {
        return rect.X.ToString(CultureInfo.InvariantCulture) + "," + rect.Y.ToString(CultureInfo.InvariantCulture)
            + " " + rect.Width.ToString(CultureInfo.InvariantCulture) + "x" + rect.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static void Expect(int actual, int expected, string name)
    {
        if (actual != expected)
        {
            throw new Exception(name + " expected " + expected.ToString(CultureInfo.InvariantCulture) + " actual " + actual.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string JsonNumber(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value))
        {
            return "null";
        }
        double rounded = Math.Round(value.Value, 1);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.05)
        {
            return Math.Round(rounded).ToString(CultureInfo.InvariantCulture);
        }
        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        if (value == null)
        {
            return "";
        }
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool Has(string[] args, string expected)
    {
        if (args == null)
        {
            return false;
        }
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static void BindConsole()
    {
        IntPtr stdout = NativeMethods.GetStdHandle(-11);
        if (stdout == IntPtr.Zero || stdout == new IntPtr(-1))
        {
            if (!NativeMethods.AttachConsole(-1))
            {
                NativeMethods.AllocConsole();
            }
            stdout = NativeMethods.GetStdHandle(-11);
        }
        StreamWriter writer = new StreamWriter(new FileStream(stdout, FileAccess.Write), new UTF8Encoding(false));
        writer.AutoFlush = true;
        Console.SetOut(writer);
        Console.SetError(writer);
    }
}
