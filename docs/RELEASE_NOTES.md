# SimpleMicCompressor Release Notes / Примечания к выпуску

[English](#-english) | [Русский](#-русский)

---

## 🇬🇧 English

### Version 1.2

#### Overview
Version 1.2 introduces a completely redesigned dark studio interface, responsive multi-resolution scaling across desktop and laptop displays, non-intrusive notifications, a startup update checker, a 1-click clean uninstaller, and hardened DSP protections with full test suite coverage for both C# and Rust editions.

---

#### 1. Professional Studio Dark UI Redesign
- **Aesthetic Overhaul**: Replaced the UI with a custom hardware-inspired dark studio palette (`#0c0e14` workspace background, `#151722` card panels, `#1a1d28` control wells, `#2563eb` accent blue).
- **Hardware Command Center**: Unified microphone input selection, virtual cable routing, 1-click VB-Cable installation/repair status, and headphone monitoring into a cohesive hardware card.
- **Calibrated Studio VU Meters**: Real-time Input, Gain Reduction (GR), and Output level meters featuring calibrated dB graduation marks (-48 dB to 0 dBFS) and gate status indicators.
- **3-Module DSP Channel Strip Rack**: Clear signal-flow hierarchy labeled by processing stages:
  1. `Noise Gate` & `Low-Cut 80 Hz` pre-filtering
  2. `Dynamic Compressor` with Threshold, Ratio, Makeup Gain, and Attack/Release timing
  3. `Master & FX` with Brickwall Limiter (-1.0 dBFS), Dry Bypass, Mute, and Meme Mode overdrive.

---

#### 2. Multi-Resolution & Display Scaling Support
- **Adaptive Layout**: Fully verified and responsive on 4K UHD, 1440p QHD, 1080p Full HD, laptop displays (1366x768, 1280x720), and compact split-screen viewports (1020x600).
- **Custom Sleek WPF ScrollViewer**: Replaced the default Windows scrollbar template; eliminated the standard white/grey corner artifact, ensuring seamless `#0c0e14` background blending.
- **Zero Horizontal Clipping**: Proportional column sizing (`*`) and compact header controls ensure action buttons and profile selectors remain fully visible without horizontal scrollbars.
- **Fixed Minimum Bounds**: Configured `MinWidth="1020"` and `MinHeight="580"` to guarantee DSP rack structural integrity.

---

#### 3. Quiet Notifications & Startup Update Checker
- **Removed Desktop Notification Spam**: Eliminated repetitive balloon/toast popups for routine profile loading and hotkey toggling.
- **Startup Update Check**: Non-blocking asynchronous version check runs once upon interface launch. When a newer version is released, a clean message dialog offers a direct link to the GitHub Releases page.

---

#### 4. 1-Click Clean Uninstaller
- **Safe Self-Deletion**: Built-in uninstaller button in Application Settings (`UninstallHelper.cs`).
- **Complete Cleanup**: Closes active processes, deletes autostart registry entries (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), purges `%APPDATA%\SimpleMicCompressor`, and self-deletes the executable without leaving residual files.

---

#### 5. DSP Hardening & Test Harness
- **Rust Light DSP Parity**: Ported NaN and Infinity sanitization to Rust `BiquadHighPassFilter`, `FastLimiter`, `NoiseGate`, and `DynamicCompressor`, ensuring self-healing recovery after audio hardware glitches.
- **Automated CI Test Suite**: Verified 62/62 tests passing in C# (`tests/DspTests/DspTests.csproj`) and 15/15 tests passing in Rust (`cargo test`).

---
---

### Version 1.1

#### Overview
Version 1.1 delivers major stability, audio quality, and lifecycle improvements across the real-time DSP pipeline, WASAPI audio engine, UI/ViewModel layers, and system utilities. It also adds strict single-instance process enforcement for both the WPF GUI and Rust Light editions.

---

#### 1. DSP Pipeline & Audio Processing
- **Biquad High-Pass Filter (`BiquadHighPassFilter.cs`)**:
  - Added NaN and Infinity detection with automatic self-healing (resets filter delay line registers `_x1, _x2, _y1, _y2` to zero when invalid floats occur).
- **Dynamic Compressor (`DynamicCompressor.cs`)**:
  - Dynamically calculates the detector filter coefficient ($\alpha$) based on the active sample rate ($\tau \approx 3.0$ ms), ensuring identical attack and release behavior across 44.1 kHz, 48 kHz, 96 kHz, and 192 kHz.
  - Added protective guards against NaN/Infinity in envelope detection and gain reduction calculations.
- **Noise Gate (`NoiseGate.cs`)**:
  - Fixed a state-machine freeze bug in the hysteresis band: `_holdCounter` now decrements continuously during hold time between open and close thresholds.
  - Scaled detector filter alpha to the sample rate ($\tau \approx 1.5$ ms).
  - Added NaN/Infinity guards and envelope clamping.
- **Fast Limiter (`FastLimiter.cs`)**:
  - Fixed envelope recovery freeze when input dropped below ceiling during active limiting.
  - Added NaN/Infinity sanitization.
- **Audio Processing Chain (`AudioChain.cs`)**:
  - **Thread Safety**: Introduced `_dspLock` to serialize dynamic parameter updates with real-time `ProcessInterleaved` execution, eliminating race conditions.
  - **Dynamic Multi-Channel Allocation**: Expanded support up to 8 channels, preventing array boundary exceptions on stereo, 5.1, and 7.1 devices.
  - **Meme Mode Fix**: Corrected filter channel mapping.
  - **Sample-Rate Independent Meter Decay**: Replaced hardcoded sample decay with continuous exponential decay.

---

#### 2. Audio Engine & Hardware Compatibility
- **WASAPI Polling & Event-Sync Fallback (`AudioEngine.cs`)**:
  - Output initialization automatically falls back to event-driven synchronization (`useEventSync: true`) if standard polling fails.
- **IEEE Float & 32-bit Integer Format Support**:
  - Corrected format detection for `WaveFormatExtensible` using standard GUID, preventing distortion on 32-bit float audio interfaces.
- **Anti-Click Resampling**:
  - Added continuous fractional phase accumulation in the linear resampler across buffer chunks.
- **COM & Stream Resource Leak Fixes**:
  - Fixed resource disposal in `ScheduleAutoRecovery` and `OnRecordingStopped`. Deterministic disposal of `MMDevice` COM instances.

---

#### 3. Rust Light Edition Improvements
- **Fixed Hotkey Double-Trigger**: Removed duplicate `RegisterHotKey` calls that fought with low-level `WH_KEYBOARD_LL` hook.
- **Shared Configuration Path**: Points to unified `%APPDATA%\SimpleMicCompressor\config.json`.
- **Friendly Device Matching**: Synchronizes `InputDeviceName` and `OutputDeviceName` with GUI selection.
- **Automated CI Tests**: Integrated Rust and C# tests into GitHub Actions workflow.

---
---

## 🇷🇺 Русский

### Версия 1.2

#### Обзор изменений
Версия 1.2 представляет полностью переработанный тёмный студийный интерфейс, поддержку любых разрешений экранов и ноутбуков, тихие уведомления, автопроверку обновлений при запуске, встроенную кнопку чистой деинсталляции и полное тестирование DSP-тракта C# и Rust.

---

#### 1. Профессиональный студийный интерфейс (Studio Dark UI)
- **Полный редизайн**: Создана тёмная студийная цветовая палитра (`#0c0e14`, `#151722`, `#1a1d28`, синий акцент `#2563eb`) без шаблонных элементов.
- **Центр управления аудиоустройствами**: Настройки микрофона, виртуального кабеля, статуса автоустановщика VB-Cable и мониторинга в наушники объединены в один блок.
- **Студийные VU-метры**: Высокоточные индикаторы входного уровня, компрессии (GR) и выходного сигнала с калиброванной шкалой в дБ (-48 … 0 dBFS) и индикатором гейта.
- **Трёхмодульный рэк DSP**: Наглядное разделение цепочки обработки:
  1. `Noise Gate` и ФВЧ-фильтр `Low-Cut 80 Hz`
  2. `Dynamic Compressor` со слайдерами порога, сжатия, усиления и атаки/спада
  3. `Master & FX` с лимитером (-1.0 dBFS), байпасом, мутом и режимом «Перегруз».

---

#### 2. Адаптивность под любые мониторы и экраны
- **Поддержка любых разрешений**: Протестировано на 4K UHD, 1440p QHD, 1080p Full HD, ноутбуках (1366x768, 1280x720) и в компактном режиме (1020x600).
- **Кастомный тёмный ScrollViewer**: Переопределен системный шаблон прокрутки; полностью устранён артефакт белого угла при отображении полос прокрутки.
- **Отсутствие обрезания элементов**: Пропорциональные колонки и компактные кнопки в шапке гарантируют полную видимость элементов без горизонтального сдвига.
- **Защитные минимальные границы**: Установлены `MinWidth="1020"` и `MinHeight="580"`.

---

#### 3. Тихие уведомления и проверка обновлений
- **Удалены назойливые всплывающие окна**: Больше нет спама уведомлениями в трее при каждом переключении пресетов или хоткеев.
- **Проверка обновлений при запуске**: Быстрая тихая проверка релизов GitHub при открытии окна. При наличии новой версии отображается диалог с предложением перейти на страницу релизов.

---

#### 4. Полная деинсталляция в 1 клик
- **Безопасная очистка**: Кнопка полной деинсталляции в окне «Настройки» (`UninstallHelper.cs`).
- **Удаление без следов**: Завершает процессы, удаляет автозапуск из реестра Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), очищает рабочую папку `%APPDATA%\SimpleMicCompressor` и безопасно удаляет исполняемый файл приложения.

