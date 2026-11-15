using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MicCompressor.Audio.Dsp;
using MicCompressor.Models;

namespace MicCompressor.Audio;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Error
}

public sealed class AudioEngine : IDisposable
{
    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _outputBuffer;

    private WasapiOut? _monitor;
    private BufferedWaveProvider? _monitorBuffer;

    private AudioChain? _chain;
    private AudioSettings _settings;

    private EngineState _state = EngineState.Stopped;
    private string? _lastErrorMessage;

    // Output target format parameters
    private WaveFormat? _outputMixFormat;
    private WaveFormat? _monitorMixFormat;

    // Scratch buffers
    private float[] _inputFloatBuffer = new float[8192];
    private float[] _outputFloatBuffer = new float[16384];
    private byte[] _outputBytes = new byte[65536];
    private byte[] _monitorBytes = new byte[65536];

    public EngineState State
    {
        get => _state;
        private set
        {
            _state = value;
            StateChanged?.Invoke(_state, _lastErrorMessage);
        }
    }

    public string? LastErrorMessage => _lastErrorMessage;
    public AudioMetrics? Metrics => _chain?.Metrics;

    public event Action<EngineState, string?>? StateChanged;

    public AudioEngine(AudioSettings settings)
    {
        _settings = settings;
    }

    public void UpdateSettings(AudioSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            if (_chain != null && _capture != null)
            {
                _chain.UpdateSettings(settings, _capture.WaveFormat.SampleRate);
            }

            if (_state == EngineState.Running)
            {
                if (settings.MonitorEnabled && _monitor == null && !string.IsNullOrEmpty(settings.MonitorDeviceId))
                {
                    StartMonitorInternal();
                }
                else if (!settings.MonitorEnabled && _monitor != null)
                {
                    StopMonitorInternal();
                }
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_state == EngineState.Running || _state == EngineState.Starting) return;

            State = EngineState.Starting;
            _lastErrorMessage = null;

            try
            {
                using var enumerator = new MMDeviceEnumerator();

                // 1. Resolve Capture Device
                MMDevice? captureDevice = ResolveCaptureDevice(enumerator, _settings.InputDeviceId);
                if (captureDevice == null)
                {
                    throw new InvalidOperationException("Микрофон не найден. Подключите микрофон и нажмите 'Обновить'.");
                }

                // Try event-driven capture first, fallback to timer
                try
                {
                    _capture = new WasapiCapture(captureDevice, useEventSync: true, audioBufferMillisecondsLength: 20);
                }
                catch
                {
                    _capture = new WasapiCapture(captureDevice, useEventSync: false, audioBufferMillisecondsLength: 25);
                }

                var capFormat = _capture.WaveFormat;
                _chain = new AudioChain(_settings, capFormat.SampleRate);

                // 2. Resolve Output Device (Virtual Cable or Default Render)
                MMDevice? outputDevice = ResolveRenderDevice(enumerator, _settings.OutputDeviceId);
                if (outputDevice == null)
                {
                    throw new InvalidOperationException("Устройство вывода не найдено.");
                }

                _outputMixFormat = outputDevice.AudioClient.MixFormat;

                // Try timer-driven WASAPI first for output (most compatible with virtual cables like VB-Cable)
                try
                {
                    _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: false, latency: 25);
                }
                catch
                {
                    _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 30);
                }

                _outputBuffer = new BufferedWaveProvider(_outputMixFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(150),
                    DiscardOnBufferOverflow = true
                };

                _output.Init(_outputBuffer);
                _output.Play();

                // 3. Optional Headphone Monitor
                if (_settings.MonitorEnabled && !string.IsNullOrEmpty(_settings.MonitorDeviceId))
                {
                    StartMonitorInternal();
                }

                // 4. Hook capture event & start recording
                _capture.DataAvailable += OnCaptureDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();

                State = EngineState.Running;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                CleanUp();
                State = EngineState.Error;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            CleanUp();
            State = EngineState.Stopped;
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private void StartMonitorInternal()
    {
        try
        {
            StopMonitorInternal();
            if (string.IsNullOrEmpty(_settings.MonitorDeviceId)) return;

            using var enumerator = new MMDeviceEnumerator();
            var monDevice = ResolveDeviceById(enumerator, DataFlow.Render, _settings.MonitorDeviceId);
            if (monDevice == null) return;

            _monitorMixFormat = monDevice.AudioClient.MixFormat;
            _monitor = new WasapiOut(monDevice, AudioClientShareMode.Shared, useEventSync: false, latency: 30);
            _monitorBuffer = new BufferedWaveProvider(_monitorMixFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(150),
                DiscardOnBufferOverflow = true
            };
            _monitor.Init(_monitorBuffer);
            _monitor.Play();
        }
        catch
        {
            StopMonitorInternal();
        }
    }

