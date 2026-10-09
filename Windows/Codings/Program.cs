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
        if (Math.Abs(snapshot.Primary.Remaining.Value - 75) > 0.1)
        {
            throw new Exception("allowance remain " + snapshot.Primary.Remaining.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (snapshot.Primary.Badge != "额度")
        {
            throw new Exception("allowance badge " + snapshot.Primary.Badge);
        }
        if (snapshot.Primary.Detail != "$5 / $20")
        {
            throw new Exception("allowance detail " + snapshot.Primary.Detail);
        }
        if (Math.Abs(snapshot.Secondary.Remaining.Value - 100) > 0.1)
        {
            throw new Exception("other remain");
        }
        TestLiveAllowance();
        TestSmallPoolPercent();
        if (QuotaFormatter.PlanTitle(snapshot.PlanType) != "Pro 额度")
        {
            throw new Exception("plan title " + QuotaFormatter.PlanTitle(snapshot.PlanType));
        }
        if (QuotaFormatter.Percent(10) != "10%")
        {
            throw new Exception("percent format");
        }
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
        if (Math.Abs(snapshot.Primary.Remaining.Value - 74.6) > 0.05)
        {
            throw new Exception("live allowance remain " + snapshot.Primary.Remaining.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (snapshot.Primary.Detail != "$5.08 / $20")
        {
            throw new Exception("live allowance detail " + snapshot.Primary.Detail);
        }
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
