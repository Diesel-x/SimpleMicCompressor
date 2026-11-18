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

    private IWavePlayer? _monitor;
    private BufferedWaveProvider? _monitorBuffer;

    private AudioChain? _chain;
    private AudioSettings _settings;

    private EngineState _state = EngineState.Stopped;
    private string? _lastErrorMessage;

    // Target formats
    private WaveFormat? _outputMixFormat;
    private WaveFormat? _monitorMixFormat;

    // Scratch buffers
    private float[] _inputFloatBuffer = new float[16384];
    private float[] _outputFloatBuffer = new float[16384];
    private float[] _monitorFloatBuffer = new float[16384];
    private byte[] _outputBytes = new byte[65536];
    private byte[] _monitorBytes = new byte[65536];

    private int _captureBufferMs = 15;
    private int _outputLatencyMs = 20;

    private static readonly Guid SubFormatIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private double _outPhasePos = 0.0;
    private double _monPhasePos = 0.0;

    public int EstimatedLatencyMs => _state == EngineState.Running ? (_captureBufferMs + _outputLatencyMs) : 0;

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

            CleanUp();

            State = EngineState.Starting;
            _lastErrorMessage = null;

            try
            {
                using var enumerator = new MMDeviceEnumerator();

                // 1. Capture Device
                MMDevice? captureDevice = ResolveCaptureDevice(enumerator, _settings.InputDeviceId);
                if (captureDevice == null)
                {
                    throw new InvalidOperationException("Микрофон не найден. Проверьте подключение.");
                }

                try
                {
                    _captureBufferMs = 10;
                    _capture = new WasapiCapture(captureDevice, useEventSync: true, audioBufferMillisecondsLength: 10);
                }
                catch
                {
                    _captureBufferMs = 15;
                    _capture = new WasapiCapture(captureDevice, useEventSync: false, audioBufferMillisecondsLength: 15);
                }

                var capFormat = _capture.WaveFormat;
                _chain = new AudioChain(_settings, capFormat.SampleRate);

                // 2. Output Device
                MMDevice? outputDevice = ResolveRenderDevice(enumerator, _settings.OutputDeviceId);
                if (outputDevice == null)
                {
                    throw new InvalidOperationException("Устройство вывода не найдено.");
                }

                _outputMixFormat = outputDevice.AudioClient.MixFormat;

                _outputBuffer = new BufferedWaveProvider(_outputMixFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(150),
                    DiscardOnBufferOverflow = true
                };

                try
                {
                    _outputLatencyMs = 15;
                    _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: false, latency: 15);
                    _output.Init(_outputBuffer);
                }
                catch
                {
                    try { _output?.Dispose(); } catch { }
                    _outputLatencyMs = 20;
                    _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
                    _output.Init(_outputBuffer);
                }

                _output.Play();

                // 3. Monitor
                if (_settings.MonitorEnabled && !string.IsNullOrEmpty(_settings.MonitorDeviceId))
                {
                    StartMonitorInternal();
                }

                // 4. Hook events and start
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

            // If monitor device is the exact same as output device, output is already playing there!
            // But if user wants to use separate monitor output:
            using var enumerator = new MMDeviceEnumerator();
            var monDevice = ResolveDeviceById(enumerator, DataFlow.Render, _settings.MonitorDeviceId);
            if (monDevice == null) return;

            _monitorMixFormat = monDevice.AudioClient.MixFormat;
            _monitorBuffer = new BufferedWaveProvider(_monitorMixFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(150),
                DiscardOnBufferOverflow = true
            };

            // Try WasapiOut first
            try
            {
                var wasapiMon = new WasapiOut(monDevice, AudioClientShareMode.Shared, useEventSync: false, latency: 30);
                wasapiMon.Init(_monitorBuffer);
                wasapiMon.Play();
                _monitor = wasapiMon;
            }
            catch
            {
                // Fallback to WaveOutEvent to avoid WASAPI exclusive/device conflicts
                int waveOutDev = -1;
                for (int d = 0; d < WaveOut.DeviceCount; d++)
                {
                    try
                    {
                        var caps = WaveOut.GetCapabilities(d);
                        if (!string.IsNullOrEmpty(caps.ProductName) && monDevice.FriendlyName.Contains(caps.ProductName, StringComparison.OrdinalIgnoreCase))
                        {
                            waveOutDev = d;
                            break;
                        }
                    }
                    catch { }
                }
                var waveOut = new WaveOutEvent { DeviceNumber = waveOutDev, DesiredLatency = 40 };
                waveOut.Init(_monitorBuffer);
                waveOut.Play();
                _monitor = waveOut;
            }
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
        int inBits = capFormat.BitsPerSample;
        int inBytesPerSample = inBits / 8;
        if (inBytesPerSample <= 0 || inChannels <= 0) return;

        int inTotalSamples = e.BytesRecorded / inBytesPerSample;
        int inFrames = inTotalSamples / inChannels;
        if (inFrames <= 0) return;

        EnsureInputBuffer(inTotalSamples);

        // 1. Convert input bytes to float samples
        // Check if format is IEEE float vs PCM integer
        bool isFloat = capFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (capFormat is WaveFormatExtensible wfe && wfe.SubFormat == SubFormatIeeeFloat);

        if (inBits == 32)
        {
            if (isFloat)
            {
                var byteSpan = e.Buffer.AsSpan(0, e.BytesRecorded);
                var floatSpan = MemoryMarshal.Cast<byte, float>(byteSpan);
                floatSpan.CopyTo(_inputFloatBuffer.AsSpan(0, inTotalSamples));
            }
            else
            {
                // 32-bit PCM integer
                int sampleIdx = 0;
                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    int val = e.Buffer[i] | (e.Buffer[i + 1] << 8) | (e.Buffer[i + 2] << 16) | (e.Buffer[i + 3] << 24);
                    _inputFloatBuffer[sampleIdx++] = val / 2147483648.0f;
                }
            }
        }
        else if (inBits == 16)
        {
            int sampleIdx = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short val = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                _inputFloatBuffer[sampleIdx++] = val / 32768.0f;
            }
        }
        else if (inBits == 24)
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

        // 2. DSP chain processing (High-Pass -> Gate -> Compressor -> Limiter)
        _chain.ProcessInterleaved(_inputFloatBuffer, 0, inTotalSamples, inChannels);

        // 3. Send to Output Buffer (resampled and channel-mapped)
        int inSampleRate = capFormat.SampleRate;
        int outSampleRate = _outputMixFormat.SampleRate;
        int outChannels = _outputMixFormat.Channels;
        int outFrames = (int)Math.Round((double)inFrames * outSampleRate / inSampleRate);
        int outTotalSamples = outFrames * outChannels;
        EnsureOutputBuffer(outTotalSamples);

        ResampleAndMapChannels(_inputFloatBuffer, inFrames, inChannels, inSampleRate,
                               _outputFloatBuffer, outFrames, outChannels, outSampleRate, 1.0f, ref _outPhasePos);

        int outBytesWritten = WriteFloatToBytes(_outputFloatBuffer, outTotalSamples, _outputMixFormat, ref _outputBytes);
        _outputBuffer.AddSamples(_outputBytes, 0, outBytesWritten);

        // 4. Send to Monitor Buffer (independent resampling and volume control)
        if (_monitorBuffer != null && _monitorMixFormat != null && _settings.MonitorEnabled)
        {
            int monSampleRate = _monitorMixFormat.SampleRate;
            int monChannels = _monitorMixFormat.Channels;
            int monFrames = (int)Math.Round((double)inFrames * monSampleRate / inSampleRate);
            int monTotalSamples = monFrames * monChannels;
            EnsureMonitorBuffer(monTotalSamples);

            float monVol = _settings.MonitorVolume;
            ResampleAndMapChannels(_inputFloatBuffer, inFrames, inChannels, inSampleRate,
                                   _monitorFloatBuffer, monFrames, monChannels, monSampleRate, monVol, ref _monPhasePos);

            int monBytesWritten = WriteFloatToBytes(_monitorFloatBuffer, monTotalSamples, _monitorMixFormat, ref _monitorBytes);
            _monitorBuffer.AddSamples(_monitorBytes, 0, monBytesWritten);
        }
    }

    private static void ResampleAndMapChannels(
        float[] inBuffer, int inFrames, int inChannels, int inRate,
        float[] outBuffer, int outFrames, int outChannels, int outRate,
        float volumeMultiplier, ref double phasePos)
    {
        if (inRate == outRate)
        {
            int frames = Math.Min(inFrames, outFrames);
            for (int outFrame = 0; outFrame < frames; outFrame++)
            {
                int inBase = outFrame * inChannels;
                int outBase = outFrame * outChannels;

                float left = inBuffer[inBase] * volumeMultiplier;
                float right = (inChannels >= 2 ? inBuffer[inBase + 1] : left);
                if (inChannels >= 2) right *= volumeMultiplier;

                outBuffer[outBase] = Math.Clamp(left, -1.0f, 1.0f);
                if (outChannels >= 2)
                {
                    outBuffer[outBase + 1] = Math.Clamp(right, -1.0f, 1.0f);
                    for (int c = 2; c < outChannels; c++)
                    {
                        outBuffer[outBase + c] = 0f;
                    }
                }
            }
            phasePos = 0.0;
            return;
        }

        double rateRatio = (double)inRate / outRate;
        double currentPos = phasePos;

        for (int outFrame = 0; outFrame < outFrames; outFrame++)
        {
            double inPos = currentPos + outFrame * rateRatio;
            int inFrame0 = (int)inPos;
            int inFrame1 = Math.Min(inFrame0 + 1, inFrames - 1);
            float frac = (float)(inPos - inFrame0);

            inFrame0 = Math.Clamp(inFrame0, 0, inFrames - 1);

            float left0 = inBuffer[inFrame0 * inChannels];
            float left1 = inBuffer[inFrame1 * inChannels];
            float leftSample = (left0 + frac * (left1 - left0)) * volumeMultiplier;

            float rightSample;
            if (inChannels >= 2)
            {
                float right0 = inBuffer[inFrame0 * inChannels + 1];
                float right1 = inBuffer[inFrame1 * inChannels + 1];
                rightSample = (right0 + frac * (right1 - right0)) * volumeMultiplier;
            }
            else
            {
                rightSample = leftSample;
            }

            int outBase = outFrame * outChannels;
            outBuffer[outBase] = Math.Clamp(leftSample, -1.0f, 1.0f);
            if (outChannels >= 2)
            {
                outBuffer[outBase + 1] = Math.Clamp(rightSample, -1.0f, 1.0f);
                for (int c = 2; c < outChannels; c++)
                {
                    outBuffer[outBase + c] = 0f;
                }
            }
        }

        phasePos = (currentPos + outFrames * rateRatio) - inFrames;
        if (phasePos < 0.0 || phasePos >= 1.0) phasePos = 0.0;
    }

    private static int WriteFloatToBytes(float[] floatBuf, int sampleCount, WaveFormat format, ref byte[] byteBuf)
    {
        bool isOutFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                          (format is WaveFormatExtensible wfe && wfe.SubFormat == SubFormatIeeeFloat);

        if (format.BitsPerSample == 32)
        {
            if (isOutFloat)
            {
                int bytesNeeded = sampleCount * sizeof(float);
                if (byteBuf.Length < bytesNeeded) byteBuf = new byte[bytesNeeded];
                var floatSpan = floatBuf.AsSpan(0, sampleCount);
                var byteSpan = MemoryMarshal.Cast<float, byte>(floatSpan);
                byteSpan.CopyTo(byteBuf.AsSpan(0, bytesNeeded));
                return bytesNeeded;
            }
            else
            {
                int bytesNeeded = sampleCount * 4;
                if (byteBuf.Length < bytesNeeded) byteBuf = new byte[bytesNeeded];
                for (int i = 0; i < sampleCount; i++)
                {
                    int sample = (int)Math.Clamp(floatBuf[i] * 2147483647.0f, -2147483648f, 2147483647f);
                    byteBuf[i * 4] = (byte)(sample & 0xFF);
                    byteBuf[i * 4 + 1] = (byte)((sample >> 8) & 0xFF);
                    byteBuf[i * 4 + 2] = (byte)((sample >> 16) & 0xFF);
                    byteBuf[i * 4 + 3] = (byte)((sample >> 24) & 0xFF);
                }
                return bytesNeeded;
            }
        }
        else if (format.BitsPerSample == 16)
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
        else if (format.BitsPerSample == 24)
        {
            int bytesNeeded = sampleCount * 3;
            if (byteBuf.Length < bytesNeeded) byteBuf = new byte[bytesNeeded];
            for (int i = 0; i < sampleCount; i++)
            {
                int sample = (int)Math.Clamp(floatBuf[i] * 8388607.0f, -8388608f, 8388607f);
                byteBuf[i * 3] = (byte)(sample & 0xFF);
                byteBuf[i * 3 + 1] = (byte)((sample >> 8) & 0xFF);
                byteBuf[i * 3 + 2] = (byte)((sample >> 16) & 0xFF);
            }
            return bytesNeeded;
        }
        return 0;
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

    private void EnsureMonitorBuffer(int samples)
    {
        if (_monitorFloatBuffer.Length < samples)
        {
            _monitorFloatBuffer = new float[Math.Max(samples, _monitorFloatBuffer.Length * 2)];
        }
    }

    private CancellationTokenSource? _autoRecoveryCts;

    private void ScheduleAutoRecovery()
    {
        _autoRecoveryCts?.Cancel();
        _autoRecoveryCts = new CancellationTokenSource();
        var token = _autoRecoveryCts.Token;

        Task.Run(async () =>
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    await Task.Delay(2500, token);
                }
                catch
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                if (_state == EngineState.Error)
                {
                    try
                    {
                        Start();
                        if (_state == EngineState.Running) return;
                    }
                    catch { }
                }
            }
        }, token);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            if (_state == EngineState.Running)
            {
                CleanUp();
                if (e.Exception != null)
                {
                    _lastErrorMessage = e.Exception.Message;
                    State = EngineState.Error;
                    ScheduleAutoRecovery();
                }
                else
                {
                    State = EngineState.Stopped;
                }
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
        catch { }
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
            _autoRecoveryCts?.Cancel();
            _autoRecoveryCts = null;
        }
        catch { }

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
        _outPhasePos = 0.0;
        _monPhasePos = 0.0;
    }

    public void Dispose()
    {
        Stop();
    }
}
