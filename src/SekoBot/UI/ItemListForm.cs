using System.Data;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.UI;

/// <summary>
/// Every item the game knows (read from its item list, or learned from the bag), with its type and
/// shop price: the user ticks "Topla" (always pick up) or "Alma" (never walk to it). Searching by
/// name and filtering by type. One row per item name without its "+N" (Ağıt Plaka Zırh+0 … +9 is one
/// row, as are the several "Cesaret Pelerini" of the game): a tick holds for all of them.
/// </summary>
internal sealed class ItemListForm : Form
{
    private readonly BotProfile _profile;
    private readonly DataTable _table = new();
    private readonly DataGridView _grid;
    private readonly TextBox _search;
    private readonly ComboBox _type;
    private readonly CheckBox _onlyTicked;
    private readonly Label _count;

    public ItemListForm(BotProfile profile)
    {
        _profile = profile;
        Text = "Eşya listesi — mutlaka topla / alma / at";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(640, 400);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        if (ItemBook.Count == 0)
            ItemBook.Load(Path.Combine(ProfileStore.Directory, "esya-bilgisi.json"));

        _table.Columns.Add("Topla", typeof(bool));
        _table.Columns.Add("Alma", typeof(bool));
        _table.Columns.Add("At", typeof(bool));
        _table.Columns.Add("Ad", typeof(string));
        _table.Columns.Add("Tür", typeof(string));
        _table.Columns.Add("Satış", typeof(string)); // what a shop pays (a fifth of the game's price)
        _table.Columns.Add("Çeşit", typeof(int));
        _table.Columns.Add("Nolar", typeof(string)); // hidden: ",12,13,…," (the items of the row)
        var pick = profile.LootPick.ToHashSet();
        var skip = profile.LootSkip.ToHashSet();
        var drop = profile.LootThrow.ToHashSet();
        // Leftovers in the game's data (empty names, text in another code page like "¿ë½ÅÀÇ") are left out.
        foreach (var group in ItemBook.All().Where(i => Readable(i.Name)).GroupBy(i => (Name: BaseName(i.Name), Type: TypeName(i)))
                     .OrderBy(g => g.Key.Name, StringComparer.CurrentCulture).ThenBy(g => g.Key.Type))
        {
            var items = group.OrderBy(i => i.Vnum).ToList();
            int low = items.Min(i => i.Sell), high = items.Max(i => i.Sell);
            _table.Rows.Add(items.Any(i => pick.Contains(i.Vnum)), items.Any(i => skip.Contains(i.Vnum)), items.Any(i => drop.Contains(i.Vnum)),
                group.Key.Name, group.Key.Type, low == high ? $"{low:N0}" : $"{low:N0}–{high:N0}", items.Count,
                "," + string.Join(",", items.Select(i => i.Vnum)) + ",");
        }

        Theme.Label(this, "Ara:", new Rectangle(12, 12, 40, 26), 9f, Theme.Muted);
        _search = new TextBox { Bounds = new Rectangle(52, 12, 260, 26), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        _search.TextChanged += (_, _) => Filter();
        Controls.Add(_search);
        _type = new ComboBox
        {
            Bounds = new Rectangle(324, 12, 160, 26), DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Inset, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
        };
        _type.Items.Add("Hepsi");
        _type.Items.AddRange(_table.Rows.Cast<DataRow>().Select(r => (string)r["Tür"]).Distinct().Order().Cast<object>().ToArray());
        _type.SelectedIndex = 0;
        _type.SelectedIndexChanged += (_, _) => Filter();
        Controls.Add(_type);
        _onlyTicked = new CheckBox { Text = "Sadece işaretliler", Bounds = new Rectangle(494, 12, 130, 26), ForeColor = Theme.Text, BackColor = BackColor };
        _onlyTicked.CheckedChanged += (_, _) => Filter();
        Controls.Add(_onlyTicked);
        _count = Theme.Label(this, "", new Rectangle(626, 12, 190, 26), 8f, Theme.Muted);
        _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _grid = new DataGridView
        {
            Bounds = new Rectangle(12, 46, 796, 500),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            DataSource = _table,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Theme.Inset,
            BorderStyle = BorderStyle.None,
            GridColor = Theme.Border,
            EnableHeadersVisualStyles = false,
        };
        _grid.DefaultCellStyle.BackColor = Theme.Inset;
        _grid.DefaultCellStyle.ForeColor = Theme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.Secondary;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        _grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn column in _grid.Columns)
                column.ReadOnly = column.Name is not ("Topla" or "Alma" or "At");
            _grid.Columns["Topla"]!.FillWeight = 12;
            _grid.Columns["Alma"]!.FillWeight = 12;
            _grid.Columns["At"]!.FillWeight = 10;
            _grid.Columns["Ad"]!.FillWeight = 60;
            _grid.Columns["Tür"]!.FillWeight = 30;
            _grid.Columns["Satış"]!.FillWeight = 24;
            _grid.Columns["Çeşit"]!.FillWeight = 10;
            _grid.Columns["Nolar"]!.Visible = false;
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => Ticked(e.RowIndex, e.ColumnIndex);
        Controls.Add(_grid);

        Theme.Label(this, "Topla: her zaman alınır (diğer seçenekler kapalı olsa da), düşünce Telegram'a yazılır. Alma: hiç yürünmez. At: alınmaz; envanter " +
            "dolunca çantadan atılır. Her satır +0, +1, +2… hepsini kapsar (Çeşit: kaç tane).", new Rectangle(12, 552, 640, 40), 8.5f, Theme.Muted,
            FontStyle.Regular, ContentAlignment.TopLeft).Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        var close = Theme.Button(this, "Kaydet ve kapat", new Rectangle(668, 556, 140, 30), Theme.Primary, Theme.PrimaryHover);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        FormClosing += (_, _) => Save();
        Filter();
    }

