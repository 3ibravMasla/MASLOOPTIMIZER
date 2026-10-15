using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public static class UpdateManager
{
    /// <summary>
    /// Версія застосунку — з версії збірки (csproj), щоб банер оновлення не розходився
    /// з реальним релізом (Фаза 5.5 плану продуктивності).
    /// </summary>
    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    private const string RepoOwner = "3ibravMasla";
    private const string RepoName = "MASLOOPTIMIZER";

    #region Фаза 5 — оновлення та мережа

    // 5.1: один HttpClient на весь застосунок — пул з'єднань замість сокета на кожен виклик.
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    // 5.1: single-flight — MainWindow і WidgetWindow не роблять два запити одночасно.
    private static readonly SemaphoreSlim SingleFlightGate = new(1, 1);

    // 5.2: збережений throttle у HKCU\Software\MASLOOPTIMIZER; 24 год — критерій
    // «не більше одного фонового запиту за 24 год»; ручна перевірка throttle ігнорує.
    private const string StateKeyPath = @"Software\MASLOOPTIMIZER";
    private const string LastCheckValueName = "LastUpdateCheckUtc";
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromHours(24);

    // Свіжий результат іншого вікна (≤ 2 хв) віддається без повторного запиту.
    private static readonly TimeSpan ResultCacheLifetime = TimeSpan.FromMinutes(2);
    private static (bool UpdateAvailable, string NewVersion, string DownloadUrl) _cachedResult = (false, CurrentVersion, string.Empty);
    private static DateTime _cachedResultUtc = DateTime.MinValue;

    #endregion

    /// <summary>
    /// Перевірка останнього релізу на GitHub.
    /// manual=false (фонові виклики): поважає quiet-режим (Фаза 5.3), збережений throttle (5.2)
    /// і single-flight (5.1). manual=true (кнопка «Перевірити оновлення») виконується завжди.
    /// </summary>
    public static async Task<(bool UpdateAvailable, string NewVersion, string DownloadUrl)> CheckForUpdateAsync(bool manual = false)
    {
        // 5.3: під час гри/Game Mode фоновий трафік заборонено — перевірка відкладається
        // до виходу з quiet-режиму (наступний тік таймера після гри).
        if (!manual && AppActivityCoordinator.IsQuietMode)
            return (false, CurrentVersion, string.Empty);

        if (!manual)
        {
            if (TryGetFreshCachedResult(out var earlyCached)) return earlyCached;
            if (!IsThrottleWindowOpen()) return (false, CurrentVersion, string.Empty);
        }

        await SingleFlightGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Поки чекали на шлюз — інше вікно могло вже виконати перевірку.
            if (!manual)
            {
                if (TryGetFreshCachedResult(out var cached)) return cached;
                if (!IsThrottleWindowOpen()) return (false, CurrentVersion, string.Empty);
            }

            try
            {
                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                string response = await SharedHttpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);

                // Позначку throttle оновлюємо лише за вдалого звернення (offline не «з'їдає» вікно).
                StampLastCheckUtc();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(response, options);

                var result = (UpdateAvailable: false, NewVersion: CurrentVersion, DownloadUrl: string.Empty);

                if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
                {
                    string cleanRemoteVer = release.TagName.TrimStart('v', 'V').Trim();

                    if (TryParseSemanticVersion(cleanRemoteVer, out var remoteVer) &&
                        TryParseSemanticVersion(CurrentVersion, out var localVer))
                    {
                        if (remoteVer > localVer)
                        {
                            string assetUrl = ResolveAssetDownloadUrl(release.Assets);

                            if (!string.IsNullOrWhiteSpace(assetUrl))
                            {
                                result = (UpdateAvailable: true, NewVersion: cleanRemoteVer, DownloadUrl: assetUrl);
                            }
                        }
                    }
                }

                _cachedResult = result;
                _cachedResultUtc = DateTime.UtcNow;
                return result;
            }
            catch
            {
                return (false, CurrentVersion, string.Empty);
            }
        }
        finally
        {
            SingleFlightGate.Release();
        }
    }

    /// <summary>
    /// Фаза 5.2/5.4: стартова перевірка виконується не одразу після логіна, а після кількох
    /// хвилин простою з випадковим jitter'ом, щоб логін не бив по мережі. Виклик не кидає.
    /// </summary>
    public static async Task WaitStartupJitterAsync()
    {
        var delay = TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(Random.Shared.Next(0, 361));
        try { await Task.Delay(delay).ConfigureAwait(false); } catch { }
    }

    /// <summary>Фаза 5.2: вікно throttle відкрите, якщо з останнього вдалого звернення минуло ≥ 24 год.</summary>
    private static bool IsThrottleWindowOpen()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKeyPath, writable: false);
            if (key?.GetValue(LastCheckValueName) is string raw &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastCheck))
            {
                return DateTime.UtcNow - lastCheck.ToUniversalTime() >= MinCheckInterval;
            }
        }
        catch { }

        // Позначки немає (перший запуск або новий профіль) — перевірку дозволено.
        return true;
    }

    /// <summary>Фаза 5.2: фіксує час вдалої перевірки для міжсесійного throttle.</summary>
    private static void StampLastCheckUtc()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StateKeyPath);
            key?.SetValue(LastCheckValueName,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                RegistryValueKind.String);
        }
        catch { }
    }

    /// <summary>Результат, щойно отриманий іншим вікном (≤ 2 хв), — без нового мережевого запиту.</summary>
    private static bool TryGetFreshCachedResult(out (bool UpdateAvailable, string NewVersion, string DownloadUrl) result)
    {
        var cached = _cachedResult;
        if (DateTime.UtcNow - _cachedResultUtc <= ResultCacheLifetime)
        {
            result = cached;
            return true;
        }

        result = (false, CurrentVersion, string.Empty);
        return false;
    }

    /// <summary>5.1: спільний клієнт із пулом з'єднань; таймаути задаються пер-запитом через CancellationTokenSource.</summary>
    private static HttpClient CreateSharedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MASLOOPTIMIZER", CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return client;
    }

    /// <summary>Фаза 5.5: версія береться з збірки — без ручного розсинхрону з csproj.</summary>
    private static string ResolveCurrentVersion()
    {
        try
        {
            var asm = typeof(UpdateManager).Assembly;
            string? ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(ver)) ver = asm.GetName().Version?.ToString(3);
            if (string.IsNullOrWhiteSpace(ver)) return "0.6.0";

            int plusIdx = ver.IndexOf('+'); // відкидаємо build-метадані («0.6.0+abcdef»)
            return plusIdx > 0 ? ver.Substring(0, plusIdx) : ver;
        }
        catch
        {
            return "0.6.0";
        }
    }

    public static async Task DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
    {
        try
        {
            string? currentExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath)) return;

            // Фаза 6: визначаємо тип релізу за URL (query-параметри відсікаємо).
            bool isZip = downloadUrl.Split('?')[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            if (isZip)
                await DownloadAndApplyZipAsync(downloadUrl, currentExePath, progress).ConfigureAwait(false);
            else
                await DownloadAndApplyExeAsync(downloadUrl, currentExePath, progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка процесу автооновлення: {ex.Message}", "ERROR");
        }
    }

    /// <summary>
    /// Фаза 6: модульне оновлення з .zip-архіву GitHub Release — завантаження у
    /// %TEMP%\MASLOOPTIMIZER_update.zip, розпакування у %TEMP%\MASLOOPTIMIZER_update_extracted
    /// та самозаміна всієї папки застосунку (exe + Scripts + tweaks.bundle.json) через .bat.
    /// </summary>
    private static async Task DownloadAndApplyZipAsync(string downloadUrl, string currentExePath, IProgress<double>? progress)
    {
        string tempDir = Path.GetTempPath();
        string zipPath = Path.Combine(tempDir, "MASLOOPTIMIZER_update.zip");
        string extractDir = Path.Combine(tempDir, "MASLOOPTIMIZER_update_extracted");
        string updaterBatPath = Path.Combine(tempDir, "maslo_updater.bat");

        // 1. Завантаження архіву (спільний клієнт із пулом з'єднань; довгий таймаут — через CTS).
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        using (var response = await SharedHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

            if (File.Exists(zipPath)) { try { File.Delete(zipPath); } catch { } }
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

            var buffer = new byte[16384];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
                totalRead += bytesRead;
                if (totalBytes > 0) progress?.Report((double)totalRead / totalBytes * 100);
            }
        }

        // 2. Розпакування (повторний запуск — чистимо попередню теку).
        if (Directory.Exists(extractDir)) { try { Directory.Delete(extractDir, true); } catch { } }
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        // 3. Визначаємо корінь вмісту (flat-архів або архів з єдиною кореневою підтекою).
        string sourceRoot = ResolveExtractedRoot(extractDir);

        // 4. Генеруємо .bat, який чекає виходу exe, копіює файли й перезапускає застосунок.
        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string batContent = BuildZipUpdaterScript(sourceRoot, baseDir, currentExePath, extractDir, zipPath);
        await File.WriteAllTextAsync(updaterBatPath, batContent).ConfigureAwait(false);

        LaunchUpdaterAndExit(updaterBatPath);
    }

    /// <summary>Fallback Фази 5: legacy single-file .exe-оновлення (стара логіка заміни бінарника).</summary>
    private static async Task DownloadAndApplyExeAsync(string downloadUrl, string currentExePath, IProgress<double>? progress)
    {
        string tempDir = Path.GetTempPath();
        string tempNewExePath = Path.Combine(tempDir, $"MASLOOPTIMIZER_vNext_{Guid.NewGuid():N}.exe");
        string updaterBatPath = Path.Combine(tempDir, "maslo_updater.bat");

        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        using (var response = await SharedHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            await using var fileStream = new FileStream(tempNewExePath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

            var buffer = new byte[16384];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    progress?.Report((double)totalRead / totalBytes * 100);
                }
            }
        }

        // Заміна timeout на ping для стабільності у NoWindow фоновому режимі
        string escapedExePath = EscapeBatchVariable(currentExePath);
        string escapedNewFile = EscapeBatchVariable(tempNewExePath);

        string batContent = $@"@echo off
