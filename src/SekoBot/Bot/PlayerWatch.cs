using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Another player farming our place: when someone has stayed around (within <see cref="NearRange"/>)
/// for the set minutes and the experience coming in has dropped by the set share against before
/// they came, it's time for another place. One player passing by changes nothing; a short gap
/// (they step out of sight for a moment) doesn't restart the clock. Pure logic.
/// </summary>
internal sealed class PlayerWatch
{
    public const double NearRange = 3000;                       // 30 m
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinBefore = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly List<(TimeSpan When, long Exp)> _exp = [];
    private TimeSpan _since = TimeSpan.MinValue, _lastSeen = TimeSpan.MinValue, _lastMove = TimeSpan.MinValue;

    /// <summary>Since when someone has been around (null: nobody now).</summary>
    public TimeSpan? Since => _since == TimeSpan.MinValue ? null : _since;

    public void Reset()
    {
        _exp.Clear();
        _since = _lastSeen = TimeSpan.MinValue;
    }

    /// <param name="totalExp">Experience gained this session so far.</param>
    /// <param name="players">The other players (not GMs) around.</param>
    /// <returns>A Turkish reason when it's time to move, else null.</returns>
    public string? Update(TimeSpan now, long totalExp, Vec3 me, IEnumerable<OtherPlayer> players, double minutes, double dropPercent)
    {
        _exp.Add((now, totalExp));
        _exp.RemoveAll(e => now - e.When > TimeSpan.FromMinutes(40));
        var near = players.Where(p => !p.IsGm && p.Position.DistanceXY(me) <= NearRange).ToList();
        if (near.Count > 0)
        {
            if (_since == TimeSpan.MinValue || now - _lastSeen > Gap)
                _since = now;
            _lastSeen = now;
        }
        else if (_since != TimeSpan.MinValue && now - _lastSeen > Gap)
            _since = TimeSpan.MinValue;

        if (minutes <= 0 || _since == TimeSpan.MinValue || now - _since < TimeSpan.FromMinutes(minutes) ||
            _lastMove != TimeSpan.MinValue && now - _lastMove < Cooldown)
            return null;
        // Experience a minute before they came, and since.
        var before = _exp.Where(e => e.When <= _since).ToList();
        if (before.Count < 2 || before[^1].When - before[0].When < MinBefore)
            return null;
        double rateBefore = (before[^1].Exp - before[0].Exp) / (before[^1].When - before[0].When).TotalMinutes;
        var after = _exp.Where(e => e.When >= _since).ToList();
        double rateNow = (after[^1].Exp - after[0].Exp) / Math.Max(0.1, (after[^1].When - after[0].When).TotalMinutes);
        if (rateBefore <= 0 || rateNow > rateBefore * (1 - dropPercent / 100))
            return null;
        _lastMove = now;
        _since = TimeSpan.MinValue;
        string who = near.Count > 0 ? string.Join(", ", near.Select(p => p.Name).Take(3)) : "başka oyuncu";
        return $"{who} {minutes:0} dk'dır burada ve EXP %{100 - rateNow / rateBefore * 100:0} düştü " +
               $"(dakikada {rateBefore:0} → {rateNow:0})";
    }
}