    private bool _ticking;

    /// <summary>"Topla" excludes "Alma" and "At" (a row holds all the "+N" of an item).</summary>
    private void Ticked(int rowIndex, int columnIndex)
    {
        if (_ticking || rowIndex < 0 || columnIndex < 0)
            return;
        string column = _grid.Columns[columnIndex].Name;
        if (column is not ("Topla" or "Alma" or "At"))
            return;
        var row = ((DataRowView)_grid.Rows[rowIndex].DataBoundItem).Row;
        bool on = (bool)row[column];
        _ticking = true;
        if (on && column == "Topla")
            row["Alma"] = row["At"] = false;
        else if (on)
            row["Topla"] = false;
        _ticking = false;
        UpdateCount();
    }

    private void Filter()
    {
        var parts = new List<string>();
        string text = _search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        if (text.Length > 0)
            parts.Add($"Ad LIKE '%{text}%'" + (int.TryParse(text, out int number) ? $" OR Nolar LIKE '%,{number},%'" : ""));
        if (_type.SelectedIndex > 0)
            parts.Add($"Tür = '{((string)_type.SelectedItem!).Replace("'", "''")}'");
        if (_onlyTicked.Checked)
            parts.Add("(Topla = true OR Alma = true OR At = true)");
        _table.DefaultView.RowFilter = string.Join(" AND ", parts.Select(p => "(" + p + ")"));
        UpdateCount();
    }

    private void UpdateCount()
    {
        int pick = _table.Rows.Cast<DataRow>().Count(r => (bool)r["Topla"]);
        int skip = _table.Rows.Cast<DataRow>().Count(r => (bool)r["Alma"]);
        int drop = _table.Rows.Cast<DataRow>().Count(r => (bool)r["At"]);
        _count.Text = $"{_table.DefaultView.Count} / {_table.Rows.Count} · topla {pick} · alma {skip} · at {drop}";
    }

    private void Save()
    {
        _grid.EndEdit();
        _profile.LootPick = Ticked("Topla");
        _profile.LootSkip = Ticked("Alma");
        _profile.LootThrow = Ticked("At");
    }

    /// <summary>Every item of the rows ticked in this column.</summary>
    private List<int> Ticked(string column) =>
        [.. _table.Rows.Cast<DataRow>().Where(r => (bool)r[column])
            .SelectMany(r => ((string)r["Nolar"]).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)).Distinct().Order()];

    /// <summary>"Ağıt Plaka Zırh+3" → "Ağıt Plaka Zırh" (the "+N" of an item's level).</summary>
    internal static string BaseName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s*\+\s*\d+$", "");

    internal static bool Readable(string name) =>
        name.Trim().Length > 1 && !name.Any(c => c is '¿' or '¼' or '½' or '¾' or 'À' or 'Á' or 'Â' or 'Ã' or 'Å' or 'È' or 'É' or 'Ê' or 'Ë' or 'Ì' or 'Í' or 'Ó' or 'Ô' or 'Õ' or '¸' or '¹' or 'º' or '¶');

    /// <summary>The game's item type in Turkish, e.g. "Silah", "İksir", "Malzeme".</summary>
    internal static string TypeName(ItemInfo item) => (item.Type, item.SubType) switch
    {
        (_, "USE_POTION") => "İksir",
        ("ITEM_WEAPON", _) => "Silah",
        ("ITEM_ARMOR", "ARMOR_BODY") => "Zırh",
        ("ITEM_ARMOR", "ARMOR_HEAD") => "Miğfer",
        ("ITEM_ARMOR", "ARMOR_SHIELD") => "Kalkan",
        ("ITEM_ARMOR", "ARMOR_FOOTS") => "Ayakkabı",
        ("ITEM_ARMOR", _) => "Takı",
        ("ITEM_MATERIAL", _) => "Malzeme",
        ("ITEM_SKILLBOOK", _) => "Beceri kitabı",
        ("ITEM_TREASURE_BOX", _) or ("ITEM_GIFTBOX", _) => "Sandık / hediye",
        ("ITEM_METIN", _) => "Ruh taşı",
        ("ITEM_FISH", _) => "Balık",
        ("ITEM_USE", _) => "Kullanım",
        ("ITEM_QUEST", _) => "Görev",
        ("ITEM_COSTUME", _) => "Kostüm",
        ("ITEM_UNIQUE", _) or ("ITEM_SPECIAL", _) => "Özel",
        ("ITEM_PET", _) => "Evcil hayvan",
        ("ITEM_BLEND", _) => "Özüt",
        ("ITEM_NONE", _) => "Diğer",
        _ => item.Type.Length > 5 ? item.Type[5..].ToLowerInvariant() : "Diğer",
    };
}
