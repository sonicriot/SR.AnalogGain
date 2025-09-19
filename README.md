# SR.AnalogGain 🎚️

A simple **gain plugin** built with [NPlug](https://github.com/xoofx/NPlug) demonstrating:

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

## 🎨 Screenshot

![AnalogGain UI](docs/screenshot.png)

---

## 📂 Project Structure

```bash
src/
├── SR.AnalogGain.sln              # Solution file
└── SR.AnalogGain/                 # Main project folder
    ├── SR.AnalogGain.csproj       # Project file
    ├── AnalogGainController.cs    # Plugin controller (parameter handling, editor creation)
    ├── AnalogGainEditor.cs        # Main editor interface
    ├── AnalogGainModel.cs         # Parameter model
    ├── AnalogGainPlugin.cs        # Plugin factory and registration
    ├── AnalogGainProcessor.cs     # Audio processing engine
    ├── Assets/                    # Embedded bitmap resources
    │   ├── Bg1024x512.png         # Background image
    │   ├── KnobFace512.png        # Knob face texture
    │   ├── KnobTop512.png         # Knob top (gain)
    │   ├── KnobTop512_gray.png    # Knob top (output)
    │   └── knob_pointer_sprite_*.png # Animated pointer sprites
    └── UI/Win32/                  # Windows-specific UI implementation
        ├── FixedDualKnobWindow.cs # Main editor window with dual knobs
        ├── AnalogKnobWindow.cs    # Individual knob implementation
        └── Embeded.cs             # Bitmap resource loader utility
```

---

## 🚀 Getting Started

### Prerequisites

- **Windows** (UI is Win32-specific).
- [.NET 9 SDK](https://dotnet.microsoft.com/download).
- A VST3-compatible host (Cubase, Reaper, Studio One, etc.).

### Build

```sh
git clone https://github.com/sonicriot/SR.AnalogGain.git
cd SR.AnalogGain/src
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

This will produce a **self-contained, ahead-of-time compiled** `.vst3` plugin in the `SR.AnalogGain/bin/Release/net9.0/win-x64/publish/` folder.

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
