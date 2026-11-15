namespace MicCompressor.Models;

public class UserProfile
{
    public string Name { get; set; } = "Новый профиль";

    // High Pass Filter
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

    // Meme mode
    public bool DestroyedMicMemeMode { get; set; } = false;

    public static UserProfile FromSettings(string name, AudioSettings settings)
    {
        return new UserProfile
        {
            Name = name,
            HighPassEnabled = settings.HighPassEnabled,
            HighPassCutoffHz = settings.HighPassCutoffHz,
            GateEnabled = settings.GateEnabled,
            GateThresholdDb = settings.GateThresholdDb,
            GateAttackMs = settings.GateAttackMs,
            GateReleaseMs = settings.GateReleaseMs,
            GateHoldMs = settings.GateHoldMs,
            CompressorEnabled = settings.CompressorEnabled,
            CompressorThresholdDb = settings.CompressorThresholdDb,
            CompressorRatio = settings.CompressorRatio,
            CompressorAttackMs = settings.CompressorAttackMs,
            CompressorReleaseMs = settings.CompressorReleaseMs,
            CompressorMakeupGainDb = settings.CompressorMakeupGainDb,
            CompressorKneeDb = settings.CompressorKneeDb,
            LimiterEnabled = settings.LimiterEnabled,
            LimiterCeilingDb = settings.LimiterCeilingDb,
            LimiterReleaseMs = settings.LimiterReleaseMs,
            DestroyedMicMemeMode = settings.DestroyedMicMemeMode
        };
    }

    public void ApplyToSettings(AudioSettings settings)
    {
        settings.HighPassEnabled = HighPassEnabled;
        settings.HighPassCutoffHz = HighPassCutoffHz;
        settings.GateEnabled = GateEnabled;
        settings.GateThresholdDb = GateThresholdDb;
        settings.GateAttackMs = GateAttackMs;
        settings.GateReleaseMs = GateReleaseMs;
        settings.GateHoldMs = GateHoldMs;
        settings.CompressorEnabled = CompressorEnabled;
        settings.CompressorThresholdDb = CompressorThresholdDb;
        settings.CompressorRatio = CompressorRatio;
        settings.CompressorAttackMs = CompressorAttackMs;
        settings.CompressorReleaseMs = CompressorReleaseMs;
        settings.CompressorMakeupGainDb = CompressorMakeupGainDb;
        settings.CompressorKneeDb = CompressorKneeDb;
        settings.LimiterEnabled = LimiterEnabled;
        settings.LimiterCeilingDb = LimiterCeilingDb;
        settings.LimiterReleaseMs = LimiterReleaseMs;
        settings.DestroyedMicMemeMode = DestroyedMicMemeMode;
        settings.SelectedPresetName = Name;
    }
}
