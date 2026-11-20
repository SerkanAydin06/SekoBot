namespace SekoBot.Bot;

/// <summary>
/// How the wait before the pull (from the bite balloon to Space) went: every pull ever made
/// (balik-cekis.csv), grouped by that wait, so the best wait can be seen.
/// </summary>
internal static class FishPulls
{
    /// <summary>The groups' upper edges (seconds); the last group takes everything longer.</summary>
    public static readonly double[] Edges = [0.8, 1.1, 1.4, 1.7, 2.0];

    /// <summary>One group of pulls: waits from <paramref name="From"/> to <paramref name="To"/> seconds.</summary>
    public sealed record Bin(double From, double To, int Pulls, int Caught)
    {
        public double Rate => Pulls > 0 ? Caught / (double)Pulls : 0;
        public string Name => To >= 99 ? $"{From:0.0}+ sn" : From <= 0 ? $"< {To:0.0} sn" : $"{From:0.0}–{To:0.0} sn";
    }

    /// <summary>The pulls of the file (wait in ms, caught or not); lines without a wait are left out.</summary>
    public static List<(int Ms, bool Caught)> Read(string file)
    {
        var pulls = new List<(int, bool)>();
        try
        {
            if (!File.Exists(file))
                return pulls;
            foreach (string line in File.ReadLines(file).Skip(1))
            {
                var cells = line.Split(';');
                if (cells.Length >= 3 && int.TryParse(cells[1], out int ms) && ms > 0)
                    pulls.Add((ms, cells[2] == "yakalandı"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return pulls;
    }

    /// <summary>The pulls grouped by their wait (every group, empty ones too).</summary>
    public static List<Bin> Bins(IEnumerable<(int Ms, bool Caught)> pulls)
    {
        var bins = new List<Bin>();
        double from = 0;
        foreach (double to in Edges.Append(99))
        {
            var these = pulls.Where(p => p.Ms / 1000.0 >= from && p.Ms / 1000.0 < to).ToList();
            bins.Add(new Bin(from, to, these.Count, these.Count(p => p.Caught)));
            from = to;
        }
        return bins;
    }
}
