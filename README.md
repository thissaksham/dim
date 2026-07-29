# dim

A screen dimmer for Windows that goes darker than your monitor's own 0%.

One PowerShell file. No install, no dependencies, no build step.

![the slider flyout](docs/flyout.png)

## Why

Monitor brightness bottoms out well above "comfortable" in a dark room, and on
external displays the Windows brightness slider often does nothing at all. `dim`
draws a click-through black overlay on every monitor instead, so it keeps
darkening past whatever floor your hardware has.

## Run

```powershell
powershell -ExecutionPolicy Bypass -WindowStyle Hidden -File .\dim.ps1
```

A half-moon icon appears in the notification area. Left-click it for the slider,
right-click for Exit. Exiting always restores full brightness.

| Flag | Effect |
|------|--------|
| `-Percent 60` | start at 60% instead of 0 |
| `-Test` | run the self-check and quit |

Slider supports drag, scroll wheel, arrow keys (±5), and Home/End.

### Start it with Windows

Drop a shortcut to the command above into the folder that opens from
`Win+R` → `shell:startup`.

## What it does not dim

The overlay is a normal topmost window, which puts a few things out of reach:

| Not dimmed | Why |
|---|---|
| Mouse cursor | Drawn on the hardware cursor plane, above all composition |
| Start menu, Search, Action Center | Live in a higher window band that needs a UIAccess manifest |
| UAC prompt, lock screen, Ctrl+Alt+Del | Separate secure desktop, by design |
| Fullscreen-exclusive games | Bypass the compositor entirely |

Dropdowns and context menus *are* covered — they open above any pre-existing
topmost window, so the app re-claims the top every 700ms. Expect a brief flash
before it catches up.

Adjusting the display **gamma ramp** instead would dim all of the above, since
gamma applies after composition. That was the first implementation and it was
dropped: `SetDeviceGammaRamp` returns false on plenty of machines, because
Windows clamps ICM gamma unless `GdiIcmGammaRange` (DWORD) is set to `256`
under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM` followed by a
reboot — and on HDR displays gamma is unavailable no matter what. An overlay
needs no registry edit and no admin rights.

## Notes

- Capped at 90%. A 100% overlay is an unrecoverable black screen — you could not
  find the slider to undo it.
- Overlays are built once at startup. Plug in a monitor later and it stays
  undimmed until you restart the app.
- Theme and accent colour are read from the registry at startup, so switching
  Windows between light and dark needs a restart.

## Self-check

```powershell
powershell -ExecutionPolicy Bypass -File .\dim.ps1 -Test
```

Covers the pointer→value math, clamping at both ends, overlay opacity, and
renders the flyout to `%TEMP%\dim-flyout.png` so a silently-thrown paint
handler shows up as a blank image.
