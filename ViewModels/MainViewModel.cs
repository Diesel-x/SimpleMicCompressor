using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MicCompressor.Audio;
using MicCompressor.Audio.Dsp;
using MicCompressor.Models;
using MicCompressor.Utils;

namespace MicCompressor.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioEngine _engine;
    private readonly AudioSettings _settings;
    private readonly DispatcherTimer _meterTimer;

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

    public ObservableCollection<string> Presets { get; } = new()
    {
        "Gaming & Discord",
        "Default / Balanced",
        "Radio / Podcast",
        "Whisper / Quiet Night",
        "Aggressive / Anti-Rage",
        "🎙️ Уничтоженный микрофон (Meme)",
        "Пользовательский (Custom)"
    };

    public RelayCommand ToggleEngineCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand OpenVbCableWebsiteCommand { get; }
    public RelayCommand InstallVbCableCommand { get; }
    public RelayCommand ReinstallVbCableCommand { get; }
    public Action? RequestMinimizeToTray { get; set; }

    private string? _installerStatus;
    private bool _isInstalling = false;

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

    public bool HasVirtualCable => OutputDevices.Any(d => d.IsVirtualCable);

    public MainViewModel()
    {
        _settings = AudioSettings.Load();
        _engine = new AudioEngine(_settings);
        _engine.StateChanged += OnEngineStateChanged;

        ToggleEngineCommand = new RelayCommand(ToggleEngine);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        OpenVbCableWebsiteCommand = new RelayCommand(OpenVbCableWebsite);
        InstallVbCableCommand = new RelayCommand(InstallVbCable);
        ReinstallVbCableCommand = new RelayCommand(ReinstallVbCable);

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

    private void OnEngineStateChanged(EngineState state, string? error)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case EngineState.Running:
                    IsRunning = true;
                    EngineStatusText = "Активен (В эфире)";
                    EngineStatusColor = "#57f287"; // Discord Green
                    StartStopButtonText = "Остановить";
                    break;
                case EngineState.Starting:
                    EngineStatusText = "Запуск...";
                    EngineStatusColor = "#fee75c"; // Amber
                    break;
                case EngineState.Error:
                    IsRunning = false;
                    EngineStatusText = $"Ошибка: {error ?? "Сбой аудио"}";
                    EngineStatusColor = "#ed4245"; // Red
                    StartStopButtonText = "Повторить запуск";
                    break;
                case EngineState.Stopped:
                default:
                    IsRunning = false;
                    EngineStatusText = "Остановлен";
                    EngineStatusColor = "#8e9297"; // Muted Gray
                    StartStopButtonText = "Запустить";
                    break;
            }
        });
    }

    #endregion

    #region Presets

    public string SelectedPreset
    {
        get => _settings.SelectedPresetName;
        set
        {
            if (_settings.SelectedPresetName != value && value != null)
            {
                _isUpdatingPreset = true;
                _settings.ApplyPreset(value);
                _settings.Save();
                UpdateEngineSettings();
                NotifyAllDspPropertiesChanged();
                _isUpdatingPreset = false;
                OnPropertyChanged();
            }
        }
    }

    private void MarkPresetCustom()
    {
        if (_isUpdatingPreset) return;
        if (_settings.SelectedPresetName != "Пользовательский (Custom)")
        {
            _settings.SelectedPresetName = "Пользовательский (Custom)";
            OnPropertyChanged(nameof(SelectedPreset));
        }
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
