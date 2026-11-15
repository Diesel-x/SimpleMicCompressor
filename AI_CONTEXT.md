# 🤖 AI_CONTEXT.md — Архитектурный контекст для нейросетей и AI-агентов

> **Файл предназначен для языковых моделей (LLM), AI-ассистентов (ChatGPT, Claude, Gemini, Copilot, Cursor) и разработчиков.**  
> Описывает структуру проекта, архитектурные решения, звуковой тракт (DSP), модель потоков, форматы конфигурации и ключевые инварианты кодовой базы.

---

## 1. 📌 О проекте (Project Overview)

* **Название:** SimpleMicCompressor (SMC)
* **Автор:** [Diesel-X](https://github.com/Diesel-x)
* **Репозиторий:** `https://github.com/Diesel-x/SimpleMicCompressor`
* **Назначение:** Высокопроизводительный системный процессор звука микрофона в реальном времени для **Discord**, **стримов** (OBS) и **соревновательных игр** (CS2, Valorant, Apex Legends, Warzone).
* **Стек технологий:**
  * **Основная версия (GUI):** C# (.NET 8.0, Windows x64), WPF, NAudio (WASAPI Shared Mode).
  * **Легковесная версия (Light):** Rust (edition 2021, `windows-sys`, `cpal`, `ringbuf`, Win32 API).
* **Ключевые технические метрики:**
  * Сквозная задержка (End-to-End Latency): **~20–25 мс** (буфер ввода 15 мс + буфер вывода 20 мс).
  * Нагрузка на процессор (CPU): **< 0.1% CPU** в активном режиме, **0.0% CPU** в свернутом виде (таймер UI замораживается).
  * Нагрузка на видеокарту (GPU): **0% GPU** (в отличие от OBS Studio).
  * Потребление памяти (RAM):
    * **C# WPF:** ~15–20 МБ в фоне (благодаря системному сбросу `EmptyWorkingSet`).
    * **Rust Light:** ~3–5 МБ RAM (нативный бинарник без сборщика мусора и рантайма).

---

## 2. 📁 Карта кодовой базы (Codebase Map)

```text
SimpleMicCompressor/
├── Audio/                          # Аудио-движок и математика звукового тракта (DSP)
│   ├── AudioEngine.cs              # WASAPI Capture & Render, авторекавери при отключении USB
│   └── Dsp/
│       ├── AudioChain.cs           # Конвейер обработки (LowCut -> Gate -> Comp -> Limiter -> Meme)
│       ├── AudioMetrics.cs         # Метрики пиков в dB, Gain Reduction и статус гейта
│       ├── BiquadHighPassFilter.cs # Фильтр среза гула 80 Hz (2-полюсный Butterworth)
│       ├── DynamicCompressor.cs    # Студийный компрессор с мягким коленом (Soft Knee)
│       ├── FastLimiter.cs          # Кирпичный лимитер пиков (-1.0 dBFS) с tanh-сатурацией
│       └── NoiseGate.cs            # Шумоподавитель с гистерезисом 2.5 dB и удержанием 60 мс
├── Localization/
│   └── LocalizationService.cs      # Мультиязычность (RU, EN, DE, ES) с автодетектом языка ОС
├── Models/
│   ├── AudioSettings.cs            # Модель настроек (сериализуется в mic_compressor_config.json)
│   ├── DeviceInfo.cs               # WASAPI устройство ввода / вывода (ID, имя, статус)
│   ├── HotkeySetting.cs            # Модель горячих клавиш (Ctrl, Shift, Alt, Key, VK коды)
│   ├── PresetItem.cs               # Модель элементов выпадающего списка пресетов
│   └── UserProfile.cs              # Модель сохранения пользовательских профилей
├── Tray/
│   └── TrayManager.cs              # Управление треем, balloon-нотификации, EmptyWorkingSet
├── Utils/
│   ├── GlobalHotkeyService.cs      # Низкоуровневый хук клавиатуры (WH_KEYBOARD_LL)
│   ├── RelayCommand.cs             # Базовая реализация ICommand для MVVM
│   └── VbCableInstaller.cs         # Автоскачивание и тихая установка VB-Audio Virtual Cable
├── ViewModels/
│   ├── BaseViewModel.cs            # Базовый класс с реализацией INotifyPropertyChanged
│   └── MainViewModel.cs            # Главный презентер (маршрутизация, команды, профили)
├── light/                          # ⚡ Автономная редакция на Rust (экспериментальная / без GUI)
│   ├── Cargo.toml                  # Зависимости cpal, ringbuf, serde, windows-sys
│   └── src/
│       ├── main.rs                 # Точка входа #![windows_subsystem = "windows"]
│       ├── audio.rs                # cpal WASAPI input/output потоки через ring buffer
│       ├── config.rs               # Совместимый парсер mic_compressor_config.json
│       ├── dsp.rs                  # Математика Low-Cut, Gate, Compressor, Limiter, Meme
│       └── tray.rs                 # Win32 системный трей, меню и WH_KEYBOARD_LL хук
├── assets/                         # Скриншоты и иконка приложения app.ico
├── App.xaml / App.xaml.cs          # Инициализация WPF, мьютекс одиночного экземпляра
├── MainWindow.xaml / .cs           # Разметка окна, модальные диалоги, темная тема
├── README.md / README_EN.md        # Документация для пользователей (RU / EN)
└── SimpleMicCompressor.csproj      # Конфигурация сборки .NET 8 WPF Single-File
```

---

## 3. 🎛️ Звуковой тракт (DSP Signal Chain)

Обработка аудиопотока выполняется в формате `float` (значения от `-1.0` до `+1.0`):
1. **Low-Cut Filter (80 Hz):**
   * 2-полюсный high-pass фильтр Баттерворта (Biquad Direct Form I).
   * Отсекает инфранизкий гул стола, механические вибрации кулеров ПК и глухие удары по микрофонной стойке.
2. **Noise Gate:**
   * Порог срабатывания по умолчанию: `-42.0 dB`.
   * Атака: `4.0 мс`, Релиз: `120.0 мс`.
   * **Hold Time (60 мс)** и **Гистерезис (2.5 dB):** предотвращают случайное "проглатывание" окончаний слов при затухании голоса.
3. **Dynamic Compressor:**
   * Порог (Threshold): `-20.0 dB`, Соотношение (Ratio): `4.0:1`.
   * Мягкое колено (Soft Knee): `4.0 dB` (плавный вход в компрессию).
   * Атака: `10.0 мс`, Релиз: `150.0 мс`.
   * Компенсация усиления (Makeup Gain): `+5.0 dB`.
   * Вычисляет точное значение ослабления в dB (`GainReductionDb`) для визуализации в GUI.
4. **Fast Limiter (-1.0 dBFS):**
   * Кирпичный лимитер с временем восстановления `40.0 мс`.
   * Выше 95% от потолка применяется гладкая сатурация `tanh`, исключающая жесткий цифровой клиппинг при криках.
5. **Meme Mode («Перегруз / Уничтоженный микрофон»):**
   * Эффект перегруженного микрофона из мемов Discord: вычисляет нелинейное отношение сжатого сигнала к исходному, раскачивая гармоники и искажения.

---

## 4. ⌨️ Глобальные горячие клавиши (Global Hotkeys)

* **Архитектурный выбор:** Вместо устаревшего Win32 `RegisterHotKey` (который конфликтует с браузерами `1409 ERROR_HOTKEY_ALREADY_REGISTERED` и блокируется играми) используется **низкоуровневый хук Windows (`WH_KEYBOARD_LL` через `SetWindowsHookEx`)**.
* **Преимущества:**
  * Работает поверх любых полноэкранных игр с античитами (CS2, Valorant, Apex).
  * Не зависит от дескриптора окна и работает, когда окно скрыто в системный трей.
  * Реализован трекинг зажатия клавиш (`_downKeys`), исключающий спам-триггеры автоповтора при удержании.
* **Действия по умолчанию:**
  * `Ctrl + Shift + B` — Байпас обработки (Bypass);
  * `Ctrl + Shift + M` — Режим «Перегруз» (Meme Mode);
  * `Ctrl + Shift + X` — Мгновенное заглушение микрофона (Mute).
* **Поддерживаемые клавиши:** `A–Z`, `F1–F12`, `0–9`, `NUM0–NUM9`, `SPACE`, `TILDE (~ / ё)`, `TAB`, `PAUSE`, `SCROLL`, `INSERT`, `DELETE`, `HOME`, `END`, `PAGEUP`, `PAGEDOWN`.

---

## 5. 🌐 Локализация и интернационализация (i18n)

* **Файл:** `Localization/LocalizationService.cs`.
* **Языки:** Русский (`ru`), English (`en`), Deutsch (`de`), Español (`es`).
* **Автоопределение:** При первом запуске язык определяется из `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName`.
* **Реактивность:** Реализует `INotifyPropertyChanged` с индексатором `this[string key]`. WPF-привязка вида `{Binding Loc[Key]}` мгновенно обновляет все тексты интерфейса при смене языка без перезапуска программы.

---

## 6. ⚙️ Конфигурация (Configuration Schema)

Конфигурация сохраняется в файл `mic_compressor_config.json` в корневой директории приложения и разделяется между C# версией и версией на Rust:

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

## 7. 🚀 Инструкции по сборке (Build Commands)

### Сборка основной GUI-версии (C# / .NET 8 WPF):
```powershell
# Сборка проекта
dotnet build -c Release

# Публикация единого портативного исполняемого файла
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o ./publish
```
Результат: `publish/SimpleMicCompressor.exe` (~850 КБ).

### Сборка легковесной версии Light (Rust):
```powershell
cd light
cargo build --release
```
Результат: `light/target/release/SimpleMicCompressor_Light.exe` (~529 КБ).

---

## 8. ⚠️ Правила для AI-агентов (Guidelines for AI Agents)

1. **Zero Allocations в аудиопотоке:** Внутри метода обработки аудиокадров (`ProcessBlock` / `DataAvailable`) **категорически запрещено** создавать новые объекты, массивы `new float[]` или выполнять LINQ-запросы. Все промежуточные буферы должны быть предвыделены заранее.
2. **Память в трее:** При скрытии окна WPF всегда должен вызываться `EmptyWorkingSet(Process.GetCurrentProcess().Handle)` совместно с `GC.Collect()`, чтобы освобождать рабочий набор страниц Windows до ~15–20 МБ.
3. **Хоткеи:** Всегда использовать низкоуровневый хук `WH_KEYBOARD_LL`, избегая системных конфликтов с `RegisterHotKey`.
4. **Кроссплатформенность путей:** В Markdown-документации и коде использовать прямые слеши `/`.
