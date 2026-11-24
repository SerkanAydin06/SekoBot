using System.Drawing.Drawing2D;
using SekoBot.Bot;
using SekoBot.Config;

namespace SekoBot.UI;

/// <summary>
/// The market window ("Pazar"): everything the market bot read, to find an item fast with all its
/// details. Three views:
/// <list type="bullet">
/// <item>İlanlar: every sale, searched while typing (all words, in the name or the bonuses), filtered by
/// item group, a bonus of at least a value, price range, seller, only with bonuses; sorted by any column.
/// The "Piyasa" column says how the price stands against the item's usual price (green: under).
/// On the right the sale picked: all bonuses with how good each is, the market's cheapest and average,
/// the price over the last weeks, what else the seller sells, and "Dükkana götür".</item>
/// <item>Fiyat listesi: every item with how many sell it, cheapest, average, dearest and the change in a week.</item>
/// <item>Aranan ve kelepir: items to be told about (at a price), and when a deal is told.</item>
/// </list>
/// At the bottom the watch: how fresh the list is, how many shops; the continuous watch is started and stopped here.
/// Nothing is ever bought.
/// </summary>
internal sealed class MarketForm : Form
{
    private readonly Func<BotEngine?> _engine;
    private readonly BotProfile _profile;
    private readonly Action _save;
    private readonly Action<string> _log;
    private readonly Action<bool> _startScan;
    private readonly Action _stopScan;

    private readonly TextBox _find, _seller;
    private readonly ComboBox _group, _bonus, _sort;
    private readonly NumericUpDown _minValue, _minPrice, _maxPrice;
    private readonly CheckBox _onlyBonused;
    private readonly Label _count, _status;
    private readonly ListView _list, _prices, _sellerItems;
    private readonly Panel _salesView, _pricesView, _wantedView, _detail;
    private readonly Label _dName, _dPrice, _dSeller, _dSeen, _dBonuses, _dMarket;
    private readonly Panel _chart;
    private readonly Button _watch, _once, _go, _refresh;
    private readonly CheckBox _autoRefresh;
    private DateTime _searchedAt;
    private readonly Button[] _views;
    private readonly TextBox _priceFind;

    private List<MarketListing> _rows = [];
    private List<ItemStats> _priceRows = [];
    private List<string?> _groupKeys = [];
    private int _sortColumn = 3;
    private bool _sortDown;
    private MarketListing? _picked;
    private DateTime _shownUpdate;
    private readonly System.Windows.Forms.Timer _typing = new() { Interval = 250 }, _tick = new() { Interval = 2000 };

    private const int Max = 5000;

