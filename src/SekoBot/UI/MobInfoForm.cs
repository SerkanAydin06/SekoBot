using System.Data;
using SekoBot.Bot;
using SekoBot.Memory;

namespace SekoBot.UI;

/// <summary>
/// The mobs of the current map as the game describes them (level, experience, yang, the item the
/// game lists for them) next to what the bot saw them drop (kills, drops). Searching finds mobs by
/// their name or by an item they drop ("Kaplan Postu" → Kaplan).
/// </summary>
internal sealed class MobInfoForm : Form
{
    private readonly DataTable _table = new();
    private readonly TextBox _search;
    private readonly Label _count;

    public MobInfoForm(BotEngine? engine)
    {
        Text = "Moblar ve düşenler";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(700, 400);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        string map = engine?.World?.MapName ?? "";
        foreach (var (name, type) in new (string, Type)[] { ("Harita", typeof(string)), ("Mob", typeof(string)), ("Sv", typeof(int)),
                     ("EXP", typeof(int)), ("Yang", typeof(string)), ("Oyunun yazdığı eşya", typeof(string)), ("Haritada", typeof(int)),
                     ("Öldürülen", typeof(int)), ("Görülen düşenler", typeof(string)) })
            _table.Columns.Add(name, type);

        // This map's mobs (from the game), with what was seen; then mobs seen on other maps.
        var listed = new HashSet<(string, int)>();
        foreach (var mob in engine?.MapMobs ?? [])
        {
            var drops = engine!.Drops(map, mob.Vnum);
            _table.Rows.Add(map, mob.Name, mob.Level, mob.Exp, $"{mob.GoldMin}–{mob.GoldMax}",
                mob.DropItem > 0 ? ItemBook.NameOf(mob.DropItem) : "—", mob.Spawns, drops?.Kills ?? 0,
                drops != null ? Seen(drops) : "—");
            listed.Add((map, mob.Vnum));
        }
        foreach (var (otherMap, vnum) in engine?.JournalEntries() ?? [])
        {
            if (!listed.Add((otherMap, vnum)) || engine!.Drops(otherMap, vnum) is not { } drops)
                continue;
            string name = engine.MapMobs.FirstOrDefault(m => m.Vnum == vnum)?.Name ?? $"mob #{vnum}";
            _table.Rows.Add(otherMap, name, 0, 0, "", "", 0, drops.Kills, Seen(drops));
        }

        Theme.Label(this, "Ara (mob ya da eşya):", new Rectangle(12, 12, 130, 26), 9f, Theme.Muted);
        _search = new TextBox { Bounds = new Rectangle(146, 12, 280, 26), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        _search.TextChanged += (_, _) => Filter();
        Controls.Add(_search);
        _count = Theme.Label(this, "", new Rectangle(440, 12, 448, 26), 8.5f, Theme.Muted);
        _count.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var grid = new DataGridView
        {
            Bounds = new Rectangle(12, 46, 876, 500),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            DataSource = _table,
            ReadOnly = true,
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
        grid.DefaultCellStyle.BackColor = Theme.Inset;
        grid.DefaultCellStyle.ForeColor = Theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = Theme.Secondary;
        grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.DataBindingComplete += (_, _) =>
        {
            float[] weights = [10, 24, 6, 8, 11, 22, 9, 10, 46];
            for (int i = 0; i < grid.Columns.Count && i < weights.Length; i++)
                grid.Columns[i].FillWeight = weights[i];
        };
        Controls.Add(grid);

        Theme.Label(this, engine == null
                ? "Oyun bağlı değil: yalnızca kaydedilenler görünür."
                : "Oyunun yazdığı eşya: oyunun o mob için tuttuğu tek eşya (çoğu malzeme). Tam düşme listesi sunucuda; \"Görülen düşenler\" botun " +
                  "kendi öldürdüklerinde gördükleri (bot çalıştıkça dolar; 20 öldürmeden sonra yüzde de yazar).",
            new Rectangle(12, 552, 740, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft).Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        var close = Theme.Button(this, "Kapat", new Rectangle(788, 556, 100, 30), Theme.Secondary, Theme.SecondaryHover);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        Filter();
    }

    private static string Seen(MobDrops drops) =>
        DropJournal.Describe(drops, 5) + (drops.Yang > 0 && drops.Kills > 0 ? $" · ort. {drops.Yang / drops.Kills} yang" : "");

    private void Filter()
    {
        string text = _search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        _table.DefaultView.RowFilter = text.Length == 0 ? ""
            : $"Mob LIKE '%{text}%' OR [Oyunun yazdığı eşya] LIKE '%{text}%' OR [Görülen düşenler] LIKE '%{text}%'";
        _count.Text = $"{_table.DefaultView.Count} mob · toplam öldürülen {_table.Rows.Cast<DataRow>().Sum(r => (int)r["Öldürülen"]):N0}";
    }
}
