# NPlug.SimpleGain 🎚️

A simple **gain plugin** built with [NPlug](https://github.com/atsushieno/nplug) demonstrating:

- Basic **audio processing** (linear gain with dB mapping).
- A **custom Win32 UI** featuring analog-style knobs.
- Integration with the **VST3** plugin model (Processor + Controller separation).

---

## ✨ Features

- **Gain Control**: Adjustable from deep attenuation to boost.
- **Output Level**: Independent knob to control final volume.
- **Custom UI**:
  - Dual analog knobs (`GAIN` and `OUTPUT`).
  - Tick marks, dB labels, and animated pointer sprites.
  - Embedded bitmaps for realistic metal-style knobs.
- **Host Integration**:
  - Compatible with VST3 hosts (tested with Windows).
  - Controller + Processor separation following Steinberg’s architecture.

---

## 📂 Project Structure

- **`SimpleGainController.cs`**  
  Defines the plugin’s controller, responsible for parameter handling and creating the editor view.

- **`FixedDualKnobWindow.cs`**  
  Main editor window with two side-by-side analog knobs.

- **`AnalogKnobWindow.cs`**  
  Low-level knob implementation: rendering ticks, labels, pointer sprites, and handling mouse interaction.

- **`Embeded.cs`**  
  Utility to load embedded bitmap resources (e.g., `KnobFace512.png`, `Bg1024x512.png`).

- **Embedded Resources**  
  - `KnobFace512.png`, `KnobTop512.png`, `KnobTop512_gray.png`  
  - `knob_pointer_sprite_300x300_x73_clockwise263.png` (+ red/gray variants)  
  - `Bg1024x512.png` (background)  

---

## 🚀 Getting Started

### Prerequisites

- **Windows** (UI is Win32-specific).
- [.NET 9 SDK](https://dotnet.microsoft.com/download).
- A VST3-compatible host (Cubase, Reaper, Studio One, etc.).

### Build

```sh
git clone https://github.com/yourusername/NPlug.SimpleGain.git
cd NPlug.SimpleGain
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

This will produce a **self-contained, ahead-of-time compiled** `.vst3` plugin in the `bin/Release/net9.0/win-x64/publish/` folder.

### Install

Copy the built `.vst3` file into your system’s VST3 folder:

- **Windows (64-bit):**  
  `C:\Program Files\Common Files\VST3`

Restart your DAW and scan for new plugins.

---

## 🎛️ Usage

1. Insert **SimpleGain** on an audio track.
2. Adjust the **GAIN** knob to boost or attenuate input signal.
3. Fine-tune with the **OUTPUT** knob to control final level.
4. Observe dB readouts and knob pointer for precision control.

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

## 🙌 Acknowledgments

- [NPlug](https://github.com/xoofx/NPlug) for the managed VST3 binding.  
- Steinberg VST3 SDK for the plugin architecture.  
- Inspiration from analog gear knob aesthetics.
