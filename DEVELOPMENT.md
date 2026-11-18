# 🛠️ DEVELOPMENT.md — Контекст для нейросетей (AI Context) и руководство разработчика

> **SimpleMicCompressor (SMC)**  
> **Автор:** [Diesel-X](https://github.com/Diesel-x)  
> **Репозиторий:** [github.com/Diesel-x/SimpleMicCompressor](https://github.com/Diesel-x/SimpleMicCompressor)

Этот документ является **главным файлом архитектурного контекста** для языковых моделей (LLM), AI-ассистентов (ChatGPT, Claude, Gemini, Cursor, Copilot) и разработчиков. Здесь описаны стек технологий, полная карта кодовой базы, математика DSP, модель потокобезопасности, CI/CD, схема конфигурации, известные аппаратные нюансы звуковых карт и правила модификации кода.

---

## 1. 💻 Стек технологий и зависимости

Проект разделен на две взаимодополняющие редакции с единым форматом настроек:

```
┌────────────────────────────────────────────────────────┐
│               SimpleMicCompressor                       │
├──────────────────────────┬─────────────────────────────┤
│  🎨 GUI Edition (C# WPF) │ ⚡ Light Edition (Rust)     │
│  - Полный UI и VU-метры  │ - Фоновый сервис в трее     │
│  - Установка VB-Cable    │ - Экстремально легкий       │
│  - .NET 8 (Windows x64)  │ - ~3–5 МБ RAM, 0% CPU       │
└──────────────────────────┴─────────────────────────────┘
```

### 1.1. Основная редакция (C# / .NET 8 WPF)
* **Платформа и рантайм:** `.NET 8.0 Windows` (x64), сборка в единый переносимый файл (Single-File).
* **UI-фреймворк:** **WPF (Windows Presentation Foundation)** с кастомной темной темой в стиле Studio Dark & Discord Blurple, векторная графика `Path`, аппаратное сглаживание шрифтов Segoe UI Variable.
* **Аудио-библиотека:** **NAudio 2.2.1** (`NAudio.CoreAudioApi`, `NAudio.Wave`):
  * `WasapiCapture` — перехват аудиопотока микрофона в режиме `AudioClientShareMode.Shared`.
  * `WasapiOut` — вывод обработанного аудиопотока в виртуальный кабель (`CABLE Input`).
* **Системные Win32 API (P/Invoke):**
  * `user32.dll`: `SetWindowsHookEx` (`WH_KEYBOARD_LL`), `CallNextHookEx`, `UnhookWindowsHookEx`, `GetAsyncKeyState` — глобальный перехват горячих клавиш в любых полноэкранных играх.
  * `psapi.dll`: `EmptyWorkingSet` — принудительный сброс неиспользуемых страниц виртуальной памяти процесса при сворачивании в трей (снижает ОЗУ со 100 МБ до ~15–20 МБ).
  * `System.Windows.Forms.NotifyIcon`: интеграция с системным треем Windows 10/11.
* **Драйвер виртуального аудио:** **VB-Audio Virtual Cable** (виртуальный loopback-драйвер 48000 Hz, 24-bit).

### 1.2. Легковесная редакция (Rust Light)
* **Язык и компилятор:** **Rust 2021 Edition**, тулчейн `stable-x86_64-pc-windows-gnu` / `msvc`.
* **Библиотеки (crates):**
  * `cpal 0.15` — низкоуровневый аудио-бэкенд для Windows WASAPI.
  * `ringbuf 0.3` — lock-free кольцевой буфер (SPSC — Single Producer, Single Consumer) без блокировок потоков.
  * `windows-sys 0.52` — прямые легковесные биндинги к Win32 API (`Win32_UI_WindowsAndMessaging`, `Win32_UI_Shell`, `Win32_Foundation`).
  * `serde` / `serde_json 1.0` — сериализация конфигурационного файла `mic_compressor_config.json`.

---

## 2. 📁 Карта кодовой базы (Codebase Map)

```text
SimpleMicCompressor/
├── Audio/                          # Аудио-движок и математика звукового тракта (DSP)
│   ├── AudioEngine.cs              # WASAPI Capture & Render, авторекавери при сбоях и отключении USB
│   ├── AudioDeviceService.cs       # Перечисление устройств WASAPI (DeviceInfo record)
│   └── Dsp/
│       ├── AudioChain.cs           # Конвейер обработки (LowCut -> Gate -> Comp -> Limiter -> Meme)
│       ├── AudioMetrics.cs         # Метрики пиков в dB, Gain Reduction и статус гейта
│       ├── BiquadHighPassFilter.cs # Фильтр среза гула 80 Hz (2-полюсный Butterworth)
│       ├── DynamicCompressor.cs    # Студийный компрессор с мягким коленом (Soft Knee)
│       ├── FastLimiter.cs          # Быстрый пик-лимитер (-1.0 dBFS) с soft tanh-сатурацией
│       └── NoiseGate.cs            # Шумоподавитель с гистерезисом 2.5 dB и удержанием 60 мс
├── Localization/
│   └── LocalizationService.cs      # Мультиязычность (RU, EN, DE, ES) с автодетектом языка ОС
├── Models/
│   ├── AudioSettings.cs            # Модель настроек (%APPDATA%\SimpleMicCompressor\config.json)
│   ├── HotkeySetting.cs            # Модель горячих клавиш (Ctrl, Shift, Alt, Key, VK коды)
│   ├── PresetItem.cs               # Модель элементов выпадающего списка пресетов
│   └── UserProfile.cs              # Модель сохранения пользовательских профилей
├── Tray/
│   └── TrayManager.cs              # Управление треем, balloon-нотификации, EmptyWorkingSet
├── Utils/
│   ├── AutoStartHelper.cs          # Добавление в автозагрузку Windows через реестр
│   ├── GlobalHotkeyService.cs      # Низкоуровневый хук клавиатуры (WH_KEYBOARD_LL)
│   └── VbCableInstallerService.cs  # Автоскачивание и тихая установка VB-Audio Virtual Cable
├── ViewModels/
│   ├── MainViewModel.cs            # Главный презентер (маршрутизация, команды, профили)
│   └── RelayCommand.cs             # Базовая реализация ICommand для MVVM
├── light/                          # ⚡ Автономная редакция на Rust (экспериментальная / без GUI)
│   ├── Cargo.toml                  # Зависимости cpal, ringbuf, serde, windows-sys
│   └── src/
│       ├── main.rs                 # Точка входа #![windows_subsystem = "windows"]
│       ├── audio.rs                # cpal WASAPI input/output потоки через ring buffer
│       ├── config.rs               # Синхронизированный парсер конфигурации
│       ├── dsp.rs                  # Математика Low-Cut, Gate, Compressor, Limiter, Meme
│       └── tray.rs                 # Win32 системный трей, меню и WH_KEYBOARD_LL хук
├── docs/
│   └── RELEASE_NOTES.md            # Заметки к релизам (EN / RU)
├── assets/                         # Скриншоты и иконка приложения app.ico
├── .github/workflows/build.yml     # GitHub Actions CI/CD (сборка C# и Rust)
├── App.xaml / App.xaml.cs          # Инициализация WPF, мьютекс одиночного экземпляра
├── MainWindow.xaml / .cs           # Разметка окна, модальные диалоги, темная тема
├── README.md                       # Документация для пользователей (English)
├── README_RU.md                    # Документация для пользователей (Русский)
├── DEVELOPMENT.md                  # Данный файл архитектурного контекста
├── llms.txt                        # Индекс документации по стандарту llmstxt.org
└── SimpleMicCompressor.csproj      # Конфигурация сборки .NET 8 WPF Single-File
```

---

## 3. 🏗️ Архитектура и логика работы

### 3.1. Конвейер аудиопотока (Data Flow)

```
[Физический Микрофон]
        │
        ▼ (WASAPI Capture: 48 kHz, float32, 15ms buffer)
[Буфер захвата / DataAvailable]
        │
        ▼
[AudioChain DSP Processor]
  ├── 1. Low-Cut 80 Hz (2-pole Butterworth Biquad)
  ├── 2. Noise Gate (Threshold: -42dB, Hysteresis: 2.5dB, Hold: 60ms)
  ├── 3. Dynamic Compressor (Soft Knee, Makeup Gain, GR metering)
  ├── 4. Fast Limiter (-1.0 dBFS, Tanh Soft-Clipping)
  └── 5. Meme Mode (Опциональный гармонический овердрайв)
        │
        ▼ (Lock-Free FIFO / BufferedWaveProvider)
[WASAPI Out: 48 kHz, float32, 20ms buffer]
        │
        ▼
[CABLE Input (VB-Audio Virtual Cable)]
        │
        ▼
[Discord / Telegram / CS2 / OBS Studio]
```

### 3.2. Потокобезопасность и устранение артефактов (Zero Glitch / Zero Latency)

1. **Zero Allocations в цикле обработки (Zero GC Pressure):**
   * В реальном времени сборщик мусора (.NET GC) — главный источник задержек и аудио-дропов («хрипов» и «заиканий»).
   * В `AudioEngine.cs` все промежуточные буферы (`_inputFloatBuffer`, `_outputFloatBuffer`, `_outputBytes`) выделены **однократно** в конструкторе.
   * Во время вызова `OnDataAvailable` не создается ни одного нового объекта, не вызывается `LINQ`, не происходит упаковки (boxing).
2. **Разделение потоков (Producer-Consumer):**
   * **Поток 1 (Capture Thread):** WASAPI аппаратный поток захвата сэмплов с микрофона. Вызывает `AudioChain.ProcessBlock` и помещает готовые сэмплы в буфер.
   * **Поток 2 (Render Thread):** WASAPI поток воспроизведения в виртуальный кабель. Читает сэмплы из буфера.
   * **Поток 3 (UI Thread):** Работает на 30 FPS через `DispatcherTimer`. Только читает готовые метрики пиков (`AudioMetrics`) для отрисовки VU-метров. При сворачивании окна в трей таймер **полностью замораживается**, снижая нагрузку на CPU до **0.0%**.
3. **Безопасное обновление настроек на лету:**
   * При перемещении ползунков пользователем настройки копируются в легковесную структуру и передаются в DSP-тракт через быстрый `lock (_lock)`. Поток обработки удерживает блокировку всего на доли микросекунды (пересчет коэффициентов фильтров).
4. **USB Watchdog (Авторекавери микрофона):**
   * Если USB-микрофон случайно отключается или возникает ошибка при старте, движок не крашится, а запускает автоматическое восстановление (`ScheduleAutoRecovery`, 8 попыток по 2.5 с) для бесшовного переподключения сессии.

### 3.3. Глобальные горячие клавиши (Low-Level Keyboard Hook)
* Традиционный `RegisterHotKey` часто отклонялся Windows с кодом `1409 (ERROR_HOTKEY_ALREADY_REGISTERED)`, так как браузеры Chrome/Edge и другие приложения регистрируют схожие комбинации (`Ctrl+Shift+B`, `Ctrl+Shift+M`).
* В SMC применен **`WH_KEYBOARD_LL` (SetWindowsHookEx)**:
  * Перехват происходит до того, как игра или браузер поглотят событие.
  * Работает в любых полноэкранных играх с античитами (CS2, Valorant, Apex).
  * Реализован фильтр `_downKeys`, исключающий дребезг и повторные срабатывания при зажатой клавише.

---

## 4. ⚙️ Конфигурация (Configuration Schema)

Конфигурация сохраняется в файл `%APPDATA%\SimpleMicCompressor\config.json` (с fallback на локальный `mic_compressor_config.json`) и полностью синхронизирована между C# GUI и Rust Light версиями:

```json
{
  "InputDeviceId": null,
  "OutputDeviceId": null,
  "HighPassEnabled": true,
  "HighPassCutoffHz": 80.0,
  "GateEnabled": true,
  "GateThresholdDb": -42.0,
  "GateAttackMs": 4.0,
  "GateReleaseMs": 120.0,
  "GateHoldMs": 60.0,
  "CompressorEnabled": true,
  "CompressorThresholdDb": -20.0,
  "CompressorRatio": 4.0,
  "CompressorAttackMs": 10.0,
  "CompressorReleaseMs": 150.0,
  "CompressorMakeupGainDb": 5.0,
  "CompressorKneeDb": 4.0,
  "LimiterEnabled": true,
  "LimiterCeilingDb": -1.0,
  "LimiterReleaseMs": 40.0,
  "BypassAll": false,
  "DestroyedMicMemeMode": false,
  "IsMuted": false,
  "HotkeysEnabled": true,
  "HotkeyBypass": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "B" },
  "HotkeyMeme": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "M" },
  "HotkeyMute": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "X" },
  "Language": "ru",
  "CustomProfiles": []
}
```

---

## 5. 📦 Схема сборки и релиза

### 5.1. Локальная сборка

#### Требования:
* ОС: **Windows 10 / 11 (x64)**
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Rust Toolchain (1.75+)](https://rustup.rs/) (для редакции Light)

#### Сборка основной GUI-версии:
```powershell
# Компиляция в Release конфигурации
dotnet build -c Release

# Сборка автономного переносимого Single-File исполняемого файла
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o ./publish
```
Готовый файл: `publish/SimpleMicCompressor.exe` (~850 КБ).

#### Сборка Rust Light-версии:
```powershell
cd light
cargo build --release
```
Готовый файл: `light/target/release/SimpleMicCompressor_Light.exe` (~529 КБ).

---

### 5.2. Автоматизация сборки через GitHub Actions (CI/CD)

В репозитории настроен автоматический рабочий процесс `.github/workflows/build.yml`.

**Триггеры:**
* Любой `push` в ветку `main`.
* Создание `pull request`.
* Создание тега версии (`v*`).

**Шаги пайплайна:**
1. Развертывание виртуальной машины `windows-latest`.
2. Установка .NET 8.0 SDK и стабильного Rust-тулчейна.
3. Проверка форматирования и компиляция C# WPF проекта.
4. Сборка портативного `SimpleMicCompressor.exe`.
5. Компиляция `SimpleMicCompressor_Light.exe` через `cargo build --release`.
6. Сохранение артефактов сборки (Artifacts) для тестирования.
7. При публикации тега `v*` — автоматическое создание GitHub Release и прикрепление обоих исполняемых файлов.

---

## 6. 🗺️ План развития (Roadmap) и известные проблемы

### 6.1. Известные аппаратные нюансы и решения

| Проблема / Симптом | Причина | Реализованное решение / Статус |
| :--- | :--- | :--- |
| **Ошибка WASAPI `0xE000020B`** | Наличие отключенных или фантомных аудиоустройств в реестре Windows. | В `MainViewModel.cs` добавлена фильтрация устройств с пустым именем или статусом `NotPresent`. |
| **Sample Rate Mismatch (44.1 kHz vs 48 kHz)** | Микрофон работает на 44100 Hz, а кабель настроен на 48000 Hz. | `WasapiCapture` и `WasapiOut` инициализируются в native-формате устройств. Запланирован встроенный `WdlResampler`. |
| **USB Power Suspend (Засыпание микрофона)** | Windows отключает питание USB-порта при отсутствии звука. | Фоновый поток Watchdog проверяет активность буфера и восстанавливает поток за 1 секунду. |
| **Высокое потребление ОЗУ в фоне** | Рантайм WPF и DirectX кэшируют страницы в памяти. | Вызов `EmptyWorkingSet` при скрытии окна в трей — снижает ОЗУ со 100 МБ до 15 МБ. |

### 6.2. План развития (Roadmap)

- [ ] **Встроенный 3-полосный параметрический эквалайзер (EQ):**
  - High Shelf (сглаживание шипящих 'С' и 'Ц').
  - Mid Bell (вырезание коробочного призвука на 400–600 Hz).
  - Presence Boost (добавление теплоты на 2.5–3 kHz).
- [ ] **Поддержка ASIO:** Для звуковых карт Focusrite Scarlett, MOTU, Behringer с прямой аппаратной задержкой < 5 мс.
- [ ] **Поддержка сторонних VST3 плагинов:** Базовый хостинг 64-битных VST3 (например, iZotope или FabFilter).
- [ ] **Визуальный анализатор спектра (Realtime FFT Spectrum):** 60 FPS отображение частотного спектра микрофона.

---

## 7. 🤖 Рекомендации и промпты для работы с ИИ (LLM Guidelines)

Если вы модифицируете проект с помощью AI-ассистентов (Claude, ChatGPT, Gemini, Cursor), используйте следующие формулировки и правила для поддержания чистоты кода:

### 7.1. Ключевые архитектурные правила для промптов
1. **Правило Zero Allocations:**
   > *"Внеси изменения в класс AudioChain.cs / DynamicCompressor.cs. Помни, что метод Process выполняется в реальном времени внутри аудиопотока 48000 раз в секунду. Запрещено использовать `new`, вызовы LINQ, замыкания или боксинг. Все переменные должны быть локальными примитивами или полями класса."*
2. **Правило WPF Локализации:**
   > *"При добавлении нового элемента UI в MainWindow.xaml используй привязку `{Binding Loc[New_Key_Name]}`. Не забудь добавить строковый ключ во все четыре словаря (StringsRu, StringsEn, StringsDe, StringsEs) в `LocalizationService.cs`."*
3. **Правило работы с памятью процесса:**
   > *"При добавлении фоновых задач не удерживай ссылки на закрытые окна. Для освобождения страниц рабочего множества при сворачивании используй вызов `EmptyWorkingSet`."*

### 7.2. Пример удачного промпта для добавления DSP-модуля
```text
Задача: Добавить в AudioChain.cs простой De-Esser (фильтр свистящих звуков) на основе Biquad Bandpass + Compressor.
Требования:
1. Написан на чистом C# 8 без сторонних аллокаций.
2. Параметры: Частота 6000 Hz, Порог -18 dB, Атака 1 мс, Релиз 20 мс.
3. Полная потокобезопасность при вызове из WasapiCapture.
4. Добавить соответствующие поля в AudioSettings.cs с дефолтными значениями для сохранения в JSON.
```
