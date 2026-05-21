# ForzaShock — OpenShock Desktop module

A module for [OpenShock Desktop](https://github.com/OpenShock/Desktop) that
reads Forza Horizon 6 "Data Out" UDP telemetry, detects collisions, and fires
your configured shockers through the host.

The host owns the OpenShock connection, authentication, and shocker list — the
module just decides *when* and *how hard*, and calls `IOpenShockControl.Control`.

## FH6 setup

`SETTINGS > HUD AND GAMEPLAY > Data Out` → On, IP `127.0.0.1`, port `5300`
(or whatever you set in the module config).

### Microsoft Store / Xbox app users: one-time loopback exemption

The Store/Xbox build of FH6 is sandboxed and cannot send UDP to `127.0.0.1`
without an explicit loopback exemption. Without it, **zero packets arrive**.
The Steam build does not need this.

Admin PowerShell, then check via `Get-AppxPackage *Forza*` for the actual PFN:

```powershell
CheckNetIsolation LoopbackExempt -a -n="<Forza package family name>"
CheckNetIsolation LoopbackExempt -s
```

## Build & install

```powershell
dotnet publish -c Release
```

That produces `bin/Release/net10.0/publish/OpenShock.Desktop.Modules.ForzaShock.module.zip`.
Install it into OpenShock Desktop via the module installer / drop into the
modules folder, restart the host.

## First run — diagnostics mode

The module ships with `Diagnostics = true`. Collisions are **detected and
logged but no shock is sent**. Start FH6, watch the ForzaShock page in the
host — packets counter should climb, hits should populate "Last:" and the
detection counter. When you're happy with the thresholds:

1. Pick which shockers should fire from the list on the ForzaShock page.
2. Tune intensity range / duration / action (default: Vibrate).
3. Uncheck **Diagnostics mode**.
4. Save, restart the listener.

## Verified FH6 packet offsets (324 B, little-endian)

| Offset | Field |
|---|---|
| 0   | IsRaceOn (S32) |
| 4   | TimestampMs (U32) |
| 20  | AccelerationX (F32) |
| 24  | AccelerationY (F32) |
| 28  | AccelerationZ (F32) |
| 32  | VelocityX (F32) |
| 36  | VelocityY (F32) |
| 40  | VelocityZ (F32) |
| 236 | SmashableVelDiff (F32) |
| 240 | SmashableMass (F32) |
| 256 | Speed m/s (F32) |

Confirmed against `haritha99ch/HorizonHaptics` + a live capture session.

## Collision detection

Two complementary signals, OR'd:

- **Acceleration spike** — frame-to-frame jump in `|accel|`. Catches solid-wall
  crashes that don't populate the smashable field.
- **SmashableVelDiff** — destructible-scenery impact (fences, signs, hedges).

Plus: min-speed gate, cooldown, and a "swallow first frame after RaceOn 0→1"
guard to suppress the startup transient.

Default `SmashableVelDiffThreshold = 1.0 m/s` (lower than the HorizonHaptics
reference's 5.0; real fence hits in FH6 peak around 1–3 m/s).

## Files

- `ForzaShockModule.cs` — module entry, registers services, starts listener
- `Config.cs` — `ForzaShockModuleConfig` + collision/shock subsections
- `ForzaPacket.cs` — packet parser + `Frame` record struct
- `CollisionDetector.cs` — stateful detector with cooldown
- `TelemetryListener.cs` — UDP loop, calls `IOpenShockControl.Control`
- `Components/ForzaShockUi.razor` — status + config page (MudBlazor)
- `Icon.svg` — module icon
- `Program.cs.bak` — old smashable-validator dump tool (kept for reference)