    private void StopMonitorInternal()
    {
        try
        {
            _monitor?.Stop();
            _monitor?.Dispose();
        }
        catch { }
        finally
        {
            _monitor = null;
            _monitorBuffer = null;
            _monitorMixFormat = null;
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _chain == null || _capture == null || _outputBuffer == null || _outputMixFormat == null) return;

        var capFormat = _capture.WaveFormat;
        int inChannels = capFormat.Channels;
        int inBytesPerSample = capFormat.BitsPerSample / 8;
        if (inBytesPerSample <= 0 || inChannels <= 0) return;

        int inTotalSamples = e.BytesRecorded / inBytesPerSample;
        int inFrames = inTotalSamples / inChannels;
        if (inFrames <= 0) return;

        EnsureInputBuffer(inTotalSamples);

        // 1. Convert input bytes to float samples
        if (capFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            var byteSpan = e.Buffer.AsSpan(0, e.BytesRecorded);
            var floatSpan = MemoryMarshal.Cast<byte, float>(byteSpan);
            floatSpan.CopyTo(_inputFloatBuffer.AsSpan(0, inTotalSamples));
        }
        else if (capFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            if (capFormat.BitsPerSample == 16)
            {
                int sampleIdx = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short val = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    _inputFloatBuffer[sampleIdx++] = val / 32768.0f;
                }
            }
            else if (capFormat.BitsPerSample == 24)
            {
                int sampleIdx = 0;
                for (int i = 0; i < e.BytesRecorded; i += 3)
                {
                    int val = (e.Buffer[i] << 8) | (e.Buffer[i + 1] << 16) | (e.Buffer[i + 2] << 24);
                    _inputFloatBuffer[sampleIdx++] = val / 2147483648.0f;
                }
            }
            else
            {
                return;
            }
        }
        else
        {
            return;
        }

        // 2. Process DSP Chain (HighPass -> Gate -> Compressor -> Limiter)
        _chain.ProcessInterleaved(_inputFloatBuffer, 0, inTotalSamples, inChannels);

        // 3. Resample / channel-map to Output MixFormat
        int outSampleRate = _outputMixFormat.SampleRate;
        int outChannels = _outputMixFormat.Channels;
        int inSampleRate = capFormat.SampleRate;

        int outFrames = (int)Math.Round((double)inFrames * outSampleRate / inSampleRate);
        int outTotalSamples = outFrames * outChannels;
        EnsureOutputBuffer(outTotalSamples);

        // High quality linear interpolation + channel mapping
        double rateRatio = (double)inSampleRate / outSampleRate;
        for (int outFrame = 0; outFrame < outFrames; outFrame++)
        {
            double inPos = outFrame * rateRatio;
            int inFrame0 = (int)inPos;
            int inFrame1 = Math.Min(inFrame0 + 1, inFrames - 1);
            float frac = (float)(inPos - inFrame0);

            // Compute mono or stereo processed sample
            float s0 = _inputFloatBuffer[inFrame0 * inChannels];
            float s1 = _inputFloatBuffer[inFrame1 * inChannels];
            float interpolatedLeft = s0 + frac * (s1 - s0);

            float interpolatedRight;
            if (inChannels >= 2)
            {
                float sr0 = _inputFloatBuffer[inFrame0 * inChannels + 1];
                float sr1 = _inputFloatBuffer[inFrame1 * inChannels + 1];
                interpolatedRight = sr0 + frac * (sr1 - sr0);
            }
            else
            {
                interpolatedRight = interpolatedLeft; // Duplicate mono to stereo
            }

            int outIdx = outFrame * outChannels;
            _outputFloatBuffer[outIdx] = interpolatedLeft;
            if (outChannels >= 2)
            {
                _outputFloatBuffer[outIdx + 1] = interpolatedRight;
                // If more than 2 channels (e.g. 5.1/7.1 virtual endpoints), clear remaining
                for (int c = 2; c < outChannels; c++)
                {
                    _outputFloatBuffer[outIdx + c] = 0f;
                }
            }
        }

        // 4. Send to Output Buffer (IEEE Float or 16-bit PCM depending on mix format)
        int outByteCount = WriteFloatToBytes(_outputFloatBuffer, outTotalSamples, _outputMixFormat, ref _outputBytes);
        _outputBuffer.AddSamples(_outputBytes, 0, outByteCount);

