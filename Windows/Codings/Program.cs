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
        json.Append("  \"PlanType\": ").Append(snapshot.PlanType == null ? "null" : "\"" + Escape(snapshot.PlanType) + "\"").Append(",\n");
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
            TestLocator();
            TestHover();
            TestSqlite();
            if (Has(args, "--preview"))
            {
                string path = Path.Combine(Path.GetTempPath(), "cursor-quota-preview.png");
                SavePreview(path);
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
            throw new Exception("builtin remain " + snapshot.Primary.Remaining.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (Math.Abs(snapshot.Secondary.Remaining.Value - 100) > 0.1)
        {
            throw new Exception("other remain");
        }
        if (QuotaFormatter.PlanTitle(snapshot.PlanType) != "Pro 额度")
        {
            throw new Exception("plan title " + QuotaFormatter.PlanTitle(snapshot.PlanType));
        }
        if (QuotaFormatter.Percent(10) != "10%")
        {
            throw new Exception("percent format");
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

    private static void SavePreview(string path)
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
        Rectangle refresh;
        Rectangle close;
        using (Bitmap bitmap = DetailPainter.Render(snapshot, "实时 10:40", out refresh, out close))
        {
            bitmap.Save(path, ImageFormat.Png);
        }
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
