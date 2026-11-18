using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicCompressor.Models;

public class AudioSettings
{
    // Audio Routing
    public string? InputDeviceId { get; set; }
    public string? InputDeviceName { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? OutputDeviceName { get; set; }
    public string? MonitorDeviceId { get; set; }
    public string? MonitorDeviceName { get; set; }
    public bool MonitorEnabled { get; set; } = false;
    public float MonitorVolume { get; set; } = 1.0f;

    // High Pass Filter (Low cut)
    public bool HighPassEnabled { get; set; } = true;
    public float HighPassCutoffHz { get; set; } = 80.0f;

    // Noise Gate
    public bool GateEnabled { get; set; } = true;
    public float GateThresholdDb { get; set; } = -42.0f;
    public float GateAttackMs { get; set; } = 4.0f;
    public float GateReleaseMs { get; set; } = 120.0f;
    public float GateHoldMs { get; set; } = 60.0f;

    // Compressor
    public bool CompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -20.0f;
    public float CompressorRatio { get; set; } = 4.0f;
    public float CompressorAttackMs { get; set; } = 10.0f;
    public float CompressorReleaseMs { get; set; } = 150.0f;
    public float CompressorMakeupGainDb { get; set; } = 5.0f;
    public float CompressorKneeDb { get; set; } = 4.0f;

    // Limiter
    public bool LimiterEnabled { get; set; } = true;
    public float LimiterCeilingDb { get; set; } = -1.0f;
    public float LimiterReleaseMs { get; set; } = 40.0f;

    // General & UI
    public bool BypassAll { get; set; } = false;
    public bool DestroyedMicMemeMode { get; set; } = false; // Meme / joke destroyed mic mode
    public bool AutoStartEngine { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public string SelectedPresetName { get; set; } = "Gaming & Discord";

    // Localization
    public string? Language { get; set; } = null; // null => auto-detected

    // Mute state
    public bool IsMuted { get; set; } = false;

    // Configurable Global Hotkeys
    public bool HotkeysEnabled { get; set; } = true;
    public HotkeySetting HotkeyBypass { get; set; } = new() { Enabled = true, Ctrl = true, Shift = true, Alt = false, Key = "B" };
    public HotkeySetting HotkeyMeme { get; set; } = new() { Enabled = true, Ctrl = true, Shift = true, Alt = false, Key = "M" };
    public HotkeySetting HotkeyMute { get; set; } = new() { Enabled = true, Ctrl = true, Shift = true, Alt = false, Key = "X" };

    // Custom Saved Profiles
    public List<UserProfile> CustomProfiles { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetConfigFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SimpleMicCompressor");
        Directory.CreateDirectory(folder);
        string newPath = Path.Combine(folder, "config.json");

        // Migrate from old path if present
        string oldFolder = Path.Combine(appData, "MicCompressor");
        string oldPath = Path.Combine(oldFolder, "config.json");
        if (!File.Exists(newPath) && File.Exists(oldPath))
        {
            try { File.Copy(oldPath, newPath); } catch { }
        }

        return newPath;
    }

    public static AudioSettings Load()
    {
        AudioSettings? settings = null;
        try
        {
            string path = GetConfigFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<AudioSettings>(json, JsonOptions);
            }
        }
        catch
        {
            // Fallback to default
        }

        settings ??= CreateGamingPreset();

        // Auto-detect language if first run
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            settings.Language = Localization.LocalizationService.DetectSystemLanguage();
            settings.Save();
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            string path = GetConfigFilePath();
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public void ApplyPreset(string presetName)
    {
        var custom = CustomProfiles.FirstOrDefault(p => p.Name == presetName);
        if (custom != null)
        {
            custom.ApplyToSettings(this);
            return;
        }

        AudioSettings preset = presetName switch
        {
            "Default / Balanced" => CreateDefaultPreset(),
            "Gaming & Discord" => CreateGamingPreset(),
            "Radio / Podcast" => CreatePodcastPreset(),
            "Whisper / Quiet Night" => CreateWhisperPreset(),
            "Aggressive / Anti-Rage" => CreateAntiRagePreset(),
            "Уничтоженный микрофон (Meme)" or "🎙️ Уничтоженный микрофон (Meme)" => CreateDestroyedMicPreset(),
            _ => this
        };

        HighPassEnabled = preset.HighPassEnabled;
        HighPassCutoffHz = preset.HighPassCutoffHz;
        GateEnabled = preset.GateEnabled;
        GateThresholdDb = preset.GateThresholdDb;
        GateAttackMs = preset.GateAttackMs;
        GateReleaseMs = preset.GateReleaseMs;
        GateHoldMs = preset.GateHoldMs;
        CompressorEnabled = preset.CompressorEnabled;
        CompressorThresholdDb = preset.CompressorThresholdDb;
        CompressorRatio = preset.CompressorRatio;
        CompressorAttackMs = preset.CompressorAttackMs;
        CompressorReleaseMs = preset.CompressorReleaseMs;
        CompressorMakeupGainDb = preset.CompressorMakeupGainDb;
        CompressorKneeDb = preset.CompressorKneeDb;
        LimiterEnabled = preset.LimiterEnabled;
        LimiterCeilingDb = preset.LimiterCeilingDb;
        LimiterReleaseMs = preset.LimiterReleaseMs;
        DestroyedMicMemeMode = preset.DestroyedMicMemeMode;
        SelectedPresetName = presetName;
    }

    public void SaveCustomProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var existing = CustomProfiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            CustomProfiles.Remove(existing);
        }
        var newProfile = UserProfile.FromSettings(name, this);
        CustomProfiles.Add(newProfile);
        SelectedPresetName = name;
        Save();
    }

