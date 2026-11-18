# SimpleMicCompressor v1.0.1 Release Notes / Примечания к выпуску

[English] | [Русский]

---

## 🇬🇧 English

### Overview
Version 1.0.1 delivers major stability, audio quality, and lifecycle improvements across the real-time DSP pipeline, WASAPI audio engine, UI/ViewModel layers, and system utilities. It also adds strict single-instance process enforcement for both the WPF GUI and Rust Light editions.

---

### 1. DSP Pipeline & Audio Processing
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
  - **Thread Safety**: Introduced `_dspLock` to serialize dynamic parameter updates (`UpdateParameters`, `SetMemeMode`, `SetEqualizer`) with real-time `ProcessInterleaved` execution, eliminating race conditions.
  - **Dynamic Multi-Channel Allocation**: Expanded support up to 8 channels, preventing array boundary exceptions on stereo, 5.1, and 7.1 devices.
  - **Meme Mode Fix**: Corrected filter channel mapping (channel 0's filter was previously called twice while channel 1 was bypassed).
  - **Sample-Rate Independent Meter Decay**: Replaced hardcoded sample decay (`0.85`) with continuous exponential decay (`MathF.Exp(-blockDurationSec / 0.150f)`).

---

### 2. Audio Engine & Hardware Compatibility
- **WASAPI Polling & Event-Sync Fallback (`AudioEngine.cs`)**:
  - Output initialization is now guarded by exception handling: if standard WASAPI polling mode fails on exclusive or low-latency virtual soundcards, it automatically falls back to event-driven synchronization (`useEventSync: true`).
- **IEEE Float & 32-bit Integer Format Support**:
  - Corrected format detection for `WaveFormatExtensible` using standard GUID `00000003-0000-0010-8000-00aa00389b71`, preventing severe white noise / distortion when using 32-bit float or 32-bit PCM audio hardware.
- **Anti-Click Resampling**:
  - Added continuous fractional phase accumulation in the linear resampler across buffer chunks, completely eliminating buffer-boundary clicks and pops during sample rate conversion (e.g. 44.1 kHz microphone to 48 kHz virtual cable).
- **COM & Stream Resource Leak Fixes**:
  - Fixed resource disposal in `ScheduleAutoRecovery` and `OnRecordingStopped`.
- **Audio Device Service (`AudioDeviceService.cs`)**:
  - Guaranteed disposal of `MMDevice` and `AudioClient` COM instances via `using` and `finally` blocks.
  - Localized device identification strings.

---

### 3. UI, ViewModel & System Tray
- **Clean Tray Exit & Window Lifecycle (`MainWindow.xaml.cs`, `TrayManager.cs`)**:
  - Resolved fatal tray exit bug where closing from tray context menu was blocked by minimize-to-tray handling; added clean `ExitApplication()` method.
  - Added `RestoreAndActivate()` via Win32 `SetForegroundWindow` to reliably bring the window to the front when clicked in tray or when a duplicate app instance is launched.
  - Fixed GDI `HICON` handle leak in tray icon generator using Win32 `DestroyIcon`.
  - Resolved relative icon path resolution by using `AppContext.BaseDirectory`.
- **Dynamic Device Switching (`MainViewModel.cs`)**:
  - `RefreshDevices()` now detects active device changes and automatically restarts the audio engine with new routing without requiring manual stop/start.
  - Real-time gate status ("Open" / "Closed") and latency metrics are fully bound to `LocalizationService`.
  - Metering update intervals throttled to prevent UI thread dispatcher flooding.
- **Localization (`LocalizationService.cs`)**:
  - Added missing localization tokens (`Tray_Active_Balloon`, `Telemetry_Ms`, `Routing_Default`, `Routing_VirtualCable`) across EN, RU, DE, and ES.

---

### 4. Single-Instance Application Enforcement
- **C# WPF GUI Edition**:
  - Integrated system-wide named `Mutex` (`SimpleMicCompressor_SingleInstance_Mutex_2026`).
  - Added named `EventWaitHandle` (`SimpleMicCompressor_ShowEvent_2026`). Launching a second instance signals the primary instance to restore and activate its window, then immediately exits.
- **Rust Light Edition**:
  - Enforced single instance using Windows `CreateMutexW`. Secondary processes terminate immediately if `ERROR_ALREADY_EXISTS` is encountered.

---

### 5. Rust Light Edition Improvements
- Fixed mono-to-stereo channel desync and 2x chipmunk audio speedup when capturing from mono microphones to stereo output.
- Integrated linear sample rate conversion for sample rate matching.

---
---

## 🇷🇺 Русский

### Обзор изменений
В версии 1.0.1 проведена масштабная работа по исправлению ошибок, повышению стабильности и качества звука в DSP-пайплайне, WASAPI аудио-движке, интерфейсе пользователя и системном трее. Реализован строгий запуск приложения в единственном экземпляре для C# WPF и Rust Light версий.

---

### 1. DSP-пайплайн и обработка аудио
- **ФНЧ-фильтр (`BiquadHighPassFilter.cs`)**:
  - Добавлена защита от значений NaN и Infinity с автоматическим сбросом регистров задержки (`_x1, _x2, _y1, _y2`) в ноль при обнаружении некорректных чисел.
- **Динамический компрессор (`DynamicCompressor.cs`)**:
  - Коэффициент сглаживания детектора огибающей ($\alpha$) теперь рассчитывается динамически на основе текущей частоты дискретизации ($\tau \approx 3.0$ мс), обеспечивая одинаковое поведение атаки и спада на 44.1, 48, 96 и 192 кГц.
  - Добавлена защита от распространения NaN/Infinity в детекторе уровня и звене сжатия.
- **Нойз-гейт (`NoiseGate.cs`)**:
  - Исправлено зависание счетчика удержания (`_holdCounter`) в полосе гистерезиса: теперь удержание корректно отсчитывается при падении уровня между порогом открытия и закрытия.
  - Коэффициент сглаживания детектора масштабируется по частоте дискретизации ($\tau \approx 1.5$ мс).
  - Внедрена защита от денормализованных чисел и NaN.
- **Лимитер (`FastLimiter.cs`)**:
  - Устранено зависание огибающей при падении уровня сигнала ниже потолка во время активного ограничения.
  - Добавлены проверки на NaN/Infinity.
- **Аудио-цепочка (`AudioChain.cs`)**:
  - **Потокобезопасность**: Добавлена синхронизация `_dspLock` между методами обновления параметров (`UpdateParameters`, `SetMemeMode`, `SetEqualizer`) и обработчиком `ProcessInterleaved`, что исключает гонки потоков и искажение буфера.
  - **Поддержка многоканальности**: Динамическое масштабирование до 8 каналов, предотвращающее вылет `IndexOutOfRangeException` на стерео, 5.1 и 7.1 аудиоустройствах.
  - **Исправление Meme Mode**: Исправлена ошибка маршрутизации каналов (фильтр нулевого канала вызывался дважды, а первый канал пропускался без фильтрации).
  - **Независимый от частоты спад индикатора Gain Reduction**: Жесткий множитель `0.85` заменен на экспоненциальный спад по времени (`MathF.Exp(-blockDurationSec / 0.150f)`).

---

### 2. Аудио-движок и аппаратная совместимость
- **WASAPI Polling и переход на Event-Sync (`AudioEngine.cs`)**:
  - Инициализация вывода помещена в блок обработки исключений: если режим опроса (polling) не поддерживается звуковой картой или виртуальным кабелем, движок автоматически переключается на событийную синхронизацию (`useEventSync: true`).
  - **Поддержка 32-bit Float и 32-bit PCM**: Корректная идентификация `WaveFormatExtensible` через GUID `00000003-0000-0010-8000-00aa00389b71`, устраняющая белый шум и треск на профессиональных звуковых интерфейсах.
- **Ресемплер без щелчков**:
  - Внедрено непрерывное накопление дробной фазы в линейном ресемплере между чанками буфера, полностью устранив щелчки на стыках блоков при конвертации частот (например, микрофон 44.1 кГц -> виртуальный кабель 48 кГц).
- **Устранение утечек ресурсов COM и потоков**:
  - Исправлено освобождение ресурсов в `ScheduleAutoRecovery` и `OnRecordingStopped`.
- **Сервис аудиоустройств (`AudioDeviceService.cs`)**:
  - Гарантированное освобождение экземпляров `MMDevice` и `AudioClient` через `using` и `finally`.
  - Локализация системных названий устройств.

---

### 3. Пользовательский интерфейс и трей
- **Корректный выход через трей и управление окном (`MainWindow.xaml.cs`, `TrayManager.cs`)**:
  - Исправлен критический баг: выбор пункта "Выход" в меню трея блокировался отменой закрытия окна; добавлен метод `ExitApplication()`, корректно завершающий процесс.
  - Добавлен метод `RestoreAndActivate()` с вызовом Win32 `SetForegroundWindow` для надежного восстановления и вывода окна на передний план при клике в трее или запуске второй копии.
  - Устранена утечка дескрипторов GDI `HICON` в генераторе иконки трея с помощью Win32 `DestroyIcon`.
  - Путь к иконкам теперь рассчитывается через `AppContext.BaseDirectory`.
- **Динамическая смена устройств (`MainViewModel.cs`)**:
  - `RefreshDevices()` автоматически определяет изменение активных устройств и перезапускает движок без необходимости вручную останавливать обработку.
  - Текстовый статус гейта («Открыт» / «Закрыт») и задержка («{0:F1} мс») полностью переведены на ключи `LocalizationService`.
  - Ограничена частота обновления интерфейса во избежание перегрузки очереди диспетчера WPF.
- **Локализация (`LocalizationService.cs`)**:
  - Добавлены недостающие ключи для русского, английского, немецкого и испанского языков.

---

### 4. Работа только в одном экземпляре
- **C# WPF GUI**:
  - Реализован общесистемный мьютекс `SimpleMicCompressor_SingleInstance_Mutex_2026`.
  - Добавлено событие `EventWaitHandle` (`SimpleMicCompressor_ShowEvent_2026`). При повторном запуске приложение сигнализирует работающему экземпляру развернуть главное окно и завершает свою работу.
- **Rust Light Edition**:
  - Интегрирована проверка через системный `CreateMutexW`. При обнаружении дубликата консольное приложение мгновенно завершается.

---

### 5. Улучшения Rust Light Edition
- Исправлена рассинхронизация каналов моно-в-стерео и ускоренное в 2 раза воспроизведение («голос бурундука»).
- Реализован ресемплер для согласования частот дискретизации входа и выхода.
