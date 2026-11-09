using SekoBot.Config;

namespace SekoBot.UI;

/// <summary>
/// The log also goes to %LOCALAPPDATA%\SekoBot\gunluk.txt (with the date), so what happened can be
/// looked at later. Over 2 MB the file is moved to gunluk-eski.txt and a new one is started.
/// Writing problems are ignored (the log window still shows everything).
/// </summary>
internal static class LogFile
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(ProfileStore.Directory, "gunluk.txt");

    public static void Append(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ProfileStore.Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(ProfileStore.Directory, "gunluk-eski.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd} {line}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
