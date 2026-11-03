using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SekoBot.Update;

internal sealed record PreparedUpdate(string Version, string FilePath);
internal sealed record UpdateCheckResult(PreparedUpdate? Update, string? Error);

/// <summary>
/// Silent auto-update from the project-owned GitHub preview release.
/// Downloads to a sibling temporary file and verifies SHA-256 before replacing anything.
/// </summary>
internal static class UpdateService
{
    private const string ManifestUrl =
        "https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.manifest.json";
    private const string ExecutableUrl =
        "https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SekoBot", "0.4"));
        // Never get a cached manifest that still names the previous build.
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return client;
    }

    private sealed class UpdateManifest
    {
        public string? Commit { get; set; }
        public string? Version { get; set; }
        public string? Sha256 { get; set; }
        public long Length { get; set; }
    }

    public static async Task<UpdateCheckResult> CheckAndDownloadAsync(
        CancellationToken token = default)
    {
        // Only published release builds auto-update. Developers never overwrite build output.
        if (BuildInfo.Commit.Length != 40)
            return new UpdateCheckResult(null, null);

        string? executable = Environment.ProcessPath;
        // Any file name works ("SekoBot (1).exe" from a browser download included).
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new UpdateCheckResult(null, "Otomatik güncelleme: çalışan dosya bulunamadı.");

        // Clean up the backup retained by a successful previous replacement.
        try { File.Delete(executable + ".old"); } catch { }

        string tempFile = Path.Combine(Path.GetDirectoryName(executable)!, ".SekoBot.update.exe");
        try
        {
            using var manifestResponse = await Client.GetAsync(
                ManifestUrl, HttpCompletionOption.ResponseHeadersRead, token);
            if (!manifestResponse.IsSuccessStatusCode)
                return new UpdateCheckResult(null,
                    $"Güncelleme bilgisi okunamadı (HTTP {(int)manifestResponse.StatusCode}).");

            await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(token);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
                manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Commit) ||
                string.IsNullOrWhiteSpace(manifest.Sha256) ||
                manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit) ||
                manifest.Length < 200_000 || manifest.Length > 400_000_000)
                return new UpdateCheckResult(null, "Güncelleme bildirimi geçersiz.");

            if (string.Equals(BuildInfo.Commit, manifest.Commit, StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckResult(null, null);

            using var response = await Client.GetAsync(
                ExecutableUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write,
                FileShare.None, 131_072, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, token);
            }

            var downloaded = new FileInfo(tempFile);
            if (downloaded.Length != manifest.Length)
                throw new InvalidDataException("Güncellemenin dosya boyutu doğrulanamadı.");

            await using var checkFile = new FileStream(tempFile, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131_072, FileOptions.Asynchronous);
            byte[] hash = await SHA256.HashDataAsync(checkFile, token);
            if (!CryptographicOperations.FixedTimeEquals(
                hash, Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("Güncellemenin SHA-256 değeri doğrulanamadı.");

            return new UpdateCheckResult(new PreparedUpdate(manifest.Version ?? manifest.Commit[..7], tempFile), null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(null, null);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            return new UpdateCheckResult(null, "Otomatik güncelleme başarısız: " + ex.Message);
        }
    }

    /// <summary>
    /// An EXE cannot reliably replace itself while running. Launch Windows cmd
    /// to wait until this process (by PID) has exited, swap verified files, and restart.
    /// </summary>
    public static void InstallAfterExit(PreparedUpdate update)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Çalıştırılabilir dosya bulunamadı.");
        if (!File.Exists(update.FilePath))
            throw new FileNotFoundException("Doğrulanmış güncelleme dosyası kayboldu.", update.FilePath);

        string updaterFile = Path.Combine(Path.GetTempPath(),
            "SekoBot-updater-" + Guid.NewGuid().ToString("N") + ".cmd");

        const string script = """
@echo off
setlocal DisableDelayedExpansion
set "target=%~1"
set "replacement=%~2"
set "old=%~1.old"
set "pid=%~3"
rem Wait (max ~30 s) until the old SekoBot has really exited: a running EXE can be renamed,
rem so without this the new one could start while the old one still owns the F10/F11 hotkeys.
for /L %%I in (1,1,30) do (
  tasklist /FI "PID eq %pid%" /NH 2>NUL | find " %pid% " >NUL || goto swap
  ping -n 2 127.0.0.1 >NUL
)
:swap
for /L %%I in (1,1,60) do (
  ping -n 2 127.0.0.1 >NUL
  move /Y "%target%" "%old%" >NUL 2>&1
  if not exist "%target%" (
    move /Y "%replacement%" "%target%" >NUL 2>&1
    if exist "%target%" (
      start "" "%target%"
      exit /B 0
    )
    if exist "%old%" move /Y "%old%" "%target%" >NUL 2>&1
  )
)
exit /B 1
""";

        File.WriteAllText(updaterFile, script, new UTF8Encoding(false));
        var launch = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        launch.ArgumentList.Add("/D");
        launch.ArgumentList.Add("/C");
        launch.ArgumentList.Add(updaterFile);
        launch.ArgumentList.Add(executable);
        launch.ArgumentList.Add(update.FilePath);
        launch.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        _ = Process.Start(launch)
            ?? throw new InvalidOperationException("Windows güncelleme işlemi başlatılamadı.");
    }
}
