using SekoBot.Config;
using System.Text.Json;

namespace SekoBot.Bot;

/// <summary>
/// A JSON file several SekoBots learn into at the same time (one per game): read, changed and
/// written back while it is held, so no one's learning is lost and everyone gets the others'.
/// </summary>
internal static class SharedFile
{
    /// <summary>Reads the file (empty/new when missing or broken), lets <paramref name="change"/> apply
    /// this SekoBot's news to it, writes it back; all while no other SekoBot can touch it.</summary>
    /// <returns>The file as now written (what everyone knows), or null when it couldn't be reached.</returns>
    public static T? Update<T>(string path, Action<T> change) where T : class, new()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Replacing the JSON changes its file identity. Keep the lock on a stable sibling.
            using var gate = Acquire(path + ".lock");
            if (gate == null) return null;

            T data = new();
            if (File.Exists(path))
            {
                try
                {
                    using var input = File.OpenRead(path);
                    if (input.Length != 0)
                        data = JsonSerializer.Deserialize<T>(input) ?? new T();
                }
                catch (JsonException)
                {
                    File.Copy(path, path + ".bozuk", overwrite: true);
                }
            }
            change(data);
            Trouble.WriteSafely(path, JsonSerializer.Serialize(data));
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trouble.Tell("Ortak dosya güncellenemedi: " + Path.GetFileName(path), ex);
            return null;
        }
    }

    private static FileStream? Acquire(string path)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
        }
        return null;
    }
}
