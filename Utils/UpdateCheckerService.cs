using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MicCompressor.Localization;

namespace MicCompressor.Utils;

public static class UpdateCheckerService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/Diesel-x/SimpleMicCompressor/releases/latest";
    private const string DefaultReleasesPageUrl = "https://github.com/Diesel-x/SimpleMicCompressor/releases";
    private static bool _hasCheckedThisSession = false;

    public static async Task CheckForUpdatesAsync()
    {
        if (_hasCheckedThisSession) return;
        _hasCheckedThisSession = true;

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add("User-Agent", "SimpleMicCompressor");

            var response = await client.GetAsync(GitHubApiUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElem)) return;
            string? tagName = tagElem.GetString();
            if (string.IsNullOrWhiteSpace(tagName)) return;

            string releaseUrl = root.TryGetProperty("html_url", out var urlElem) && urlElem.GetString() is { } u
                ? u
                : DefaultReleasesPageUrl;

            var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 0, 0);

            if (VersionHelper.IsNewerVersion(tagName, currentVer))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var loc = LocalizationService.Instance;
                    string title = loc["Update_Available_Title"];
                    string message = loc.Get("Update_Available_Message", tagName);

                    var result = MessageBox.Show(
                        message,
                        title,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                        }
                        catch
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(DefaultReleasesPageUrl) { UseShellExecute = true });
                            }
                            catch { }
                        }
                    }
                });
            }
        }
        catch
        {
            // Fail silently: never disturb user if offline or GitHub API is throttled
        }
    }
}
