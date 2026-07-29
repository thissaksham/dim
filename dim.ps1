<#
.SYNOPSIS
Tray screen dimmer. A click-through black overlay on every monitor, driven by a slider.
Goes darker than your monitor's own 0% because it sits on top of everything.

.EXAMPLE
.\dim.ps1              # tray icon; left-click for the slider
.\dim.ps1 -Percent 60  # start already at 60%
.\dim.ps1 -Test        # self-check
#>
param(
    [ValidateRange(0, 90)][int]$Percent = 0,
    [switch]$Test
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Native {
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int val);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@

if (-not $Test) { [void][Native]::ShowWindow([Native]::GetConsoleWindow(), 0) }  # hide our own console

# ponytail: 90% cap. A 100% overlay is an unrecoverable black screen -- you could not find the slider to undo it.
$maxPct = 90

# ---- theme: follow Windows, don't invent one ----
$dark = $true
try { $dark = 0 -eq (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction Stop).AppsUseLightTheme } catch {}

$accent = [Drawing.Color]::FromArgb(0, 120, 212)      # Windows default blue if the key is missing
try {
    $a = [uint32](Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\DWM' -Name AccentColor -ErrorAction Stop).AccentColor
    $accent = [Drawing.Color]::FromArgb(255, [int]($a -band 0xFF), [int](($a -shr 8) -band 0xFF), [int](($a -shr 16) -band 0xFF))  # stored ABGR
} catch {}

if ($dark) {
    $cBack = [Drawing.Color]::FromArgb(32, 32, 32)
    $cRail = [Drawing.Color]::FromArgb(72, 72, 72)
    $cText = [Drawing.Color]::FromArgb(255, 255, 255)
    $cDim  = [Drawing.Color]::FromArgb(160, 160, 160)
    $cEdge = [Drawing.Color]::FromArgb(64, 64, 64)
} else {
    $cBack = [Drawing.Color]::FromArgb(249, 249, 249)
    $cRail = [Drawing.Color]::FromArgb(205, 205, 205)
    $cText = [Drawing.Color]::FromArgb(26, 26, 26)
    $cDim  = [Drawing.Color]::FromArgb(96, 96, 96)
    $cEdge = [Drawing.Color]::FromArgb(224, 224, 224)
}
# Windows keeps a lighter accent shade for dark mode; approximate it by blending toward white.
# ponytail: blend, don't scale the channels -- scaling shifts hue (the stock blue came out cyan).
if ($dark -and ($accent.GetBrightness() -lt 0.55)) {
    $t = 0.35
    $accent = [Drawing.Color]::FromArgb(255,
        [int]($accent.R + (255 - $accent.R) * $t),
        [int]($accent.G + (255 - $accent.G) * $t),
        [int]($accent.B + (255 - $accent.B) * $t))
}

function New-Font([string]$family, [single]$size, [string]$style = 'Regular') {
    foreach ($f in $family, 'Segoe UI Variable Text', 'Segoe UI') {
        $font = New-Object Drawing.Font $f, $size, ([Drawing.FontStyle]$style)
        if ($font.Name -eq $f) { return $font }      # Name falls back silently when the family is absent
    }
    return $font
}
$fontBody   = New-Font 'Segoe UI Variable Text' 9
$fontNumber = New-Font 'Segoe UI Variable Display' 15 'Regular'

# ---- overlays: one per monitor, so multi-head dims together ----
$overlays = foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
    $f = New-Object Windows.Forms.Form
    $f.FormBorderStyle = 'None'
    $f.StartPosition   = 'Manual'
    $f.Bounds          = $screen.Bounds
    $f.BackColor       = 'Black'
    $f.TopMost         = $true
    $f.ShowInTaskbar   = $false
    $f.Opacity         = 0
    $f.Show()
    # WS_EX_TRANSPARENT (clicks pass through) | LAYERED | TOOLWINDOW (no alt-tab) | NOACTIVATE (never steals focus)
    $ex = [Native]::GetWindowLong($f.Handle, -20)
    [void][Native]::SetWindowLong($f.Handle, -20, $ex -bor 0x20 -bor 0x80000 -bor 0x80 -bor 0x8000000)
    $f
}

$notify = New-Object Windows.Forms.NotifyIcon
$current = 0

function Set-Level([int]$pct) {
    $pct = [Math]::Max(0, [Math]::Min($maxPct, $pct))
    foreach ($f in $overlays) { $f.Opacity = $pct / 100 }
    $script:current = $pct
    $notify.Text = "Dim: $pct%"
    $value.Text  = "$pct%"
    $slider.Invalidate()
}

# ---- flyout ----
$panel = New-Object Windows.Forms.Form
$panel.FormBorderStyle = 'None'
$panel.ShowInTaskbar   = $false
$panel.TopMost         = $true
$panel.StartPosition   = 'Manual'
$panel.ClientSize      = New-Object Drawing.Size 268, 96
$panel.BackColor       = $cBack
$panel.KeyPreview      = $true

$title = New-Object Windows.Forms.Label
$title.SetBounds(18, 20, 120, 18)
$title.Text      = 'Screen dim'
$title.Font      = $fontBody
$title.ForeColor = $cDim
$title.BackColor = [Drawing.Color]::Transparent
$panel.Controls.Add($title)

$value = New-Object Windows.Forms.Label
$value.SetBounds(140, 10, 110, 34)
$value.Font      = $fontNumber
$value.ForeColor = $cText
$value.BackColor = [Drawing.Color]::Transparent
$value.TextAlign = 'MiddleRight'
$value.Text      = '0%'
$panel.Controls.Add($value)

# ponytail: hand-drawn slider. The stock TrackBar cannot be themed at all -- it is a Win32 common
# control that ignores ForeColor/BackColor, so matching Windows 11 means drawing the three shapes ourselves.
$slider = New-Object Windows.Forms.UserControl
$slider.SetBounds(18, 48, 232, 36)
$slider.BackColor = $cBack
$slider.TabStop   = $true
$slider.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($slider, $true)

$pad   = 10                                # keeps the thumb fully inside the control at 0% and max
$railW = $slider.Width - (2 * $pad)

function Get-ThumbX { $pad + [int]($railW * ($current / $maxPct)) }

# single source of truth for pointer -> value, so the drag handlers and the self-check share one path
function Set-FromX([int]$x) {
    Set-Level ([int][Math]::Round((($x - $pad) / $railW) * $maxPct))
}

$slider.Add_Paint({
    $g = $_.Graphics
    $g.SmoothingMode = 'AntiAlias'
    $cy = [int]($slider.Height / 2)
    $tx = Get-ThumbX
    $g.FillRectangle((New-Object Drawing.SolidBrush $cRail), $pad, $cy - 2, $railW, 4)
    $g.FillRectangle((New-Object Drawing.SolidBrush $accent), $pad, $cy - 2, $tx - $pad, 4)
    $g.FillEllipse((New-Object Drawing.SolidBrush $cBack), $tx - 10, $cy - 10, 20, 20)
    $g.FillEllipse((New-Object Drawing.SolidBrush $accent), $tx - 7, $cy - 7, 14, 14)
    if ($slider.Focused) {
        $g.DrawEllipse((New-Object Drawing.Pen $cText, 2), $tx - 12, $cy - 12, 24, 24)   # visible focus ring
    }
})

$dragging = $false
$slider.Add_MouseDown({ $script:dragging = $true; $slider.Focus(); Set-FromX $_.X })
$slider.Add_MouseMove({ if ($script:dragging) { Set-FromX $_.X } })
$slider.Add_MouseUp({ $script:dragging = $false })
$slider.Add_MouseWheel({ Set-Level ($current + [Math]::Sign($_.Delta) * 5) })
$slider.Add_GotFocus({ $slider.Invalidate() })
$slider.Add_LostFocus({ $slider.Invalidate() })
$slider.Add_PreviewKeyDown({ $_.IsInputKey = $true })     # otherwise the form eats the arrow keys
$slider.Add_KeyDown({
    switch ($_.KeyCode) {
        'Left'  { Set-Level ($current - 5) }
        'Right' { Set-Level ($current + 5) }
        'Home'  { Set-Level 0 }
        'End'   { Set-Level $maxPct }
    }
})
$panel.Controls.Add($slider)

$panel.Add_Paint({ $_.Graphics.DrawRectangle((New-Object Drawing.Pen $cEdge), 0, 0, $panel.Width - 1, $panel.Height - 1) })
$panel.Add_Deactivate({ $panel.Hide() })                       # click away to dismiss, like a real flyout
$panel.Add_FormClosing({ $_.Cancel = $true; $panel.Hide() })   # Exit lives in the tray menu
$panel.Add_KeyDown({ if ($_.KeyCode -eq 'Escape') { $panel.Hide() } })

function Show-Panel {
    $wa = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $panel.Location = New-Object Drawing.Point (($wa.Right - $panel.Width - 12), ($wa.Bottom - $panel.Height - 12))
    $panel.Show()
    $panel.Activate()
    $slider.Focus()
    $r = 2                                                     # DWMWCP_ROUND
    try { [void][Native]::DwmSetWindowAttribute($panel.Handle, 33, [ref]$r, 4) } catch {}   # no-op before Win11
}

# ---- tray icon ----
$bmp = New-Object Drawing.Bitmap 16, 16          # half-moon glyph, drawn instead of shipping an .ico
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.DrawEllipse((New-Object Drawing.Pen ([Drawing.Color]::White), 1.5), 2, 2, 11, 11)
$g.FillPie([Drawing.Brushes]::White, 2, 2, 11, 11, 90, 180)
$g.Dispose()

$notify.Icon    = [Drawing.Icon]::FromHandle($bmp.GetHicon())
$notify.Text    = "Dim: 0%"
$notify.Visible = -not $Test

$ctx  = New-Object Windows.Forms.ApplicationContext
$menu = New-Object Windows.Forms.ContextMenuStrip
$menu.Font = $fontBody
$notify.ContextMenuStrip = $menu

$exit = $menu.Items.Add("Exit")
$exit.Add_Click({
    Set-Level 0                                   # never leave the screen dark after we are gone
    $notify.Visible = $false
    $ctx.ExitThread()
})

$notify.Add_MouseClick({ if ($_.Button -eq 'Left') { Show-Panel } })

# Among topmost windows the last one shown wins, and menus/dropdowns are created fresh each time they
# open -- so they land above an overlay that was shown once at startup. Re-claim the top periodically.
# ponytail: a poll, not a hook. Catching this properly needs a CBT/shell hook in a real DLL; a 700ms
# tick costs nothing and only runs while actually dimming.
$topmost = New-Object Windows.Forms.Timer
$topmost.Interval = 700
$topmost.Add_Tick({
    if ($current -le 0) { return }
    $HWND_TOPMOST = [IntPtr](-1)
    $flags = 0x0001 -bor 0x0002 -bor 0x0010          # NOSIZE | NOMOVE | NOACTIVATE
    foreach ($f in $overlays) { [void][Native]::SetWindowPos($f.Handle, $HWND_TOPMOST, 0, 0, 0, 0, $flags) }
    # ...but never end up dimming our own slider
    if ($panel.Visible) { [void][Native]::SetWindowPos($panel.Handle, $HWND_TOPMOST, 0, 0, 0, 0, $flags) }
})
$topmost.Start()

Set-Level $Percent

if ($Test) {
    Set-Level 40
    if ([Math]::Abs($overlays[0].Opacity - 0.4) -gt 0.001) { throw "overlay opacity wrong: $($overlays[0].Opacity)" }
    if ($notify.Text -ne "Dim: 40%") { throw "tooltip wrong: $($notify.Text)" }
    if ((Get-ThumbX) -le $pad)       { throw "thumb did not move: $(Get-ThumbX)" }
    Set-FromX ($pad + $railW)                     # dragging to the far right
    if ($current -ne $maxPct)        { throw "drag to end gave $current" }
    Set-FromX $pad                                # and back to the far left
    if ($current -ne 0)              { throw "drag to start gave $current" }
    Set-FromX ($pad - 50)                         # pointer dragged off the control must clamp
    if ($current -ne 0)              { throw "not clamped low: $current" }
    Set-Level 999
    if ($current -ne $maxPct)        { throw "not clamped high: $current" }
    if ((Get-ThumbX) -ne ($pad + $railW)) { throw "thumb past rail: $(Get-ThumbX)" }
    Set-Level 45
    # render the flyout to a file -- a silently-thrown Paint handler shows up as a blank image.
    # Must be shown for the child controls to get window handles, so show it far offscreen.
    $panel.Location = New-Object Drawing.Point -3000, -3000
    $panel.Show(); $panel.Refresh()
    $shot = New-Object Drawing.Bitmap $panel.Width, $panel.Height
    $panel.DrawToBitmap($shot, (New-Object Drawing.Rectangle 0, 0, $panel.Width, $panel.Height))
    $shot.Save("$env:TEMP\dim-flyout.png", [Drawing.Imaging.ImageFormat]::Png)
    $shot.Dispose(); $panel.Hide()
    Set-Level 0
    if ($overlays[0].Opacity -ne 0)  { throw "reset failed: $($overlays[0].Opacity)" }
    $notify.Dispose(); $panel.Dispose(); foreach ($f in $overlays) { $f.Dispose() }
    "ok  (theme: $(if ($dark) { 'dark' } else { 'light' }), accent: $($accent.R),$($accent.G),$($accent.B))"
    return
}

# ponytail: overlays are built once. Plugging in a monitor later leaves it undimmed -- restart the app.
# Fullscreen-exclusive games and some video players draw past any overlay; nothing here can fix that.
[Windows.Forms.Application]::Run($ctx)
