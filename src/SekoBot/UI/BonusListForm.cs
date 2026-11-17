using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.UI;

/// <summary>
/// The bonus list ("efsun listesi"), all by picking, nothing to type: on the left the bonuses the
/// shops showed (most seen first), each with a tick; on the right, for the one selected, its least
/// value from the values it really comes with (EXP: 6 / 8 / 10 / 12 / 15 — no "1") and the items it
/// is wanted on: every item it can come on, or only the ones ticked in its item list (Deri Ayakkabı…).
/// Both lists come from the bonus book, so a value or item first seen in a shop shows up here too.
/// A weapon or armour with a ticked bonus at least that big is kept (and told on Telegram).
/// </summary>
internal sealed class BonusListForm : Form
{
    private sealed class Choice
    {
        public required int Type;
        public required string Name;
        public bool On;
        public int Min;
        public HashSet<string> Items = new(StringComparer.CurrentCultureIgnoreCase);
    }

    private readonly BotProfile _profile;
    private readonly BonusBook? _book;
    private readonly List<Choice> _choices = [];
    private readonly ListView _bonuses;
    private readonly Label _title, _where;
    private readonly FlowLayoutPanel _values;
    private readonly RadioButton _allItems, _someItems;
    private readonly TextBox _filter;
    private readonly CheckedListBox _items;
    private readonly Label _allNote;
    private Choice? _current;
    private bool _filling;
    private List<(string Group, string Name)> _itemRows = [];

