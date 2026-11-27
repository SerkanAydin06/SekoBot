using System.Diagnostics;

namespace SekoBot.UI;

/// <summary>
/// PARDUS started with Windows: a scheduled task at the user's sign-in, run with the highest rights (PARDUS needs them
/// to read the game; a plain "Run" entry of a program that asks for them is skipped by Windows at sign-in). Started so,
/// it gets <see cref="Argument"/> and goes on with the last character (the game opened from its launcher, logged in
/// with the game's own saved details — the password is never touched — and the work started again).
/// </summary>
internal static class AutoStart
{
    public const string Argument = "--autostart";

    private static string TaskName => Instance.Number == 1 ? "PARDUS" : $"PARDUS-{Instance.Number}";

    /// <summary>Started by Windows at sign-in (not by the user).</summary>
    public static bool StartedByWindows => Environment.GetCommandLineArgs().Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>The task made (or taken away); the error in words, or null.</summary>
    public static string? Set(bool on)
    {
        string? exe = Environment.ProcessPath;
        if (on && exe == null)
            return "PARDUS'un dosyası bulunamadı.";
        string args = on
            ? $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" {Argument}\" /SC ONLOGON /RL HIGHEST /DELAY 0000:30"
            : $"/Delete /F /TN \"{TaskName}\"";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });
            if (p == null)
                return "Görev Zamanlayıcı açılamadı.";
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            // Taking away a task that isn't there is no error.
            return p.ExitCode == 0 || !on ? null : error.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
