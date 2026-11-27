using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SekoBot.Update;

// The LAN share is a mailbox. Each running bot owns a fresh address; an old button cannot
// address its replacement. The token and screenshots never pass through the share.
internal sealed class TelegramFleet
{
    internal sealed record Node(Guid Id, string Pc, int Number, string Character, DateTimeOffset At)
    {
        public string Label => $"{Pc} · {(Character.Length == 0 ? "PARDUS" : Character)} #{Number}";
        public string Button => $"{Label} [{Id.ToString("N")[..10]}]";
    }

    internal sealed record Request(Guid Target, string Character, string Action, long Update, DateTimeOffset At, string Text = "");
    private sealed record Envelope(string Data, string Mac);
    private readonly string _dir;
    private readonly byte[] _key;

    public TelegramFleet(string folder, string token, string chat)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new IOException("Ortak klasörün tam ağ yolunu gir.");
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes("SekoBot/fleet/v1/" + chat));
        _dir = Path.Combine(folder, "PARDUS-" + Convert.ToHexString(SHA256.HashData(_key))[..20]);
        Directory.CreateDirectory(_dir);
    }

    private void Write<T>(string path, T item, bool replace)
    {
        string data = JsonSerializer.Serialize(item);
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data));
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Envelope(data, Convert.ToHexString(mac))));
            File.Move(tmp, path, replace);
        }
        finally { File.Delete(tmp); }
    }

    private T? Read<T>(string path) where T : class
    {
        try
        {
            if (new FileInfo(path).Length > 65536) return null;
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            if (envelope?.Data == null || envelope.Mac == null) return null;
            var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(envelope.Data));
            if (!CryptographicOperations.FixedTimeEquals(mac, Convert.FromHexString(envelope.Mac))) return null;
            return JsonSerializer.Deserialize<T>(envelope.Data);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or FileNotFoundException) { return null; }
    }

    private static bool Fresh(DateTimeOffset at) => (DateTimeOffset.UtcNow - at).TotalSeconds is >= -15 and <= 45;
    public void Announce(Node node) => Write(Path.Combine(_dir, node.Id.ToString("N") + ".node"), node, true);
    public Node[] Online() => Directory.EnumerateFiles(_dir, "*.node").Select(Read<Node>)
        .OfType<Node>().Where(n => Fresh(n.At)).OrderBy(n => n.Pc).ThenBy(n => n.Number).ToArray();

    public void Send(Request command) => Write(Path.Combine(_dir, $"{command.Target:N}-{command.Update:D20}.cmd"), command, false);

    public List<Request> Take(Guid id)
    {
        var result = new List<Request>();
        foreach (string path in Directory.EnumerateFiles(_dir, $"{id:N}-*.cmd").Order())
        {
            var cmd = Read<Request>(path);
            File.Delete(path);
            if (cmd?.Target == id && Fresh(cmd.At)) result.Add(cmd);
        }
        return result;
    }

    public void Prune()
    {
        foreach (var path in Directory.EnumerateFiles(_dir))
            if (Path.GetExtension(path) is ".node" or ".cmd" or ".tmp" && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1))
                File.Delete(path);
    }
}
