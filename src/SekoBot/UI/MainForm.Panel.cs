using SekoBot.Bot;

namespace SekoBot.UI;

/// <summary>The panel's top card, the session's numbers and the tagged log.</summary>
internal sealed partial class MainForm
{
    // ── The top card: the character, how long ──

    private void ShowTop()
    {
        var world = _engine?.World;
        string name = _engine?.CharacterName ?? "";
        string initials = new(name.Where(char.IsLetterOrDigit).Take(2).ToArray());
        _avatar.Initials = initials.Length > 0 ? initials.ToUpperInvariant() : "?";
        _avatar.Level = world?.Level ?? 0;
        string title = _engine == null ? "Oyun bağlı değil" : name.Length > 0 ? name : "Karakter okunuyor…";
        if (_heroName.Text != title)
            _heroName.Text = title;
        _heroName.ForeColor = _engine == null ? Theme.Muted : Theme.Text;
        string elapsed = ElapsedNow();
        string time = elapsed.Length > 0 ? elapsed : "--:--";
        if (_timer.Text != time)
            _timer.Text = time;
        _timer.ForeColor = elapsed.Length > 0 ? Theme.Text : Theme.Faint;
        string game = _engine == null ? "Oyunu seç" : name.Length > 0 ? name : "Bağlı";
        if (_gameButton.Text != game)
            _gameButton.Text = game;
    }

    // ── The numbers ──

    private readonly List<double> _expCurve = [];
    private DateTime _expCurveAt = DateTime.MinValue;

    private void ShowNumbers()
    {
        var engine = _engine;
        var stats = engine?.Stats;
        var session = stats?.Session;
        var world = engine?.World;
        bool timed = stats != null && stats.BotUptime.TotalMinutes >= 1;
        double hours = Math.Max(1e-6, stats?.BotUptime.TotalHours ?? 0);
        if (engine == null || session == null)
        {
            _expCurve.Clear();
            _kExp.Curve = [];
        }

        // Experience per hour, with a curve of the last hour (one point a minute).
        if (session != null && timed && stats!.BotRunning && DateTime.Now - _expCurveAt >= TimeSpan.FromMinutes(1))
        {
            _expCurveAt = DateTime.Now;
            _expCurve.Add(session.Exp / hours);
            if (_expCurve.Count > 60)
                _expCurve.RemoveAt(0);
            _kExp.Curve = _expCurve.Count >= 2 ? [.. _expCurve] : [];
        }
        string trend = "";
        Color trendColor = Theme.Green;
        if (_expCurve.Count >= 3 && _expCurve[0] > 0)
        {
            double change = (_expCurve[^1] - _expCurve[0]) / _expCurve[0] * 100;
            if (Math.Abs(change) >= 1)
            {
                trend = (change > 0 ? "▲ %" : "▼ %") + Math.Abs(change).ToString("0");
                trendColor = change > 0 ? Theme.Green : Theme.Red;
            }
        }
        _kExp.Show(session != null && timed ? Short((long)(session.Exp / hours)) : "—",
            sub: session != null ? "toplam " + Short(session.Exp) : "", corner: trend, cornerColor: trendColor);

        _kKills.Show(session != null ? session.Kills.ToString("N0") : "—",
            sub: session != null && timed ? (session.Kills / hours).ToString("N0") + " / saat" : "");

        _kYang.Show(session != null ? Short(session.Yang) : "—",
            sub: session != null && timed && session.Yang > 0 ? "+" + Short((long)(session.Yang / hours)) + " / saat" : "",
            subColor: Theme.Green);

        // Potions: the HP ones left against what the bot fills up to; amber when it is time to buy.
        var (hpLeft, mpLeft) = engine?.PotionsLeft() ?? (null, null);
        int upTo = Math.Max(1, _profile.BuyPotionsUpTo), below = _profile.BuyPotionsBelow;
        bool low = _profile.BuyPotions && hpLeft is int h && h < below;
        int used = (session?.HpPotionsUsed ?? 0) + (session?.MpPotionsUsed ?? 0);
        // Which is which, written out (the user: "HP" and "MP" weren't told apart).
        _kPotion.Show(hpLeft is int hp ? hp.ToString("N0") : "—", unit: hpLeft is int ? "HP" : "",
            sub: string.Join(" · ", new[] { mpLeft is int mp ? $"{mp:N0} MP" : "", hpLeft is int && _profile.BuyPotions ? $"{upTo:N0}'e tamamlar" : used > 0 ? $"{used:N0} içildi" : "" }.Where(x => x.Length > 0)),
            bar: hpLeft is int left && _profile.BuyPotions ? Math.Clamp((double)left / upTo, 0, 1) : null,
            corner: low ? "azaldı" : "", cornerColor: Theme.Yellow, valueColor: low ? Theme.Yellow : Theme.Text);
        _kPotion.BarColor = Theme.Yellow;

        // The level: how far, how long to the next one; the deaths in the corner.
        int deaths = stats?.Deaths ?? 0;
        if (world is { Progress: { } progress })
        {
            double percent = 100.0 * progress.Exp / Math.Max(1, progress.Exp + progress.NeedExp);
            string eta = session?.ToLevel is TimeSpan t ? "~" + Duration(t) + " kaldı" : "";
            _kLevel.Caption = $"SEVİYE {world.Level} → {world.Level + 1}";
            _kLevel.Show("%" + percent.ToString("0.0"), sub: eta, bar: percent / 100,
                corner: $"ölüm {deaths}", cornerColor: deaths > 0 ? Theme.Red : Theme.Faint);
        }
        else
        {
            _kLevel.Caption = "SEVİYE";
            _kLevel.Show("—", bar: null, corner: engine != null ? $"ölüm {deaths}" : "", cornerColor: deaths > 0 ? Theme.Red : Theme.Faint);
        }
        _kLevel.BarColor = Theme.Gold;

        // HP / MP as numbers when known (the bars show the share).
        _hpMeter.Detail = engine?.State?.Hp is double hpShare ? $"%{hpShare * 100:0}" : "";
        _targetMeter.Caption = world?.Target is { } target && engine?.State?.TargetHp > 0 ? "Hedef · " + target.Name : "Hedef";
    }

