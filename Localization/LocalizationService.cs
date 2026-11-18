using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MicCompressor.Localization;

public record LanguageOption(string Code, string DisplayName);

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new List<LanguageOption>
    {
        new("ru", "Русский (RU)"),
        new("en", "English (EN)"),
        new("de", "Deutsch (DE)"),
        new("es", "Español (ES)")
    };

    private string _currentLanguage = "ru";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(nameof(CurrentLanguageOption));
                OnPropertyChanged("Item[]");
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public LanguageOption CurrentLanguageOption
    {
        get => SupportedLanguages.FirstOrDefault(l => l.Code == _currentLanguage) ?? SupportedLanguages[0];
        set
        {
            if (value != null && value.Code != _currentLanguage)
            {
                SetLanguage(value.Code);
            }
        }
    }

    public string this[string key]
    {
        get => Get(key);
        set { /* No-op to support TwoWay default bindings in WPF (e.g. Run.Text) */ }
    }

    private LocalizationService()
    {
    }

    public void SetLanguage(string langCode)
    {
        langCode = langCode.ToLowerInvariant();
        if (!SupportedLanguages.Any(l => l.Code == langCode))
        {
            langCode = "en";
        }
        CurrentLanguage = langCode;
    }

    public static string DetectSystemLanguage()
    {
        try
        {
            string code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return code switch
            {
                "ru" or "uk" or "be" or "kk" => "ru",
                "de" => "de",
                "es" => "es",
                _ => "en"
            };
        }
        catch
        {
            return "en";
        }
    }

    public string Get(string key, params object[] args)
    {
        Dictionary<string, string> dict = _currentLanguage switch
        {
            "en" => StringsEn,
            "de" => StringsDe,
            "es" => StringsEs,
            _ => StringsRu
        };

        if (!dict.TryGetValue(key, out string? value))
        {
            if (!StringsEn.TryGetValue(key, out value))
            {
                value = key;
            }
        }

        if (args != null && args.Length > 0)
        {
            try
            {
                return string.Format(value, args);
            }
            catch
            {
                return value;
            }
        }

        return value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region String Dictionaries

    private static readonly Dictionary<string, string> StringsRu = new()
    {
        // Header
        ["App_Title"] = "SIMPLE MIC COMPRESSOR",
        ["Engine_Dsp"] = "WASAPI DSP",
        ["Engine_Active"] = "Активен (В эфире)",
        ["Engine_Stopped"] = "Остановлен",
        ["Engine_Error"] = "Ошибка",
        ["Telemetry_Latency"] = "LATENCY:",
        ["Header_Profile"] = "ПРОФИЛЬ",
        ["Action_Save"] = "Сохранить",
        ["Action_New"] = "Новый",
        ["Action_Delete"] = "Удалить",
        ["Action_Bypass"] = "Bypass",
        ["Action_ToTray"] = "В трей",
        ["Action_Start"] = "Запустить",
        ["Action_Stop"] = "Остановить",
        ["Action_Hotkeys"] = "Клавиши",
        ["Action_Language"] = "Язык",
        ["Badge_Muted"] = "МИКРОФОН ЗАГЛУШЕН",

        // Presets
        ["Preset_Gaming"] = "🎮 Гейминг и Discord",
        ["Preset_Default"] = "🎙️ Стандартный / Баланс",
        ["Preset_Podcast"] = "📻 Радио / Подкаст",
        ["Preset_Whisper"] = "🤫 Шепот / Тихая ночь",
        ["Preset_AntiRage"] = "⚡ Анти-Рейдж / Агрессивный",
        ["Preset_Meme"] = "🔊 Уничтоженный микрофон (Meme)",
        ["Preset_Custom"] = "Пользовательский",

        // Audio Routing
        ["Card_Routing"] = "МАРШРУТИЗАЦИЯ ЗВУКА",
        ["Routing_Refresh"] = "Обновить",
        ["Routing_Input"] = "Вход:",
        ["Routing_Output"] = "Выход:",
        ["Routing_Default"] = "По умолчанию",
        ["Routing_VirtualCable"] = "Виртуальный кабель",
        ["Routing_Monitor"] = "Мониторинг",
        ["Routing_MonitorVolume"] = "Громкость самопрослушивания в наушниках",
        ["Cable_Active"] = "Виртуальный кабель активен. В Discord выберите CABLE Output",
        ["Cable_NotFound"] = "Виртуальный кабель не обнаружен (нужен для Discord)",
        ["Cable_Install"] = "Установить кабель",
        ["Cable_Reinstall"] = "Переустановить",
        ["Cable_Installing"] = "Установка...",

        // Level Meters
        ["Card_Meters"] = "ИНДИКАТОРЫ УРОВНЯ",
        ["Meters_Gate"] = "GATE:",
        ["Gate_Open"] = "ОТКРЫТ",
        ["Gate_Closed"] = "ЗАКРЫТ",
        ["Meters_Input"] = "Вход (Микрофон)",
        ["Meters_GR"] = "Сжатие компрессора (GR)",
        ["Meters_Output"] = "Выход (В эфир)",

        // Noise Gate
        ["Card_Gate"] = "NOISE GATE",
        ["Gate_Threshold"] = "Порог (Threshold)",
        ["Gate_Threshold_Tip"] = "Уровень громкости, ниже которого микрофон автоматически глушится",
        ["Gate_Release"] = "Затухание (Release)",
        ["Gate_Release_Tip"] = "Время плавного закрытия гейта (сохраняет тихие окончания слов)",

        // Compressor
        ["Card_Compressor"] = "COMPRESSOR",
        ["Comp_Threshold"] = "Порог (Threshold)",
        ["Comp_Threshold_Tip"] = "Громкость, выше которой сигнал начинает сжиматься",
        ["Comp_Ratio"] = "Сжатие (Ratio)",
        ["Comp_Ratio_Tip"] = "Степень подавления громких звуков",
        ["Comp_Makeup"] = "Усиление (Makeup Gain)",
        ["Comp_Makeup_Tip"] = "Компенсация общей громкости после сжатия",
        ["Comp_Attack"] = "Атака",
        ["Comp_Release"] = "Спад",

        // Master & FX
        ["Card_Master"] = "MASTER & FX",
        ["FX_LowCut"] = "Low-Cut 80 Hz (Срез гула)",
        ["FX_LowCut_Tip"] = "Срезает низкочастотный гул микрофона и удары по столу",
        ["FX_Limiter"] = "Пик-лимитер (-1.0 dB)",
        ["FX_Limiter_Sub"] = "Защита от клиппинга и хрипа",
        ["FX_Meme"] = "Перегруз (Meme)",
        ["FX_Meme_Notice"] = "В Discord отключите Krisp (иначе звук заглушится)",
        ["Pref_AutoStart"] = "Автозапуск с Windows",
        ["Pref_MinimizeClose"] = "Сворачивать в трей на [X]",
        ["Btn_HotkeysConfig"] = "Настроить горячие клавиши",

        // Footer
        ["Footer_Engine"] = "SimpleMicCompressor • Low-Latency Audio Engine",
        ["Footer_Developer"] = "Разработчик: ",
        ["Footer_Output"] = "Выход: ",

        // Modal New Profile
        ["Modal_NewProfile_Title"] = "Новый профиль",
        ["Modal_NewProfile_Desc"] = "Введите название для сохранения текущих настроек компрессора, гейта и фильтров:",
        ["Modal_NewProfile_Field"] = "НАЗВАНИЕ ПРОФИЛЯ",
        ["Modal_Cancel"] = "Отмена",
        ["Modal_Save"] = "Сохранить",

        // Modal Hotkeys
        ["Modal_Hotkeys_Title"] = "Горячие клавиши (Global Hotkeys)",
        ["Modal_Hotkeys_Desc"] = "Управляйте компрессором прямо из игр без переключения окон:",
        ["Modal_Hotkeys_Enable"] = "Включить глобальные горячие клавиши",
        ["Hotkeys_Bypass"] = "Режим Bypass (Оригинал):",
        ["Hotkeys_Meme"] = "Перегруз (Meme Mode):",
        ["Hotkeys_Mute"] = "Заглушить микрофон (Mute):",
        ["Modal_Reset"] = "Сбросить по умолчанию",
        ["Modal_Close"] = "Закрыть",

        // Notifications & Tooltips
        ["Msg_Profile_Saved"] = "✓ Профиль «{0}» успешно сохранен",
        ["Msg_Profile_Updated"] = "✓ Настройки сохранены в профиль «{0}»",
        ["Msg_Profile_Deleted"] = "Профиль «{0}» удален",
        ["Msg_Delete_Confirm"] = "Вы уверены, что хотите удалить профиль «{0}»?",
        ["Msg_Delete_Title"] = "Удаление профиля",
        ["Msg_Hotkeys_Saved"] = "✓ Горячие клавиши обновлены",
        ["Msg_Bypass_On"] = "⚡ Bypass: ВКЛЮЧЕН (Оригинал)",
        ["Msg_Bypass_Off"] = "✓ Bypass: ВЫКЛЮЧЕН (Обработка активна)",
        ["Msg_Meme_On"] = "🎙️ Перегруз: ВКЛЮЧЕН",
        ["Msg_Meme_Off"] = "✓ Перегруз: ВЫКЛЮЧЕН",
        ["Msg_Mute_On"] = "🔇 Микрофон: ЗАГЛУШЕН",
        ["Msg_Mute_Off"] = "🎙️ Микрофон: АКТИВЕН",

        // Tray
        ["Tray_Open"] = "Открыть SimpleMicCompressor",
        ["Tray_Bypass"] = "Режим Bypass (Оригинал)",
        ["Tray_ToggleEngine"] = "Запустить / Остановить",
        ["Tray_Exit"] = "Закрыть приложение",
        ["Tray_Active_Balloon"] = "Программа активна в трее. Обработка звука включена.",
        ["Telemetry_Ms"] = "мс",

        // Updates
        ["Update_Available_Title"] = "Доступно обновление",
        ["Update_Available_Message"] = "Доступна новая версия SimpleMicCompressor ({0}).\n\nХотите перейти на страницу релизов на GitHub для скачивания?",

        // Uninstall
        ["Pref_Uninstall"] = "Полная деинсталляция",
        ["Pref_Uninstall_Tip"] = "Полное удаление настроек, автозапуска и исполняемого файла программы",
        ["Uninstall_Confirm_Title"] = "Полная деинсталляция",
        ["Uninstall_Confirm_Message"] = "Вы действительно хотите полностью удалить SimpleMicCompressor?\n\nБудут удалены:\n• Автозагрузка Windows (реестр)\n• Папка настроек и профилей (%APPDATA%\\SimpleMicCompressor)\n• Исполняемый файл приложения\n\nПриложение будет немедленно закрыто.",

        // Settings Modal
        ["Action_Settings"] = "Настройки",
        ["Modal_Settings_Title"] = "Настройки приложения",
        ["Settings_General"] = "ОСНОВНЫЕ ПАРАМЕТРЫ",
        ["Settings_Hotkeys"] = "ГОРЯЧИЕ КЛАВИШИ",
        ["Settings_DangerZone"] = "ЗОНА УДАЛЕНИЯ",
        ["FX_Bypass_Desc"] = "Прямой сигнал без обработки",
        ["FX_Mute_Desc"] = "Быстрое заглушение микрофона"
    };

    private static readonly Dictionary<string, string> StringsEn = new()
    {
        // Header
        ["App_Title"] = "SIMPLE MIC COMPRESSOR",
        ["Engine_Dsp"] = "WASAPI DSP",
        ["Engine_Active"] = "Active (On Air)",
        ["Engine_Stopped"] = "Stopped",
        ["Engine_Error"] = "Error",
        ["Telemetry_Latency"] = "LATENCY:",
        ["Header_Profile"] = "PROFILE",
        ["Action_Save"] = "Save",
        ["Action_New"] = "New",
        ["Action_Delete"] = "Delete",
        ["Action_Bypass"] = "Bypass",
        ["Action_ToTray"] = "To Tray",
        ["Action_Start"] = "Start",
        ["Action_Stop"] = "Stop",
        ["Action_Hotkeys"] = "Hotkeys",
        ["Action_Language"] = "Language",
        ["Badge_Muted"] = "MICROPHONE MUTED",

        // Presets
        ["Preset_Gaming"] = "🎮 Gaming & Discord",
        ["Preset_Default"] = "🎙️ Default / Balanced",
        ["Preset_Podcast"] = "📻 Radio / Podcast",
        ["Preset_Whisper"] = "🤫 Whisper / Quiet Night",
        ["Preset_AntiRage"] = "⚡ Aggressive / Anti-Rage",
        ["Preset_Meme"] = "🔊 Destroyed Mic (Meme)",
        ["Preset_Custom"] = "Custom",

        // Audio Routing
        ["Card_Routing"] = "AUDIO ROUTING",
        ["Routing_Refresh"] = "Refresh",
        ["Routing_Input"] = "Input:",
        ["Routing_Output"] = "Output:",
        ["Routing_Default"] = "Default",
        ["Routing_VirtualCable"] = "Virtual Cable",
        ["Routing_Monitor"] = "Monitoring",
        ["Routing_MonitorVolume"] = "Headphone monitoring volume",
        ["Cable_Active"] = "Virtual Cable active. In Discord select CABLE Output",
        ["Cable_NotFound"] = "Virtual Cable not detected (required for Discord)",
        ["Cable_Install"] = "Install Cable",
        ["Cable_Reinstall"] = "Reinstall",
        ["Cable_Installing"] = "Installing...",

        // Level Meters
        ["Card_Meters"] = "LEVEL METERS",
        ["Meters_Gate"] = "GATE:",
        ["Gate_Open"] = "OPEN",
        ["Gate_Closed"] = "CLOSED",
        ["Meters_Input"] = "Input (Microphone)",
        ["Meters_GR"] = "Gain Reduction (GR)",
        ["Meters_Output"] = "Output (On Air)",

        // Noise Gate
        ["Card_Gate"] = "NOISE GATE",
        ["Gate_Threshold"] = "Threshold",
        ["Gate_Threshold_Tip"] = "Volume level below which microphone is automatically muted",
        ["Gate_Release"] = "Release",
        ["Gate_Release_Tip"] = "Smooth gate closing time (preserves quiet word endings)",

        // Compressor
        ["Card_Compressor"] = "COMPRESSOR",
        ["Comp_Threshold"] = "Threshold",
        ["Comp_Threshold_Tip"] = "Volume above which compression begins",
        ["Comp_Ratio"] = "Ratio",
        ["Comp_Ratio_Tip"] = "Strength of loud sound compression",
        ["Comp_Makeup"] = "Makeup Gain",
        ["Comp_Makeup_Tip"] = "Overall volume compensation after compression",
        ["Comp_Attack"] = "Attack",
        ["Comp_Release"] = "Release",

        // Master & FX
        ["Card_Master"] = "MASTER & FX",
        ["FX_LowCut"] = "Low-Cut 80 Hz (Rumble Filter)",
        ["FX_LowCut_Tip"] = "Cuts low-frequency microphone rumble and desk vibrations",
        ["FX_Limiter"] = "Peak Limiter (-1.0 dB)",
        ["FX_Limiter_Sub"] = "Protection against clipping and distortion",
        ["FX_Meme"] = "Overdrive (Meme)",
        ["FX_Meme_Notice"] = "In Discord disable Krisp (otherwise sound is blocked)",
        ["Pref_AutoStart"] = "Start with Windows",
        ["Pref_MinimizeClose"] = "Minimize to tray on [X]",
        ["Btn_HotkeysConfig"] = "Configure Hotkeys",

        // Footer
        ["Footer_Engine"] = "SimpleMicCompressor • Low-Latency Audio Engine",
        ["Footer_Developer"] = "Developer: ",
        ["Footer_Output"] = "Output: ",

        // Modal New Profile
        ["Modal_NewProfile_Title"] = "New Profile",
        ["Modal_NewProfile_Desc"] = "Enter a name to save current compressor, gate, and filter settings:",
        ["Modal_NewProfile_Field"] = "PROFILE NAME",
        ["Modal_Cancel"] = "Cancel",
        ["Modal_Save"] = "Save",

        // Modal Hotkeys
        ["Modal_Hotkeys_Title"] = "Global Hotkeys",
        ["Modal_Hotkeys_Desc"] = "Control compressor directly from inside games without alt-tabbing:",
        ["Modal_Hotkeys_Enable"] = "Enable global hotkeys",
        ["Hotkeys_Bypass"] = "Bypass Mode (Original):",
        ["Hotkeys_Meme"] = "Overdrive (Meme Mode):",
        ["Hotkeys_Mute"] = "Mute Microphone:",
        ["Modal_Reset"] = "Reset to Defaults",
        ["Modal_Close"] = "Close",

        // Notifications & Tooltips
        ["Msg_Profile_Saved"] = "✓ Profile «{0}» saved successfully",
        ["Msg_Profile_Updated"] = "✓ Settings saved to profile «{0}»",
        ["Msg_Profile_Deleted"] = "Profile «{0}» deleted",
        ["Msg_Delete_Confirm"] = "Are you sure you want to delete profile «{0}»?",
        ["Msg_Delete_Title"] = "Delete Profile",
        ["Msg_Hotkeys_Saved"] = "✓ Hotkeys updated",
        ["Msg_Bypass_On"] = "⚡ Bypass: ENABLED (Raw mic)",
        ["Msg_Bypass_Off"] = "✓ Bypass: DISABLED (Processing active)",
        ["Msg_Meme_On"] = "🎙️ Overdrive: ENABLED",
        ["Msg_Meme_Off"] = "✓ Overdrive: DISABLED",
        ["Msg_Mute_On"] = "🔇 Microphone: MUTED",
        ["Msg_Mute_Off"] = "🎙️ Microphone: ACTIVE",

        // Tray
        ["Tray_Open"] = "Open SimpleMicCompressor",
        ["Tray_Bypass"] = "Bypass Mode (Original)",
        ["Tray_ToggleEngine"] = "Start / Stop",
        ["Tray_Exit"] = "Exit Application",
        ["Tray_Active_Balloon"] = "Application active in tray. Audio processing enabled.",
        ["Telemetry_Ms"] = "ms",

        // Updates
        ["Update_Available_Title"] = "Update Available",
        ["Update_Available_Message"] = "A new version of SimpleMicCompressor ({0}) is available.\n\nWould you like to open the GitHub releases page to download it?",

        // Uninstall
        ["Pref_Uninstall"] = "Full Uninstallation",
        ["Pref_Uninstall_Tip"] = "Completely remove settings, autostart, and the executable",
        ["Uninstall_Confirm_Title"] = "Full Uninstallation",
        ["Uninstall_Confirm_Message"] = "Are you sure you want to completely uninstall SimpleMicCompressor?\n\nThe following will be removed:\n• Windows autostart (registry)\n• Settings and profiles folder (%APPDATA%\\SimpleMicCompressor)\n• The application executable\n\nThe application will close immediately.",

        // Settings Modal
        ["Action_Settings"] = "Settings",
        ["Modal_Settings_Title"] = "Application Settings",
        ["Settings_General"] = "GENERAL PREFERENCES",
        ["Settings_Hotkeys"] = "GLOBAL HOTKEYS",
        ["Settings_DangerZone"] = "DANGER ZONE",
        ["FX_Bypass_Desc"] = "Direct dry microphone passthrough",
        ["FX_Mute_Desc"] = "Quick microphone mute"
    };

    private static readonly Dictionary<string, string> StringsDe = new()
    {
        // Header
        ["App_Title"] = "SIMPLE MIC COMPRESSOR",
        ["Engine_Dsp"] = "WASAPI DSP",
        ["Engine_Active"] = "Aktiv (On Air)",
        ["Engine_Stopped"] = "Gestoppt",
        ["Engine_Error"] = "Fehler",
        ["Telemetry_Latency"] = "LATENCY:",
        ["Header_Profile"] = "PROFIL",
        ["Action_Save"] = "Speichern",
        ["Action_New"] = "Neu",
        ["Action_Delete"] = "Löschen",
        ["Action_Bypass"] = "Bypass",
        ["Action_ToTray"] = "Ins Tray",
        ["Action_Start"] = "Starten",
        ["Action_Stop"] = "Stoppen",
        ["Action_Hotkeys"] = "Hotkeys",
        ["Action_Language"] = "Sprache",
        ["Badge_Muted"] = "MIKROFON STUMMGESCHALTET",

        // Presets
        ["Preset_Gaming"] = "🎮 Gaming & Discord",
        ["Preset_Default"] = "🎙️ Standard / Ausgewogen",
        ["Preset_Podcast"] = "📻 Radio / Podcast",
        ["Preset_Whisper"] = "🤫 Flüstern / Leise Nacht",
        ["Preset_AntiRage"] = "⚡ Aggressiv / Anti-Rage",
        ["Preset_Meme"] = "🔊 Zerstörtes Mikrofon (Meme)",
        ["Preset_Custom"] = "Benutzerdefiniert",

        // Audio Routing
        ["Card_Routing"] = "AUDIO-ROUTING",
        ["Routing_Refresh"] = "Aktualisieren",
        ["Routing_Input"] = "Eingang:",
        ["Routing_Output"] = "Ausgang:",
        ["Routing_Default"] = "Standard",
        ["Routing_VirtualCable"] = "Virtuelles Kabel",
        ["Routing_Monitor"] = "Monitoring",
        ["Routing_MonitorVolume"] = "Kopfhörer-Monitoring-Lautstärke",
        ["Cable_Active"] = "Virtuelles Kabel aktiv. In Discord CABLE Output wählen",
        ["Cable_NotFound"] = "Kein virtuelles Kabel gefunden (für Discord benötigt)",
        ["Cable_Install"] = "Kabel installieren",
        ["Cable_Reinstall"] = "Neu installieren",
        ["Cable_Installing"] = "Wird installiert...",

        // Level Meters
        ["Card_Meters"] = "PEGELANZEIGEN",
        ["Meters_Gate"] = "GATE:",
        ["Gate_Open"] = "OFFEN",
        ["Gate_Closed"] = "ZU",
        ["Meters_Input"] = "Eingang (Mikrofon)",
        ["Meters_GR"] = "Kompressor-Dämpfung (GR)",
        ["Meters_Output"] = "Ausgang (On Air)",

        // Noise Gate
        ["Card_Gate"] = "NOISE GATE",
        ["Gate_Threshold"] = "Schwellenwert",
        ["Gate_Threshold_Tip"] = "Lautstärke, unter der das Mikrofon stummgeschaltet wird",
        ["Gate_Release"] = "Abklingzeit",
        ["Gate_Release_Tip"] = "Sanfte Schließzeit des Gates",

        // Compressor
        ["Card_Compressor"] = "KOMPRESSOR",
        ["Comp_Threshold"] = "Schwellenwert",
        ["Comp_Threshold_Tip"] = "Lautstärke, ab der die Kompression beginnt",
        ["Comp_Ratio"] = "Ratio",
        ["Comp_Ratio_Tip"] = "Stärke der Dämpfung lauter Geräusche",
        ["Comp_Makeup"] = "Aufholverstärkung",
        ["Comp_Makeup_Tip"] = "Lautstärkeausgleich nach Kompression",
        ["Comp_Attack"] = "Attack",
        ["Comp_Release"] = "Release",

        // Master & FX
        ["Card_Master"] = "MASTER & FX",
        ["FX_LowCut"] = "Low-Cut 80 Hz (Trittschall)",
        ["FX_LowCut_Tip"] = "Entfernt tiefes Brummen und Tischerschütterungen",
        ["FX_Limiter"] = "Peak-Limiter (-1.0 dB)",
        ["FX_Limiter_Sub"] = "Schutz vor Übersteuerung und Verzerrung",
        ["FX_Meme"] = "Verzerrung (Meme)",
        ["FX_Meme_Notice"] = "In Discord Krisp deaktivieren (sonst wird Ton stumm)",
        ["Pref_AutoStart"] = "Mit Windows starten",
        ["Pref_MinimizeClose"] = "Auf [X] ins Tray minimieren",
        ["Btn_HotkeysConfig"] = "Hotkeys anpassen",

        // Footer
        ["Footer_Engine"] = "SimpleMicCompressor • Low-Latency Audio Engine",
        ["Footer_Developer"] = "Entwickler: ",
        ["Footer_Output"] = "Ausgang: ",

        // Modal New Profile
        ["Modal_NewProfile_Title"] = "Neues Profil",
        ["Modal_NewProfile_Desc"] = "Geben Sie einen Namen ein, um die aktuellen Einstellungen zu speichern:",
        ["Modal_NewProfile_Field"] = "PROFILNAME",
        ["Modal_Cancel"] = "Abbrechen",
        ["Modal_Save"] = "Speichern",

        // Modal Hotkeys
        ["Modal_Hotkeys_Title"] = "Globale Hotkeys",
        ["Modal_Hotkeys_Desc"] = "Steuern Sie den Kompressor direkt im Spiel ohne Tab-Wechsel:",
        ["Modal_Hotkeys_Enable"] = "Globale Hotkeys aktivieren",
        ["Hotkeys_Bypass"] = "Bypass-Modus (Original):",
        ["Hotkeys_Meme"] = "Verzerrung (Meme-Modus):",
        ["Hotkeys_Mute"] = "Mikrofon stummschalten:",
        ["Modal_Reset"] = "Standard wiederherstellen",
        ["Modal_Close"] = "Schließen",

        // Notifications & Tooltips
        ["Msg_Profile_Saved"] = "✓ Profil «{0}» erfolgreich gespeichert",
        ["Msg_Profile_Updated"] = "✓ Einstellungen im Profil «{0}» gespeichert",
        ["Msg_Profile_Deleted"] = "Profil «{0}» gelöscht",
        ["Msg_Delete_Confirm"] = "Möchten Sie das Profil «{0}» wirklich löschen?",
        ["Msg_Delete_Title"] = "Profil löschen",
        ["Msg_Hotkeys_Saved"] = "✓ Hotkeys aktualisiert",
        ["Msg_Bypass_On"] = "⚡ Bypass: AKTIVIERT",
        ["Msg_Bypass_Off"] = "✓ Bypass: DEAKTIVIERT",
        ["Msg_Meme_On"] = "🎙️ Verzerrung: AKTIVIERT",
        ["Msg_Meme_Off"] = "✓ Verzerrung: DEAKTIVIERT",
        ["Msg_Mute_On"] = "🔇 Mikrofon: STUMMGESCHALTET",
        ["Msg_Mute_Off"] = "🎙️ Mikrofon: AKTIV",

        // Tray
        ["Tray_Open"] = "SimpleMicCompressor öffnen",
        ["Tray_Bypass"] = "Bypass-Modus (Original)",
        ["Tray_ToggleEngine"] = "Starten / Stoppen",
        ["Tray_Exit"] = "Beenden",
        ["Tray_Active_Balloon"] = "Anwendung im Infobereich aktiv. Audioverarbeitung aktiviert.",
        ["Telemetry_Ms"] = "ms",

        // Updates
        ["Update_Available_Title"] = "Update verfügbar",
        ["Update_Available_Message"] = "Eine neue Version von SimpleMicCompressor ({0}) ist verfügbar.\n\nMöchten Sie die GitHub-Releases-Seite öffnen, um sie herunterzuladen?",

        // Uninstall
        ["Pref_Uninstall"] = "Vollständige Deinstallation",
        ["Pref_Uninstall_Tip"] = "Einstellungen, Autostart und Anwendungsdatei vollständig entfernen",
        ["Uninstall_Confirm_Title"] = "Vollständige Deinstallation",
        ["Uninstall_Confirm_Message"] = "Möchten Sie SimpleMicCompressor wirklich vollständig deinstallieren?\n\nFolgendes wird entfernt:\n• Windows-Autostart (Registrierung)\n• Einstellungs- und Profilordner (%APPDATA%\\SimpleMicCompressor)\n• Die Anwendungsdatei\n\nDie Anwendung wird sofort beendet.",

        // Settings Modal
        ["Action_Settings"] = "Einstellungen",
        ["Modal_Settings_Title"] = "Programmeinstellungen",
        ["Settings_General"] = "ALLGEMEINE EINSTELLUNGEN",
        ["Settings_Hotkeys"] = "GLOBALE HOTKEYS",
        ["Settings_DangerZone"] = "GEFAHRENZONE",
        ["FX_Bypass_Desc"] = "Direktes Mikrofon-Signal",
        ["FX_Mute_Desc"] = "Mikrofon schnell stummschalten"
    };

    private static readonly Dictionary<string, string> StringsEs = new()
    {
        // Header
        ["App_Title"] = "SIMPLE MIC COMPRESSOR",
        ["Engine_Dsp"] = "WASAPI DSP",
        ["Engine_Active"] = "Activo (Al aire)",
        ["Engine_Stopped"] = "Detenido",
        ["Engine_Error"] = "Error",
        ["Telemetry_Latency"] = "LATENCY:",
        ["Header_Profile"] = "PERFIL",
        ["Action_Save"] = "Guardar",
        ["Action_New"] = "Nuevo",
        ["Action_Delete"] = "Eliminar",
        ["Action_Bypass"] = "Bypass",
        ["Action_ToTray"] = "A la bandeja",
        ["Action_Start"] = "Iniciar",
        ["Action_Stop"] = "Detener",
        ["Action_Hotkeys"] = "Teclas",
        ["Action_Language"] = "Idioma",
        ["Badge_Muted"] = "MICRÓFONO SILENCIADO",

        // Presets
        ["Preset_Gaming"] = "🎮 Gaming y Discord",
        ["Preset_Default"] = "🎙️ Estándar / Equilibrado",
        ["Preset_Podcast"] = "📻 Radio / Pódcast",
        ["Preset_Whisper"] = "🤫 Susurro / Noche tranquila",
        ["Preset_AntiRage"] = "⚡ Agresivo / Anti-Rage",
        ["Preset_Meme"] = "🔊 Micrófono destruido (Meme)",
        ["Preset_Custom"] = "Personalizado",

        // Audio Routing
        ["Card_Routing"] = "ENRUTAMIENTO DE AUDIO",
        ["Routing_Refresh"] = "Actualizar",
        ["Routing_Input"] = "Entrada:",
        ["Routing_Output"] = "Salida:",
        ["Routing_Default"] = "Predeterminado",
        ["Routing_VirtualCable"] = "Cable Virtual",
        ["Routing_Monitor"] = "Monitorización",
        ["Routing_MonitorVolume"] = "Volumen de monitorización",
        ["Cable_Active"] = "Cable virtual activo. En Discord elija CABLE Output",
        ["Cable_NotFound"] = "Cable virtual no detectado (necesario para Discord)",
        ["Cable_Install"] = "Instalar cable",
        ["Cable_Reinstall"] = "Reinstalar",
        ["Cable_Installing"] = "Instalando...",

        // Level Meters
        ["Card_Meters"] = "INDICADORES DE NIVEL",
        ["Meters_Gate"] = "GATE:",
        ["Gate_Open"] = "ABIERTO",
        ["Gate_Closed"] = "CERRADO",
        ["Meters_Input"] = "Entrada (Micrófono)",
        ["Meters_GR"] = "Reducción de ganancia (GR)",
        ["Meters_Output"] = "Salida (Al aire)",

        // Noise Gate
        ["Card_Gate"] = "NOISE GATE",
        ["Gate_Threshold"] = "Umbral",
        ["Gate_Threshold_Tip"] = "Nivel de volumen por debajo del cual el micrófono se silencia",
        ["Gate_Release"] = "Liberación",
        ["Gate_Release_Tip"] = "Tiempo de cierre suave del gate",

        // Compressor
        ["Card_Compressor"] = "COMPRESOR",
        ["Comp_Threshold"] = "Umbral",
        ["Comp_Threshold_Tip"] = "Volumen por encima del cual comienza la compresión",
        ["Comp_Ratio"] = "Compresión",
        ["Comp_Ratio_Tip"] = "Fuerza de reducción de sonidos fuertes",
        ["Comp_Makeup"] = "Ganancia de compensación",
        ["Comp_Makeup_Tip"] = "Compensación de volumen tras compresión",
        ["Comp_Attack"] = "Ataque",
        ["Comp_Release"] = "Liberación",

        // Master & FX
        ["Card_Master"] = "MASTER & FX",
        ["FX_LowCut"] = "Low-Cut 80 Hz (Filtro de graves)",
        ["FX_LowCut_Tip"] = "Corta ruidos graves y vibraciones del escritorio",
        ["FX_Limiter"] = "Limitador de picos (-1.0 dB)",
        ["FX_Limiter_Sub"] = "Protección contra saturación y distorsión",
        ["FX_Meme"] = "Distorsión (Meme)",
        ["FX_Meme_Notice"] = "En Discord desactive Krisp (de lo contrario se silenciará)",
        ["Pref_AutoStart"] = "Iniciar con Windows",
        ["Pref_MinimizeClose"] = "Minimizar a la bandeja al cerrar [X]",
        ["Btn_HotkeysConfig"] = "Configurar teclas",

        // Footer
        ["Footer_Engine"] = "SimpleMicCompressor • Low-Latency Audio Engine",
        ["Footer_Developer"] = "Desarrollador: ",
        ["Footer_Output"] = "Salida: ",

        // Modal New Profile
        ["Modal_NewProfile_Title"] = "Nuevo perfil",
        ["Modal_NewProfile_Desc"] = "Introduzca un nombre para guardar la configuración actual:",
        ["Modal_NewProfile_Field"] = "NOMBRE DEL PERFIL",
        ["Modal_Cancel"] = "Cancelar",
        ["Modal_Save"] = "Guardar",

        // Modal Hotkeys
        ["Modal_Hotkeys_Title"] = "Teclas globales",
        ["Modal_Hotkeys_Desc"] = "Controle el compresor directamente en el juego sin cambiar de ventana:",
        ["Modal_Hotkeys_Enable"] = "Habilitar teclas globales",
        ["Hotkeys_Bypass"] = "Modo Bypass (Original):",
        ["Hotkeys_Meme"] = "Distorsión (Modo Meme):",
        ["Hotkeys_Mute"] = "Silenciar micrófono:",
        ["Modal_Reset"] = "Restablecer por defecto",
        ["Modal_Close"] = "Cerrar",

        // Notifications & Tooltips
        ["Msg_Profile_Saved"] = "✓ Perfil «{0}» guardado con éxito",
        ["Msg_Profile_Updated"] = "✓ Configuración guardada en el perfil «{0}»",
        ["Msg_Profile_Deleted"] = "Perfil «{0}» eliminado",
        ["Msg_Delete_Confirm"] = "¿Seguro que desea eliminar el perfil «{0}»?",
        ["Msg_Delete_Title"] = "Eliminar perfil",
        ["Msg_Hotkeys_Saved"] = "✓ Teclas actualizadas",
        ["Msg_Bypass_On"] = "⚡ Bypass: ACTIVADO",
        ["Msg_Bypass_Off"] = "✓ Bypass: DESACTIVADO",
        ["Msg_Meme_On"] = "🎙️ Distorsión: ACTIVADA",
        ["Msg_Meme_Off"] = "✓ Distorsión: DESACTIVADA",
        ["Msg_Mute_On"] = "🔇 Micrófono: SILENCIADO",
        ["Msg_Mute_Off"] = "🎙️ Micrófono: ACTIVO",

        // Tray
        ["Tray_Open"] = "Abrir SimpleMicCompressor",
        ["Tray_Bypass"] = "Modo Bypass (Original)",
        ["Tray_ToggleEngine"] = "Iniciar / Detener",
        ["Tray_Exit"] = "Salir",
        ["Tray_Active_Balloon"] = "Aplicación activa en la bandeja. Procesamiento de audio activado.",
        ["Telemetry_Ms"] = "ms",

        // Updates
        ["Update_Available_Title"] = "Actualización disponible",
        ["Update_Available_Message"] = "Una nueva versión de SimpleMicCompressor ({0}) está disponible.\n\n¿Desea abrir la página de lanzamientos de GitHub para descargarla?",

        // Uninstall
        ["Pref_Uninstall"] = "Desinstalación completa",
        ["Pref_Uninstall_Tip"] = "Eliminar por completo los ajustes, el inicio automático y el ejecutable",
        ["Uninstall_Confirm_Title"] = "Desinstalación completa",
        ["Uninstall_Confirm_Message"] = "¿Realmente desea desinstalar SimpleMicCompressor por completo?\n\nSe eliminará lo siguiente:\n• Inicio automático de Windows (registro)\n• Carpeta de ajustes y perfiles (%APPDATA%\\SimpleMicCompressor)\n• Archivo ejecutable de la aplicación\n\nLa aplicación se cerrará inmediatamente.",

        // Settings Modal
        ["Action_Settings"] = "Ajustes",
        ["Modal_Settings_Title"] = "Ajustes de la aplicación",
        ["Settings_General"] = "PREFERENCIAS GENERALES",
        ["Settings_Hotkeys"] = "TECLAS DE ACCESO RÁPIDO",
        ["Settings_DangerZone"] = "ZONA DE PELIGRO",
        ["FX_Bypass_Desc"] = "Señal de micrófono directa",
        ["FX_Mute_Desc"] = "Silenciar micrófono rápidamente"
    };

    #endregion
}
