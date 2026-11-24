using System.Drawing;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Other players killing the character (another kingdom, or one of ours with PvP on). Killed by a player
/// (players close by and no mob on us), the bot doesn't revive at once — the killer would only kill it again:
/// it waits, dead, watching whether they are still around. Gone for a while: it revives and goes on. Still
/// there after a minute: it changes channel (Esc → "Kanal değiştir" → the emptiest channel) and goes on there.
/// Hit by a player while alive (HP falling, no mob on us, a player right by): the channel is changed at once.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>Players this close when the character dies (world units, 25 m) count as its killers.</summary>
    internal const double KillerRange = 2500;
    /// <summary>A killer within this (world units, 50 m) is still around.</summary>
    internal const double EnemyAround = 5000;
    /// <summary>A player this close (world units, 15 m) while our HP falls with no mob on us is hitting us.</summary>
    internal const double HitterRange = 1500;
    /// <summary>The killers out of sight this long: safe to revive.</summary>
    internal static readonly TimeSpan EnemyGoneFor = TimeSpan.FromSeconds(20);
    /// <summary>Still there this long after the death: the channel is changed.</summary>
    internal static readonly TimeSpan EnemyStaysFor = TimeSpan.FromSeconds(60);
    /// <summary>A mob hit us this recently: a death is the mob's, not a player's.</summary>
    internal static readonly TimeSpan MobHitRecently = TimeSpan.FromSeconds(4);
    /// <summary>A channel change not done in this time is given up.</summary>
    internal static readonly TimeSpan ChannelChangeFor = TimeSpan.FromSeconds(40);
    /// <summary>After "Kanal değiştir" was pressed: this long for the new channel to show.</summary>
    internal static readonly TimeSpan ChannelLoadFor = TimeSpan.FromSeconds(90);

    private readonly Dictionary<string, DateTime> _enemies = new(StringComparer.Ordinal);
    private DateTime? _pvpDeathAt;
    private DateTime _enemySeenAt = DateTime.MinValue, _mobHitAt = DateTime.MinValue, _playerHitAt = DateTime.MinValue;
    private readonly List<(DateTime At, double Hp)> _hpTrail = [];
    /// <summary>Waiting dead for the killers to leave: the brain doesn't click "Burada yeniden başla".</summary>
    private bool _holdRevive;
    private int _ownEmpire;
    private DateTime _empireReadAt = DateTime.MinValue;

    // ── changing channel ──
    private DateTime? _channelSince;
    private int _channelFrom, _escTries;
    private DateTime _channelActedAt = DateTime.MinValue;
    private bool _channelGrown;
    private DateTime _changePressedAt = DateTime.MinValue;
    private int _menuWaits;
    /// <summary>The menu didn't open while dead: revived first (the brain), the channel changed once alive.</summary>
    private bool _channelAfterRevive;

    /// <summary>A player who attacked us is fought only this close (world units, 6 m): further off — running away — not chased.</summary>
    internal const double EnemyFightRange = 600;
    /// <summary>Our bots this close on the map (world units, 100 m) farm the same part of it: their channel isn't taken.</summary>
    internal const double SamePart = 10000;

    /// <summary>
    /// The players who attacked us (within the last half minute), close by: targets like a mob attacking us, first of
    /// all. Beyond <see cref="EnemyFightRange"/> they are left (not chased).
    /// </summary>
    private List<TargetInfo> EnemyTargets(WorldSnapshot world, BotProfile p, int width, int height)
    {
        var targets = new List<TargetInfo>();
        if (!p.PvpGuard || world.Camera is not CameraView camera || _enemies.Count == 0)
            return targets;
        var now = DateTime.Now;
        double reach = world.AttackDistance > 0 ? world.AttackDistance : 130;
        int i = 0;
        foreach (var pl in (world.Players ?? []).Where(RealPlayer))
        {
            double away = pl.Position.DistanceXY(world.Player);
            if (!_enemies.TryGetValue(pl.Name, out var seen) || now - seen > TimeSpan.FromSeconds(30) || away > EnemyFightRange)
                continue;
            var spot = camera.ToScreen(pl.Position, width, height) is PointF on && OnPlayfield(on, width, height)
                ? Point.Round(on) : OffScreenSpot(camera, world.Player, pl.Position, width, height);
            targets.Add(new TargetInfo(-1_000_000 - i++, spot, away, reach, 0, 1, 1, 0, Priority: true, AttacksMe: true, Position: pl.Position));
        }
        return targets;
    }

    /// <summary>Whether this player counts as an enemy by what the game tells of it (another kingdom, PvP on).</summary>
    internal static bool LooksHostile(OtherPlayer p, int ownEmpire) =>
        p.TargetsMe || p.PvpMode != 0 || ownEmpire > 0 && p.Empire > 0 && p.Empire != ownEmpire;

    /// <summary>A real player (a name, not a shop's stand-in, not a game master).</summary>
    private static bool RealPlayer(OtherPlayer p) => p.Name.Length > 0 && !p.LooksLikeGm;

    /// <summary>Inside <see cref="_botGate"/>: the actions for this tick while the PvP guard drives, or null.</summary>
    private IReadOnlyList<BotAction>? PvpTick(GameState state, BotProfile p)
    {
        if (!p.PvpGuard || World is not { } world)
        {
            _holdRevive = false;
            return null;
        }
        var now = DateTime.Now;
        if (now - _empireReadAt > TimeSpan.FromSeconds(30))
        {
            _empireReadAt = now;
            lock (_worldGate)
                _ownEmpire = _world?.ReadOwnEmpire() ?? _ownEmpire;
        }
        var me = world.Player;
        var players = (world.Players ?? []).Where(RealPlayer).ToList();
        if (world.Mobs.Any(m => m.AttacksMe && m.Hp > 0))
            _mobHitAt = now;
        foreach (var pl in players)
            if (_enemies.ContainsKey(pl.Name) && pl.Position.DistanceXY(me) <= EnemyAround)
            {
                _enemies[pl.Name] = now;
                _enemySeenAt = now;
            }

        if (_channelSince != null)
        {
            if (state.Dead && _channelAfterRevive)
                return null; // the brain revives first
            _channelAfterRevive = false;
            return ChannelTick(state, p, now);
        }

        if (state.Dead)
            return DeadTick(state, p, world, players, now);
        _pvpDeathAt = null;
        _holdRevive = false;

        // Alive: HP falling with no mob on us and a player right by — a player is hitting us.
        if (state.Hp is double hp)
        {
            _hpTrail.Add((now, hp));
            _hpTrail.RemoveAll(t => now - t.At > TimeSpan.FromSeconds(6));
            double lost = _hpTrail.Max(t => t.Hp) - hp;
            var hitters = players.Where(pl => pl.Position.DistanceXY(me) <= HitterRange).ToList();
            // Strictly: a mob close by (an archer, one not marked as attacking) may be the one hurting us.
            bool mobClose = world.Mobs.Any(m => m.Hp > 0 && m.Position.DistanceXY(me) <= 1000);
            // Only with HP really going down (a player merely having us selected is no attack; seen: a channel change
            // for an attack nobody saw): targeted and hurt, or hurt a lot with no mob anywhere near.
            // Never while mobs are on us: their hits can't be told from a player's (seen: VaSPeN at 59 % among mobs, an
            // "attack" by a player, the channel change instead of the fight — and dead).
            bool mobsOnUs = now - _mobHitAt <= MobHitRecently || state.Attackers.Count > 0;
            bool byPlayer = !mobsOnUs && (hitters.Any(pl => pl.TargetsMe) && lost >= 0.1 || lost >= 0.25 && !mobClose && hitters.Count > 0);
            // A player hitting us: fought back (the user: "saldıran ile savaşsın"); the channel is changed only if
            // they kill us and stay. Not chased when they run (they drop out of the targets beyond EnemyFightRange).
            if (byPlayer)
            {
                foreach (var pl in hitters)
                    _enemies[pl.Name] = now;
                if (now - _playerHitAt > TimeSpan.FromSeconds(30))
                {
                    string names = string.Join(", ", hitters.Select(h => h.Name));
                    _log($"⚔ Bir oyuncu saldırıyor ({names}); karşılık veriliyor (kaçarsa kovalanmaz).");
                    Notify($"⚔ {world.MapName}: {CharacterName} oyuncu saldırısına uğradı ({names}); karşılık veriliyor.");
                }
                _playerHitAt = now;
            }
        }
        return null;
    }

    private IReadOnlyList<BotAction>? DeadTick(GameState state, BotProfile p, WorldSnapshot world, List<OtherPlayer> players, DateTime now)
    {
        var me = world.Player;
        if (_pvpDeathAt == DateTime.MaxValue)
        {
            _holdRevive = false; // this death was handled: revived as usual
            return null;
        }
        if (_pvpDeathAt == null)
        {
            var near = players.Where(pl => pl.Position.DistanceXY(me) <= KillerRange).ToList();
            bool known = near.Any(pl => _enemies.ContainsKey(pl.Name));
            bool hostile = near.Any(pl => LooksHostile(pl, _ownEmpire));
            bool noMob = now - _mobHitAt > MobHitRecently;
            if (near.Count == 0 || !(known || hostile || noMob || now - _playerHitAt < TimeSpan.FromSeconds(30)))
                return null; // a mob's kill: revived as usual
            _pvpDeathAt = now;
            _enemySeenAt = now;
            foreach (var pl in near)
                _enemies[pl.Name] = now;
            string names = string.Join(", ", near.Select(pl => pl.Name));
            _log($"⚔ Karakter oyuncu tarafından öldürüldü ({names}); hemen dirilmiyor, gidip gitmedikleri izleniyor.");
            Notify($"⚔ {world.MapName}: {CharacterName} oyuncu tarafından öldürüldü ({names}). Bot bekliyor; giderlerse dirilecek, kalırlarsa kanal değiştirecek.");
        }
        _holdRevive = true;
        if (now - _enemySeenAt >= EnemyGoneFor)
        {
            _log($"⚔ Öldüren oyuncu(lar) {EnemyGoneFor.TotalSeconds:0} saniyedir görünmüyor; dirilip devam ediliyor.");
            _holdRevive = false;
            _pvpDeathAt = DateTime.MaxValue; // this death handled: revived as usual
            return null;
        }
        if (_pvpDeathAt is DateTime died && died != DateTime.MaxValue && now - died >= EnemyStaysFor)
        {
            _log($"⚔ Öldüren oyuncu(lar) {EnemyStaysFor.TotalSeconds:0} saniyedir hâlâ yakında; kanal değiştiriliyor.");
            return StartChannelChange(state, p, now);
        }
        return null;
    }

    private IReadOnlyList<BotAction> StartChannelChange(GameState state, BotProfile p, DateTime now)
    {
        _channelSince = now;
        _channelActedAt = DateTime.MinValue;
        _changePressedAt = DateTime.MinValue;
        _menuWaits = 0;
        if (_input != null)
            _input.HoldFront = true; // the menus clicked with the game in front the whole time
        _escTries = 0;
        // Whatever the reason (a player, a crowd of ours): never onto a channel one of our bots is on, on this map
        // (seen: off from a player to channel 5, right where another of ours farmed — both lost experience).
        if (World is { } here)
            foreach (var o in ReadPlaces(SekoBot.Instance.Number).Where(o => o.Map == here.MapName && o.Channel > 0
                         && Math.Sqrt((o.X - here.Player.X) * (o.X - here.Player.X) + (o.Y - here.Player.Y) * (o.Y - here.Player.Y)) <= SamePart))
                _channelAvoid.Add(o.Channel);
        _channelGrown = false;
        lock (_worldGate)
            _channelFrom = _world?.ReadChannels()?.Current ?? 0;
        // Nothing held while the menus are clicked.
        var actions = new List<BotAction> { new KeyUpAction(p.Keys.Attack) };
        foreach (var key in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
            actions.Add(new KeyUpAction(key));
        return actions;
    }

    /// <summary>One step of changing channel: Esc menu → "Kanal değiştir" → the emptiest other channel → "Kanal değiştir".</summary>
    private IReadOnlyList<BotAction> ChannelTick(GameState state, BotProfile p, DateTime now)
    {
        // Mobs on us while alive and not yet pressed: the fight comes first (the menus would leave us standing there).
        if (!state.Dead && _changePressedAt == DateTime.MinValue && (state.Attackers.Count > 0 || World?.Mobs.Any(m => m.AttacksMe && m.Hp > 0) == true))
        {
            ChannelScreen? open;
            lock (_worldGate)
                open = _world?.ReadChannels();
            EndChannelChange("🔀 Moblar saldırıyor; kanal değişimi bırakıldı, önce dövüşülüyor.", notify: false);
            return open is { WindowOpen: true } || open?.MenuButton != null ? [new KeyTapAction(p.Keys.CloseWindow)] : [];
        }
        ChannelScreen? screen;
        lock (_worldGate)
            screen = _world?.ReadChannels();
        // The channel we are leaving, once it can be read (0 at the start: read now).
        if (_channelFrom == 0 && screen is { Current: > 0 } first && _changePressedAt == DateTime.MinValue)
            _channelFrom = first.Current;
        // Changed (or the game is loading the new channel).
        if (screen is { Current: > 0 } done && _channelFrom > 0 && done.Current != _channelFrom)
        {
            EndChannelChange($"🔀 Kanal değiştirildi: {_channelFrom} → {done.Current}.", notify: true);
            return [];
        }
        // Pressed: the game takes its time to move over (more so with a death in between) — waited on longer.
        var limit = _changePressedAt != DateTime.MinValue ? _changePressedAt + ChannelLoadFor : _channelSince!.Value + ChannelChangeFor;
        if (now > limit)
        {
            EndChannelChange("🔀 Kanal değiştirilemedi (menü açılmadı ya da oyun izin vermedi); bot devam ediyor.", notify: true);
            return screen is { WindowOpen: true } || screen?.MenuButton != null ? [new KeyTapAction(p.Keys.CloseWindow)] : [];
        }
        if (now - _channelActedAt < TimeSpan.FromMilliseconds(1200))
            return [];
        // Clicking needs the mouse: one bot at a time; a small game made big enough to show the windows whole.
        if (!TakeTurn())
            return [];
        if (!_channelGrown)
        {
            _channelGrown = true;
            if (GrowForWork())
            {
                _channelActedAt = now;
                return [];
            }
        }
        _channelActedAt = now;
        if (screen is { WindowOpen: true } window)
        {
            // The emptiest other channel — one none of our bots is on here when there is such (see CrowdTick).
            var target = window.Boxes.Where(b => b.Channel != window.Current).OrderBy(b => _channelAvoid.Contains(b.Channel)).ThenBy(b => b.Full).FirstOrDefault();
            if (target.Channel == 0)
            {
                EndChannelChange("🔀 Geçilecek başka kanal yok.", notify: false);
                return [new KeyTapAction(p.Keys.CloseWindow)];
            }
            if (window.Selected != target.Channel)
                return [new ClickAction(BagCleaner.ClickPoint(target.At), Slow: true)];
            // Pressed: the game takes a while to move over (seen: 20 s); not pressed again meanwhile.
            if (now - _changePressedAt < TimeSpan.FromSeconds(12))
                return [];
            if (window.Change is { } change)
            {
                _changePressedAt = now;
                _log($"🔀 Kanal {target.Channel} seçildi (%{target.Full} dolu); 'Kanal değiştir'e basılıyor.");
                return [new ClickAction(BagCleaner.ClickPoint(change), Slow: true)];
            }
            return [];
        }
        if (screen?.MenuButton is { } menuButton)
            return [new ClickAction(BagCleaner.ClickPoint(menuButton), Slow: true)];
        // The game menu is up but its button not drawn yet (it slides in): waited on, not Esc'd shut again
        // (seen on VaSPeN: the menu opened and shut three times, no channel change).
        if (World?.OpenWindows?.Contains("EscMenu") == true && _menuWaits++ < 5)
            return [];
        // Nothing open: Esc opens the game menu (tried a few times).
        if (_escTries++ < 3)
            return [new KeyTapAction(p.Keys.CloseWindow)];
        if (state.Dead && _holdRevive)
        {
            // The menu doesn't open while dead: revived here first, then the channel is changed.
            _log("🔀 Ölüyken menü açılmıyor; önce dirilip sonra kanal değiştirilecek.");
            _holdRevive = false;
            _channelAfterRevive = true;
            _escTries = 0;
            _channelSince = now;
            ShrinkIfGrown();
            LetGoTurn();
            return [];
        }
        return [];
    }

    private void EndChannelChange(string message, bool notify)
    {
        _channelSince = null;
        _channelAfterRevive = false;
        if (_input != null && !_cleaner.Active)
            _input.HoldFront = false;
        _channelAvoid.Clear();
        _lastChannelChange = DateTime.Now;
        _myChannel = 0; // read again
        _holdRevive = false;
        _pvpDeathAt = DateTime.MaxValue;
        _enemies.Clear(); // a new channel: other players
        _hpTrail.Clear();
        _log(message);
        if (notify)
            Notify($"{World?.MapName}: {CharacterName} — {message}");
        ShrinkIfGrown();
        LetGoTurn();
    }

    private void ShrinkIfGrown()
    {
        if (!_channelGrown)
            return;
        _channelGrown = false;
        ShrinkAfterWork();
    }

}