    // ── This computer ──

    private InfoList _machine = null!;
    private string _publicIp = "";

    /// <summary>What the game sees about this computer (from its memory), then what Windows shows.</summary>
    private void ShowMachine()
    {
        if (_machine == null)
            return;
        var rows = new List<InfoList.Row>();
        var seen = _engine?.MachineSeen();
        string gameMac = seen != null ? MachineInfo.Mac(seen.Mac) : "";
        rows.Add(new("OYUNUN GÖRDÜĞÜ", seen == null ? "oyun bağlı değil" : "oyunun belleğinden", Caption: true));
        if (seen != null)
        {
            var nets = MachineInfo.Networks();
            var card = nets.FirstOrDefault(n => gameMac.Length > 0 && n.Mac == gameMac);
            rows.Add(new("MAC adresi", gameMac.Length > 0 ? gameMac + (card.Name != null ? "  ·  " + card.Name : "") : "—", Theme.GoldLight));
            rows.Add(new("Konum / IP", seen.LocationIp.Length > 0 ? seen.LocationIp : "— (oyun bu alanı doldurmamış)"));
            rows.Add(new("Sunucu", seen.Server.Length > 0 ? seen.Server + (seen.ServerIp.Length > 0 ? "  ·  " + seen.ServerIp : "") : "—"));
            rows.Add(new("Oyun sürümü · dil", string.Join("  ·  ", new[] { seen.Version, seen.Language }.Where(x => x.Length > 0))));
        }
        rows.Add(new("WINDOWS'UN GÖSTERDİĞİ", "", Caption: true));
        rows.Add(new("Bilgisayar adı", Environment.MachineName));
        rows.Add(new("Windows", MachineInfo.Windows()));
        rows.Add(new("İşlemci", MachineInfo.Processor()));
        rows.Add(new("Bellek (RAM)", MachineInfo.Memory()));
        rows.Add(new("Ekran kartı", MachineInfo.Graphics()));
        rows.Add(new("Ekran", MachineInfo.Screens()));
        foreach (var (name, mac, ips) in MachineInfo.Networks())
            rows.Add(new("Ağ kartı", $"{mac}  ·  {ips}  ·  {name}" + (mac == gameMac && gameMac.Length > 0 ? "  ★ oyunun gördüğü" : ""),
                mac == gameMac && gameMac.Length > 0 ? Theme.GoldLight : null));
        rows.Add(new("Dış IP (internetin gördüğü)", _publicIp.Length > 0 ? _publicIp : "\"Dış IP'yi göster\"e bas"));
        _machine.Show(rows);
    }