chcp 65001 > nul

set ""TARGET={escapedExePath}""
set ""NEWFILE={escapedNewFile}""
set RETRIES=0

:loop
ping 127.0.0.1 -n 2 > nul
del /f /q ""%TARGET%"" > nul 2>&1
if not exist ""%TARGET%"" goto replace

set /a RETRIES+=1
if %RETRIES% leq 15 goto loop
goto cleanup

:replace
move /y ""%NEWFILE%"" ""%TARGET%"" > nul 2>&1
start """" ""%TARGET%""

:cleanup
del /f /q ""%NEWFILE%"" > nul 2>&1
del /f /q ""%~f0"" > nul 2>&1
";
        await File.WriteAllTextAsync(updaterBatPath, batContent).ConfigureAwait(false);
        LaunchUpdaterAndExit(updaterBatPath);
    }

    private static void LaunchUpdaterAndExit(string updaterBatPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = updaterBatPath,
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var updaterProcess = Process.Start(psi);
        if (updaterProcess == null)
        {
            AppLogger.Log("Не вдалося запустити скрипт автооновлення.", "ERROR");
            return;
        }

        Environment.Exit(0);
    }

    /// <summary>Архів може мати flat-структуру або єдину кореневу підтеку — визначаємо реальний корінь вмісту.</summary>
    private static string ResolveExtractedRoot(string extractDir)
    {
        string root = extractDir;
        for (int depth = 0; depth < 3; depth++)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(root); }
            catch { return root; }

            if (entries.Length == 1 && Directory.Exists(entries[0]))
            {
                root = entries[0];
                continue;
            }
            break;
        }
        return root;
    }

    /// <summary>
    /// Фаза 6: .bat-скрипт самозаміни модульного релізу. Чекає виходу MASLOOPTIMIZER.exe,
    /// копіює вміст розпакованого архіву (включно з папкою Scripts) у теку застосунку через robocopy,
    /// зберігаючи користувацький settings.json та папку Backups, перезапускає exe й чистить тимчасові файли.
    /// </summary>
    private static string BuildZipUpdaterScript(string sourceRoot, string baseDir, string exePath, string extractDir, string zipPath)
    {
        string src = EscapeBatchVariable(sourceRoot);
        string dest = EscapeBatchVariable(baseDir);
        string exe = EscapeBatchVariable(exePath);
        string extract = EscapeBatchVariable(extractDir);
        string zip = EscapeBatchVariable(zipPath);

        return $@"@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

set ""SRC={src}""
set ""DEST={dest}""
set ""EXE={exe}""
set RETRIES=0

:waitloop
tasklist /FI ""IMAGENAME eq MASLOOPTIMIZER.exe"" 2>NUL | find /I ""MASLOOPTIMIZER.exe"" >NUL
if errorlevel 1 goto copyfiles
set /a RETRIES+=1
if !RETRIES! geq 40 goto copyfiles
ping 127.0.0.1 -n 2 > nul
goto waitloop

:copyfiles
robocopy ""%SRC%"" ""%DEST%"" /E /XF settings.json /XD Backups /R:2 /W:1 /NFL /NDL /NJH /NJS /NC /NS > nul
start """" ""%EXE%""

rd /s /q ""{extract}"" > nul 2>&1
del /f /q ""{zip}"" > nul 2>&1
del /f /q ""%~f0"" > nul 2>&1
";
    }

    private static string EscapeBatchVariable(string value)
    {
        return value
            .Replace("%", "%%")
            .Replace("^", "^^");
    }

    /// <summary>
    /// Фаза 6: вибір релізного асета — пріоритет модульний .zip (напр. MASLOOPTIMIZER-v0.6.0-win-x64.zip),
    /// fallback на legacy single-file .exe.
    /// </summary>
    private static string ResolveAssetDownloadUrl(GitHubAsset[]? assets)
    {
        if (assets == null || assets.Length == 0) return string.Empty;

        var zip = assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl));
        if (zip != null) return zip.BrowserDownloadUrl;

        var exe = assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl));
        return exe?.BrowserDownloadUrl ?? string.Empty;
    }

    private static bool TryParseSemanticVersion(string verStr, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(verStr)) return false;

        // Очищаємо можливі постфікси (наприклад, "-beta", "-hotfix")
        int dashIdx = verStr.IndexOf('-');
        if (dashIdx > 0) verStr = verStr.Substring(0, dashIdx);

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