    public MarketForm(Func<BotEngine?> engine, BotProfile profile, Action save, Action<string> log, Action<bool> startScan, Action stopScan)
    {
        _engine = engine;
        _profile = profile;
        _save = save;
        _log = log;
        _startScan = startScan;
        _stopScan = stopScan;
        Text = "PARDUS — Pazar";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1320, 780);
        MinimumSize = new Size(1080, 620);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        // ── Bottom: the watch ──
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Theme.Surface, Padding = new Padding(10, 8, 10, 8) };
        Controls.Add(bottom);
        _watch = Theme.Button(bottom, "🔁  Sürekli takibi başlat", new Rectangle(10, 8, 210, 30), Theme.Primary, Theme.PrimaryHover);
        _once = Theme.Button(bottom, "🛒  Bir kez tara", new Rectangle(228, 8, 130, 30), Theme.Secondary, Theme.SecondaryHover);
        var report = Theme.Button(bottom, "📊  Rapor", new Rectangle(366, 8, 96, 30), Theme.Secondary, Theme.SecondaryHover);
        var excel = Theme.Button(bottom, "📄  Excel", new Rectangle(470, 8, 90, 30), Theme.Secondary, Theme.SecondaryHover);
        _status = Theme.Label(bottom, "", new Rectangle(572, 8, 740, 30), 9f, Theme.Muted);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _watch.Click += (_, _) =>
        {
            if (_engine()?.ScanningMarket == true)
                _stopScan();
            else
                _startScan(true);
            UpdateStatus();
        };
        _once.Click += (_, _) =>
        {
            if (_engine()?.ScanningMarket == true)
                _stopScan();
            else
                _startScan(false);
            UpdateStatus();
        };
        report.Click += (_, _) => Open(BotEngine.LatestReport);
        excel.Click += (_, _) => Open(BotEngine.LatestReport is string r ? Path.ChangeExtension(r, ".csv") : null);

        // ── Top: the views ──
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Background, Padding = new Padding(10, 8, 10, 0) };
        Controls.Add(top);
        _views =
        [
            Theme.Button(top, "🔎  İlanlar", new Rectangle(10, 8, 140, 30), Theme.Primary, Theme.PrimaryHover),
            Theme.Button(top, "📈  Fiyat listesi", new Rectangle(156, 8, 150, 30), Theme.Secondary, Theme.SecondaryHover),
            Theme.Button(top, "🎯  Aranan ve kelepir", new Rectangle(312, 8, 180, 30), Theme.Secondary, Theme.SecondaryHover),
        ];
        Theme.Label(top, "Hiçbir şey satın alınmaz; almak senin elinde.", new Rectangle(506, 8, 420, 30), 8.5f, Theme.Muted);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(10, 6, 10, 6) };
        Controls.Add(body);
        body.BringToFront();

        // ── View 1: sales ──
        _salesView = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        body.Controls.Add(_salesView);

        _detail = new Panel { Dock = DockStyle.Right, Width = 420, BackColor = Theme.Surface, Padding = new Padding(12) };
        _salesView.Controls.Add(_detail);
        var filters = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Background };
        _salesView.Controls.Add(filters);

        _find = Box(filters, new Rectangle(0, 0, 330, 28), "Ara: eşya ya da efsun, birkaç kelime (ör. kılıç +9, kavrama)", 10.5f);
        _group = Pick(filters, new Rectangle(338, 1, 130, 26));
        _bonus = Pick(filters, new Rectangle(474, 1, 190, 26));
        Theme.Label(filters, "en az", new Rectangle(668, 1, 36, 26), 8.5f, Theme.Muted);
        _minValue = Theme.Number(filters, new Rectangle(704, 2, 56, 24), -100, 100_000, 0);
        _sort = Pick(filters, new Rectangle(766, 1, 112, 26));
        _sort.Items.AddRange(["En ucuz", "Efsun değeri", "Efsun puanı", "En yeni", "En pahalı"]);
        _sort.SelectedIndex = 0;

        Theme.Label(filters, "Tanesi", new Rectangle(0, 38, 44, 26), 8.5f, Theme.Muted);
        _minPrice = Theme.Number(filters, new Rectangle(46, 40, 120, 24), 0, 10_000_000_000, 0, 0, 100_000);
        _minPrice.ThousandsSeparator = true;
        Theme.Label(filters, "–", new Rectangle(168, 38, 12, 26), 9f, Theme.Muted);
        _maxPrice = Theme.Number(filters, new Rectangle(182, 40, 120, 24), 0, 10_000_000_000, 0, 0, 100_000);
        _maxPrice.ThousandsSeparator = true;
        Theme.Label(filters, "yang (0 = sınırsız)", new Rectangle(306, 38, 110, 26), 8f, Theme.Muted);
        _seller = Box(filters, new Rectangle(420, 38, 150, 26), "Satıcı adı", 9f);
        _onlyBonused = new CheckBox { Text = "Sadece efsunlu", Bounds = new Rectangle(580, 40, 120, 24), ForeColor = Theme.Text, BackColor = Theme.Background };
        filters.Controls.Add(_onlyBonused);
        var clear = Theme.Button(filters, "Temizle", new Rectangle(704, 38, 74, 26), Theme.Secondary, Theme.SecondaryHover);
        // The list changes only when asked (or once a minute when ticked): the market bot reads shops all the
        // time, a list changing under the eyes jumps and loses rows (user, 2026-11-19).
        _refresh = Theme.Button(filters, "🔄  Yenile", new Rectangle(784, 38, 150, 26), Theme.Secondary, Theme.SecondaryHover);
        _refresh.Click += (_, _) => Search();
        _autoRefresh = new CheckBox
        {
            Text = "Dakikada bir yenile", Bounds = new Rectangle(884, 2, 150, 24), ForeColor = Theme.Text, BackColor = Theme.Background,
            Checked = profile.MarketAutoRefresh,
        };
        filters.Controls.Add(_autoRefresh);
        _autoRefresh.CheckedChanged += (_, _) => { _profile.MarketAutoRefresh = _autoRefresh.Checked; _save(); };
        _count = Theme.Label(filters, "", new Rectangle(942, 38, 300, 26), 8.5f, Theme.Muted);
        _count.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        _list = Grid(["Eşya", "Efsunlar", "Adet", "Tanesi", "Piyasa", "Satıcı", "Uzaklık", "Okundu"],
            [210, 210, 52, 96, 74, 120, 60, 64], [false, false, true, true, true, false, true, false]);
        _list.VirtualMode = true;
        _list.RetrieveVirtualItem += (_, e) => e.Item = Row(e.ItemIndex);
        _list.ColumnClick += (_, e) =>
        {
            _sortDown = _sortColumn == e.Column ? !_sortDown : e.Column is 3 or 6 or 7 ? false : e.Column is 2 ? true : false;
            _sortColumn = e.Column;
            SortRows();
        };
        _list.SelectedIndexChanged += (_, _) => Pick(_list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null);
        _list.DoubleClick += (_, _) => GoToShop();
        _list.DrawSubItem += DrawSaleCell;
        var listHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0), BackColor = Theme.Background };
        listHolder.Controls.Add(_list);
        _salesView.Controls.Add(listHolder);
        listHolder.BringToFront();

        // The sale picked: a fixed part on top, the seller's items filling the rest, the buttons at the bottom.
        var head = new Panel { Dock = DockStyle.Top, Height = 448, BackColor = Theme.Surface };
        _dName = Theme.Label(head, "Soldan bir ilan seç", new Rectangle(0, 10, 396, 30), 12.5f, Theme.Text, FontStyle.Bold);
        _dPrice = Theme.Label(head, "", new Rectangle(0, 42, 396, 24), 11f, Theme.Green, FontStyle.Bold);
        _dSeller = Theme.Label(head, "", new Rectangle(0, 68, 396, 20), 9f, Theme.Text);
        _dSeen = Theme.Label(head, "", new Rectangle(0, 88, 396, 18), 8.5f, Theme.Muted);
        Theme.Label(head, "EFSUNLAR", new Rectangle(0, 112, 396, 16), 8f, Theme.Accent, FontStyle.Bold);
        _dBonuses = Theme.Label(head, "", new Rectangle(0, 130, 396, 96), 9f, Theme.Text, FontStyle.Regular, ContentAlignment.TopLeft);
        Theme.Label(head, "PAZARDA", new Rectangle(0, 230, 396, 16), 8f, Theme.Accent, FontStyle.Bold);
        _dMarket = Theme.Label(head, "", new Rectangle(0, 248, 396, 58), 9f, Theme.Text, FontStyle.Regular, ContentAlignment.TopLeft);
        Theme.Label(head, "FİYAT GEÇMİŞİ (ortalama — en ucuz)", new Rectangle(0, 308, 396, 16), 8f, Theme.Accent, FontStyle.Bold);
        _chart = new Panel { Bounds = new Rectangle(0, 326, 396, 96), BackColor = Theme.Inset };
        _chart.Paint += DrawChart;
        head.Controls.Add(_chart);
        // Several lines each: no ellipsis (it swallows the line breaks).
        _dBonuses.AutoEllipsis = false;
        _dMarket.AutoEllipsis = false;
        Theme.Label(head, "SATICININ DİĞER ÜRÜNLERİ", new Rectangle(0, 428, 396, 16), 8f, Theme.Accent, FontStyle.Bold);
        // Below the fixed part: the seller's items fill what is left, the buttons stay at the bottom.
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Surface };
        _go = Theme.Button(buttons, "🚶  Dükkana götür", new Rectangle(0, 6, 194, 32), Theme.Primary, Theme.PrimaryHover);
        var want = Theme.Button(buttons, "🎯  Bu fiyattan haber ver", new Rectangle(202, 6, 194, 32), Theme.Secondary, Theme.SecondaryHover);
        var all = Theme.Button(buttons, "🔎  Tüm satışları", new Rectangle(0, 44, 194, 28), Theme.Secondary, Theme.SecondaryHover);
        var copy = Theme.Button(buttons, "📋  Satıcı adını kopyala", new Rectangle(202, 44, 194, 28), Theme.Secondary, Theme.SecondaryHover);
        var sellerHolder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        _sellerItems = Grid(["Eşya", "Adet", "Tanesi"], [230, 50, 96], [false, true, true]);
        sellerHolder.Controls.Add(_sellerItems);
        _detail.Controls.Add(head);
        _detail.Controls.Add(buttons);
        _detail.Controls.Add(sellerHolder);
        sellerHolder.BringToFront();
        _go.Click += (_, _) => GoToShop();
        want.Click += (_, _) => WantPicked();
        all.Click += (_, _) =>
        {
            if (_picked == null)
                return;
            ClearFilters(keepText: false);
            _find.Text = _picked.Name;
            Search();
        };
        copy.Click += (_, _) =>
        {
            if (_picked != null)
            {
                Clipboard.SetText(_picked.Owner);
                _log($"📋 Satıcı adı kopyalandı: {_picked.Owner}");
            }
        };

        // ── View 2: prices ──
        _pricesView = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
        body.Controls.Add(_pricesView);
        var priceTop = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Background };
        _pricesView.Controls.Add(priceTop);
        _priceFind = Box(priceTop, new Rectangle(0, 0, 360, 28), "Eşya ara (boş: hepsi)", 10.5f);
        Theme.Label(priceTop, "Bir satıra çift tıkla: o eşyanın bütün satışları açılır.", new Rectangle(372, 0, 500, 28), 8.5f, Theme.Muted);
        _prices = Grid(["Eşya", "İlan", "Dükkan", "Adet", "En ucuz", "Ortalama", "En pahalı", "7 gün", "En ucuz satıcı"],
            [260, 50, 56, 70, 100, 100, 100, 64, 150], [false, true, true, true, true, true, true, true, false]);
        _prices.VirtualMode = true;
        _prices.RetrieveVirtualItem += (_, e) => e.Item = PriceRow(e.ItemIndex);
        _prices.DrawSubItem += DrawPriceCell;
        _prices.DoubleClick += (_, _) =>
        {
            if (_prices.SelectedIndices.Count == 0 || _prices.SelectedIndices[0] >= _priceRows.Count)
                return;
            string name = _priceRows[_prices.SelectedIndices[0]].Name;
            Show(0);
            ClearFilters(keepText: false);
            _find.Text = name;
            Search();
        };
        var pricesHolder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        pricesHolder.Controls.Add(_prices);
        _pricesView.Controls.Add(pricesHolder);
        pricesHolder.BringToFront();
        var priceTyping = new System.Windows.Forms.Timer { Interval = 250 };
        priceTyping.Tick += (_, _) => { priceTyping.Stop(); FillPrices(); };
        _priceFind.TextChanged += (_, _) => { priceTyping.Stop(); priceTyping.Start(); };

        // ── View 3: wanted items and deals ──
        _wantedView = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
        body.Controls.Add(_wantedView);
        BuildWanted(_wantedView);

        for (int i = 0; i < _views.Length; i++)
        {
            int view = i;
            _views[i].Click += (_, _) => Show(view);
        }

        // Filters search while typing / picking.
        FillGroups();
        FillBonuses();
        _typing.Tick += (_, _) => { _typing.Stop(); Search(); };
        void Later() { _typing.Stop(); _typing.Start(); }
        _find.TextChanged += (_, _) => Later();
        _seller.TextChanged += (_, _) => Later();
        _minValue.ValueChanged += (_, _) => Later();
        _minPrice.ValueChanged += (_, _) => Later();
        _maxPrice.ValueChanged += (_, _) => Later();
        _onlyBonused.CheckedChanged += (_, _) => Search();
        _group.SelectedIndexChanged += (_, _) => { FillBonuses(); Search(); };
        _bonus.SelectedIndexChanged += (_, _) =>
        {
            if (_bonus.SelectedIndex > 0 && _sort.SelectedIndex == 0)
                _sort.SelectedIndex = 1;
            Search();
        };
        _sort.SelectedIndexChanged += (_, _) => Search();
        _group.DropDown += (_, _) => { if (BonusBook.Shared.Groups().Count + 1 != _group.Items.Count) FillGroups(); };
        _bonus.DropDown += (_, _) => FillBonuses();
        clear.Click += (_, _) => { ClearFilters(keepText: false); Search(); };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                Show(0);
                _find.Focus();
                _find.SelectAll();
                e.SuppressKeyPress = true;
            }
        };

        _tick.Tick += (_, _) => Refresh2s();
        Shown += (_, _) =>
        {
            Search();
            UpdateStatus();
            _tick.Start();
            _find.Focus();
        };
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            _typing.Stop();
        };
        Pick(null);
    }

    // ── The data: the engine's, else the market read last (the window works without a game too) ──

    private MarketBook? _offline;
    private MarketHistory? _offlineHistory;
    private IReadOnlyDictionary<string, ItemStats>? _offlineStats;

    private MarketBook? Book()
    {
        if (_engine() is { } engine)
            return engine.CurrentMarket();
        if (_offline == null && BotEngine.LatestMarketMap() is string map)
        {
            _offline = MarketBook.Load(BotEngine.MarketFolder, map);
            _offlineHistory = MarketHistory.Load(BotEngine.MarketFolder, map);
            _offlineStats = _offline.Stats().ToDictionary(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        return _offline;
    }

    private IReadOnlyDictionary<string, ItemStats>? Stats() => _engine() is { } engine ? engine.MarketStats() : Book() == null ? null : _offlineStats;

    private MarketHistory? History() => _engine() is { } engine ? engine.CurrentHistory() : Book() == null ? null : _offlineHistory;

    private List<MarketListing> Find(MarketQuery query) => Book()?.Find(query, BonusBook.Shared, Max, allowEmpty: true) ?? [];

    // ── Building blocks ──

    private static TextBox Box(Control parent, Rectangle at, string hint, float size)
    {
        var box = new TextBox
        {
            Bounds = at, BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", size), PlaceholderText = hint,
        };
        parent.Controls.Add(box);
        return box;
    }

    private static ComboBox Pick(Control parent, Rectangle at)
    {
        var box = new ComboBox
        {
            Bounds = at, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Inset, ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f), MaxDropDownItems = 24,
        };
        parent.Controls.Add(box);
        return box;
    }

    private static ListView Grid(string[] columns, int[] widths, bool[] right)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f),
            OwnerDraw = true, GridLines = false,
        };
        for (int i = 0; i < columns.Length; i++)
            list.Columns.Add(columns[i], widths[i], right[i] ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        list.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Theme.Secondary);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                (e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left));
        };
        list.DrawItem += (_, _) => { };
        list.DrawSubItem += (_, e) =>
        {
            if (list.Tag is "custom")
                return;
            DrawCell(list, e, Theme.Text);
        };
        // The last column fills the width (no blank header beyond the columns).
        void Fill()
        {
            if (list.Columns.Count == 0 || list.ClientSize.Width <= 0)
                return;
            int others = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
                others += list.Columns[i].Width;
            list.Columns[^1].Width = Math.Max(widths[^1], list.ClientSize.Width - others);
        }
        list.Resize += (_, _) => Fill();
        list.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex != list.Columns.Count - 1) Fill(); };
        // Stops the flicker of a long list.
        typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(list, true);
        return list;
    }

    private static void DrawCell(ListView list, DrawListViewSubItemEventArgs e, Color color)
    {
        bool selected = e.Item?.Selected == true;
        using var back = new SolidBrush(selected ? Theme.Primary : (e.ItemIndex % 2 == 0 ? Theme.Inset : Color.FromArgb(13, 20, 36)));
        e.Graphics.FillRectangle(back, e.Bounds);
        var align = list.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, list.Font, Rectangle.Inflate(e.Bounds, -6, 0), selected ? Theme.Text : color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | align);
    }

    private void Show(int view)
    {
        _salesView.Visible = view == 0;
        _pricesView.Visible = view == 1;
        _wantedView.Visible = view == 2;
        for (int i = 0; i < _views.Length; i++)
        {
            _views[i].BackColor = i == view ? Theme.Primary : Theme.Secondary;
            _views[i].FlatAppearance.MouseOverBackColor = i == view ? Theme.PrimaryHover : Theme.SecondaryHover;
        }
        if (view == 1)
            FillPrices();
    }

    private void Open(string? path)
    {
        if (path == null || !File.Exists(path))
        {
            _log("Henüz pazar listesi yok: önce pazar haritasında tarat.");
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _log("Açılamadı: " + path); }
    }

    // ── Filters ──

    private void FillGroups()
    {
        var book = BonusBook.Shared;
        string? was = _group.SelectedIndex > 0 ? _groupKeys[_group.SelectedIndex] : null;
        _groupKeys = [null, .. book.Groups()];
        _group.Items.Clear();
        _group.Items.AddRange([.. _groupKeys.Select(g => g == null ? "Her eşya" : BonusBook.GroupName(g))]);
        _group.SelectedIndex = was != null && _groupKeys.IndexOf(was) is > 0 and var i ? i : 0;
    }

    private void FillBonuses()
    {
        string? group = _group.SelectedIndex > 0 ? _groupKeys[_group.SelectedIndex] : null;
        string? keep = _bonus.SelectedIndex > 0 ? _bonus.SelectedItem as string : null;
        _bonus.Items.Clear();
        _bonus.Items.Add("Her efsun");
        _bonus.Items.AddRange([.. BonusBook.Shared.Names(group)]);
        _bonus.SelectedIndex = keep != null && _bonus.Items.IndexOf(keep) is > 0 and var i ? i : 0;
    }

    private void ClearFilters(bool keepText)
    {
        if (!keepText)
            _find.Text = "";
        _seller.Text = "";
        _group.SelectedIndex = 0;
        _bonus.SelectedIndex = 0;
        _minValue.Value = 0;
        _minPrice.Value = 0;
        _maxPrice.Value = 0;
        _onlyBonused.Checked = false;
        _sort.SelectedIndex = 0;
    }

    private MarketQuery Query() => new(_find.Text,
        _group.SelectedIndex > 0 ? _groupKeys[_group.SelectedIndex] : null,
        _bonus.SelectedIndex > 0 ? _bonus.SelectedItem as string : null,
        (int)_minValue.Value,
        (MarketSort)Math.Max(0, _sort.SelectedIndex),
        (long)_minPrice.Value, (long)_maxPrice.Value, _onlyBonused.Checked, _seller.Text);

    // ── Sales ──

    private void Search()
    {
        var engine = _engine();
        var query = Query();
        string? keep = _picked is { } p ? Key(p) : null;
        _rows = Find(query);
        // The order picked in the box is the search's; a column clicked afterwards re-sorts.
        _sortColumn = query.Sort switch { MarketSort.Cheapest or MarketSort.Dearest => 3, MarketSort.Newest => 7, _ => -1 };
        _sortDown = query.Sort == MarketSort.Dearest;
        _shownUpdate = Book()?.Updated ?? default;
        _searchedAt = DateTime.Now;
        _refresh.Text = "🔄  Yenile";
        _refresh.BackColor = Theme.Secondary;
        _refresh.FlatAppearance.MouseOverBackColor = Theme.SecondaryHover;
        _list.VirtualListSize = 0;
        _list.VirtualListSize = _rows.Count;
        _list.Invalidate();
        var book = Book();
        _count.Text = book == null ? "Henüz pazar listesi yok: pazar haritasında takibi başlat."
            : $"{_rows.Count:N0}{(_rows.Count >= Max ? "+" : "")} ilan · listede {book.Shops:N0} dükkan, {book.Items:N0} ürün ({book.Map})";
        Reselect(keep);
    }

    private static string Key(MarketListing l) => $"{l.ShopId}|{l.Name}|{l.Price}|{l.Bonuses}";

    private void Reselect(string? key)
    {
        int i = key == null ? -1 : _rows.FindIndex(r => Key(r) == key);
        _list.SelectedIndices.Clear();
        if (i >= 0)
        {
            _list.SelectedIndices.Add(i);
            _list.EnsureVisible(i);
            Pick(_rows[i]);
        }
        else
            Pick(null);
    }

    private void SortRows()
    {
        var stats = Stats();
        Func<MarketListing, IComparable> by = _sortColumn switch
        {
            0 => l => l.Name,
            1 => l => l.Score,
            2 => l => l.Count,
            4 => l => Versus(l, stats) ?? double.MaxValue,
            5 => l => l.Owner,
            6 => l => Away(l) ?? double.MaxValue,
            7 => l => l.Seen,
            _ => l => l.UnitPrice,
        };
        _rows = _sortDown ? [.. _rows.OrderByDescending(by)] : [.. _rows.OrderBy(by)];
        string? keep = _picked is { } p ? Key(p) : null;
        _list.Invalidate();
        Reselect(keep);
    }

    private double? Away(MarketListing l) =>
        _engine()?.World?.Player is { } me && (l.X != 0 || l.Y != 0) ? Math.Sqrt((l.X - me.X) * (l.X - me.X) + (l.Y - me.Y) * (l.Y - me.Y)) / 100 : null;

    /// <summary>The price against the item's usual one (-0.3 = 30 % under); null for bonused items or too few sales.</summary>
    private static double? Versus(MarketListing l, IReadOnlyDictionary<string, ItemStats>? stats) =>
        l.Bonuses.Length == 0 && stats != null && stats.TryGetValue(l.Name, out var s) && s.Listings >= 3 && s.Average > 0
            ? l.UnitPrice / s.Average - 1
            : null;

    private static string Ago(DateTime at)
    {
        if (at == default)
            return "";
        int minutes = (int)(DateTime.Now - at).TotalMinutes;
        return minutes < 1 ? "şimdi" : minutes < 60 ? $"{minutes} dk" : minutes < 1440 ? $"{minutes / 60} sa" : $"{minutes / 1440} gün";
    }

    private static string Short(string bonuses) =>
        bonuses.Replace("Ortalama zarar", "Ort.", StringComparison.Ordinal).Replace("Beceri hasarı", "Bec.", StringComparison.Ordinal);

    private ListViewItem Row(int i)
    {
        if (i < 0 || i >= _rows.Count)
            return new ListViewItem(["", "", "", "", "", "", "", ""]);
        var l = _rows[i];
        var stats = Stats();
        string versus = Versus(l, stats) is double v ? (Math.Abs(v) < 0.005 ? "ortalama" : $"{(v < 0 ? "−" : "+")}%{Math.Abs(v) * 100:0}") : "";
        string bonus = l.BonusValue is int value && Query().Bonus is string b
            ? $"{b} {(value < 0 ? "-" : "+")}{Math.Abs(value)} · {Short(l.Bonuses)}"
            : Short(l.Bonuses);
        return new ListViewItem([l.Name, bonus, l.Count.ToString("N0"), BotEngine.MarketYang(l.UnitPrice), versus, l.Owner,
            Away(l) is double m ? $"{m:0} m" : "—", Ago(l.Seen)]);
    }

    private void DrawSaleCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        _list.Tag = "custom";
        var color = Theme.Text;
        if (e.ColumnIndex == 4 && e.SubItem?.Text is { Length: > 0 } t)
            color = t.StartsWith('−') ? Theme.Green : t.StartsWith('+') ? Theme.Red : Theme.Muted;
        else if (e.ColumnIndex == 3)
            color = Theme.Yellow;
        else if (e.ColumnIndex is 1 or 6 or 7)
            color = Theme.Muted;
        DrawCell(_list, e, color);
    }

    private void Pick(MarketListing? l)
    {
        _picked = l;
        _go.Enabled = l != null;
        _sellerItems.Items.Clear();
        if (l == null)
        {
            _dName.Text = _rows.Count == 0 ? "İlan yok" : "Soldan bir ilan seç";
            _dPrice.Text = _dSeller.Text = _dSeen.Text = _dBonuses.Text = _dMarket.Text = "";
            _chart.Invalidate();
            return;
        }
        var engine = _engine();
        _dName.Text = l.Name;
        _dPrice.Text = l.Count > 1
            ? $"Tanesi {BotEngine.MarketYang(l.UnitPrice)} yang · {l.Count:N0} adet = {BotEngine.MarketYang(l.Price)}"
            : $"{BotEngine.MarketYang(l.Price)} yang";
        _dSeller.Text = $"👤 {l.Owner}{(l.Level > 0 ? $" (Sv {l.Level})" : "")}{(l.Title.Length > 0 ? $" · \"{l.Title}\"" : "")}";
        _dSeen.Text = $"Okundu: {Ago(l.Seen)} önce" + (l.First != default ? $" · dükkan ilk görülme: {l.First:dd.MM HH:mm}" : "") +
                      (Away(l) is double m ? $" · {m:0} m uzakta" : "");
        _dBonuses.Text = l.Bonuses.Length == 0 ? "Efsunsuz." : BonusBook.Shared.Describe(l.Vnum, l.Bonuses);
        var book = Book();
        var same = book?.Listings(l.Name) ?? [];
        if (Stats() is { } stats && stats.TryGetValue(l.Name, out var s))
        {
            string where = Versus(l, stats) is double v
                ? (v < -0.005 ? $"Bu fiyat ortalamanın %{-v * 100:0} altında." : v > 0.005 ? $"Bu fiyat ortalamanın %{v * 100:0} üstünde." : "Bu fiyat ortalama.")
                : l.Bonuses.Length > 0 ? "Efsunlu eşyanın fiyatı efsunlarına göre değişir." : "";
            _dMarket.Text = $"{s.Listings} ilan, {s.Shops} dükkan, toplam {s.Pieces:N0} adet.\n" +
                            $"En ucuz {BotEngine.MarketYang(s.Min)} ({s.MinSeller}) · ortalama {BotEngine.MarketYang(s.Average)} · en pahalı {BotEngine.MarketYang(s.Max)}.\n" + where;
        }
        else
            _dMarket.Text = same.Count > 0 ? $"{same.Count} ilan." : "";
        foreach (var other in book?.ShopListings(l.ShopId) ?? [])
            _sellerItems.Items.Add(new ListViewItem([other.Name + (other.Bonuses.Length > 0 ? " ✦" : ""), other.Count.ToString("N0"), BotEngine.MarketYang(other.UnitPrice)])
                { ToolTipText = other.Bonuses });
        _sellerItems.ShowItemToolTips = true;
        _chart.Invalidate();
    }

    private void DrawChart(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = Rectangle.Inflate(_chart.ClientRectangle, -8, -10);
        var days = _picked == null ? [] : History()?.Of(_picked.Name) ?? [];
        days = [.. days.TakeLast(30)];
        if (days.Count == 0)
        {
            TextRenderer.DrawText(g, _picked == null ? "" : "Geçmiş birikiyor: takip sürdükçe günlük fiyatlar burada çizilir.",
                _chart.Font, _chart.ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        double top = days.Max(d => d.Average), low = days.Min(d => d.Min);
        if (top <= low)
            (top, low) = (top * 1.1 + 1, low * 0.9);
        PointF At(int i, double v) => new(area.Left + (days.Count == 1 ? area.Width / 2f : i * area.Width / (float)(days.Count - 1)),
            (float)(area.Bottom - (v - low) / (top - low) * area.Height));
        using var avgPen = new Pen(Theme.Accent, 2f);
        using var minPen = new Pen(Theme.Green, 1.5f) { DashStyle = DashStyle.Dash };
        if (days.Count > 1)
        {
            g.DrawLines(avgPen, [.. days.Select((d, i) => At(i, d.Average))]);
            g.DrawLines(minPen, [.. days.Select((d, i) => At(i, d.Min))]);
        }
        foreach (var (d, i) in days.Select((d, i) => (d, i)))
        {
            var p = At(i, d.Average);
            g.FillEllipse(Brushes.White, p.X - 2, p.Y - 2, 4, 4);
        }
        using var small = new Font("Segoe UI", 7.5f);
        TextRenderer.DrawText(g, $"{days[0].Day[5..]} … {days[^1].Day[5..]}  ·  bugün ort. {BotEngine.MarketYang(days[^1].Average)}, en ucuz {BotEngine.MarketYang(days[^1].Min)}",
            small, new Point(6, 2), Theme.Muted);
    }

    private void GoToShop()
    {
        var engine = _engine();
        if (_picked == null)
            return;
        if (engine == null)
        {
            _log("Önce oyun bağlı olmalı.");
            return;
        }
        if (!engine.StartGoToShop(_picked.ShopId, _picked.Name, out string error))
            _log(error);
    }

    private void WantPicked()
    {
        if (_picked == null)
            return;
        long max = (long)Math.Ceiling(_picked.UnitPrice);
        _profile.MarketWanted.RemoveAll(w => string.Equals(w.Name, _picked.Name, StringComparison.CurrentCultureIgnoreCase));
        _profile.MarketWanted.Add(new MarketWant { Name = _picked.Name, MaxPrice = max });
        _save();
        ShowWanted();
        _log($"🎯 Aranana eklendi: {_picked.Name}, tanesi {BotEngine.MarketYang(max)} yang ya da daha ucuza çıkınca haber verilecek.");
    }

    // ── Prices ──

    private void FillPrices()
    {
        var engine = _engine();
        string text = _priceFind.Text.Trim();
        var stats = Stats()?.Values ?? [];
        _priceRows = [.. stats.Where(s => text.Length == 0 || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => s.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(s => s.Listings)];
        _prices.VirtualListSize = 0;
        _prices.VirtualListSize = _priceRows.Count;
        _prices.Invalidate();
    }

    private ListViewItem PriceRow(int i)
    {
        if (i < 0 || i >= _priceRows.Count)
            return new ListViewItem(["", "", "", "", "", "", "", "", ""]);
        var s = _priceRows[i];
        string trend = History()?.Trend(s.Name) is double t ? $"{(t < 0 ? "−" : "+")}%{Math.Abs(t) * 100:0}" : "";
        return new ListViewItem([s.Name, s.Listings.ToString(), s.Shops.ToString(), s.Pieces.ToString("N0"), BotEngine.MarketYang(s.Min),
            BotEngine.MarketYang(s.Average), BotEngine.MarketYang(s.Max), trend, s.MinSeller]);
    }

    private void DrawPriceCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        _prices.Tag = "custom";
        var color = e.ColumnIndex switch
        {
            4 => Theme.Green,
            5 => Theme.Yellow,
            7 when e.SubItem?.Text is { Length: > 0 } t => t.StartsWith('−') ? Theme.Green : Theme.Red,
            1 or 2 or 3 or 8 => Theme.Muted,
            _ => Theme.Text,
        };
        DrawCell(_prices, e, color);
    }

    // ── Wanted items and deals ──

    private ListBox _wantList = null!;

    private void BuildWanted(Panel view)
    {
        Theme.Label(view, "ARANAN ÜRÜNLER — pazarda bu fiyata ya da daha ucuza çıkınca haber verilir (Telegram'a da)", new Rectangle(0, 0, 900, 20), 8.5f, Theme.Accent, FontStyle.Bold);
        var name = Box(view, new Rectangle(0, 26, 380, 28), "Eşya adı (ör. Kavrama Taşı+4)", 10f);
        name.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        name.AutoCompleteSource = AutoCompleteSource.CustomSource;
        name.Enter += (_, _) =>
        {
            if (name.AutoCompleteCustomSource.Count == 0 && Memory.ItemBook.Count > 0)
                name.AutoCompleteCustomSource.AddRange([.. Memory.ItemBook.All().Select(i => i.Name).Where(n => n.Length > 0).Distinct()]);
        };
        Theme.Label(view, "tanesi en fazla", new Rectangle(390, 26, 96, 28), 9f, Theme.Muted);
        var max = Theme.Number(view, new Rectangle(488, 28, 160, 24), 0, 10_000_000_000, 0, 0, 100_000);
        max.ThousandsSeparator = true;
        var add = Theme.Button(view, "➕  Ekle", new Rectangle(656, 26, 100, 28), Theme.Primary, Theme.PrimaryHover);
        _wantList = new ListBox
        {
            Bounds = new Rectangle(0, 62, 756, 240), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f), IntegralHeight = false,
        };
        view.Controls.Add(_wantList);
        var remove = Theme.Button(view, "Seçileni çıkar", new Rectangle(0, 308, 130, 28), Theme.Secondary, Theme.SecondaryHover);
        var findIt = Theme.Button(view, "🔎  Seçileni pazarda göster", new Rectangle(138, 308, 200, 28), Theme.Secondary, Theme.SecondaryHover);
        void Add()
        {
            string n = name.Text.Trim();
            if (n.Length == 0)
                return;
            _profile.MarketWanted.RemoveAll(w => string.Equals(w.Name, n, StringComparison.CurrentCultureIgnoreCase));
            _profile.MarketWanted.Add(new MarketWant { Name = n, MaxPrice = (long)max.Value });
            _save();
            name.Text = "";
            max.Value = 0;
            ShowWanted();
        }
        add.Click += (_, _) => Add();
        name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); } };
        remove.Click += (_, _) =>
        {
            int i = _wantList.SelectedIndex;
            if (i < 0 || i >= _profile.MarketWanted.Count)
                return;
            _profile.MarketWanted.RemoveAt(i);
            _save();
            ShowWanted();
        };
        findIt.Click += (_, _) =>
        {
            int i = _wantList.SelectedIndex;
            if (i < 0 || i >= _profile.MarketWanted.Count)
                return;
            var w = _profile.MarketWanted[i];
            Show(0);
            ClearFilters(keepText: false);
            _find.Text = w.Name;
            if (w.MaxPrice > 0)
                _maxPrice.Value = Math.Min(_maxPrice.Maximum, w.MaxPrice);
            Search();
        };

        Theme.Label(view, "KELEPİR", new Rectangle(0, 352, 900, 20), 8.5f, Theme.Accent, FontStyle.Bold);
        var deals = new CheckBox
        {
            Text = "Efsunsuz bir eşya (taş, kitap, malzeme…) pazar ortalamasının şu kadar altında satışa çıkınca haber ver:",
            Bounds = new Rectangle(0, 376, 640, 24), ForeColor = Theme.Text, BackColor = Theme.Background, Checked = _profile.MarketDeals,
        };
        view.Controls.Add(deals);
        var percent = Theme.Number(view, new Rectangle(646, 377, 60, 22), 10, 90, _profile.MarketDealPercent);
        Theme.Label(view, "%", new Rectangle(708, 376, 30, 24), 9f, Theme.Muted);
        deals.CheckedChanged += (_, _) => { _profile.MarketDeals = deals.Checked; _save(); };
        percent.ValueChanged += (_, _) => { _profile.MarketDealPercent = (int)percent.Value; _save(); };
        Theme.Label(view, $"Ortalama, en az {BotEngine.DealMinListings} ilan ve 3 dükkanda görülen eşyalar için hesaplanır (aşırı ucuz / pahalı uçlar sayılmaz). Her ilan bir kez bildirilir.",
            new Rectangle(0, 402, 900, 20), 8.5f, Theme.Muted);

        Theme.Label(view, "SÜREKLİ TAKİP", new Rectangle(0, 440, 900, 20), 8.5f, Theme.Accent, FontStyle.Bold);
        Theme.Label(view, "Bir dükkan şu kadar dakikada bir yeniden okunsun:", new Rectangle(0, 464, 330, 24), 9f, Theme.Text);
        var refresh = Theme.Number(view, new Rectangle(334, 465, 70, 22), 5, 1440, _profile.MarketRefreshMinutes);
        Theme.Label(view, "dk (yeni açılan dükkanlar her zaman hemen okunur; kapananlar yarım dakikada listeden çıkar)", new Rectangle(410, 464, 600, 24), 8.5f, Theme.Muted);
        refresh.ValueChanged += (_, _) => { _profile.MarketRefreshMinutes = (int)refresh.Value; _save(); };
        var notify = new CheckBox { Text = "Bulunanları Telegram'a da yaz", Bounds = new Rectangle(0, 496, 300, 24), ForeColor = Theme.Text, BackColor = Theme.Background, Checked = _profile.MarketNotify };
        view.Controls.Add(notify);
        notify.CheckedChanged += (_, _) => { _profile.MarketNotify = notify.Checked; _save(); };
        ShowWanted();
    }

    private void ShowWanted()
    {
        _wantList.Items.Clear();
        foreach (var w in _profile.MarketWanted)
            _wantList.Items.Add(w.MaxPrice > 0 ? $"{w.Name}  —  tanesi ≤ {BotEngine.MarketYang(w.MaxPrice)} yang" : $"{w.Name}  —  her fiyat");
        if (_profile.MarketWanted.Count == 0)
            _wantList.Items.Add("(boş — İlanlar'da bir satış seçip \"🎯 Bu fiyattan haber ver\"e de basabilirsin)");
    }

    // ── Every 2 s: the watch's state; the list again when it changed (the selection stays) ──

    private void Refresh2s()
    {
        UpdateStatus();
        var updated = Book()?.Updated ?? default;
        if (updated == _shownUpdate)
            return;
        // New readings: the button says so; the list itself changes only when asked, or once a minute when ticked.
        int minutes = (int)(DateTime.Now - _searchedAt).TotalMinutes;
        _refresh.Text = minutes < 1 ? "🔄  Yenile • yeni bilgi" : $"🔄  Yenile • {minutes} dk";
        _refresh.BackColor = Theme.Primary;
        _refresh.FlatAppearance.MouseOverBackColor = Theme.PrimaryHover;
        if (_autoRefresh.Checked && DateTime.Now - _searchedAt >= TimeSpan.FromMinutes(1) && _salesView.Visible &&
            !_find.Focused && !_seller.Focused && MouseButtons == MouseButtons.None && !_list.ClientRectangle.Contains(_list.PointToClient(MousePosition)))
        {
            int top = _list.TopItem?.Index ?? 0;
            Search();
            if (top > 0 && top < _rows.Count && _list.SelectedIndices.Count == 0)
                _list.EnsureVisible(Math.Min(_rows.Count - 1, top + _list.ClientSize.Height / 22));
        }
    }

    private void UpdateStatus()
    {
        var engine = _engine();
        bool scanning = engine?.ScanningMarket == true;
        bool watching = scanning && engine!.MarketWatching;
        _watch.Text = watching ? "■  Takibi durdur" : "🔁  Sürekli takibi başlat";
        _watch.BackColor = watching ? Theme.Danger : Theme.Primary;
        _watch.FlatAppearance.MouseOverBackColor = watching ? Theme.DangerHover : Theme.PrimaryHover;
        _watch.Enabled = engine != null && (!scanning || watching);
        _once.Text = scanning && !watching ? "■  Durdur" : "🛒  Bir kez tara";
        _once.Enabled = engine != null && (!scanning || !watching);
        var book = Book();
        string fresh = book?.OldestRead() is DateTime oldest ? $" · en eski okuma {Ago(oldest)} önce" : "";
        string cover = book?.Coverage() is (int onMap and > 0, int never) ? $" · pazarda {onMap:N0} dükkan{(never > 0 ? $", {never:N0} hiç okunmadı" : "")}" : "";
        _status.Text = engine == null ? (book == null ? "Oyun bağlı değil; henüz pazar listesi yok." : $"Oyun bağlı değil: son okunan liste gösteriliyor ({book.Shops:N0} dükkan, {book.Items:N0} ürün, {book.Map}){fresh}.")
            : scanning ? engine.MarketStatus + cover
            : book == null ? "Henüz pazar listesi yok. Karakteri pazar haritasına götürüp \"Sürekli takibi başlat\"a bas."
            : $"Takip kapalı. Liste: {book.Shops:N0} dükkan, {book.Items:N0} ürün ({book.Map}){fresh}{cover}.";
        _status.ForeColor = watching ? Theme.Green : Theme.Muted;
    }
}
