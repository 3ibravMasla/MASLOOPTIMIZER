using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MASLOOPTIMIZER;

public static class UpdateManager
{
    public const string CurrentVersion = "0.3.1";
    private const string RepoOwner = "3ibravMasla";
    private const string RepoName = "MASLOOPTIMIZER";

    /// <summary>
    /// Безпечна перевірка релізів на GitHub API
    /// </summary>
    public static async Task<(bool UpdateAvailable, string NewVersion, string DownloadUrl)> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MASLOOPTIMIZER", CurrentVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            client.Timeout = TimeSpan.FromSeconds(6);

            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await client.GetStringAsync(url);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(response, options);

            if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
            {
                string cleanRemoteVer = release.TagName.TrimStart('v', 'V').Trim();
                string cleanLocalVer = CurrentVersion.TrimStart('v', 'V').Trim();

                if (TryParseSemanticVersion(cleanRemoteVer, out var remoteVer) &&
                    TryParseSemanticVersion(cleanLocalVer, out var localVer))
                {
                    if (remoteVer > localVer)
                    {
                        // Шукаємо готовий бінарник .exe в Assets
                        string exeUrl = release.Assets?
                            .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?
                            .BrowserDownloadUrl ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(exeUrl))
                        {
                            return (true, cleanRemoteVer, exeUrl);
                        }
                    }
                }
            }
        }
        catch
        {
            // Безпечний фолбек за відсутності мережі або ліміту GitHub API
        }

        return (false, CurrentVersion, string.Empty);
    }

    /// <summary>
    /// Автономне завантаження, підміна та перезапуск бінарника
    /// </summary>
    public static async Task DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
    {
        try
        {
            string? currentExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath)) return;

            string tempDir = Path.GetTempPath();
            string tempNewExePath = Path.Combine(tempDir, $"MASLOOPTIMIZER_vNext_{Guid.NewGuid():N}.exe");
            string updaterBatPath = Path.Combine(tempDir, "maslo_updater.bat");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempNewExePath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

                var buffer = new byte[16384];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes * 100);
                    }
                }
            }

            // Надійний батник оновлення з циклом очікування звільнення файлу
            string batContent = $@"@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

set ""TARGET={currentExePath}""
set ""NEWFILE={tempNewExePath}""
set RETRIES=0

:loop
timeout /t 1 /nobreak > nul
del /f /q ""!TARGET!"" > nul 2>&1
if not exist ""!TARGET!"" goto replace

set /a RETRIES+=1
if !RETRIES! leq 15 goto loop
goto cleanup

:replace
move /y ""!NEWFILE!"" ""!TARGET!"" > nul 2>&1
start """" ""!TARGET!""

:cleanup
del ""!NEWFILE!"" > nul 2>&1
del ""%~f0"" > nul 2>&1
";
            await File.WriteAllTextAsync(updaterBatPath, batContent);

            var psi = new ProcessStartInfo
            {
                FileName = updaterBatPath,
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка процесу автооновлення: {ex.Message}", "ERROR");
        }
    }

    private static bool TryParseSemanticVersion(string verStr, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(verStr)) return false;

        var parts = verStr.Split('.');
        if (parts.Length == 1) verStr += ".0.0";
        else if (parts.Length == 2) verStr += ".0";

        return Version.TryParse(verStr, out version!);
    }

    #region Моделі GitHub API

    public class GitHubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    #endregion
}