using System.Data;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.UI;

/// <summary>
/// The bonus list ("efsun listesi"): every bonus kind the game has, with a tick "Al" and the least
/// value. A weapon or armour with a ticked bonus at least that big is kept (and told on Telegram),
/// whatever its price; while anything is ticked all weapons and armour are picked up to look at them.
/// </summary>
internal sealed class BonusListForm : Form
{
    private readonly BotProfile _profile;
    private readonly DataTable _table = new();
    private readonly DataGridView _grid;

    public BonusListForm(BotProfile profile)
    {
        _profile = profile;
        Text = "Efsun listesi — bu efsunlu silah / zırhlar alınır";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 600);
        MinimumSize = new Size(460, 400);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        _table.Columns.Add("Al", typeof(bool));
        _table.Columns.Add("Efsun", typeof(string));
        _table.Columns.Add("En az", typeof(int));
        _table.Columns.Add("Tür", typeof(int));
        var wanted = profile.LootBonuses.ToDictionary(w => w.Type, w => w.Min);
        foreach (var (type, name) in ItemBonuses.All().OrderBy(b => b.Name, StringComparer.CurrentCulture))
        {
            var known = wanted.FirstOrDefault(w => ItemBonuses.Same(w.Key, type));
            bool on = known.Key != 0;
            _table.Rows.Add(on, name, on ? known.Value : 1, type);
        }

        Theme.Label(this, "Bir efsunu işaretle ve en az değerini yaz (ör. EXP bonusu, en az 10: +10 ve üstü alınır). " +
            "Liste boş değilken bütün silah ve zırhlar alınır (efsun ancak çantada görünür); işaretli bir efsunu olmayan " +
            "ve satış fiyatı sınırın altında kalan geri atılır. İşaretli efsunlu eşya alınınca Telegram'a yazılır.",
            new Rectangle(12, 8, 536, 58), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft).Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _grid = new DataGridView
        {
            Bounds = new Rectangle(12, 70, 536, 476),
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
            _grid.Columns["Al"]!.FillWeight = 12;
            _grid.Columns["Efsun"]!.FillWeight = 70;
            _grid.Columns["Efsun"]!.ReadOnly = true;
            _grid.Columns["En az"]!.FillWeight = 18;
            _grid.Columns["Tür"]!.Visible = false;
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.DataError += (_, e) => e.ThrowException = false; // a value that isn't a number: kept as it was
        Controls.Add(_grid);

        var close = Theme.Button(this, "Kaydet ve kapat", new Rectangle(408, 556, 140, 30), Theme.Primary, Theme.PrimaryHover);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        var clear = Theme.Button(this, "Hepsini kaldır", new Rectangle(12, 556, 130, 30), Theme.Secondary, Theme.SecondaryHover);
        clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        clear.Click += (_, _) =>
        {
            foreach (DataRow row in _table.Rows)
                row["Al"] = false;
        };
        FormClosing += (_, _) => Save();
    }

    private void Save()
    {
        _grid.EndEdit();
        _profile.LootBonuses = [.. _table.Rows.Cast<DataRow>().Where(r => (bool)r["Al"])
            .Select(r => new BonusWant { Type = (int)r["Tür"], Min = Math.Max(0, r["En az"] is int min ? min : 0) })];
    }
}
