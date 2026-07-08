# SR.AnalogGain 🎚️

A simple **gain plugin** built with [NPlug](https://github.com/xoofx/NPlug) demonstrating:

- Basic **audio processing** (linear gain with dB mapping).
- A **custom Win32 UI** featuring analog-style knobs.
- Integration with the **VST3** plugin model (Processor + Controller separation).

---

## ✨ Features

- **Gain Control**: Adjustable from deep attenuation to boost.
- **Output Level**: Independent knob to control final volume.
- **Switches** (analog-style toggles):
  - **SAT**: Engages harmonic enhancement (earlier saturation knee, higher wet blend, gentle asymmetry/bias) with click-free switching and a subtle **+3 dB** makeup.
  - **PAD**: Fixed –20 dB attenuation before the gain stage.
  - **PHASE**: Polarity inversion with click-free ramping.
  - **HPF**: High-pass filter around 80 Hz, 18 dB/oct slope.
- **Custom UI**:
  - Dual analog knobs (`GAIN` and `OUTPUT`).
  - Tick marks, dB labels, and animated pointer sprites.
  - Embedded bitmaps for realistic metal-style knobs.
- **Host Integration**:
  - Compatible with VST3 hosts (tested with Windows).
  - Controller + Processor separation following Steinberg’s architecture.

---

## 🎨 Screenshot

![AnalogGain UI](docs/screenshot.png)

---

## ⬇️ Download

Grab the latest release of **SR.AnalogGain** here:

[**Download v0.2.0 (Windows VST3)**](https://github.com/sonicriot/SR.AnalogGain/releases/download/v0.2.0/SR.AnalogGain.vst3)

After downloading, copy the `.vst3` file into your system’s VST3 folder:

- **Windows (64-bit):**
  `C:\Program Files\Common Files\VST3`

---

## 🚀 Getting Started

### Prerequisites

- **Windows** (UI is Win32-specific).
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- A VST3-compatible host (Cubase, Reaper, Studio One, etc.).

### Build

```sh
git clone https://github.com/sonicriot/SR.AnalogGain.git
cd SR.AnalogGain/src
dotnet publish -c Release -r win-x64
```

This will produce a **self-contained, ahead-of-time compiled** `.vst3` plugin in the `SR.AnalogGain/bin/Release/net10.0/win-x64/publish/` folder.

### Install

Copy the built `.vst3` file into your system’s VST3 folder:

- **Windows (64-bit):**  
  `C:\Program Files\Common Files\VST3`

Restart your DAW and scan for new plugins.

---

## 🎛️ Usage

1. Insert **AnalogGain** on an audio track.
2. Adjust the **GAIN** knob to boost or attenuate input signal.
3. Fine-tune with the **OUTPUT** knob to control final level.
4. Use the **switches** (SAT, PAD, PHASE, HPF) to shape the input response like on a classic preamp.
5. Observe dB readouts and knob pointer for precision control.

---

## ⚠️ Limitations

- **UI**: Currently implemented in **Win32 (GDI+)**, not cross-platform.  
  On macOS/Linux hosts, only the default generic parameter view will appear.
- **No oversampling / DSP extras**: This plugin is kept minimal for demonstration.

---

## 📜 License

MIT License © 2025  
Feel free to use, modify, and extend.

---

## 🛡️ Windows Defender / Antivirus False Positive

Some antivirus tools (including Windows Defender / SmartScreen) may flag the `.vst3` file as suspicious or even as malware. **This is a false positive.**

### Why does this happen?

SR.AnalogGain is compiled using **.NET NativeAOT** (Ahead-of-Time compilation), which produces a self-contained native binary with no external runtime dependencies. This compilation technique generates code patterns that heuristic-based antivirus engines sometimes misidentify as malicious — especially for unsigned binaries from new/unknown publishers.

The plugin **does not contain any malware, spyware, or unwanted code**. It is fully open source — you can review every line at [github.com/sonicriot/SR.AnalogGain](https://github.com/sonicriot/SR.AnalogGain).

### Verify the file integrity

Before installing, you can verify the SHA256 hash of the downloaded file matches the one published in the [GitHub Release](https://github.com/sonicriot/SR.AnalogGain/releases) notes:

```powershell
Get-FileHash .\SR.AnalogGain.vst3 -Algorithm SHA256
```

### How to allow the plugin in Windows Defender

If Windows Defender blocks or quarantines the file:

1. Open **Windows Security** → **Virus & threat protection**.
2. Click **Protection history** and find the blocked item.
3. Select **Allow on device**.

Or to add a folder exclusion (so the VST3 folder is never scanned):

1. Open **Windows Security** → **Virus & threat protection settings**.
2. Under **Exclusions**, click **Add or remove exclusions**.
3. Add the folder: `C:\Program Files\Common Files\VST3`

### Why is it not signed?

Code signing certificates cost hundreds of dollars per year. Since this is a **free, open-source project**, purchasing one is not viable. A formal signing solution for OSS projects is being explored (e.g. [SignPath.io](https://signpath.io/code-signing-for-open-source/)) and may be added in a future release.

---

## 🙌 Acknowledgments

- [NPlug](https://github.com/xoofx/NPlug) for the managed VST3 binding.  
- Steinberg VST3 SDK for the plugin architecture.  
- Inspiration from analog gear knob aesthetics.
