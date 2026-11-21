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
    private MMDevice? _activeCaptureDevice;
    private MMDevice? _activeOutputDevice;
    private MMDevice? _activeMonitorDevice;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _outputBuffer;

    private IWavePlayer? _monitor;
    private BufferedWaveProvider? _monitorBuffer;
    private string? _requestedMonitorDeviceId;

    private AudioChain? _chain;
    private AudioSettings _settings;

    private sealed record MonitorTarget(BufferedWaveProvider Buffer, WaveFormat MixFormat);
    private MonitorTarget? _monitorTarget;

    private int _acceptingAudio = 0;
    private int _activeCallbacks = 0;

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
    private float[] _outPrevFrame = new float[8];
    private bool _outHasPrevFrame = false;
    private float[] _monPrevFrame = new float[8];
    private bool _monHasPrevFrame = false;

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
            Volatile.Write(ref _settings, settings);
            if (_chain != null && _capture != null)
            {
                _chain.UpdateSettings(settings, _capture.WaveFormat.SampleRate);
            }

            if (_state == EngineState.Running)
            {
                if (settings.MonitorEnabled)
                {
                    if (_monitor == null || !string.Equals(_requestedMonitorDeviceId, settings.MonitorDeviceId, StringComparison.Ordinal))
                    {
                        StartMonitorInternal();
                    }
                }
                else if (_monitor != null)
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
                _activeCaptureDevice = captureDevice;

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

                // 2. Output Device (virtual cable only; never dump mic directly to physical speakers)
                MMDevice? outputDevice = ResolveVirtualOutputDevice(enumerator, _settings.OutputDeviceId);
                if (outputDevice != null)
                {
                    _activeOutputDevice = outputDevice;
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
                        _output = null;
                        _outputLatencyMs = 20;
                        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
                        _output.Init(_outputBuffer);
                    }

                    _output.Play();
                }
                else
                {
                    _outputLatencyMs = 0;
                }

                // 3. Monitor
                if (_settings.MonitorEnabled)
                {
                    StartMonitorInternal();
                }

                // 4. Hook events and start
                Volatile.Write(ref _acceptingAudio, 1);
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
                ScheduleAutoRecovery();
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

            using var enumerator = new MMDeviceEnumerator();
            var monDevice = ResolveMonitorDevice(enumerator, _settings.MonitorDeviceId);
            if (monDevice == null) return;
            if (_activeOutputDevice != null && string.Equals(monDevice.ID, _activeOutputDevice.ID, StringComparison.Ordinal))
            {
                // Main output is already playing to this exact device; avoid duplicate monitor stream/echo
                monDevice.Dispose();
                _requestedMonitorDeviceId = _settings.MonitorDeviceId;
                return;
            }
            _activeMonitorDevice = monDevice;
            _requestedMonitorDeviceId = _settings.MonitorDeviceId;

            var deviceMixFormat = monDevice.AudioClient.MixFormat;
            _monitorMixFormat = deviceMixFormat;
            _monitorBuffer = new BufferedWaveProvider(deviceMixFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(150),
                DiscardOnBufferOverflow = true
            };

            // 1. Try WasapiOut (polling, then event-sync)
            WasapiOut? wasapiMon = null;
            bool wasapiStarted = false;
            foreach (bool useEventSync in new[] { false, true })
            {
                try
                {
                    wasapiMon = new WasapiOut(monDevice, AudioClientShareMode.Shared, useEventSync: useEventSync, latency: 30);
                    wasapiMon.Init(_monitorBuffer);
                    wasapiMon.Play();
                    _monitor = wasapiMon;
                    wasapiStarted = true;
                    break;
                }
                catch
                {
                    try { wasapiMon?.Dispose(); } catch { }
                    wasapiMon = null;
                }
            }

            // 2. Fallback to WaveOutEvent using standard 16-bit PCM (MME rejects WaveFormatExtensible / 32-bit float)
            if (!wasapiStarted)
            {
                int pcmChannels = Math.Clamp(deviceMixFormat.Channels, 1, 2);
                var pcmFormat = new WaveFormat(deviceMixFormat.SampleRate, 16, pcmChannels);
                _monitorMixFormat = pcmFormat;
                _monitorBuffer = new BufferedWaveProvider(pcmFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(150),
                    DiscardOnBufferOverflow = true
                };

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
                _monitor = waveOut;
                waveOut.Init(_monitorBuffer);
                waveOut.Play();
            }

            if (_monitorBuffer != null && _monitorMixFormat != null)
            {
                Volatile.Write(ref _monitorTarget, new MonitorTarget(_monitorBuffer, _monitorMixFormat));
            }
        }
        catch
        {
            StopMonitorInternal();
        }
    }

    private void StopMonitorInternal()
    {
        Volatile.Write(ref _monitorTarget, null);
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
            _requestedMonitorDeviceId = null;
            _monPhasePos = 0.0;
            _monHasPrevFrame = false;
            Array.Clear(_monPrevFrame);
            try { _activeMonitorDevice?.Dispose(); } catch { }
            _activeMonitorDevice = null;
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (Volatile.Read(ref _acceptingAudio) == 0 || e.BytesRecorded <= 0) return;

        Interlocked.Increment(ref _activeCallbacks);
        try
        {
            if (Volatile.Read(ref _acceptingAudio) == 0) return;

            var chain = _chain;
            var capture = _capture;
            var outputBuffer = _outputBuffer;
            var outputMixFormat = _outputMixFormat;
            if (chain == null || capture == null) return;

            var capFormat = capture.WaveFormat;
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
            chain.ProcessInterleaved(_inputFloatBuffer, 0, inTotalSamples, inChannels);

            // 3. Send to Output Buffer (virtual cable, if installed)
            int inSampleRate = capFormat.SampleRate;
            if (outputBuffer != null && outputMixFormat != null)
            {
                int outSampleRate = outputMixFormat.SampleRate;
                int outChannels = outputMixFormat.Channels;
                int maxOutFrames = (int)Math.Ceiling(inFrames * (double)outSampleRate / inSampleRate) + 2;
                EnsureOutputBuffer(maxOutFrames * outChannels);

                int outFrames = ResampleAndMapChannels(
                    _inputFloatBuffer, inFrames, inChannels, inSampleRate,
                    _outputFloatBuffer, maxOutFrames, outChannels, outSampleRate,
                    1.0f, ref _outPrevFrame, ref _outHasPrevFrame, ref _outPhasePos);
                int outTotalSamples = outFrames * outChannels;

                if (outTotalSamples > 0)
                {
                    int outBytesWritten = WriteFloatToBytes(_outputFloatBuffer, outTotalSamples, outputMixFormat, ref _outputBytes);
                    outputBuffer.AddSamples(_outputBytes, 0, outBytesWritten);
                }
            }

            // 4. Send to Monitor Buffer (independent resampling and volume control)
            var monTarget = Volatile.Read(ref _monitorTarget);
            var settings = Volatile.Read(ref _settings);
            if (monTarget != null && settings.MonitorEnabled)
            {
                int monSampleRate = monTarget.MixFormat.SampleRate;
                int monChannels = monTarget.MixFormat.Channels;
                int maxMonFrames = (int)Math.Ceiling(inFrames * (double)monSampleRate / inSampleRate) + 2;
                EnsureMonitorBuffer(maxMonFrames * monChannels);

                float monVol = settings.MonitorVolume;
                int monFrames = ResampleAndMapChannels(
                    _inputFloatBuffer, inFrames, inChannels, inSampleRate,
                    _monitorFloatBuffer, maxMonFrames, monChannels, monSampleRate,
                    monVol, ref _monPrevFrame, ref _monHasPrevFrame, ref _monPhasePos);
                int monTotalSamples = monFrames * monChannels;

                if (monTotalSamples > 0)
                {
                    int monBytesWritten = WriteFloatToBytes(_monitorFloatBuffer, monTotalSamples, monTarget.MixFormat, ref _monitorBytes);
                    monTarget.Buffer.AddSamples(_monitorBytes, 0, monBytesWritten);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeCallbacks);
        }
    }

    private static int ResampleAndMapChannels(
        float[] inBuffer, int inFrames, int inChannels, int inRate,
        float[] outBuffer, int maxOutFrames, int outChannels, int outRate,
        float volumeMultiplier, ref float[] prevFrame, ref bool hasPrevFrame, ref double phasePos)
    {
        if (inFrames <= 0 || inChannels <= 0 || outChannels <= 0 || maxOutFrames <= 0)
        {
            return 0;
        }

        if (prevFrame.Length < inChannels)
        {
            prevFrame = new float[inChannels];
            hasPrevFrame = false;
        }

        int lastBase = (inFrames - 1) * inChannels;

        if (inRate == outRate)
        {
            int frames = Math.Min(inFrames, maxOutFrames);
            for (int outFrame = 0; outFrame < frames; outFrame++)
            {
                int inBase = outFrame * inChannels;
                int outBase = outFrame * outChannels;

                float left = inBuffer[inBase] * volumeMultiplier;
                float right = inChannels >= 2 ? inBuffer[inBase + 1] * volumeMultiplier : left;

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

            Array.Copy(inBuffer, lastBase, prevFrame, 0, inChannels);
            hasPrevFrame = true;
            phasePos = 0.0;
            return frames;
        }

        double rateRatio = (double)inRate / outRate;
        double inPos = !hasPrevFrame && phasePos < 0.0 ? 0.0 : phasePos;
        double limit = inFrames - 1.0;
        int outFrameCount = 0;

        while (inPos < limit && outFrameCount < maxOutFrames)
        {
            double floorPos = Math.Floor(inPos);
            int inFrame0 = (int)floorPos;
            float frac = (float)(inPos - floorPos);

            float left0, right0;
            if (inFrame0 < 0)
            {
                left0 = prevFrame[0];
                right0 = inChannels >= 2 ? prevFrame[1] : left0;
            }
            else
            {
                int f0 = Math.Min(inFrame0, inFrames - 1);
                left0 = inBuffer[f0 * inChannels];
                right0 = inChannels >= 2 ? inBuffer[f0 * inChannels + 1] : left0;
            }

            int f1 = Math.Clamp(inFrame0 + 1, 0, inFrames - 1);
            float left1 = inBuffer[f1 * inChannels];
            float right1 = inChannels >= 2 ? inBuffer[f1 * inChannels + 1] : left1;

            float leftSample = (left0 + frac * (left1 - left0)) * volumeMultiplier;
            float rightSample = (right0 + frac * (right1 - right0)) * volumeMultiplier;

            int outBase = outFrameCount * outChannels;
            outBuffer[outBase] = Math.Clamp(leftSample, -1.0f, 1.0f);
            if (outChannels >= 2)
            {
                outBuffer[outBase + 1] = Math.Clamp(rightSample, -1.0f, 1.0f);
                for (int c = 2; c < outChannels; c++)
                {
                    outBuffer[outBase + c] = 0f;
                }
            }

            outFrameCount++;
            inPos += rateRatio;
        }

        Array.Copy(inBuffer, lastBase, prevFrame, 0, inChannels);
        hasPrevFrame = true;

        double nextPhase = inPos - inFrames;
        phasePos = double.IsFinite(nextPhase) && nextPhase >= -1.0 && nextPhase < rateRatio ? nextPhase : 0.0;
        return outFrameCount;
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

                try
                {
                    if (TryAutoRecover()) return;
                }
                catch { }
            }
        }, token);
    }

    private bool TryAutoRecover()
    {
        lock (_lock)
        {
            if (_state != EngineState.Error) return _state == EngineState.Running;
            Start();
            return _state == EngineState.Running;
        }
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

    private static MMDevice? ResolveVirtualOutputDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        var dev = ResolveDeviceById(enumerator, DataFlow.Render, deviceId);
        if (dev != null)
        {
            if (AudioDeviceService.IsVirtualAudioName(dev.FriendlyName))
            {
                return dev;
            }
            dev.Dispose();
        }

        var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        // 1. Prioritize official VB-Audio Cable Input
        foreach (var d in all)
        {
            if (string.IsNullOrWhiteSpace(d.FriendlyName)) continue;
            if (AudioDeviceService.IsPreferredVirtualCable(d.FriendlyName))
            {
                return d;
            }
        }

        // 2. Fall back to other virtual devices (e.g. Voicemeeter, VAC)
        foreach (var d in all)
        {
            if (string.IsNullOrWhiteSpace(d.FriendlyName)) continue;
            if (AudioDeviceService.IsVirtualAudioName(d.FriendlyName))
            {
                return d;
            }
        }

        return null;
    }

    private static MMDevice? ResolveMonitorDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        var dev = ResolveDeviceById(enumerator, DataFlow.Render, deviceId);
        if (dev != null) return dev;

        // Prefer non-virtual system default render endpoint for headphone monitoring
        try
        {
            var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (def != null && !string.IsNullOrWhiteSpace(def.FriendlyName) && !AudioDeviceService.IsVirtualAudioName(def.FriendlyName))
            {
                return def;
            }
            def?.Dispose();
        }
        catch { }

        var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var d in all)
        {
            if (!string.IsNullOrWhiteSpace(d.FriendlyName) && !AudioDeviceService.IsVirtualAudioName(d.FriendlyName))
            {
                return d;
            }
        }

        foreach (var d in all)
        {
            if (!string.IsNullOrWhiteSpace(d.FriendlyName)) return d;
        }

        return null;
    }

    private void CleanUp()
    {
        Volatile.Write(ref _acceptingAudio, 0);

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
                SpinWait.SpinUntil(() => Volatile.Read(ref _activeCallbacks) == 0, 500);
                _capture.Dispose();
            }
        }
        catch { }
        finally
        {
            SpinWait.SpinUntil(() => Volatile.Read(ref _activeCallbacks) == 0, 500);
            _capture = null;
            try { _activeCaptureDevice?.Dispose(); } catch { }
            _activeCaptureDevice = null;
        }

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
            try { _activeOutputDevice?.Dispose(); } catch { }
            _activeOutputDevice = null;
        }

        StopMonitorInternal();
        _chain?.Reset();
        _outPhasePos = 0.0;
        _monPhasePos = 0.0;
        _outHasPrevFrame = false;
        _monHasPrevFrame = false;
        Array.Clear(_outPrevFrame);
        Array.Clear(_monPrevFrame);
    }

    public void Dispose()
    {
        Stop();
    }
}