    public BonusListForm(BotProfile profile)
    {
        _profile = profile;
        try { _book = BonusBook.Shared; }
        catch (IOException) { }
        Text = "Efsun listesi — bu efsunlu silah / zırhlar alınır";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 620);
        MinimumSize = new Size(760, 480);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        // Every bonus kind the game has; those the shops showed first (most seen first), the rest after.
        var names = ItemBonuses.All().GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First().Type);
        var known = _book?.Names() ?? [];
        foreach (var name in known.Where(names.ContainsKey).Concat(names.Keys.Where(n => !known.Contains(n)).Order(StringComparer.CurrentCulture)))
        {
            var choice = new Choice { Type = names[name], Name = name };
            if (profile.LootBonuses.FirstOrDefault(w => ItemBonuses.Same(w.Type, choice.Type)) is { } want)
            {
                choice.On = true;
                // A least value that doesn't exist (an old "1"): the lowest one that does.
                var values = _book?.AllValues(name) ?? [];
                choice.Min = values.Count > 0 && !values.Contains(want.Min) ? values.FirstOrDefault(v => v >= want.Min, values[^1]) : want.Min;
                foreach (var item in want.ItemNames())
                    choice.Items.Add(item);
            }
            _choices.Add(choice);
        }

        Theme.Label(this, "Soldan efsunu işaretle; sağda en az değerini ve hangi eşyalarda istediğini seç. Değerler ve eşyalar pazarda " +
            "görülenlerden gelir (yeni bir değer ya da eşya görülünce burada da çıkar). Yerde efsun görünmez: bot yalnız seçilen eşyaları alır, " +
            "çantada bakar, efsunu tutmayanı geri atar.",
            new Rectangle(12, 8, 836, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft).Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // ── Left: the bonuses ──
        _bonuses = new ListView
        {
            Bounds = new Rectangle(12, 54, 340, 512), View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false,
            HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Theme.Inset, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _bonuses.Columns.Add("Efsun", 180);
        _bonuses.Columns.Add("Seçim", 136);
        foreach (var choice in _choices)
        {
            var row = new ListViewItem([choice.Name, ""]) { Checked = choice.On, Tag = choice };
            if (_book != null && _book.AllValues(choice.Name).Count == 0)
                row.ForeColor = Theme.Muted; // never seen in a shop yet
            _bonuses.Items.Add(row);
        }
        foreach (ListViewItem row in _bonuses.Items)
            Summarize(row);
        _bonuses.ItemChecked += (_, e) =>
        {
            if (_filling || e.Item.Tag is not Choice c)
                return;
            c.On = e.Item.Checked;
            if (c.On && c.Min == 0 && _book?.AllValues(c.Name) is { Count: > 0 } values)
                c.Min = values[0];
            Summarize(e.Item);
            if (c == _current)
                ShowChoice();
        };
        _bonuses.SelectedIndexChanged += (_, _) =>
        {
            if (_bonuses.SelectedItems.Count > 0 && _bonuses.SelectedItems[0].Tag is Choice c)
            {
                _current = c;
                ShowChoice();
            }
        };
        Controls.Add(_bonuses);

        // ── Right: the selected bonus ──
        const int x = 368;
        _title = Theme.Label(this, "Soldan bir efsun seç", new Rectangle(x, 54, 480, 24), 11f, Theme.Text, FontStyle.Bold);
        _where = Theme.Label(this, "", new Rectangle(x, 80, 480, 20), 8.5f, Theme.Muted);
        Theme.Label(this, "EN AZ DEĞER", new Rectangle(x, 108, 200, 18), 8f, Theme.Accent, FontStyle.Bold);
        _values = new FlowLayoutPanel { Bounds = new Rectangle(x, 128, 480, 64), BackColor = Theme.Background, WrapContents = true, AutoScroll = true };
        Controls.Add(_values);
        Theme.Label(this, "HANGİ EŞYALARDA", new Rectangle(x, 198, 200, 18), 8f, Theme.Accent, FontStyle.Bold);
        _allItems = new RadioButton { Text = "Hepsi (bu efsunun gelebildiği her eşya)", Bounds = new Rectangle(x, 218, 300, 24), ForeColor = Theme.Text, Checked = true };
        _someItems = new RadioButton { Text = "Sadece seçtiklerim", Bounds = new Rectangle(x + 300, 218, 180, 24), ForeColor = Theme.Text };
        Controls.Add(_allItems);
        Controls.Add(_someItems);
        _filter = new TextBox
        {
            Bounds = new Rectangle(x, 246, 480, 24), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Listede ara (ör. deri)",
        };
        Controls.Add(_filter);
        _items = new CheckedListBox
        {
            Bounds = new Rectangle(x, 274, 480, 292), CheckOnClick = true, BackColor = Theme.Inset, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(_items);
        _allNote = Theme.Label(this, "", new Rectangle(x, 250, 480, 60), 9f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);
        _allItems.CheckedChanged += (_, _) =>
        {
            if (_filling || _current == null || !_allItems.Checked)
                return;
            _current.Items.Clear();
            ShowItems();
            SummarizeCurrent();
        };
        _someItems.CheckedChanged += (_, _) => { if (!_filling) ShowItems(); };
        _filter.TextChanged += (_, _) => ShowItems();
        _items.ItemCheck += (_, e) =>
        {
            if (_filling || _current == null || e.Index >= _itemRows.Count)
                return;
            string name = _itemRows[e.Index].Name;
            if (e.NewValue == CheckState.Checked)
                _current.Items.Add(name);
            else
                _current.Items.Remove(name);
            if (_current.Items.Count > 0 && !_current.On)
                Tick(_current, true);
            SummarizeCurrent();
        };

        var close = Theme.Button(this, "Kaydet ve kapat", new Rectangle(708, 576, 140, 32), Theme.Primary, Theme.PrimaryHover);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        var clear = Theme.Button(this, "Hepsini kaldır", new Rectangle(12, 576, 130, 32), Theme.Secondary, Theme.SecondaryHover);
        clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        clear.Click += (_, _) =>
        {
            foreach (var c in _choices)
            {
                c.On = false;
                c.Items.Clear();
            }
            _filling = true;
            foreach (ListViewItem row in _bonuses.Items)
            {
                row.Checked = false;
                Summarize(row);
            }
            _filling = false;
            ShowChoice();
        };
        FormClosing += (_, _) => Save();

        // Start on the first ticked bonus (else the most seen one).
        var first = _bonuses.Items.Cast<ListViewItem>().FirstOrDefault(r => r.Checked) ?? (_bonuses.Items.Count > 0 ? _bonuses.Items[0] : null);
        if (first != null)
        {
            first.Selected = true;
            first.EnsureVisible();
        }
        ShowChoice();
    }

    private void Tick(Choice c, bool on)
    {
        c.On = on;
        _filling = true;
        foreach (ListViewItem row in _bonuses.Items)
            if (row.Tag == c)
                row.Checked = on;
        _filling = false;
    }

    /// <summary>The right side for the selected bonus: its values as buttons, its items as a tick list.</summary>
    private void ShowChoice()
    {
        _filling = true;
        _values.Controls.Clear();
        var c = _current;
        bool any = c != null;
        _allItems.Enabled = _someItems.Enabled = _filter.Enabled = _items.Enabled = any;
        if (c == null)
        {
            _title.Text = "Soldan bir efsun seç";
            _where.Text = "";
            _items.Items.Clear();
            _filling = false;
            return;
        }
        _title.Text = c.Name;
        var values = _book?.AllValues(c.Name) ?? [];
        _where.Text = _book == null ? "" : "Gelir: " + _book.WhereText(c.Name);
        if (values.Count == 0)
            _values.Controls.Add(new Label
            {
                Text = "Pazarda henüz görülmedi: değeri bilinmiyor, her değeri alınır.", AutoSize = true, ForeColor = Theme.Muted,
                Margin = new Padding(0, 6, 0, 0),
            });
        // A weapon's own damage rows take any number: then a few steps, not every one.
        var steps = values.Count > 10 ? values.Where((v, i) => i % Math.Max(1, values.Count / 8) == 0 || i == values.Count - 1).ToList() : values;
        int chosen = steps.Count == 0 ? 0 : steps.Where(v => v <= Math.Max(c.Min, steps[0])).DefaultIfEmpty(steps[0]).Max();
        foreach (int v in steps)
        {
            var button = new RadioButton
            {
                Text = (v < 0 ? "" : "+") + v + (v == steps[^1] ? " ★" : ""), Appearance = Appearance.Button, AutoSize = false,
                Size = new Size(64, 28), TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Secondary, ForeColor = Theme.Text, Checked = c.On && v == chosen, Tag = v, Margin = new Padding(0, 0, 6, 6),
            };
            button.FlatAppearance.CheckedBackColor = Theme.Primary;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.CheckedChanged += (_, _) =>
            {
                if (_filling || !button.Checked || _current == null)
                    return;
                _current.Min = (int)button.Tag!;
                if (!_current.On)
                    Tick(_current, true);
                SummarizeCurrent();
            };
            _values.Controls.Add(button);
        }
        if (steps.Count > 0)
            _values.Controls.Add(new Label
            {
                Text = "ve üstü", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 7, 0, 0),
            });
        _someItems.Checked = c.Items.Count > 0;
        _allItems.Checked = c.Items.Count == 0;
        _filter.Text = "";
        _filling = false;
        ShowItems();
    }

    private void ShowItems()
    {
        var c = _current;
        _filling = true;
        _items.BeginUpdate();
        _items.Items.Clear();
        _itemRows = [];
        if (c != null && _book != null && _someItems.Checked)
        {
            string filter = _filter.Text.Trim();
            var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
            // The ticked ones first, then the rest, by group.
            _itemRows = [.. _book.ItemsFor(c.Name)
                .Where(i => filter.Length == 0 || tr.IndexOf(i.Name, filter, System.Globalization.CompareOptions.IgnoreCase) >= 0 ||
                            tr.IndexOf(BonusBook.GroupName(i.Group), filter, System.Globalization.CompareOptions.IgnoreCase) >= 0)
                .OrderByDescending(i => c.Items.Contains(i.Name))];
            foreach (var (group, name) in _itemRows)
                _items.Items.Add($"{BonusBook.GroupName(group)} · {name}", c.Items.Contains(name));
            if (_itemRows.Count == 0)
                _items.Items.Add(filter.Length > 0 ? "(bu aramayla eşya yok)" : "(bu efsunun geldiği eşyalar henüz bilinmiyor)");
        }
        _items.EndUpdate();
        // "All of them": no list to tick, just what that means.
        bool list = c != null && _someItems.Checked;
        _items.Visible = _filter.Visible = list;
        _allNote.Visible = c != null && !list;
        if (c != null && !list && _book != null)
            _allNote.Text = $"Bu efsunun gelebildiği bütün eşyalar alınır: {string.Join(", ", _book.GroupsOf(c.Name).Select(BonusBook.GroupName))}.\n" +
                            "Yalnız belirli eşyaları istiyorsan \"Sadece seçtiklerim\"i seç ve listeden işaretle.";
        _filling = false;
    }

    private void SummarizeCurrent()
    {
        foreach (ListViewItem row in _bonuses.Items)
            if (row.Tag == _current)
                Summarize(row);
    }

    private static void Summarize(ListViewItem row)
    {
        if (row.Tag is not Choice c)
            return;
        row.SubItems[1].Text = !c.On ? "" :
            $"≥ {c.Min}" + (c.Items.Count == 0 ? " · her eşya" : c.Items.Count == 1 ? $" · {c.Items.First()}" : $" · {c.Items.Count} eşya");
    }

    private void Save()
    {
        _profile.LootBonuses = [.. _choices.Where(c => c.On).Select(c => new BonusWant
        {
            Type = c.Type, Min = Math.Max(0, c.Min), Items = string.Join(", ", c.Items.Order(StringComparer.CurrentCulture)),
        })];
    }
}
