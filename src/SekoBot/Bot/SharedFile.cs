using System.Text;
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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                T data;
                try
                {
                    data = file.Length > 0 ? JsonSerializer.Deserialize<T>(file) ?? new T() : new T();
                }
                catch (JsonException)
                {
                    data = new T(); // broken: started anew
                }
                change(data);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
                file.SetLength(0);
                file.Position = 0;
                file.Write(bytes);
                return data;
            }
            catch (IOException)
            {
                Thread.Sleep(50); // another SekoBot holds it for a moment
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }
}
