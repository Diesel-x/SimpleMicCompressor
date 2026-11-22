# 🎙️ SimpleMicCompressor

<p align="center">
  <b>Language / Язык:</b>
  <br>
  <a href="README_RU.md">🇷🇺 Русский</a> &nbsp;|&nbsp;
  <b>🇬🇧 English</b>
</p>

<p align="center">
  <img src="assets/screenshot_main_en.png" alt="SimpleMicCompressor Interface" width="850" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Author-Diesel--X-blue?style=flat-square&logo=github" alt="Author" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-informational?style=flat-square&logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/Audio-WASAPI%20Shared%20Mode-success?style=flat-square" alt="Audio" />
  <img src="https://img.shields.io/badge/Latency-20--25%20ms-green?style=flat-square" alt="Latency" />
  <img src="https://img.shields.io/badge/License-MIT-orange?style=flat-square" alt="License" />
</p>

**SimpleMicCompressor** is a low-latency real-time microphone processor for **Discord**, **streaming**, and **competitive games** (CS2, Valorant, Apex Legends, Warzone). It evens out voice volume, cuts keyboard clicks and background hum, prevents clipping, and has zero impact on gaming FPS.

---

## 📦 Editions

| | 🎨 **SimpleMicCompressor (GUI) — Recommended** | ⚡ SimpleMicCompressor Light (Rust) *(Experimental)* |
| :--- | :--- | :--- |
| **File** | `SimpleMicCompressor.exe` (C# / .NET 8 / WPF) | `SimpleMicCompressor_Light.exe` (Rust) |
| **Interface** | Studio dark UI, VU meters, Setup Wizard, tray mode | Headless Windows tray service |
| **Footprint** | ~15–25 MB RAM, < 0.1% CPU | ~3–5 MB RAM, 0.0% CPU |
| **Download** | [📥 **GitHub Releases (Recommended)**](https://github.com/Diesel-x/SimpleMicCompressor/releases) | [⚡ **GitHub Releases**](https://github.com/Diesel-x/SimpleMicCompressor/releases) |

---

## 🚀 Quick Start

1. Download **`SimpleMicCompressor.exe` (Recommended)** from [GitHub Releases](https://github.com/Diesel-x/SimpleMicCompressor/releases).
2. Run the app and complete the **First-Run Setup Wizard**:
   * Select your physical microphone.
   * Install **`SMC virtual cable`** in 1 click (if not already installed).
   * Optionally choose your headphones for monitoring.
3. In **Discord**, **Telegram**, or your game:
   * Select input device: **`CABLE Output (VB-Audio Virtual Cable)`**.
   * Disable Discord's Krisp / Automatic Gain Control (AGC) for clean sound.

---

## ⌨️ Global Hotkeys

Works in fullscreen games without `Alt+Tab` (configurable in Settings):

| Action | Default | Description |
| :--- | :--- | :--- |
| **Bypass** | `Ctrl + Shift + B` | Transmits dry microphone audio without DSP |
| **Meme Mode** | `Ctrl + Shift + M` | Toggles crushed overdrive microphone effect |
| **Mute** | `Ctrl + Shift + X` | Instant microphone mute |

---

## 🛠️ DSP Signal Chain

* **Low-Cut 80 Hz** — 2nd-order Butterworth high-pass filter for desk bumps and fan hum.
* **Noise Gate** — Smooth gate with 60 ms hold and hysteresis.
* **Dynamic Compressor** — Balances quiet speech and loud peaks.
* **Fast Limiter (-1.0 dBFS)** — Soft-saturation peak limiter preventing digital clipping.
* **Meme Mode** — Overdrive distortion mode for Discord banter.

---

## 📄 License

* **Author:** [Diesel-X](https://github.com/Diesel-x)
* **License:** [MIT License](LICENSE)
