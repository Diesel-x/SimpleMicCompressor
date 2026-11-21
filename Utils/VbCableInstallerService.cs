using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MicCompressor.Audio;

namespace MicCompressor.Utils;

public static class VbCableInstallerService
{
    private const string DownloadUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip";

    public static async Task<(bool success, string message)> InstallOrReinstallAsync(bool isReinstall, Action<string>? onStatusUpdate = null)
    {
        try
        {
            string tempDir = Path.GetTempPath();
            string zipPath = Path.Combine(tempDir, "VBCable_Pack46.zip");
            string extractDir = Path.Combine(tempDir, "VBCable_Setup");

            // 1. Download if not already cached
            string setupExe = Path.Combine(extractDir, Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe");

            if (!File.Exists(setupExe))
            {
                onStatusUpdate?.Invoke("Загрузка драйвера SMC virtual cable...");
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                using var response = await httpClient.GetAsync(DownloadUrl);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                onStatusUpdate?.Invoke("Распаковка файлов драйвера...");
                if (Directory.Exists(extractDir))
                {
                    try { Directory.Delete(extractDir, true); } catch { }
                }
                ZipFile.ExtractToDirectory(zipPath, extractDir, true);
            }

            if (!File.Exists(setupExe))
            {
                return (false, "Файл установщика VBCABLE_Setup_x64.exe не найден в архиве.");
            }

            // 2. If reinstalling, uninstall first
            if (isReinstall)
            {
                onStatusUpdate?.Invoke("Удаление предыдущей версии драйвера...");
                var uninstPsi = new ProcessStartInfo
                {
                    FileName = setupExe,
                    Arguments = "-u",
                    Verb = "runas", // UAC elevation
                    UseShellExecute = true
                };
                try
                {
                    using var uninstProc = Process.Start(uninstPsi);
                    if (uninstProc != null)
                    {
                        await uninstProc.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Отменено пользователем или ошибка UAC: {ex.Message}");
                }
                await Task.Delay(1000);
            }

            // 3. Install
            onStatusUpdate?.Invoke("Установка драйвера SMC virtual cable (подтвердите UAC)...");
            var instPsi = new ProcessStartInfo
            {
                FileName = setupExe,
                Arguments = "-i",
                Verb = "runas", // UAC elevation
                UseShellExecute = true
            };

            try
            {
                using var instProc = Process.Start(instPsi);
                if (instProc != null)
                {
                    await instProc.WaitForExitAsync();
                    if (instProc.ExitCode != 0)
                    {
                        return (false, $"Установщик завершился с кодом ошибки {instProc.ExitCode}. Возможно, установка была отменена или заблокирована.");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Отменено пользователем или ошибка UAC: {ex.Message}");
            }

            onStatusUpdate?.Invoke("Проверка аудиоустройств...");
            await Task.Delay(1500);

            return (true, isReinstall
                ? "SMC virtual cable успешно переустановлен! Рекомендуется перезагрузить ПК."
                : "SMC virtual cable успешно установлен и готов к работе!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка установки: {ex.Message}");
        }
    }
}
