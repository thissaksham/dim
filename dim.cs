// dim - a screen dimmer for Windows that goes darker than the panel's own 0%.
// Build: build.cmd   (uses the C# compiler that ships with Windows; no SDK needed)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Native
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_KEEP = 0x0001 | 0x0002 | 0x0010;   // NOSIZE | NOMOVE | NOACTIVATE
}

/// <summary>Theme pulled from Windows rather than invented here.</summary>
class Theme
{
    public readonly bool Dark;
    public readonly Color Back, Rail, Text, Muted, Edge, Accent;
    public readonly Font Body, Number;
    public readonly float Scale;

    public Theme(float scale)
    {
        Scale = scale;
        Dark = ReadInt(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                       "AppsUseLightTheme", 1) == 0;

        if (Dark)
        {
            Back = Color.FromArgb(32, 32, 32);
            Rail = Color.FromArgb(72, 72, 72);
            Text = Color.FromArgb(255, 255, 255);
            Muted = Color.FromArgb(160, 160, 160);
            Edge = Color.FromArgb(64, 64, 64);
        }
        else
        {
            Back = Color.FromArgb(249, 249, 249);
            Rail = Color.FromArgb(205, 205, 205);
            Text = Color.FromArgb(26, 26, 26);
            Muted = Color.FromArgb(96, 96, 96);
            Edge = Color.FromArgb(224, 224, 224);
        }

        Accent = Color.FromArgb(0, 120, 212);
        object raw = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM", "AccentColor", null);
        if (raw != null)
        {
            uint a = unchecked((uint)Convert.ToInt32(raw));           // stored ABGR
            Accent = Color.FromArgb(255, (int)(a & 0xFF), (int)((a >> 8) & 0xFF), (int)((a >> 16) & 0xFF));
        }
        // Windows keeps a lighter accent shade for dark mode. Blend toward white to approximate it --
        // scaling the channels instead would shift the hue (the stock blue comes out cyan).
        if (Dark && Accent.GetBrightness() < 0.55f)
        {
            const float t = 0.35f;
            Accent = Color.FromArgb(255,
                (int)(Accent.R + (255 - Accent.R) * t),
                (int)(Accent.G + (255 - Accent.G) * t),
                (int)(Accent.B + (255 - Accent.B) * t));
        }

        Body = Pick(9f * scale, "Segoe UI Variable Text", "Segoe UI");
        Number = Pick(15f * scale, "Segoe UI Variable Display", "Segoe UI");
    }

    public int Px(int at96) { return (int)Math.Round(at96 * Scale); }

    static int ReadInt(string key, string name, int fallback)
    {
        object v = Registry.GetValue(key, name, null);
        return v == null ? fallback : Convert.ToInt32(v);
    }

    // A missing family substitutes silently, so compare the resolved name instead of trusting the request.
    static Font Pick(float size, params string[] families)
    {
        foreach (string f in families)
        {
            Font font = new Font(f, size);
            if (font.Name == f) return font;
            font.Dispose();
        }
        return new Font(SystemFonts.MessageBoxFont.FontFamily, size);
    }
}

/// <summary>
/// Hand-drawn slider. The stock TrackBar is a Win32 common control that ignores ForeColor/BackColor
/// entirely, so matching Windows 11 means drawing the three shapes ourselves.
/// </summary>
class DimSlider : UserControl
{
    public int Max = 90;
    public int Value { get; private set; }
    public event EventHandler ValueChanged;

    readonly Theme t;
    readonly int pad;
    bool dragging;

    public DimSlider(Theme theme)
    {
        t = theme;
        pad = t.Px(10);                       // keeps the thumb fully inside the control at both ends
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = t.Back;
    }

    int RailW { get { return Width - 2 * pad; } }
    public int ThumbX { get { return pad + (int)(RailW * (Value / (double)Max)); } }

    public void SetValue(int v)
    {
        v = Math.Max(0, Math.Min(Max, v));
        if (v == Value) return;
        Value = v;
        Invalidate();
        if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
    }

