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
                    using (var client = dev.AudioClient)
                    {
                        if (client == null) continue;
                        var fmt = client.MixFormat;
                        if (fmt == null) continue;
                    }

                    bool isDefault = defaultId != null && dev.ID == defaultId;
                    bool isVirtual = IsVirtualAudioName(name);
                    string displayName = FormatDeviceDisplayName(name, isDefault, isVirtual);

                    result.Add(new DeviceInfo(dev.ID, displayName, isDefault, isVirtual));
                }
                catch
                {
                    // Skip broken / ghost devices
                }
                finally
                {
                    try { dev.Dispose(); } catch { }
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

                    using (var client = dev.AudioClient)
                    {
                        if (client == null) continue;
                        var fmt = client.MixFormat;
                        if (fmt == null) continue;
                    }

                    bool isDefault = defaultId != null && dev.ID == defaultId;
                    bool isVirtual = IsVirtualAudioName(name);
                    string displayName = FormatDeviceDisplayName(name, isDefault, isVirtual);

                    result.Add(new DeviceInfo(dev.ID, displayName, isDefault, isVirtual));
                }
                catch
                {
                    // Skip broken / ghost devices
                }
                finally
                {
                    try { dev.Dispose(); } catch { }
                }
            }
        }
        catch
        {
            // Fallback
        }

        return result;
    }

    private static string FormatDeviceDisplayName(string name, bool isDefault, bool isVirtual)
    {
        string defTag = Localization.LocalizationService.Instance["Routing_Default"];
        if (IsPreferredVirtualCable(name))
        {
            return isDefault ? $"SMC virtual cable [{defTag}]" : "SMC virtual cable";
        }

        string displayName = name;
        string virtTag = Localization.LocalizationService.Instance["Routing_VirtualCable"];
        if (isDefault) displayName += $" [{defTag}]";
        if (isVirtual) displayName += $" [{virtTag}]";
        return displayName;
    }

    public static bool IsPreferredVirtualCable(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("smc virtual") || lower.Contains("cable") || lower.Contains("vb-audio");
    }

    public static bool IsVirtualAudioName(string name)
    {
        if (IsPreferredVirtualCable(name)) return true;
        string lower = name.ToLowerInvariant();
        return lower.Contains("voicemeeter") ||
               lower.Contains("virtual") ||
               lower.Contains("sonar") ||
               lower.Contains("vac");
    }
}