    // ── Windows of their own: the live map, the game's picture, the computer ──

    private SideWindow? _mapWindow, _viewWindow, _machineWindow;
    private LiveMap? _liveMap;
    private readonly System.Windows.Forms.Timer _mapTimer = new() { Interval = 250 };

    private bool GameViewShown => _viewWindow is { Visible: true, WindowState: not FormWindowState.Minimized };

    private static void Bring(Form window)
    {
        if (!window.Visible)
            window.Show();
        if (window.WindowState == FormWindowState.Minimized)
            window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    private void OpenLiveMap()
    {
        if (_mapWindow == null)
        {
            var window = _mapWindow = new SideWindow("Canlı harita", new Size(760, 760));
            _liveMap = new LiveMap(() => _engine, () => _profile) { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
            var bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Rail };
            var whole = new ProButton { Text = "Tüm harita", Icon = "map", Kind = ButtonKind.Ghost, Bounds = new Rectangle(10, 7, 140, 30), Font = new Font("Segoe UI", 8.5f) };
            whole.Click += (_, _) =>
            {
                _liveMap.ToggleWhole();
                whole.Text = _liveMap.Whole ? "Karakterin çevresi" : "Tüm harita";
            };
            bar.Controls.Add(whole);
            var legend = new Label
            {
                Text = "● sen   ● mob   ● sana vuran   ▲ oyuncu   ▲ düşman   ◆ eşya   ┅ gidilen yol   ○ farm alanı   ▓ dağ (girilmez)   — tekerlek: yakınlaştır",
                ForeColor = Theme.Faint, BackColor = Theme.Rail, Font = new Font("Segoe UI", 8f), AutoSize = false,
                Bounds = new Rectangle(160, 7, 1200, 30), TextAlign = ContentAlignment.MiddleLeft,
            };
            bar.Controls.Add(legend);
            window.Controls.Add(_liveMap);
            window.Controls.Add(bar);
            _mapTimer.Tick += (_, _) => { if (window.Visible && window.WindowState != FormWindowState.Minimized) _liveMap.Invalidate(); };
            window.VisibleChanged += (_, _) => { if (window.Visible) _mapTimer.Start(); else _mapTimer.Stop(); };
        }
        Bring(_mapWindow);
    }

    private void OpenGameView()
    {
        if (_viewWindow == null)
        {
            var window = _viewWindow = new SideWindow("Oyun görüntüsü", new Size(960, 560));
            window.Controls.Add(_preview);
            // The picture is taken from the game only while this window shows (lighter for the computer otherwise).
            window.VisibleChanged += (_, _) => _engine?.ShowingPreview(GameViewShown);
            window.Resize += (_, _) => _engine?.ShowingPreview(GameViewShown);
        }
        Bring(_viewWindow);
    }

    private void OpenMachineWindow()
    {
        if (_machineWindow == null)
        {
            var window = _machineWindow = new SideWindow("Bilgisayar bilgileri", new Size(820, 620));
            var bar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Rail };
            var refresh = new ProButton { Text = "Yenile", Icon = "refresh", Kind = ButtonKind.Ghost, Bounds = new Rectangle(12, 9, 110, 30), Font = new Font("Segoe UI", 8.5f) };
            var publicIp = new ProButton { Text = "Dış IP'yi göster", Icon = "globe", Kind = ButtonKind.Ghost, Bounds = new Rectangle(130, 9, 170, 30), Font = new Font("Segoe UI", 8.5f) };
            bar.Controls.Add(refresh);
            bar.Controls.Add(publicIp);
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background, Padding = new Padding(16) };
            Theme.DarkScrollBars(page);
            _machine = new InfoList { Location = new Point(16, 12), Width = 770, Font = new Font("Segoe UI", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            page.Controls.Add(_machine);
            window.Controls.Add(page);
            window.Controls.Add(bar);
            refresh.Click += (_, _) => ShowMachine();
            publicIp.Click += async (_, _) =>
            {
                publicIp.Enabled = false;
                _publicIp = "soruluyor…";
                ShowMachine();
                _publicIp = await MachineInfo.PublicIpAsync();
                publicIp.Enabled = true;
                ShowMachine();
            };
        }
        ShowMachine();
        Bring(_machineWindow);
    }

    // ── The log ──

    /// <summary>A line of the log with its tag (what kind of thing happened).</summary>
    private sealed record LogLine(DateTime At, string Tag, Tone Tone, string Icon, string Kind, string Text, bool Feed)
    {
        /// <summary>The line without its leading emoji, short (for "ŞİMDİ").</summary>
        public string Short
        {
            get
            {
                string t = Text.TrimStart();
                int i = 0;
                while (i < t.Length && !char.IsLetterOrDigit(t[i]) && t[i] != '"' && t[i] != '(')
                    i++;
                t = t[i..].Trim();
                return t.Length > 90 ? t[..90] + "…" : t;
            }
        }

        public static LogLine Of(DateTime at, string text)
        {
            string l = text.ToLower(new System.Globalization.CultureInfo("tr-TR"));
            bool Has(params string[] words) => words.Any(l.Contains);
            if (Has("öldü", "💀", "ölüm"))
                return new(at, "ÖLÜM", Tone.Red, "skull", "dead", text, true);
            if (Has("💰", "satış", "satıldı", "iksir alındı", "satıcı"))
                return new(at, "SATIŞ", Tone.Green, "coin", "sell", text, true);
            if (Has("⚠", "uyarı", "takıl", "okunamı", "bulunamadı", "yazılamadı", "vazgeç", "hata", "exp gelmiyor"))
                return new(at, "UYARI", Tone.Amber, "alert", "warn", text, true);
            if (Has("seviye atla", "⬆", "yeni seviye"))
                return new(at, "SEVİYE", Tone.Gold, "spark", "level", text, true);
            if (Has("kanal"))
                return new(at, "KANAL", Tone.Blue, "swap", "channel", text, true);
            if (Has("🌐", "sunucu"))
                return new(at, "SUNUCU", Tone.Blue, "globe", "info", text, false);
            if (Has("yürü", "alana dön", "yol üzerinden", "yola"))
                return new(at, "YOL", Tone.Violet, "walk", "path", text, false);
            if (Has("grup", "toplandı", "mob"))
                return new(at, "FARM", Tone.Gold, "bolt", "farm", text, Has("toplandı"));
            return new(at, "BİLGİ", Tone.Neutral, "check", "info", text, false);
        }
    }

    private readonly List<LogLine> _logLines = [];
    private string _logFilter = "";
    private Font? _logBold;

    private void AppendLog(LogLine line)
    {
        _logBold ??= new Font(_log.Font, FontStyle.Bold);
        void Put(string text, Color color, Font font)
        {
            _log.Select(_log.TextLength, 0);
            _log.SelectionColor = color;
            _log.SelectionFont = font;
            _log.SelectedText = text;
        }
        Put(line.At.ToString("HH:mm:ss") + "  ", Theme.Faint, _log.Font);
        Put(line.Tag.PadRight(7) + " ", line.Tone == Tone.Neutral ? Theme.Faint : Tones.Of(line.Tone), _logBold);
        Put(line.Text + "\n", line.Tone == Tone.Red ? Theme.Red : line.Tone == Tone.Amber ? Theme.Yellow : Theme.Muted, _log.Font);
    }

    /// <summary>The log again from its lines (another filter chosen, or the oldest lines dropped).</summary>
    private void ShowLog()
    {
        _log.SuspendLayout();
        _log.Clear();
        foreach (var line in _logLines.Where(l => _logFilter.Length == 0 || l.Kind == _logFilter).TakeLast(300))
            AppendLog(line);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        _log.ResumeLayout();
    }

    private void FilterLog(string kind)
    {
        _logFilter = kind;
        foreach (var button in _logFilters)
            button.Selected = (string?)button.Tag == kind;
        ShowLog();
    }
}
