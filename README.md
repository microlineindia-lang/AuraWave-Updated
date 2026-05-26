# AuraWave Enterprise

Professional **antenna radiation pattern measurement** software for **physical hardware only**: Arduino turntable (NEMA 17 stepper + driver), **vector network analyzer (SCPI)**, and RF polarization switch. No simulation — instruments must be cabled and connected before use.

Design aligns with automated far-field pattern systems: 360° azimuth or elevation sweeps, stepper-controlled rotation, real-time plots, and CSV export.

---

## Requirements

| Item | Specification |
|------|----------------|
| OS | Windows 10/11 x64 |
| IDE | Visual Studio 2022+ / .NET 10 SDK |
| Turntable | Arduino + stepper driver (firmware in `Assets/Arduino/AuraWaveTurntable/`) |
| VNA | SCPI over **TCP** (typical port 5025) or **serial** |
| RF switch | Optional serial device for E/H plane |
| USB | COM ports for Arduino and RF switch |

---

## First-time setup

### 1. Configure ports — **Settings**

| Section | Purpose |
|---------|---------|
| **Hardware Profile** | Motor step size (0.9° for NEMA 17), rotation speed, azimuth/elevation default |
| **Communication** | `COM3` turntable, VNA IP/port or serial, RF switch COM |
| **Logging** | Console verbosity, SCPI/serial mirroring |
| **User Preferences** | Operator name, chamber ID |

Click **Save & Restart AuraWave** — a countdown dialog appears (“AuraWave will restart soon”), then the app relaunches automatically.

### 2. Connect hardware — **Hardware Control**

1. **Refresh COM Ports** — verify Arduino appears (e.g. `COM3`).
2. **Connect Turntable** → **Home** (limit switch → 0° reference).
3. **Connect VNA** — address from settings (`192.168.1.100:5025` or `COM5@115200`).
4. **Connect RF Switch** (if used).
5. Or use **Connect All**.

Status chips in the main window turn **green** when linked.

### 3. Run measurement — **Measurement Setup**

- Set 0–360°, step size, center frequency (GHz).
- Choose **Azimuth** or **Elevation** scan plane.
- Enable **Auto home**, **Return to home after scan**, **Auto save CSV**.
- **Check Hardware** — must show ready before **Start Scan**.

---

## Console (VS Code style)

| Control | Location |
|---------|----------|
| **Show Console / Hide Console** | Status bar (always visible) |
| **Ctrl+`** | Toggle console |
| **Clear** | Inside console header |

The console shows **all** log lines (SCPI, serial, scan, export) — not truncated. File copy: `logs/aurawave_*.log`.

---

## Arduino firmware

Upload `Assets/Arduino/AuraWaveTurntable/AuraWaveTurntable.ino` (library: **AccelStepper**).

| Command | Action |
|---------|--------|
| `HOME` | Clockwise seek, then anticlockwise to home switch (pin 2), zero position |
| `MOVETO:deg` | Absolute angle |
| `MOVEREL:deg` | Relative move |
| `SPEED:dps` | Degrees per second |
| `ESTOP` | Emergency stop |

---

## `appsettings.json` (example)

```json
{
  "Hardware": {
    "VnaType": "TcpScpi",
    "VnaTcpHost": "192.168.1.100",
    "VnaTcpPort": 5025,
    "TurntablePort": "COM3",
    "TurntableBaud": 115200,
    "RfSwitchPort": "COM4",
    "MotorStepSizeDegrees": 0.9
  },
  "Application": {
    "OperatorName": "Lab User",
    "ChamberId": "Chamber-01",
    "DefaultScanPlane": "Azimuth"
  }
}
```

---

## Measurement flow

```
Settings (ports) → Restart → Hardware Control (connect + home)
→ Measurement Setup (validate) → Start Scan → Live plots
→ Turntable returns home → CSV in Documents\AuraWave\Exports
```

### Import Anritsu VNA CSV (offline S-parameters)

Use **Analysis → Import Anritsu VNA CSV** for ShockLine / **MS46122B** exports (LOGMAG, four traces **S11–S22**), such as:

`UEM Ayan S parameters (2).csv`

- **S-Parameters (VNA)** tab: overlay or single-trace plot vs GHz, S11 minimum, S21 @ 2.4 GHz
- **Radiation Pattern** tab: turntable **angle-sweep** CSV only (AuraWave pattern export or `Angle_deg,...` columns)

A reference sample is bundled at `Assets/Samples/UEM_Ayan_S_parameters_MS46122B.csv`.

---

## Build

```powershell
dotnet build F:\Project\AuraWave-new\AuraWave.slnx -c Release
```

Run: `AuraWave.App\bin\Release\net10.0-windows\AuraWave.App.exe`

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Scan blocked | Connect VNA + turntable; run **Home** |
| No COM ports | Drivers, USB cable, close Arduino Serial Monitor |
| Console won't reopen | Use status bar **Show Console** or **Ctrl+`** |
| VNA timeout | Ping IP, check port 5025, firewall |
| Settings tabs empty | Click category on left (Hardware Profile, Communication, …) |

---

*AuraWave — physical RF pattern measurement for the anechoic chamber and bench.*
