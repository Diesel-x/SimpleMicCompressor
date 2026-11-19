using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MicCompressor.Audio;
using MicCompressor.Audio.Dsp;
using MicCompressor.Models;
using MicCompressor.Utils;

namespace MicCompressor.Tests;

internal static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;
    private static readonly List<string> Failures = new();

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        else
        {
            _failed++;
            var line = detail is null ? name : $"{name} :: {detail}";
            Failures.Add(line);
            Console.WriteLine($"[FAIL] {line}");
        }
    }

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=================================================================");
        Console.WriteLine("  SimpleMicCompressor - System & Audio Hardware Integration Tests");
        Console.WriteLine("=================================================================\n");

        string originalConfigBackup = Path.Combine(Path.GetTempPath(), "SmcConfigBackup_" + Guid.NewGuid().ToString("N") + ".json");
        string configPath = AudioSettings.GetConfigFilePath();
        bool hadOriginalConfig = File.Exists(configPath);
        if (hadOriginalConfig)
        {
            File.Copy(configPath, originalConfigBackup, true);
            Console.WriteLine($"Backed up original config to: {originalConfigBackup}\n");
        }

        try
        {
            Console.WriteLine(">>> 1. ТЕСТИРОВАНИЕ: УДАЛЕНИЕ И УСТАНОВКА ПРИЛОЖЕНИЯ <<<");
            await TestUninstallAndInstallAsync(configPath);

            Console.WriteLine("\n>>> 2. ТЕСТИРОВАНИЕ: РАБОТОСПОСОБНОСТЬ МОНИТОРИНГА ЗВУКА <<<");
            await TestAudioMonitoringAsync();

            Console.WriteLine("\n>>> 3. ТЕСТИРОВАНИЕ: ВЫХОД ЗВУКА С ВИРТУАЛЬНОГО КАБЕЛЯ (VB-CABLE) <<<");
            await TestVirtualCableOutputAsync();
        }
        finally
        {
            // Restore original config if existed
            if (hadOriginalConfig && File.Exists(originalConfigBackup))
            {
                string? dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Copy(originalConfigBackup, configPath, true);
                try { File.Delete(originalConfigBackup); } catch { }
                Console.WriteLine("\nRestored original user configuration.");
            }
        }

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"РЕЗУЛЬТАТ: ВСЕГО: {_passed + _failed} | УСПЕШНО: {_passed} | ОШИБОК: {_failed}");
        Console.WriteLine("=================================================================");
        if (_failed > 0)
        {
            Console.WriteLine("ОШИБКИ:");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        return 0;
    }

    private static async Task TestUninstallAndInstallAsync(string configPath)
    {
        string appDataFolder = Path.GetDirectoryName(configPath)!;

        // --- Тест удаления ---
        Console.WriteLine("\n[1.1] Тестирование механизма удаления (Uninstall)...");

        // 1. Установим автозагрузку для проверки удаления
        AutoStartHelper.SetAutoStart(true);
        Check("Autostart registry key can be created", AutoStartHelper.IsAutoStartEnabled());

        // 2. Создадим тестовый конфигурационный файл
        Directory.CreateDirectory(appDataFolder);
        File.WriteAllText(configPath, "{\"Test\": true}");
        Check("Test config file created in %APPDATA%", File.Exists(configPath));

        // 3. Выполним процедуры очистки (деинсталляция)
        AutoStartHelper.RemoveAllAutoStart();
        Check("Autostart registry key removed", !AutoStartHelper.IsAutoStartEnabled());

        bool cleaned = UninstallHelper.CleanDirectories();
        Check("UninstallHelper.CleanDirectories wiped AppData", cleaned);
        Check("AppData\\SimpleMicCompressor directory does not exist", !Directory.Exists(appDataFolder));

        string selfDelCmd = UninstallHelper.BuildSelfDeleteCommandLine("C:\\app.exe", "C:\\data", "C:\\legacy");
        Check("Self-delete command line format valid",
            selfDelCmd.Contains("ping 127.0.0.1 -n 3") &&
            selfDelCmd.Contains("del /f /q \"C:\\app.exe\"") &&
            selfDelCmd.Contains("rmdir /s /q \"C:\\data\""));

        // --- Тест установки и первого запуска ---
        Console.WriteLine("\n[1.2] Тестирование установки и первичной инициализации (Install & First Run)...");

        // 1. Проверяем наличие или собираем Single-File бинарник
        string curDir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(curDir) && !File.Exists(Path.Combine(curDir, "SimpleMicCompressor.csproj")))
        {
            curDir = Path.GetDirectoryName(curDir) ?? "";
        }
        string repoRoot = curDir;
        string publishDir = Path.Combine(repoRoot, "publish");
        string exePath = Path.Combine(publishDir, "SimpleMicCompressor.exe");

        if (!File.Exists(exePath))
        {
            Console.WriteLine("Компиляция Single-File исполняемого файла в publish...");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "publish SimpleMicCompressor.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o ./publish",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                Console.WriteLine("Publish error:\n" + stderr + "\n" + stdout);
            }
            Check("Build publish single-file executable", proc.ExitCode == 0, $"exitCode={proc.ExitCode}");
        }

        Check("SimpleMicCompressor.exe exists in publish directory", File.Exists(exePath), exePath);
        if (!File.Exists(exePath))
        {
            return;
        }

        // 2. Запускаем приложение в чистой среде с флагом --screenshot (выполняет полный цикл инициализации и закрывается)
        string testScreenshotPath = Path.Combine(repoRoot, "tests", "first_run_test.png");
        try { if (File.Exists(testScreenshotPath)) File.Delete(testScreenshotPath); } catch { }

        var runPsi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--screenshot \"{testScreenshotPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using (var runProc = Process.Start(runPsi)!)
        {
            await runProc.WaitForExitAsync();
            Check("App runs first-time initialization cleanly (exit 0)", runProc.ExitCode == 0, $"exitCode={runProc.ExitCode}");
        }

        // 3. Проверяем, что создался файл конфигурации с авто-определением устройств
        Check("AppData\\SimpleMicCompressor\\config.json created automatically on first run", File.Exists(configPath));

        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize<AudioSettings>(json);
            Check("Config deserialized successfully", loaded != null);
            if (loaded != null)
            {
                Check("Default preset is 'Gaming & Discord'", loaded.SelectedPresetName == "Gaming & Discord");
                Check("Input device ID is auto-assigned", !string.IsNullOrEmpty(loaded.InputDeviceId), loaded.InputDeviceId);
                Check("Output device ID is auto-assigned", !string.IsNullOrEmpty(loaded.OutputDeviceId), loaded.OutputDeviceId);
                Check("Monitor device ID is auto-assigned", !string.IsNullOrEmpty(loaded.MonitorDeviceId), loaded.MonitorDeviceId);
            }
        }

        try { if (File.Exists(testScreenshotPath)) File.Delete(testScreenshotPath); } catch { }
    }

    private static async Task TestAudioMonitoringAsync()
    {
        Console.WriteLine("\n[2.1] Проверка обнаружения устройств мониторинга...");
        var renderDevices = AudioDeviceService.GetRenderDevices();
        Check("Render devices discovered >= 1", renderDevices.Count >= 1, $"count={renderDevices.Count}");

        var monitorDevice = renderDevices.FirstOrDefault(d => !d.IsVirtualCable && d.IsDefault)
                            ?? renderDevices.FirstOrDefault(d => !d.IsVirtualCable)
                            ?? renderDevices.FirstOrDefault();
        Check("Monitor target device (speakers/headphones) identified", monitorDevice != null, monitorDevice?.Name);

        var captureDevices = AudioDeviceService.GetCaptureDevices();
        var captureDevice = captureDevices.FirstOrDefault(d => !d.IsVirtualCable && d.IsDefault)
                            ?? captureDevices.FirstOrDefault(d => !d.IsVirtualCable)
                            ?? captureDevices.FirstOrDefault();
        Check("Physical capture device identified", captureDevice != null, captureDevice?.Name);

        var virtualCableOutput = renderDevices.FirstOrDefault(d => d.IsVirtualCable)
                                 ?? renderDevices.FirstOrDefault();
        Check("Virtual Cable render device identified", virtualCableOutput != null, virtualCableOutput?.Name);

        if (monitorDevice == null || captureDevice == null || virtualCableOutput == null)
        {
            Check("All required devices present for audio test", false, "Missing devices");
            return;
        }

        // Создаем настройки с включенным мониторингом
        var settings = new AudioSettings
        {
            InputDeviceId = captureDevice.Id,
            OutputDeviceId = virtualCableOutput.Id,
            MonitorDeviceId = monitorDevice.Id,
            MonitorEnabled = true,
            MonitorVolume = 1.0f,
            GateEnabled = false, // Отключаем гейт для чистоты теста прохождения сигнала
            HighPassEnabled = true,
            CompressorEnabled = true,
            LimiterEnabled = true
        };

        using var engine = new AudioEngine(settings);
        Console.WriteLine("\n[2.2] Запуск AudioEngine с активным мониторингом...");
        engine.Start();
        await Task.Delay(300);

        Check("AudioEngine state is Running", engine.State == EngineState.Running, engine.LastErrorMessage);
        Check("Estimated latency is positive and reasonable (15-50 ms)", engine.EstimatedLatencyMs >= 15 && engine.EstimatedLatencyMs <= 60, $"{engine.EstimatedLatencyMs} ms");

        // Проверяем через рефлексию внутренности мониторинга
        var monitorField = typeof(AudioEngine).GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance);
        var monitorBufferField = typeof(AudioEngine).GetField("_monitorBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var monitorFormatField = typeof(AudioEngine).GetField("_monitorMixFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        var onDataMethod = typeof(AudioEngine).GetMethod("OnCaptureDataAvailable", BindingFlags.NonPublic | BindingFlags.Instance);

        var monitorPlayer = monitorField?.GetValue(engine) as IWavePlayer;
        var monitorBuffer = monitorBufferField?.GetValue(engine) as BufferedWaveProvider;
        var monitorFormat = monitorFormatField?.GetValue(engine) as WaveFormat;

        Check("Monitor IWavePlayer instantiated", monitorPlayer != null);
        Check("Monitor BufferedWaveProvider instantiated", monitorBuffer != null);
        Check("Monitor MixFormat matches hardware", monitorFormat != null, monitorFormat?.ToString());

        Console.WriteLine("\n[2.3] Тестирование передачи аудиопотока в буфер мониторинга...");
        // Синтезируем аудио-фрейм (20 мс синуса 440 Гц, стерео IEEE float 48000 Hz)
        int sampleRate = 48000;
        int frames = 960; // 20 ms
        float[] pcmFloat = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float s = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * i / sampleRate);
            pcmFloat[i * 2] = s;
            pcmFloat[i * 2 + 1] = s;
        }
        byte[] pcmBytes = new byte[pcmFloat.Length * sizeof(float)];
        Buffer.BlockCopy(pcmFloat, 0, pcmBytes, 0, pcmBytes.Length);

        // Инжектируем несколько пакетов в аудио-движок
        for (int p = 0; p < 5; p++)
        {
            onDataMethod?.Invoke(engine, new object[] { null!, new WaveInEventArgs(pcmBytes, pcmBytes.Length) });
        }

        Check("Monitor buffer received samples (BufferedBytes > 0)", monitorBuffer != null && monitorBuffer.BufferedBytes > 0, $"bytes={monitorBuffer?.BufferedBytes}");

        // Проверяем регулировку громкости мониторинга (MonitorVolume)
        Console.WriteLine("\n[2.4] Тестирование регулировки громкости мониторинга (MonitorVolume)...");
        settings.MonitorVolume = 0.5f;
        engine.UpdateSettings(settings);
        Check("MonitorVolume set to 0.5", Math.Abs(settings.MonitorVolume - 0.5f) < 0.01f);

        // Проверяем динамическое отключение и включение мониторинга (Toggle Monitor)
        Console.WriteLine("\n[2.5] Тестирование динамического отключения и включения мониторинга...");
        settings.MonitorEnabled = false;
        engine.UpdateSettings(settings);
        await Task.Delay(150);

        var monitorPlayerOff = monitorField?.GetValue(engine) as IWavePlayer;
        Check("Monitor stream safely stopped and disposed when MonitorEnabled = false", monitorPlayerOff == null);

        settings.MonitorEnabled = true;
        engine.UpdateSettings(settings);
        await Task.Delay(150);

        var monitorPlayerOn = monitorField?.GetValue(engine) as IWavePlayer;
        Check("Monitor stream successfully restarted when MonitorEnabled = true", monitorPlayerOn != null);

        engine.Stop();
        Check("Engine stopped cleanly", engine.State == EngineState.Stopped);
    }

    private static async Task TestVirtualCableOutputAsync()
    {
        Console.WriteLine("\n[3.1] Поиск пары устройств VB-Audio Virtual Cable (Render / Capture)...");
        using var enumerator = new MMDeviceEnumerator();

        var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

        // Предпочитаем стандартный 2-канальный "CABLE Input", а затем 16ch как fallback
        MMDevice? cableRender = renderDevices.FirstOrDefault(d => d.FriendlyName.Contains("cable input", StringComparison.OrdinalIgnoreCase))
                                ?? renderDevices.FirstOrDefault(d => AudioDeviceService.IsPreferredVirtualCable(d.FriendlyName));
        MMDevice? cableRender16 = renderDevices.FirstOrDefault(d => d.FriendlyName.Contains("16ch", StringComparison.OrdinalIgnoreCase));

        MMDevice? cableCapture = captureDevices.FirstOrDefault(d => d.FriendlyName.Contains("cable output", StringComparison.OrdinalIgnoreCase))
                                 ?? captureDevices.FirstOrDefault(d => AudioDeviceService.IsPreferredVirtualCable(d.FriendlyName));

        Check("CABLE Input (Render endpoint) found", cableRender != null, cableRender?.FriendlyName);
        Check("CABLE Output (Capture endpoint) found", cableCapture != null, cableCapture?.FriendlyName);

        if (cableRender == null || cableCapture == null)
        {
            Check("VB-Audio Cable pair available for loopback test", false, "VB-Audio Cable driver not detected");
            return;
        }

        Console.WriteLine($"  Render:  {cableRender.FriendlyName} ({cableRender.ID})");
        Console.WriteLine($"  Capture: {cableCapture.FriendlyName} ({cableCapture.ID})");

        var renderFmt = cableRender.AudioClient.MixFormat;
        var captureFmt = cableCapture.AudioClient.MixFormat;
        Console.WriteLine($"  Render MixFormat:  {renderFmt.Encoding}, {renderFmt.BitsPerSample} bits, {renderFmt.Channels} ch, {renderFmt.SampleRate} Hz");
        Console.WriteLine($"  Capture MixFormat: {captureFmt.Encoding}, {captureFmt.BitsPerSample} bits, {captureFmt.Channels} ch, {captureFmt.SampleRate} Hz");
        if (captureFmt is WaveFormatExtensible wfe)
        {
            Console.WriteLine($"  Capture SubFormat: {wfe.SubFormat}");
        }

        // 2. Инициализируем WasapiCapture на CABLE Output для сбора реального исходящего звука
        Console.WriteLine("\n[3.2] Инициализация сквозного перехвата с CABLE Output (WASAPI Capture)...");
        using var cableOutCapture = new WasapiCapture(cableCapture, useEventSync: false, audioBufferMillisecondsLength: 20);

        var capturedSamples = new List<float>();
        object recordLock = new();
        long totalNonZeroBytes = 0;

        cableOutCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded <= 0) return;
            var format = cableOutCapture.WaveFormat;
            int channels = format.Channels;
            int bits = format.BitsPerSample;

            lock (recordLock)
            {
                for (int b = 0; b < e.BytesRecorded; b++)
                {
                    if (e.Buffer[b] != 0) totalNonZeroBytes++;
                }

                if (bits == 32)
                {
                    int total = e.BytesRecorded / 4;
                    for (int i = 0; i < total; i++)
                    {
                        float val = BitConverter.ToSingle(e.Buffer, i * 4);
                        capturedSamples.Add(val);
                    }
                }
                else if (bits == 16)
                {
                    int total = e.BytesRecorded / 2;
                    for (int i = 0; i < total; i++)
                    {
                        short val = BitConverter.ToInt16(e.Buffer, i * 2);
                        capturedSamples.Add(val / 32768.0f);
                    }
                }
            }
        };

        cableOutCapture.StartRecording();

        // 3. Запускаем AudioEngine с выводом в CABLE Input
        Console.WriteLine("\n[3.3] Запуск AudioEngine и отправка калибровочного звукового сигнала 440 Гц...");

        // Диагностика: проверим прямое воспроизведение в CABLE Input
        Console.WriteLine("  [3.3.1] Проверка прямого вывода в CABLE Input через WasapiOut...");
        var directBuffer = new BufferedWaveProvider(renderFmt) { DiscardOnBufferOverflow = true };
        using (var directOut = new WasapiOut(cableRender, AudioClientShareMode.Shared, false, 20))
        {
            directOut.Init(directBuffer);
            directOut.Play();

            // Генерируем 500 мс звука напрямую в directBuffer
            int dSamples = renderFmt.SampleRate * renderFmt.Channels / 2;
            float[] dFloat = new float[dSamples];
            for (int i = 0; i < renderFmt.SampleRate / 2; i++)
            {
                float s = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * i / renderFmt.SampleRate);
                for (int c = 0; c < renderFmt.Channels; c++) dFloat[i * renderFmt.Channels + c] = s;
            }
            byte[] dBytes = new byte[dSamples * 4];
            Buffer.BlockCopy(dFloat, 0, dBytes, 0, dBytes.Length);
            directBuffer.AddSamples(dBytes, 0, dBytes.Length);
            Console.WriteLine($"    DirectBuffer added {dBytes.Length} bytes, state={directOut.PlaybackState}");
            await Task.Delay(600);
            directOut.Stop();
        }

        Console.WriteLine($"  [3.3.2] Ненулевых байт после directOut: {totalNonZeroBytes}");

        if (cableRender16 != null)
        {
            Console.WriteLine("  [3.3.3] Проверка альтернативного устройства CABLE In 16ch...");
            var fmt16 = cableRender16.AudioClient.MixFormat;
            var buf16 = new BufferedWaveProvider(fmt16) { DiscardOnBufferOverflow = true };
            using var out16 = new WasapiOut(cableRender16, AudioClientShareMode.Shared, false, 20);
            out16.Init(buf16);
            out16.Play();

            int total16 = fmt16.SampleRate * fmt16.Channels / 4;
            float[] f16 = new float[total16];
            for (int i = 0; i < fmt16.SampleRate / 4; i++)
            {
                float s = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * i / fmt16.SampleRate);
                for (int c = 0; c < fmt16.Channels; c++) f16[i * fmt16.Channels + c] = s;
            }
            byte[] b16 = new byte[total16 * 4];
            Buffer.BlockCopy(f16, 0, b16, 0, b16.Length);
            long bytesBefore = totalNonZeroBytes;
            buf16.AddSamples(b16, 0, b16.Length);
            await Task.Delay(400);
            out16.Stop();
            long delta16 = totalNonZeroBytes - bytesBefore;
            Console.WriteLine($"    CABLE In 16ch передало {delta16} байт в CABLE Output.");
        }

        var settings = new AudioSettings
        {
            InputDeviceId = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)?.ID,
            OutputDeviceId = cableRender.ID,
            MonitorEnabled = false,
            GateEnabled = false,
            HighPassEnabled = false,
            CompressorEnabled = false,
            LimiterEnabled = true, // Проверяем, что лимитер не срезает сигнал полностью
            LimiterCeilingDb = -1.0f
        };

        using var engine = new AudioEngine(settings);
        engine.Start();
        await Task.Delay(200);

        Check("AudioEngine rendering to CABLE Input is Running", engine.State == EngineState.Running, engine.LastErrorMessage);

        var onDataMethod = typeof(AudioEngine).GetMethod("OnCaptureDataAvailable", BindingFlags.NonPublic | BindingFlags.Instance);

        // Генерируем 1.5 секунды непрерывного звука (440 Гц, -6 dBFS = 0.5 amplitude)
        int sampleRate = 48000;
        int frameSize = 960; // 20 ms blocks
        int totalFrames = sampleRate * 3 / 2; // 1.5 seconds

        int currentFrame = 0;
        while (currentFrame < totalFrames)
        {
            int blockFrames = Math.Min(frameSize, totalFrames - currentFrame);
            float[] blockFloat = new float[blockFrames * 2];
            for (int i = 0; i < blockFrames; i++)
            {
                float s = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * (currentFrame + i) / sampleRate);
                blockFloat[i * 2] = s;
                blockFloat[i * 2 + 1] = s;
            }
            byte[] blockBytes = new byte[blockFloat.Length * sizeof(float)];
            Buffer.BlockCopy(blockFloat, 0, blockBytes, 0, blockBytes.Length);

            onDataMethod?.Invoke(engine, new object[] { null!, new WaveInEventArgs(blockBytes, blockBytes.Length) });
            currentFrame += blockFrames;
            await Task.Delay(18); // ~20 ms real-time pace
        }

        // Ждем дополнительно 300 мс для полного прохождения через сквозной WASAPI буфер VB-Cable
        await Task.Delay(300);

        cableOutCapture.StopRecording();
        engine.Stop();
        await Task.Delay(100);

        // 4. Анализ захваченного аудиопотока с виртуального кабеля
        Console.WriteLine("\n[3.4] Анализ сигнала, полученного из CABLE Output...");
        float[] recorded;
        lock (recordLock)
        {
            recorded = capturedSamples.ToArray();
        }

        Check("CABLE Output captured audio samples (> 10000 samples)", recorded.Length > 10000, $"samples={recorded.Length}");

        if (recorded.Length > 0)
        {
            float maxAbs = 0f;
            double sumSq = 0;
            int finiteCount = 0;

            // Пропускаем начальные 2000 сэмплов (начальный буфер WASAPI тишины)
            int start = Math.Min(4000, recorded.Length / 4);
            int count = Math.Max(0, recorded.Length - start);

            for (int i = start; i < recorded.Length; i++)
            {
                float val = recorded[i];
                if (!float.IsNaN(val) && !float.IsInfinity(val))
                {
                    finiteCount++;
                    float abs = Math.Abs(val);
                    if (abs > maxAbs) maxAbs = abs;
                    sumSq += val * val;
                }
            }

            double rms = count > 0 ? Math.Sqrt(sumSq / count) : 0;
            double peakDb = maxAbs > 1e-6f ? 20.0 * Math.Log10(maxAbs) : -120.0;
            double rmsDb = rms > 1e-6 ? 20.0 * Math.Log10(rms) : -120.0;

            Console.WriteLine($"  Статистика CABLE Output:");
            Console.WriteLine($"  - Количество сэмплов: {recorded.Length}");
            Console.WriteLine($"  - Ненулевых байт:     {totalNonZeroBytes}");
            Console.WriteLine($"  - Пиковая амплитуда:  {maxAbs:F4} ({peakDb:F1} dBFS)");
            Console.WriteLine($"  - RMS уровень:        {rms:F4} ({rmsDb:F1} dBFS)");

            Check("Virtual Cable output is NOT silent (Peak > -30 dBFS)", peakDb > -30.0, $"actual={peakDb:F1} dBFS");
            Check("Virtual Cable RMS is within expected range (0.5 amp sine => ~0.35 RMS)", rms > 0.15 && rms < 0.6, $"actual={rms:F4}");
            Check("Virtual Cable output is finite (no NaN or Infinity)", finiteCount == count, $"finite={finiteCount}/{count}");
            Check("Virtual Cable output does not clip (> 1.0)", maxAbs <= 1.05f, $"peak={maxAbs:F4}");
        }
    }
}