    /// <summary>Single pointer-to-value path, shared by the drag handlers and the self-check.</summary>
    public void SetFromX(int x)
    {
        SetValue((int)Math.Round((x - pad) / (double)RailW * Max));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int cy = Height / 2, tx = ThumbX, h = t.Px(4), r = t.Px(10), ri = t.Px(7), rf = t.Px(12);

        using (var b = new SolidBrush(t.Rail)) g.FillRectangle(b, pad, cy - h / 2, RailW, h);
        using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, pad, cy - h / 2, tx - pad, h);
        using (var b = new SolidBrush(t.Back)) g.FillEllipse(b, tx - r, cy - r, r * 2, r * 2);
        using (var b = new SolidBrush(t.Accent)) g.FillEllipse(b, tx - ri, cy - ri, ri * 2, ri * 2);
        if (Focused)
            using (var p = new Pen(t.Text, 2f * t.Scale))
                g.DrawEllipse(p, tx - rf, cy - rf, rf * 2, rf * 2);   // visible focus ring
    }

    protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Focus(); SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
    protected override void OnMouseWheel(MouseEventArgs e) { SetValue(Value + Math.Sign(e.Delta) * 5); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); }

    // Without this the parent form swallows the arrow keys as focus navigation.
    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Left || k == Keys.Right || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SetValue(Value - 5);
        else if (e.KeyCode == Keys.Right) SetValue(Value + 5);
        else if (e.KeyCode == Keys.Home) SetValue(0);
        else if (e.KeyCode == Keys.End) SetValue(Max);
        else return;
        e.Handled = true;
    }
}

/// <summary>Full-screen black sheet. Click-through and unfocusable, so it is invisible to input.</summary>
class Overlay : Form
{
    public Overlay(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        TopMost = true;
        ShowInTaskbar = false;
        Opacity = 0;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // TRANSPARENT (clicks pass through) | LAYERED | TOOLWINDOW (no alt-tab) | NOACTIVATE
            cp.ExStyle |= 0x20 | 0x80000 | 0x80 | 0x8000000;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }
}

class DimApp : ApplicationContext
{
    // ponytail: 90% cap. A 100% overlay is an unrecoverable black screen -- you could not find the
    // slider to undo it.
    public const int MaxPct = 90;

    readonly Theme theme;
    readonly Overlay[] overlays;
    readonly NotifyIcon tray;
    readonly Form flyout;
    readonly Label valueLabel;
    public readonly DimSlider Slider;
    readonly Timer keepTop;
    readonly bool testMode;

    public int Level { get { return Slider.Value; } }

    public DimApp(int startPct, bool test)
    {
        testMode = test;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            theme = new Theme(g.DpiX / 96f);

        overlays = new Overlay[Screen.AllScreens.Length];
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            overlays[i] = new Overlay(Screen.AllScreens[i].Bounds);
            overlays[i].Show();
        }

        flyout = new Form();
        flyout.FormBorderStyle = FormBorderStyle.None;
        flyout.ShowInTaskbar = false;
        flyout.TopMost = true;
        flyout.StartPosition = FormStartPosition.Manual;
        flyout.ClientSize = new Size(theme.Px(268), theme.Px(96));
        flyout.BackColor = theme.Back;
        flyout.KeyPreview = true;

        Label title = new Label();
        title.SetBounds(theme.Px(18), theme.Px(20), theme.Px(120), theme.Px(18));
        title.Text = "Screen dim";
        title.Font = theme.Body;
        title.ForeColor = theme.Muted;
        title.BackColor = Color.Transparent;
        flyout.Controls.Add(title);

        valueLabel = new Label();
        valueLabel.SetBounds(theme.Px(140), theme.Px(10), theme.Px(110), theme.Px(34));
        valueLabel.Font = theme.Number;
        valueLabel.ForeColor = theme.Text;
        valueLabel.BackColor = Color.Transparent;
        valueLabel.TextAlign = ContentAlignment.MiddleRight;
        valueLabel.Text = "0%";
        flyout.Controls.Add(valueLabel);

        Slider = new DimSlider(theme);
        Slider.Max = MaxPct;
        Slider.SetBounds(theme.Px(18), theme.Px(48), theme.Px(232), theme.Px(36));
        Slider.ValueChanged += delegate { Apply(); };
        flyout.Controls.Add(Slider);