---

#### 5. Защита DSP и тесты
- **Паритет безопасности Rust Light**: Внедрена фильтрация NaN/Inf с самовосстановлением в компоненты Rust-версии.
- **Полное покрытие тестами**: 62/62 тестов успешно пройдены в C# (`tests/DspTests`) и 15/15 тестов в Rust (`cargo test`).

---
---

### Версия 1.1

#### Обзор изменений
В версии 1.1 проведена масштабная работа по исправлению ошибок, повышению стабильности и качества звука в DSP-пайплайне, WASAPI аудио-движке, интерфейсе пользователя и системном трее. Реализован строгий запуск приложения в единственном экземпляре для C# WPF и Rust Light версий.

---

#### 1. DSP-пайплайн и обработка аудио
- **ФНЧ-фильтр (`BiquadHighPassFilter.cs`)**: Защита от NaN и Infinity с автоматическим сбросом регистров задержки в ноль.
- **Динамический компрессор (`DynamicCompressor.cs`)**: Коэффициент сглаживания рассчитывается динамически по частоте дискретизации ($\tau \approx 3.0$ мс).
- **Нойз-гейт (`NoiseGate.cs`)**: Исправлено зависание счетчика удержания (`_holdCounter`) в полосе гистерезиса.
- **Лимитер (`FastLimiter.cs`)**: Устранено зависание огибающей при падении уровня сигнала ниже потолка.
- **AudioChain**: Потокобезопасность через `_dspLock`, поддержка до 8 аудиоканалов.

---

#### 2. Аудио-движок и стабильность
- Автоматический fallback на event-driven synchronization (`useEventSync: true`) при сбое WASAPI polling.
- Поддержка 32-bit Float и 32-bit Integer форматов без искажений.
- Устранение треска при ресемплировании между частотами дискретизации (44.1 ↔ 48 кГц).
- Устранение утечек ресурсов COM и потоков при реконнекте устройств.
