using System.Reflection;
using System.Text.Json;
using MicCompressor.Audio;
using MicCompressor.Audio.Dsp;
using MicCompressor.Localization;
using MicCompressor.Models;

namespace MicCompressor.Tests;

internal static class Program
{
    private const int SampleRate = 48000;

    private static int _passed;
    private static int _failed;
    private static readonly List<string> Failures = new();

    private static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        TestHighPass();
        TestNoiseGate();
        TestCompressor();
        TestLimiter();
        TestAudioChain();
        TestHotkeys();
        TestDeviceHeuristics();
        TestSettings();
        TestLocalization();
        TestUpdateChecker();

        Console.WriteLine();
        Console.WriteLine($"TOTAL: {_passed + _failed} | PASSED: {_passed} | FAILED: {_failed}");
        if (_failed > 0)
        {
            Console.WriteLine("FAILED TESTS:");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
        }
        Environment.ExitCode = _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- helpers

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS  {name}");
        }
        else
        {
            _failed++;
            var line = detail is null ? name : $"{name} :: {detail}";
            Failures.Add(line);
            Console.WriteLine($"FAIL  {line}");
        }
    }

    private static void Near(string name, double actual, double expected, double tolerance)
        => Check(name, Math.Abs(actual - expected) <= tolerance,
                 $"actual={actual:F4} expected={expected:F4} tol={tolerance}");

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    private static float Sine(float amplitude, double frequency, int sampleIndex)
        => amplitude * MathF.Sin((float)(2.0 * Math.PI * frequency * sampleIndex / SampleRate));

    // ------------------------------------------------------------------- HPF

    private static void TestHighPass()
    {
        var hp = new BiquadHighPassFilter(80f, SampleRate);

        float last = 0f;
        for (int i = 0; i < SampleRate; i++) last = hp.Process(1.0f);
        Check("HPF: DC blocked (< -60 dB)", Math.Abs(last) < 1e-3f, $"last={last:E2}");

        hp.Reset();
        double sumSq = 0;
        int n = 0;
        for (int i = 0; i < SampleRate; i++)
        {
            float y = hp.Process(Sine(0.5f, 1000.0, i));
            if (i >= SampleRate - 10000) { sumSq += y * y; n++; }
        }
        Near("HPF: 1 kHz passband RMS (expected 0.5/sqrt2)", Math.Sqrt(sumSq / n), 0.3536, 0.02);

        hp.Reset();
        bool finite = Finite(hp.Process(float.NaN));
        for (int i = 0; i < 2000; i++) finite &= Finite(hp.Process(0f));
        Check("HPF: NaN self-healing", finite);

        hp.Reset();
        finite = true;
        for (int i = 0; i < 1000; i++) finite &= Finite(hp.Process(float.PositiveInfinity));
        Check("HPF: Infinity does not leak to output", finite);
    }

    // ------------------------------------------------------------------ gate

    private static void TestNoiseGate()
    {
        var gate = new NoiseGate(-40f, 4f, 120f, 60f, SampleRate);

        float gain = 0f;
        for (int i = 0; i < SampleRate / 2; i++) gain = gate.ComputeGain(0f);
        Check("Gate: closed on silence", gain < 0.001f, $"gain={gain:F4}");

        for (int i = 0; i < 9600; i++) gain = gate.ComputeGain(Sine(0.5f, 200.0, i));
        Check("Gate: opens on loud signal", gain > 0.99f, $"gain={gain:F4}");
        Check("Gate: IsOpen flag true", gate.IsOpen);

        for (int i = 0; i < SampleRate / 2; i++) gain = gate.ComputeGain(0f);
        Check("Gate: closes after hold+release (500 ms)", gain < 0.05f, $"gain={gain:F4}");
        Check("Gate: IsOpen flag false", !gate.IsOpen);

        Check("Gate: NaN input safe", Finite(gate.ComputeGain(float.NaN)));
    }

    // ------------------------------------------------------------ compressor

    private static void TestCompressor()
    {
        var comp = new DynamicCompressor(-20f, 4f, 5f, 100f, 0f, 4f, SampleRate);
        float gain = 1f;
        for (int i = 0; i < SampleRate * 2; i++) gain = comp.ComputeGain(0.5f);

        // 0.5 => -6.02 dB; ( -6.02 - (-20) ) * (1 - 1/4) = 10.49 dB
        Near("Compressor: GR for 0.5 (-6 dB) at 4:1", comp.CurrentGainReductionDb, -10.49, 1.0);
        Check("Compressor: gain < 1 under compression", gain < 0.4f, $"gain={gain:F4}");

        var compMakeup = new DynamicCompressor(-20f, 4f, 5f, 100f, 6f, 4f, SampleRate);
        float makeup = compMakeup.ComputeGain(0.001f);
        Near("Compressor: makeup gain 6 dB => 1.995", makeup, 1.9953, 0.01);

        var compClean = new DynamicCompressor(-20f, 4f, 5f, 100f, 0f, 4f, SampleRate);
        for (int i = 0; i < SampleRate; i++) compClean.ComputeGain(0.001f);
        Near("Compressor: no GR below threshold", compClean.CurrentGainReductionDb, 0.0, 0.01);

        Check("Compressor: NaN input safe", Finite(comp.ComputeGain(float.NaN)));
        Check("Compressor: Infinity input safe", Finite(comp.ComputeGain(float.PositiveInfinity)));
    }

    // --------------------------------------------------------------- limiter

    private static void TestLimiter()
    {
        var lim = new FastLimiter(-1f, 40f, SampleRate);

        float maxAbs = 0f;
        bool finite = true;
        for (int i = 0; i < 4800; i++)
        {
            float y = lim.Process(2.0f);
            maxAbs = MathF.Max(maxAbs, MathF.Abs(y));
            finite &= Finite(y);
        }
        Check("Limiter: output never above ceiling (-1 dBFS)", maxAbs <= 0.8913f + 1e-3f, $"max={maxAbs:F4}");
        Check("Limiter: output reaches ceiling region", maxAbs >= 0.85f, $"max={maxAbs:F4}");
        Check("Limiter: finite output", finite);

        for (int i = 0; i < SampleRate * 3 / 10; i++) lim.Process(0f);
        float afterRelease = lim.Process(0.5f);
        Near("Limiter: gain recovered after release", MathF.Abs(afterRelease), 0.5, 0.02);

        Check("Limiter: NaN safe", Finite(lim.Process(float.NaN)));
    }

    // ------------------------------------------------------------ audio chain

    private static void TestAudioChain()
    {
        // Mute
        var settings = new AudioSettings();
        var chain = new AudioChain(settings, SampleRate);
        var buf = new float[960 * 2];
        Array.Fill(buf, 0.5f);
        settings.IsMuted = true;
        chain.ProcessInterleaved(buf, 0, buf.Length, 2);
        Check("Chain: mute zeroes output", buf.All(v => v == 0f));

        // Bypass
        settings.IsMuted = false;
        settings.BypassAll = true;
        var input = new float[2048];
        for (int i = 0; i < input.Length; i++) input[i] = Sine(0.4f, 440.0, i);
        var working = (float[])input.Clone();
        chain.ProcessInterleaved(working, 0, working.Length, 2);
        Check("Chain: bypass is bit-exact", working.SequenceEqual(input));

        // Meme mode
        settings.BypassAll = false;
        settings.DestroyedMicMemeMode = true;
        var meme = (float[])input.Clone();
        chain.ProcessInterleaved(meme, 0, meme.Length, 2);
        Check("Chain: meme mode output finite", meme.All(Finite));
        Check("Chain: meme mode output within [-1, 1]", meme.All(v => MathF.Abs(v) <= 1.0f));

        // NaN resilience
        settings.DestroyedMicMemeMode = false;
        var nanBuf = (float[])input.Clone();
        nanBuf[10] = float.NaN;
        nanBuf[11] = float.PositiveInfinity;
        chain.ProcessInterleaved(nanBuf, 0, nanBuf.Length, 2);
        Check("Chain: NaN/Inf input does not poison output", nanBuf.All(Finite));

        // Stereo independence (fresh chain: filters keep state between blocks by design)
        var stereoChain = new AudioChain(new AudioSettings(), SampleRate);
        var stereo = new float[960 * 2];
        for (int f = 0; f < 480; f++)
        {
            stereo[f * 2] = 0.5f;
            stereo[f * 2 + 1] = 0f;
        }
        stereoChain.ProcessInterleaved(stereo, 0, stereo.Length, 2);
        bool rightSilent = true;
        for (int f = 0; f < 480; f++) rightSilent &= MathF.Abs(stereo[f * 2 + 1]) < 1e-6f;
        Check("Chain: right channel stays silent when input right is silent", rightSilent);

        // Metrics
        var metricsSettings = new AudioSettings();
        var metricsChain = new AudioChain(metricsSettings, SampleRate);
        var block = new float[480 * 2];
        for (int b = 0; b < 100; b++)
        {
            for (int f = 0; f < 480; f++)
            {
                float s = Sine(0.5f, 300.0, b * 480 + f);
                block[f * 2] = s;
                block[f * 2 + 1] = s;
            }
            metricsChain.ProcessInterleaved(block, 0, block.Length, 2);
        }
        var m = metricsChain.Metrics;
        Check("Metrics: input peak in reasonable range", m.InputPeakDb > -20f && m.InputPeakDb <= 3f, $"in={m.InputPeakDb:F1}");
        Check("Metrics: output peak in reasonable range", m.OutputPeakDb > -20f && m.OutputPeakDb <= 3f, $"out={m.OutputPeakDb:F1}");
        Check("Metrics: gain reduction is not positive", m.GainReductionDb <= 0f, $"gr={m.GainReductionDb:F2}");
        Check("Metrics: gate open on loud signal", m.IsGateOpen);

        // Multi-channel (5.1) must not throw
        var surround = new float[480 * 6];
        Array.Fill(surround, 0.1f);
        try
        {
            chain.ProcessInterleaved(surround, 0, surround.Length, 6);
            Check("Chain: 6-channel processing does not throw", true);
        }
        catch (Exception ex)
        {
            Check("Chain: 6-channel processing does not throw", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- hotkeys

    private static void TestHotkeys()
    {
        var hk = new HotkeySetting { Key = "B" };
        Check("Hotkey: B => 0x42", hk.GetVirtualKeyCode() == 0x42, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "F5";
        Check("Hotkey: F5 => 0x74", hk.GetVirtualKeyCode() == 0x74, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "NUM3";
        Check("Hotkey: NUM3 => 0x63", hk.GetVirtualKeyCode() == 0x63, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "SPACE";
        Check("Hotkey: SPACE => 0x20", hk.GetVirtualKeyCode() == 0x20, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "TILDE";
        Check("Hotkey: TILDE => 0xC0", hk.GetVirtualKeyCode() == 0xC0, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "DELETE";
        Check("Hotkey: DELETE => 0x2E", hk.GetVirtualKeyCode() == 0x2E, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "0";
        Check("Hotkey: '0' => 0x30", hk.GetVirtualKeyCode() == 0x30, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "PAUSE";
        Check("Hotkey: PAUSE => 0x13", hk.GetVirtualKeyCode() == 0x13, $"vk={hk.GetVirtualKeyCode():X}");

        hk.Key = "";
        Check("Hotkey: empty key => 0", hk.GetVirtualKeyCode() == 0);

        hk.Ctrl = true;
        hk.Shift = true;
        hk.Alt = false;
        Check("Hotkey: modifiers Ctrl+Shift => 0x4006", hk.GetModifiers() == 0x4006, $"mods={hk.GetModifiers():X}");
    }

    // ------------------------------------------------------- device heuristic

    private static void TestDeviceHeuristics()
    {
        Check("Device: 'CABLE Input (VB-Audio Virtual Cable)' is virtual",
              AudioDeviceService.IsVirtualAudioName("CABLE Input (VB-Audio Virtual Cable)"));
        Check("Device: 'Microphone (Realtek(R) Audio)' is not virtual",
              !AudioDeviceService.IsVirtualAudioName("Microphone (Realtek(R) Audio)"));
        Check("Device: 'SteelSeries Sonar - Microphone' is flagged virtual",
              AudioDeviceService.IsVirtualAudioName("SteelSeries Sonar - Microphone"));
        // Documents the known false positive of the substring heuristic:
        Check("Device heuristic (known defect): 'My Cable TV' is flagged virtual",
              AudioDeviceService.IsVirtualAudioName("My Cable TV"));
        Check("Device heuristic (known defect): 'Virtual Desktop Audio' is flagged virtual",
              AudioDeviceService.IsVirtualAudioName("Virtual Desktop Audio"));
    }

    // --------------------------------------------------------------- settings

    private static void TestSettings()
    {
        var s = new AudioSettings();
        Check("Settings: default preset values",
              s.GateThresholdDb == -42f && s.CompressorRatio == 4f && s.LimiterCeilingDb == -1f && s.AutoStartEngine);

        s.ApplyPreset("Radio / Podcast");
        Check("Settings: preset 'Radio / Podcast' applied",
              Math.Abs(s.CompressorRatio - 4.5f) < 1e-6 && Math.Abs(s.CompressorThresholdDb + 24f) < 1e-6
              && s.SelectedPresetName == "Radio / Podcast",
              $"ratio={s.CompressorRatio} thr={s.CompressorThresholdDb} name={s.SelectedPresetName}");

        s.ApplyPreset("Уничтоженный микрофон (Meme)");
        Check("Settings: meme preset enables meme mode", s.DestroyedMicMemeMode);

        var profile = UserProfile.FromSettings("TestProfile", s);
        var restored = new AudioSettings();
        profile.ApplyToSettings(restored);
        Check("Profile: FromSettings/ApplyToSettings round-trip",
              restored.CompressorRatio == s.CompressorRatio
              && restored.HighPassCutoffHz == s.HighPassCutoffHz
              && restored.LimiterCeilingDb == s.LimiterCeilingDb
              && restored.SelectedPresetName == "TestProfile");

        string json = JsonSerializer.Serialize(new AudioSettings { CompressorRatio = 7.25f });
        var back = JsonSerializer.Deserialize<AudioSettings>(json);
        Check("Settings: JSON round-trip",
              back is not null && back.CompressorRatio == 7.25f
              && back.HotkeyBypass.Key == "B" && back.CustomProfiles is not null);

        string configPath = AudioSettings.GetConfigFilePath();
        Check("Settings: config path lives under %APPDATA%\\SimpleMicCompressor\\config.json",
              configPath.Replace('\\', '/').Contains("/SimpleMicCompressor/config.json"), configPath);
        Check("Settings (known divergence): config file is NOT mic_compressor_config.json",
              !configPath.EndsWith("mic_compressor_config.json", StringComparison.OrdinalIgnoreCase), configPath);
    }

    // ----------------------------------------------------------- localization

    private static void TestLocalization()
    {
        var type = typeof(LocalizationService);
        string[] names = { "StringsRu", "StringsEn", "StringsDe", "StringsEs" };
        var sets = new Dictionary<string, HashSet<string>>();

        foreach (var name in names)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field?.GetValue(null) is Dictionary<string, string> dict)
            {
                sets[name] = new HashSet<string>(dict.Keys);
            }
        }

        Check("Localization: all 4 dictionaries resolved via reflection", sets.Count == 4,
              $"found={string.Join(",", sets.Keys)}");

        if (sets.Count == 4)
        {
            var ru = sets["StringsRu"];
            foreach (var other in names.Skip(1))
            {
                var missing = ru.Except(sets[other]).Take(10).ToArray();
                var extra = sets[other].Except(ru).Take(10).ToArray();
                Check($"Localization: {other} key set matches Ru", ru.SetEquals(sets[other]),
                      $"missing=[{string.Join(",", missing)}] extra=[{string.Join(",", extra)}]");
            }
            Check("Localization: RU dictionary has >= 90 keys", ru.Count >= 90, $"count={ru.Count}");
        }

        var loc = LocalizationService.Instance;
        Check("Localization: missing key returns the key itself", loc.Get("__missing_key__") == "__missing_key__");
        Check("Localization: format arguments applied", loc.Get("Msg_Profile_Saved", "X").Contains("X"));
        Check("Localization: malformed format string does not throw", loc.Get("Msg_Profile_Saved", "{") is not null);
    }

    // -------------------------------------------------------- update checker

    private static void TestUpdateChecker()
    {
        var cur = new Version(1, 1, 0, 0);
        Check("UpdateChecker: v1.2 is newer than 1.1", MicCompressor.Utils.VersionHelper.IsNewerVersion("v1.2", cur));
        Check("UpdateChecker: v1.1.1 is newer than 1.1", MicCompressor.Utils.VersionHelper.IsNewerVersion("v1.1.1", cur));
        Check("UpdateChecker: v2.0.0 is newer than 1.1", MicCompressor.Utils.VersionHelper.IsNewerVersion("v2.0.0", cur));
        Check("UpdateChecker: v1.1 is not newer than 1.1", !MicCompressor.Utils.VersionHelper.IsNewerVersion("v1.1", cur));
        Check("UpdateChecker: v1.0.1 is not newer than 1.1", !MicCompressor.Utils.VersionHelper.IsNewerVersion("v1.0.1", cur));
        Check("UpdateChecker: empty tag is not newer", !MicCompressor.Utils.VersionHelper.IsNewerVersion("", cur));
    }
}