        flyout.Paint += delegate(object s, PaintEventArgs e)
        {
            using (var p = new Pen(theme.Edge))
                e.Graphics.DrawRectangle(p, 0, 0, flyout.Width - 1, flyout.Height - 1);
        };
        flyout.Deactivate += delegate { flyout.Hide(); };            // click away to dismiss
        flyout.FormClosing += delegate(object s, FormClosingEventArgs e)
        {
            e.Cancel = true;                                          // Exit lives in the tray menu
            flyout.Hide();
        };
        flyout.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) flyout.Hide();
        };

        tray = new NotifyIcon();
        tray.Icon = BuildIcon();
        tray.Text = "Dim: 0%";
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Font = theme.Body;
        menu.Items.Add("Exit").Click += delegate
        {
            Slider.SetValue(0);                                       // never leave the screen dark
            Apply();
            tray.Visible = false;
            ExitThread();
        };
        tray.ContextMenuStrip = menu;
        tray.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowFlyout();
        };
        tray.Visible = !testMode;

        // Among topmost windows the last one shown wins, and menus/dropdowns are created fresh each
        // time they open -- so they land above an overlay shown once at startup. Re-claim the top.
        // ponytail: a poll, not a hook. Doing this properly needs a CBT hook in a real DLL; a 700ms
        // tick costs nothing and only does work while actually dimming.
        keepTop = new Timer();
        keepTop.Interval = 700;
        keepTop.Tick += delegate
        {
            if (Level <= 0) return;
            foreach (Overlay o in overlays)
                Native.SetWindowPos(o.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_KEEP);
            if (flyout.Visible)                                       // ...but never dim our own slider
                Native.SetWindowPos(flyout.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_KEEP);
        };
        if (!testMode) keepTop.Start();

        Slider.SetValue(startPct);
        Apply();
    }

    void Apply()
    {
        int pct = Slider.Value;
        foreach (Overlay o in overlays) o.Opacity = pct / 100.0;
        tray.Text = "Dim: " + pct + "%";
        valueLabel.Text = pct + "%";
    }

    void ShowFlyout()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        flyout.Location = new Point(wa.Right - flyout.Width - theme.Px(12),
                                    wa.Bottom - flyout.Height - theme.Px(12));
        flyout.Show();
        flyout.Activate();
        Slider.Focus();
        int round = 2;                                                // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(flyout.Handle, 33, ref round, 4); // no-op before Windows 11
    }

    static Icon BuildIcon()
    {
        using (Bitmap bmp = new Bitmap(16, 16))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(Color.White, 1.5f)) g.DrawEllipse(p, 2, 2, 11, 11);
                g.FillPie(Brushes.White, 2, 2, 11, 11, 90, 180);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // --- self-check hooks ---
    public double OverlayOpacity { get { return overlays[0].Opacity; } }
    public string TrayText { get { return tray.Text; } }
    public string ValueText { get { return valueLabel.Text; } }

    /// <summary>
    /// Renders the flyout to a file. It must be shown for the child controls to get window handles,
    /// so this parks it far offscreen. A silently-thrown paint handler shows up as a blank image.
    /// </summary>
    public void RenderFlyout(string path)
    {
        flyout.Location = new Point(-3000, -3000);
        flyout.Show();
        flyout.Refresh();
        using (Bitmap shot = new Bitmap(flyout.Width, flyout.Height))
        {
            flyout.DrawToBitmap(shot, new Rectangle(0, 0, flyout.Width, flyout.Height));
            shot.Save(path, ImageFormat.Png);
        }
        flyout.Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            keepTop.Dispose();
            tray.Dispose();
            flyout.Dispose();
            foreach (Overlay o in overlays) o.Dispose();
        }
        base.Dispose(disposing);
    }
}

static class SelfTest
{
    static void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }

    public static int Run()
    {
        try
        {
            using (DimApp app = new DimApp(0, true))
            {
                DimSlider s = app.Slider;

                s.SetValue(40);
                Check(Math.Abs(app.OverlayOpacity - 0.40) < 0.001, "overlay opacity " + app.OverlayOpacity);
                Check(app.TrayText == "Dim: 40%", "tray text " + app.TrayText);
                Check(app.ValueText == "40%", "value label " + app.ValueText);
                Check(s.ThumbX > 0, "thumb did not move");

                s.SetFromX(s.Width);                       // dragged to the far right
                Check(s.Value == DimApp.MaxPct, "drag to end gave " + s.Value);
                s.SetFromX(0);                             // and back to the far left
                Check(s.Value == 0, "drag to start gave " + s.Value);
                s.SetFromX(-500);                          // pointer dragged off the control must clamp
                Check(s.Value == 0, "not clamped low: " + s.Value);
                s.SetValue(999);
                Check(s.Value == DimApp.MaxPct, "not clamped high: " + s.Value);

                s.SetValue(45);
                string shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dim-flyout.png");
                app.RenderFlyout(shot);

                s.SetValue(0);
                Check(app.OverlayOpacity == 0, "reset failed: " + app.OverlayOpacity);

                Console.WriteLine("ok  (theme: " + (new Theme(1f).Dark ? "dark" : "light") +
                                  ", render: " + shot + ")");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Native.SetProcessDPIAware();       // otherwise Windows bitmap-stretches the UI on scaled displays
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        int start = 0;
        bool test = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--test") test = true;
            else if (args[i] == "--percent" && i + 1 < args.Length) int.TryParse(args[++i], out start);
        }

        if (test) return SelfTest.Run();
        Application.Run(new DimApp(start, false));
        return 0;
    }
}
