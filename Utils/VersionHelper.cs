using System;

namespace MicCompressor.Utils;

public static class VersionHelper
{
    public static bool IsNewerVersion(string latestTag, Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(latestTag)) return false;
        string clean = latestTag.Trim().TrimStart('v', 'V');

        // Remove pre-release tags if any (e.g. 1.2.0-beta -> 1.2.0)
        int dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0) clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        if (parts.Length == 1) clean += ".0.0";
        else if (parts.Length == 2) clean += ".0";

        if (Version.TryParse(clean, out var parsedLatest))
        {
            var curNorm = new Version(Math.Max(0, currentVersion.Major), Math.Max(0, currentVersion.Minor), Math.Max(0, currentVersion.Build));
            var latNorm = new Version(Math.Max(0, parsedLatest.Major), Math.Max(0, parsedLatest.Minor), Math.Max(0, parsedLatest.Build));
            return latNorm > curNorm;
        }
        return false;
    }
}