        // 5. Send to Monitor if enabled
        if (_monitorBuffer != null && _monitorMixFormat != null && _settings.MonitorEnabled)
        {
            float monVol = _settings.MonitorVolume;
            if (Math.Abs(monVol - 1.0f) > 0.02f)
            {
                // Scale volume for monitor
                for (int i = 0; i < outTotalSamples; i++)
                {
                    _outputFloatBuffer[i] *= monVol;
                }
            }
            int monByteCount = WriteFloatToBytes(_outputFloatBuffer, outTotalSamples, _monitorMixFormat, ref _monitorBytes);
            _monitorBuffer.AddSamples(_monitorBytes, 0, monByteCount);
        }
    }

    private static int WriteFloatToBytes(float[] floatBuf, int sampleCount, WaveFormat format, ref byte[] byteBuf)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int bytesNeeded = sampleCount * sizeof(float);
            if (byteBuf.Length < bytesNeeded) byteBuf = new byte[bytesNeeded];
            var floatSpan = floatBuf.AsSpan(0, sampleCount);
            var byteSpan = MemoryMarshal.Cast<float, byte>(floatSpan);
            byteSpan.CopyTo(byteBuf.AsSpan(0, bytesNeeded));
            return bytesNeeded;
        }
        else // 16-bit PCM fallback
        {
            int bytesNeeded = sampleCount * 2;
            if (byteBuf.Length < bytesNeeded) byteBuf = new byte[bytesNeeded];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)Math.Clamp(floatBuf[i] * 32767.0f, -32768f, 32767f);
                byteBuf[i * 2] = (byte)(sample & 0xFF);
                byteBuf[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return bytesNeeded;
        }
    }

    private void EnsureInputBuffer(int samples)
    {
        if (_inputFloatBuffer.Length < samples)
        {
            _inputFloatBuffer = new float[Math.Max(samples, _inputFloatBuffer.Length * 2)];
        }
    }

    private void EnsureOutputBuffer(int samples)
    {
        if (_outputFloatBuffer.Length < samples)
        {
            _outputFloatBuffer = new float[Math.Max(samples, _outputFloatBuffer.Length * 2)];
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_state == EngineState.Running)
        {
            if (e.Exception != null)
            {
                _lastErrorMessage = e.Exception.Message;
                State = EngineState.Error;
            }
            else
            {
                State = EngineState.Stopped;
            }
        }
    }

    private static MMDevice? ResolveDeviceById(MMDeviceEnumerator enumerator, DataFlow dataFlow, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;

        try
        {
            var dev = enumerator.GetDevice(deviceId);
            if (dev != null && dev.State == DeviceState.Active && !string.IsNullOrWhiteSpace(dev.FriendlyName))
            {
                return dev;
            }
        }
        catch
        {
            // Device removed / invalid
        }
        return null;
    }

    private static MMDevice? ResolveCaptureDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        var dev = ResolveDeviceById(enumerator, DataFlow.Capture, deviceId);
        if (dev != null) return dev;

        try
        {
            var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            if (def != null && !string.IsNullOrWhiteSpace(def.FriendlyName)) return def;
        }
        catch { }

        try
        {
            var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            if (def != null && !string.IsNullOrWhiteSpace(def.FriendlyName)) return def;
        }
        catch { }

        // Pick first active with valid name
        var all = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (var d in all)
        {
            if (!string.IsNullOrWhiteSpace(d.FriendlyName)) return d;
        }

        return null;
    }

    private static MMDevice? ResolveRenderDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        var dev = ResolveDeviceById(enumerator, DataFlow.Render, deviceId);
        if (dev != null) return dev;

        // Try to automatically find a virtual cable
        var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var d in all)
        {
            if (string.IsNullOrWhiteSpace(d.FriendlyName)) continue;
            if (AudioDeviceService.IsVirtualAudioName(d.FriendlyName))
            {
                return d;
            }
        }

        try
        {
            var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (def != null && !string.IsNullOrWhiteSpace(def.FriendlyName)) return def;
        }
        catch { }

        foreach (var d in all)
        {
            if (!string.IsNullOrWhiteSpace(d.FriendlyName)) return d;
        }

        return null;
    }

    private void CleanUp()
    {
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnCaptureDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch { }
        finally { _capture = null; }

        try
        {
            _output?.Stop();
            _output?.Dispose();
        }
        catch { }
        finally
        {
            _output = null;
            _outputBuffer = null;
            _outputMixFormat = null;
        }

        StopMonitorInternal();
        _chain?.Reset();
    }

    public void Dispose()
    {
        Stop();
    }
}
