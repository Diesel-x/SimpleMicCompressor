using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using MicCompressor.Audio;
using MicCompressor.Audio.Dsp;
using MicCompressor.Localization;
using MicCompressor.Models;
using MicCompressor.Utils;

namespace MicCompressor.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioEngine _engine;
    private readonly AudioSettings _settings;
    private readonly DispatcherTimer _meterTimer;

    public LocalizationService Loc => LocalizationService.Instance;
    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.SupportedLanguages;

    public LanguageOption SelectedLanguage
    {
        get => Loc.CurrentLanguageOption;
        set
        {
            if (value != null && value.Code != Loc.CurrentLanguage)
            {
                Loc.SetLanguage(value.Code);
                _settings.Language = value.Code;
                _settings.Save();
                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(SaveButtonToolTip));
                OnPropertyChanged(nameof(DeleteToolTip));
                UpdateEngineStatusDisplay();
                RefreshPresetsList();
            }
        }
    }

    private DeviceInfo? _selectedInputDevice;
    private DeviceInfo? _selectedOutputDevice;
    private DeviceInfo? _selectedMonitorDevice;

    private float _inputPeakDb = -60f;
    private float _outputPeakDb = -60f;
    private float _gainReductionDb = 0f;
    private bool _isGateOpen = false;

    private string _engineStatusText = "Остановлен";
    private string _engineStatusColor = "#8e9297"; // Gray
    private string _startStopButtonText = "Запустить";
    private bool _isRunning = false;
    private bool _isUpdatingPreset = false;

    public AudioSettings Settings => _settings;

    public ObservableCollection<DeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<DeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<DeviceInfo> MonitorDevices { get; } = new();

    public ObservableCollection<string> Presets { get; } = new();

    public RelayCommand ToggleEngineCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand OpenVbCableWebsiteCommand { get; }
    public RelayCommand InstallVbCableCommand { get; }
    public RelayCommand ReinstallVbCableCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand OpenNewProfileDialogCommand { get; }
    public RelayCommand ConfirmSaveProfileCommand { get; }
    public RelayCommand CancelSaveProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand OpenHotkeysDialogCommand { get; }
    public RelayCommand CloseHotkeysDialogCommand { get; }
    public RelayCommand ResetHotkeysCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public Action? RequestMinimizeToTray { get; set; }
    public Action? RequestUpdateHotkeys { get; set; }

    public IReadOnlyList<string> AvailableHotkeyKeys => HotkeySetting.AvailableKeys;

    private string? _installerStatus;
    private bool _isInstalling = false;
    private string _profileNameInput = "";
    private bool _isCustomProfileSelected = false;
    private bool _isSaveProfileDialogVisible = false;
    private bool _isHotkeysDialogVisible = false;
    private bool _hasUnsavedProfileChanges = false;
    private string _statusNotification = "";
    private DispatcherTimer? _statusNotificationTimer;

    public string? InstallerStatus
    {
        get => _installerStatus;
        set => SetField(ref _installerStatus, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetField(ref _isInstalling, value);
    }

    public string ProfileNameInput
    {
        get => _profileNameInput;
        set => SetField(ref _profileNameInput, value);
    }

    public bool IsSaveProfileDialogVisible
    {
        get => _isSaveProfileDialogVisible;
        set => SetField(ref _isSaveProfileDialogVisible, value);
    }

    public bool IsHotkeysDialogVisible
    {
        get => _isHotkeysDialogVisible;
        set => SetField(ref _isHotkeysDialogVisible, value);
    }

    public bool HotkeysEnabled
    {
        get => _settings.HotkeysEnabled;
        set
        {
            if (_settings.HotkeysEnabled != value)
            {
                _settings.HotkeysEnabled = value;
                _settings.Save();
                OnPropertyChanged();
                RequestUpdateHotkeys?.Invoke();
            }
        }
    }

    public HotkeySetting HotkeyBypass => _settings.HotkeyBypass;
    public HotkeySetting HotkeyMeme => _settings.HotkeyMeme;
    public HotkeySetting HotkeyMute => _settings.HotkeyMute;

    public bool IsMuted
    {
        get => _settings.IsMuted;
        set
        {
            if (_settings.IsMuted != value)
            {
                _settings.IsMuted = value;
                _settings.Save();
                UpdateEngineSettings();
                OnPropertyChanged();
                ShowStatusNotification(value ? Loc["Msg_Mute_On"] : Loc["Msg_Mute_Off"]);
            }
        }
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void HandleHotkey(int id)
    {
        switch (id)
        {
            case GlobalHotkeyService.HotkeyIdBypass:
                BypassAll = !BypassAll;
                ShowStatusNotification(BypassAll ? Loc["Msg_Bypass_On"] : Loc["Msg_Bypass_Off"]);
                break;
            case GlobalHotkeyService.HotkeyIdMeme:
                DestroyedMicMemeMode = !DestroyedMicMemeMode;
                ShowStatusNotification(DestroyedMicMemeMode ? Loc["Msg_Meme_On"] : Loc["Msg_Meme_Off"]);
                break;
            case GlobalHotkeyService.HotkeyIdMute:
                ToggleMute();
                break;
        }
    }

    public bool IsCustomProfileSelected
    {
        get => _isCustomProfileSelected;
        private set => SetField(ref _isCustomProfileSelected, value);
    }

    public bool HasUnsavedProfileChanges
    {
        get => _hasUnsavedProfileChanges;
        set
        {
            if (SetField(ref _hasUnsavedProfileChanges, value))
            {
                OnPropertyChanged(nameof(SaveButtonToolTip));
            }
        }
    }

    public string SaveButtonToolTip
    {
        get
        {
            if (IsCustomProfileSelected)
            {
                string name = NormalizePresetName(_settings.SelectedPresetName);
                return _hasUnsavedProfileChanges
                    ? Loc.Get("Msg_Profile_Updated", name)
                    : Loc.Get("Msg_Profile_Saved", name);
            }
            return Loc["Action_Save"];
        }
    }

    public string DeleteToolTip
    {
        get
        {
            string name = NormalizePresetName(_settings.SelectedPresetName);
            return Loc.Get("Msg_Delete_Confirm", name);
        }
    }

    public string StatusNotification
    {
        get => _statusNotification;
        private set => SetField(ref _statusNotification, value);
    }

    public bool HasVirtualCable => OutputDevices.Any(d => d.IsVirtualCable);

    public MainViewModel()
    {
        _settings = AudioSettings.Load();

        if (!string.IsNullOrEmpty(_settings.Language))
        {
            Loc.SetLanguage(_settings.Language);
        }

        _engine = new AudioEngine(_settings);
        _engine.StateChanged += OnEngineStateChanged;

        ToggleEngineCommand = new RelayCommand(ToggleEngine);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        OpenVbCableWebsiteCommand = new RelayCommand(OpenVbCableWebsite);
        InstallVbCableCommand = new RelayCommand(InstallVbCable);
        ReinstallVbCableCommand = new RelayCommand(ReinstallVbCable);
        SaveProfileCommand = new RelayCommand(SaveOrOpenDialog);
        OpenNewProfileDialogCommand = new RelayCommand(OpenNewProfileDialog);
        ConfirmSaveProfileCommand = new RelayCommand(ConfirmSaveProfile);
        CancelSaveProfileCommand = new RelayCommand(CancelSaveProfile);
        DeleteProfileCommand = new RelayCommand(DeleteCurrentProfile);

        OpenHotkeysDialogCommand = new RelayCommand(() => IsHotkeysDialogVisible = true);
        CloseHotkeysDialogCommand = new RelayCommand(() =>
        {
            IsHotkeysDialogVisible = false;
            _settings.Save();
            RequestUpdateHotkeys?.Invoke();
            ShowStatusNotification(Loc["Msg_Hotkeys_Saved"]);
        });
        ResetHotkeysCommand = new RelayCommand(() =>
        {
            _settings.HotkeyBypass.Enabled = true;
            _settings.HotkeyBypass.Ctrl = true;
            _settings.HotkeyBypass.Shift = true;
            _settings.HotkeyBypass.Alt = false;
            _settings.HotkeyBypass.Key = "B";

            _settings.HotkeyMeme.Enabled = true;
            _settings.HotkeyMeme.Ctrl = true;
            _settings.HotkeyMeme.Shift = true;
            _settings.HotkeyMeme.Alt = false;
            _settings.HotkeyMeme.Key = "M";

            _settings.HotkeyMute.Enabled = true;
            _settings.HotkeyMute.Ctrl = true;
            _settings.HotkeyMute.Shift = true;
            _settings.HotkeyMute.Alt = false;
            _settings.HotkeyMute.Key = "X";

            _settings.HotkeysEnabled = true;
            _settings.Save();

            OnPropertyChanged(nameof(HotkeyBypass));
            OnPropertyChanged(nameof(HotkeyMeme));
            OnPropertyChanged(nameof(HotkeyMute));
            OnPropertyChanged(nameof(HotkeysEnabled));

            RequestUpdateHotkeys?.Invoke();
            ShowStatusNotification(Loc["Msg_Hotkeys_Saved"]);
        });

        ToggleMuteCommand = new RelayCommand(ToggleMute);

        UpdateEngineStatusDisplay();
        RefreshPresetsList();
        RefreshDevices();

        // 30 FPS UI timer for meters
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _meterTimer.Tick += OnMeterTick;
        _meterTimer.Start();

        if (_settings.AutoStartEngine)
        {
            StartEngine();
        }
    }

    #region Device Management

    public void RefreshDevices()
    {
        InputDevices.Clear();
        foreach (var dev in AudioDeviceService.GetCaptureDevices())
        {
            InputDevices.Add(dev);
        }

        OutputDevices.Clear();
        MonitorDevices.Clear();
        foreach (var dev in AudioDeviceService.GetRenderDevices())
        {
            OutputDevices.Add(dev);
            MonitorDevices.Add(dev);
        }

        // Restore selected input
        _selectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == _settings.InputDeviceId)
                               ?? InputDevices.FirstOrDefault(d => d.IsDefault)
                               ?? InputDevices.FirstOrDefault();
        _settings.InputDeviceId = _selectedInputDevice?.Id;
        OnPropertyChanged(nameof(SelectedInputDevice));

        // Restore selected output (prefer virtual cable if available)
        _selectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == _settings.OutputDeviceId)
                                ?? OutputDevices.FirstOrDefault(d => d.IsVirtualCable)
                                ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
                                ?? OutputDevices.FirstOrDefault();
        _settings.OutputDeviceId = _selectedOutputDevice?.Id;
        OnPropertyChanged(nameof(SelectedOutputDevice));

        // Restore selected monitor
        _selectedMonitorDevice = MonitorDevices.FirstOrDefault(d => d.Id == _settings.MonitorDeviceId)
                                 ?? MonitorDevices.FirstOrDefault(d => !d.IsVirtualCable && d.IsDefault)
                                 ?? MonitorDevices.FirstOrDefault();
        _settings.MonitorDeviceId = _selectedMonitorDevice?.Id;
        OnPropertyChanged(nameof(SelectedMonitorDevice));

        OnPropertyChanged(nameof(HasVirtualCable));
        _settings.Save();
        UpdateEngineSettings();
    }

    public DeviceInfo? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set
        {
            if (SetField(ref _selectedInputDevice, value))
            {
                _settings.InputDeviceId = value?.Id;
                _settings.Save();
                if (_isRunning) _engine.Restart();
            }
        }
    }

    public DeviceInfo? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (SetField(ref _selectedOutputDevice, value))
            {
                _settings.OutputDeviceId = value?.Id;
                _settings.Save();
                if (_isRunning) _engine.Restart();
            }
        }
    }

    public DeviceInfo? SelectedMonitorDevice
    {
        get => _selectedMonitorDevice;
        set
        {
            if (SetField(ref _selectedMonitorDevice, value))
            {
                _settings.MonitorDeviceId = value?.Id;
                _settings.Save();
                UpdateEngineSettings();
            }
        }
    }

    private async void InstallVbCable()
    {
        if (_isInstalling) return;
        IsInstalling = true;
        InstallerStatus = "Подготовка к установке...";

        var (success, message) = await VbCableInstallerService.InstallOrReinstallAsync(false, msg =>
        {
            App.Current?.Dispatcher.Invoke(() => InstallerStatus = msg);
        });

        IsInstalling = false;
        InstallerStatus = message;

        RefreshDevices();
        var cable = OutputDevices.FirstOrDefault(d => d.IsVirtualCable);
        if (cable != null)
        {
            SelectedOutputDevice = cable;
        }
    }

    private async void ReinstallVbCable()
    {
        if (_isInstalling) return;
        IsInstalling = true;
        InstallerStatus = "Подготовка к переустановке...";

        var (success, message) = await VbCableInstallerService.InstallOrReinstallAsync(true, msg =>
        {
            App.Current?.Dispatcher.Invoke(() => InstallerStatus = msg);
        });

        IsInstalling = false;
        InstallerStatus = message;

        RefreshDevices();
        var cable = OutputDevices.FirstOrDefault(d => d.IsVirtualCable);
        if (cable != null)
        {
            SelectedOutputDevice = cable;
        }
    }

    private void OpenVbCableWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://vb-audio.com/Cable/",
                UseShellExecute = true
            });
        }
        catch { }
    }

    #endregion

    #region Engine State

    public string LatencyDisplay => IsRunning ? $"{_engine.EstimatedLatencyMs} мс" : "-- мс";

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public string EngineStatusText
    {
        get => _engineStatusText;
        private set => SetField(ref _engineStatusText, value);
    }

    public string EngineStatusColor
    {
        get => _engineStatusColor;
        private set => SetField(ref _engineStatusColor, value);
    }

    public string StartStopButtonText
    {
        get => _startStopButtonText;
        private set => SetField(ref _startStopButtonText, value);
    }

    public void ToggleEngine()
    {
        if (_isRunning)
        {
            _engine.Stop();
        }
        else
        {
            StartEngine();
        }
    }

    public void StartEngine()
    {
        _engine.Start();
    }

    public void StopEngine()
    {
        _engine.Stop();
    }

    public void UpdateEngineStatusDisplay()
    {
        switch (_engine.State)
        {
            case EngineState.Running:
                IsRunning = true;
                EngineStatusText = Loc["Engine_Active"];
                EngineStatusColor = "#57f287"; // Discord Green
                StartStopButtonText = Loc["Action_Stop"];
                break;
            case EngineState.Starting:
                EngineStatusText = "...";
                EngineStatusColor = "#fee75c"; // Amber
                break;
            case EngineState.Error:
                IsRunning = false;
                EngineStatusText = $"{Loc["Engine_Error"]}: {_engine.LastErrorMessage ?? "Error"}";
                EngineStatusColor = "#ed4245"; // Red
                StartStopButtonText = Loc["Action_Start"];
                break;
            case EngineState.Stopped:
            default:
                IsRunning = false;
                EngineStatusText = Loc["Engine_Stopped"];
                EngineStatusColor = "#8e9297"; // Muted Gray
                StartStopButtonText = Loc["Action_Start"];
                break;
        }
        OnPropertyChanged(nameof(LatencyDisplay));
    }

    private void OnEngineStateChanged(EngineState state, string? error)
    {
        App.Current?.Dispatcher.Invoke(UpdateEngineStatusDisplay);
    }

    #endregion

    #region Presets & Custom Profiles

    public void RefreshPresetsList()
    {
        Presets.Clear();
        Presets.Add("Gaming & Discord");
        Presets.Add("Default / Balanced");
        Presets.Add("Radio / Podcast");
        Presets.Add("Whisper / Quiet Night");
        Presets.Add("Aggressive / Anti-Rage");
        Presets.Add("Уничтоженный микрофон (Meme)");

        string prefix = Loc["Header_Profile"] + ": ";
        foreach (var p in _settings.CustomProfiles)
        {
            Presets.Add($"{prefix}{p.Name}");
        }

        Presets.Add("Пользовательский (Custom)");

        string current = NormalizePresetName(_settings.SelectedPresetName);
        if (_settings.CustomProfiles.Any(p => p.Name.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.SelectedPresetName = $"{prefix}{current}";
        }

        OnPropertyChanged(nameof(SelectedPreset));
        CheckCustomProfileSelected();
    }

    public string SelectedPreset
    {
        get => _settings.SelectedPresetName;
        set
        {
            if (_settings.SelectedPresetName != value && value != null)
            {
                _isUpdatingPreset = true;
                string cleanName = NormalizePresetName(value);
                _settings.ApplyPreset(cleanName);
                _settings.SelectedPresetName = value;
                _settings.Save();
                UpdateEngineSettings();
                NotifyAllDspPropertiesChanged();
                _isUpdatingPreset = false;
                HasUnsavedProfileChanges = false;
                OnPropertyChanged();
                CheckCustomProfileSelected();
            }
        }
    }

    private static string NormalizePresetName(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return "";
        string[] prefixes = ["Профиль: ", "Profile: ", "Profil: ", "Perfil: ", "[Профиль] ", "[Profile] ", "⭐ ", "🎙️ "];
        foreach (var p in prefixes)
        {
            if (preset.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return preset[p.Length..].Trim();
        }
        return preset;
    }

    private void CheckCustomProfileSelected()
    {
        string current = NormalizePresetName(_settings.SelectedPresetName);
        IsCustomProfileSelected = _settings.CustomProfiles.Any(p => p.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(DeleteToolTip));
        OnPropertyChanged(nameof(SaveButtonToolTip));
    }

    private void SaveOrOpenDialog()
    {
        if (IsCustomProfileSelected)
        {
            string name = NormalizePresetName(_settings.SelectedPresetName);
            _settings.SaveCustomProfile(name);
            HasUnsavedProfileChanges = false;
            ShowStatusNotification(Loc.Get("Msg_Profile_Updated", name));
        }
        else
        {
            OpenNewProfileDialog();
        }
    }

    private void OpenNewProfileDialog()
    {
        ProfileNameInput = "";
        IsSaveProfileDialogVisible = true;
    }

    private void ConfirmSaveProfile()
    {
        string name = string.IsNullOrWhiteSpace(_profileNameInput) ? "Мой профиль" : _profileNameInput.Trim();
        _settings.SaveCustomProfile(name);
        IsSaveProfileDialogVisible = false;
        ProfileNameInput = "";
        HasUnsavedProfileChanges = false;
        RefreshPresetsList();
        SelectedPreset = $"{Loc["Header_Profile"]}: {name}";
        ShowStatusNotification(Loc.Get("Msg_Profile_Saved", name));
    }

    private void CancelSaveProfile()
    {
        IsSaveProfileDialogVisible = false;
        ProfileNameInput = "";
    }

    private void DeleteCurrentProfile()
    {
        string name = NormalizePresetName(_settings.SelectedPresetName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var result = System.Windows.MessageBox.Show(
            Loc.Get("Msg_Delete_Confirm", name),
            Loc["Msg_Delete_Title"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            if (_settings.DeleteCustomProfile(name))
            {
                RefreshPresetsList();
                SelectedPreset = "Gaming & Discord";
                ShowStatusNotification(Loc.Get("Msg_Profile_Deleted", name));
            }
        }
    }

    private void MarkPresetCustom()
    {
        if (_isUpdatingPreset) return;

        if (IsCustomProfileSelected)
        {
            HasUnsavedProfileChanges = true;
            return;
        }

        if (_settings.SelectedPresetName != "Пользовательский (Custom)")
        {
            _settings.SelectedPresetName = "Пользовательский (Custom)";
            OnPropertyChanged(nameof(SelectedPreset));
            CheckCustomProfileSelected();
        }
    }

    public void ShowStatusNotification(string message)
    {
        StatusNotification = message;
        _statusNotificationTimer?.Stop();
        _statusNotificationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _statusNotificationTimer.Tick += (s, e) =>
        {
            StatusNotification = "";
            _statusNotificationTimer?.Stop();
        };
        _statusNotificationTimer.Start();
    }

    #endregion

    #region DSP Properties

    public bool BypassAll
    {
        get => _settings.BypassAll;
        set
        {
            if (_settings.BypassAll != value)
            {
                _settings.BypassAll = value;
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public bool HighPassEnabled
    {
        get => _settings.HighPassEnabled;
        set
        {
            if (_settings.HighPassEnabled != value)
            {
                _settings.HighPassEnabled = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float HighPassCutoffHz
    {
        get => _settings.HighPassCutoffHz;
        set
        {
            if (Math.Abs(_settings.HighPassCutoffHz - value) > 0.5f)
            {
                _settings.HighPassCutoffHz = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public bool DestroyedMicMemeMode
    {
        get => _settings.DestroyedMicMemeMode;
        set
        {
            if (_settings.DestroyedMicMemeMode != value)
            {
                _settings.DestroyedMicMemeMode = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    // Noise Gate
    public bool GateEnabled
    {
        get => _settings.GateEnabled;
        set
        {
            if (_settings.GateEnabled != value)
            {
                _settings.GateEnabled = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float GateThresholdDb
    {
        get => _settings.GateThresholdDb;
        set
        {
            if (Math.Abs(_settings.GateThresholdDb - value) > 0.2f)
            {
                _settings.GateThresholdDb = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float GateReleaseMs
    {
        get => _settings.GateReleaseMs;
        set
        {
            if (Math.Abs(_settings.GateReleaseMs - value) > 2f)
            {
                _settings.GateReleaseMs = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    // Compressor
    public bool CompressorEnabled
    {
        get => _settings.CompressorEnabled;
        set
        {
            if (_settings.CompressorEnabled != value)
            {
                _settings.CompressorEnabled = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float CompressorThresholdDb
    {
        get => _settings.CompressorThresholdDb;
        set
        {
            if (Math.Abs(_settings.CompressorThresholdDb - value) > 0.2f)
            {
                _settings.CompressorThresholdDb = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float CompressorRatio
    {
        get => _settings.CompressorRatio;
        set
        {
            if (Math.Abs(_settings.CompressorRatio - value) > 0.1f)
            {
                _settings.CompressorRatio = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float CompressorAttackMs
    {
        get => _settings.CompressorAttackMs;
        set
        {
            if (Math.Abs(_settings.CompressorAttackMs - value) > 0.5f)
            {
                _settings.CompressorAttackMs = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float CompressorReleaseMs
    {
        get => _settings.CompressorReleaseMs;
        set
        {
            if (Math.Abs(_settings.CompressorReleaseMs - value) > 2f)
            {
                _settings.CompressorReleaseMs = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float CompressorMakeupGainDb
    {
        get => _settings.CompressorMakeupGainDb;
        set
        {
            if (Math.Abs(_settings.CompressorMakeupGainDb - value) > 0.2f)
            {
                _settings.CompressorMakeupGainDb = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    // Limiter
    public bool LimiterEnabled
    {
        get => _settings.LimiterEnabled;
        set
        {
            if (_settings.LimiterEnabled != value)
            {
                _settings.LimiterEnabled = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float LimiterCeilingDb
    {
        get => _settings.LimiterCeilingDb;
        set
        {
            if (Math.Abs(_settings.LimiterCeilingDb - value) > 0.1f)
            {
                _settings.LimiterCeilingDb = value;
                MarkPresetCustom();
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    // Monitoring
    public bool MonitorEnabled
    {
        get => _settings.MonitorEnabled;
        set
        {
            if (_settings.MonitorEnabled != value)
            {
                _settings.MonitorEnabled = value;
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public float MonitorVolume
    {
        get => _settings.MonitorVolume;
        set
        {
            if (Math.Abs(_settings.MonitorVolume - value) > 0.01f)
            {
                _settings.MonitorVolume = value;
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    // App Preferences
    public bool AutoStartEngine
    {
        get => _settings.AutoStartEngine;
        set
        {
            if (_settings.AutoStartEngine != value)
            {
                _settings.AutoStartEngine = value;
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoStartWithWindows
    {
        get => AutoStartHelper.IsAutoStartEnabled();
        set
        {
            AutoStartHelper.SetAutoStart(value);
            _settings.AutoStartWithWindows = value;
            OnSettingChanged();
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set
        {
            if (_settings.MinimizeToTrayOnClose != value)
            {
                _settings.MinimizeToTrayOnClose = value;
                OnSettingChanged();
                OnPropertyChanged();
            }
        }
    }

    private void OnSettingChanged()
    {
        _settings.Save();
        UpdateEngineSettings();
    }

    private void UpdateEngineSettings()
    {
        _engine.UpdateSettings(_settings);
    }

    private void NotifyAllDspPropertiesChanged()
    {
        OnPropertyChanged(nameof(DestroyedMicMemeMode));
        OnPropertyChanged(nameof(HighPassEnabled));
        OnPropertyChanged(nameof(HighPassCutoffHz));
        OnPropertyChanged(nameof(GateEnabled));
        OnPropertyChanged(nameof(GateThresholdDb));
        OnPropertyChanged(nameof(GateReleaseMs));
        OnPropertyChanged(nameof(CompressorEnabled));
        OnPropertyChanged(nameof(CompressorThresholdDb));
        OnPropertyChanged(nameof(CompressorRatio));
        OnPropertyChanged(nameof(CompressorAttackMs));
        OnPropertyChanged(nameof(CompressorReleaseMs));
        OnPropertyChanged(nameof(CompressorMakeupGainDb));
        OnPropertyChanged(nameof(LimiterEnabled));
        OnPropertyChanged(nameof(LimiterCeilingDb));
    }

    #endregion

    #region Metering / Real-time UI

    public float InputPeakDb
    {
        get => _inputPeakDb;
        private set => SetField(ref _inputPeakDb, value);
    }

    public double InputMeterPercent => DbToPercent(_inputPeakDb);

    public float OutputPeakDb
    {
        get => _outputPeakDb;
        private set => SetField(ref _outputPeakDb, value);
    }

    public double OutputMeterPercent => DbToPercent(_outputPeakDb);

    public float GainReductionDb
    {
        get => _gainReductionDb;
        private set => SetField(ref _gainReductionDb, value);
    }

    public double GainReductionMeterPercent
    {
        get
        {
            float gr = MathF.Abs(_gainReductionDb);
            return Math.Clamp((gr / 24.0) * 100.0, 0.0, 100.0);
        }
    }

    public bool IsGateOpen
    {
        get => _isGateOpen;
        private set => SetField(ref _isGateOpen, value);
    }

    public string GateStatusText => _isGateOpen ? "ОТКРЫТ" : "ЗАКРЫТ";
    public string GateStatusColor => _isGateOpen ? "#57f287" : "#585b62";

    public void PauseMeters()
    {
        _meterTimer.Stop();
    }

    public void ResumeMeters()
    {
        if (!_meterTimer.IsEnabled)
        {
            _meterTimer.Start();
        }
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        var metrics = _engine.Metrics;
        if (metrics == null || !_isRunning)
        {
            InputPeakDb = -60f;
            OutputPeakDb = -60f;
            GainReductionDb = 0f;
            IsGateOpen = false;
        }
        else
        {
            InputPeakDb = metrics.InputPeakDb;
            OutputPeakDb = metrics.OutputPeakDb;
            GainReductionDb = metrics.GainReductionDb;
            IsGateOpen = metrics.IsGateOpen;
        }

        OnPropertyChanged(nameof(InputMeterPercent));
        OnPropertyChanged(nameof(OutputMeterPercent));
        OnPropertyChanged(nameof(GainReductionMeterPercent));
        OnPropertyChanged(nameof(GateStatusText));
        OnPropertyChanged(nameof(GateStatusColor));
    }

    private static double DbToPercent(float db)
    {
        if (db <= -60f) return 0.0;
        if (db >= 0f) return 100.0;
        return ((db + 60.0) / 60.0) * 100.0;
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _engine.Dispose();
    }
}