    public bool DeleteCustomProfile(string name)
    {
        var existing = CustomProfiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            CustomProfiles.Remove(existing);
            Save();
            return true;
        }
        return false;
    }

    public static AudioSettings CreateDefaultPreset() => new()
    {
        SelectedPresetName = "Default / Balanced",
        HighPassEnabled = true,
        HighPassCutoffHz = 80f,
        GateEnabled = true,
        GateThresholdDb = -45f,
        GateAttackMs = 4f,
        GateReleaseMs = 120f,
        CompressorEnabled = true,
        CompressorThresholdDb = -18f,
        CompressorRatio = 3.0f,
        CompressorAttackMs = 12f,
        CompressorReleaseMs = 160f,
        CompressorMakeupGainDb = 4f,
        CompressorKneeDb = 4f,
        LimiterEnabled = true,
        LimiterCeilingDb = -1.0f
    };

    public static AudioSettings CreateGamingPreset() => new()
    {
        SelectedPresetName = "Gaming & Discord",
        HighPassEnabled = true,
        HighPassCutoffHz = 80f,
        GateEnabled = true,
        GateThresholdDb = -40f,
        GateAttackMs = 3f,
        GateReleaseMs = 100f,
        CompressorEnabled = true,
        CompressorThresholdDb = -20f,
        CompressorRatio = 4.0f,
        CompressorAttackMs = 8f,
        CompressorReleaseMs = 140f,
        CompressorMakeupGainDb = 6f,
        CompressorKneeDb = 5f,
        LimiterEnabled = true,
        LimiterCeilingDb = -1.0f
    };

    public static AudioSettings CreatePodcastPreset() => new()
    {
        SelectedPresetName = "Radio / Podcast",
        HighPassEnabled = true,
        HighPassCutoffHz = 75f,
        GateEnabled = true,
        GateThresholdDb = -48f,
        GateAttackMs = 5f,
        GateReleaseMs = 180f,
        CompressorEnabled = true,
        CompressorThresholdDb = -24f,
        CompressorRatio = 4.5f,
        CompressorAttackMs = 15f,
        CompressorReleaseMs = 200f,
        CompressorMakeupGainDb = 7f,
        CompressorKneeDb = 6f,
        LimiterEnabled = true,
        LimiterCeilingDb = -0.5f
    };

    public static AudioSettings CreateWhisperPreset() => new()
    {
        SelectedPresetName = "Whisper / Quiet Night",
        HighPassEnabled = true,
        HighPassCutoffHz = 90f,
        GateEnabled = true,
        GateThresholdDb = -52f,
        GateAttackMs = 2f,
        GateReleaseMs = 150f,
        CompressorEnabled = true,
        CompressorThresholdDb = -32f,
        CompressorRatio = 6.0f,
        CompressorAttackMs = 5f,
        CompressorReleaseMs = 120f,
        CompressorMakeupGainDb = 12f,
        CompressorKneeDb = 6f,
        LimiterEnabled = true,
        LimiterCeilingDb = -1.0f
    };

    public static AudioSettings CreateAntiRagePreset() => new()
    {
        SelectedPresetName = "Aggressive / Anti-Rage",
        HighPassEnabled = true,
        HighPassCutoffHz = 85f,
        GateEnabled = true,
        GateThresholdDb = -38f,
        GateAttackMs = 2f,
        GateReleaseMs = 90f,
        CompressorEnabled = true,
        CompressorThresholdDb = -22f,
        CompressorRatio = 8.0f,
        CompressorAttackMs = 3f,
        CompressorReleaseMs = 100f,
        CompressorMakeupGainDb = 4f,
        CompressorKneeDb = 3f,
        LimiterEnabled = true,
        LimiterCeilingDb = -2.0f
    };

    public static AudioSettings CreateDestroyedMicPreset() => new()
    {
        SelectedPresetName = "🎙️ Уничтоженный микрофон (Meme)",
        DestroyedMicMemeMode = true,
        HighPassEnabled = true,
        HighPassCutoffHz = 80f,
        GateEnabled = false,
        CompressorEnabled = true,
        CompressorThresholdDb = -24f,
        CompressorRatio = 8.0f,
        CompressorAttackMs = 5f,
        CompressorReleaseMs = 100f,
        CompressorMakeupGainDb = 8f,
        CompressorKneeDb = 4f,
        LimiterEnabled = true,
        LimiterCeilingDb = -0.5f
    };
}
