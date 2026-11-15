using NAudio.CoreAudioApi;

namespace MicCompressor.Audio;

public record DeviceInfo(string Id, string Name, bool IsDefault, bool IsVirtualCable)
{
    public override string ToString() => Name;
}

public static class AudioDeviceService
{
    public static List<DeviceInfo> GetCaptureDevices()
    {
        var result = new List<DeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultId = defaultDev?.ID;
            }
            catch
            {
                try
                {
                    using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    defaultId = defaultDev?.ID;
                }
                catch { }
            }

            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var dev in devices)
            {
                try
                {
                    string name = dev.FriendlyName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Test if AudioClient can be initialized without error
                    var client = dev.AudioClient;
                    if (client == null) continue;
                    var fmt = client.MixFormat;
                    if (fmt == null) continue;

                    bool isDefault = defaultId != null && dev.ID == defaultId;
                    bool isVirtual = IsVirtualAudioName(name);

                    string displayName = name;
                    if (isDefault) displayName += " [По умолчанию]";
                    if (isVirtual) displayName += " [Виртуальный кабель]";

                    result.Add(new DeviceInfo(dev.ID, displayName, isDefault, isVirtual));
                }
                catch
                {
                    // Skip broken / ghost devices
                }
            }
        }
        catch
        {
            // Fallback
        }

        return result;
    }

    public static List<DeviceInfo> GetRenderDevices()
    {
        var result = new List<DeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDev?.ID;
            }
            catch
            {
                try
                {
                    using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
                    defaultId = defaultDev?.ID;
                }
                catch { }
            }

            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                try
                {
                    string name = dev.FriendlyName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var client = dev.AudioClient;
                    if (client == null) continue;
                    var fmt = client.MixFormat;
                    if (fmt == null) continue;

                    bool isDefault = defaultId != null && dev.ID == defaultId;
                    bool isVirtual = IsVirtualAudioName(name);

                    string displayName = name;
                    if (isDefault) displayName += " [По умолчанию]";
                    if (isVirtual) displayName += " [Виртуальный кабель]";

                    result.Add(new DeviceInfo(dev.ID, displayName, isDefault, isVirtual));
                }
                catch
                {
                    // Skip broken / ghost devices
                }
            }
        }
        catch
        {
            // Fallback
        }

        return result;
    }

    public static bool IsVirtualAudioName(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("cable") ||
               lower.Contains("vb-audio") ||
               lower.Contains("voicemeeter") ||
               lower.Contains("virtual") ||
               lower.Contains("sonar") ||
               lower.Contains("vac");
    }
}
