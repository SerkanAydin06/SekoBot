using SekoBot.Config;

namespace SekoBot.UI;

/// <summary>
/// The pick-up settings saved under a name (see <see cref="LootPresets"/>): the ones shown now saved,
/// or a saved one loaded into this character (made once on one bot, loaded on the others).
/// </summary>
internal sealed class LootPresetForm : Form
{
    private readonly BotProfile _profile;
    private readonly ListBox _list;
    private readonly TextBox _name;
    private readonly Label _note;

    /// <summary>The name loaded into the profile (null: nothing was loaded).</summary>
    public string? Loaded { get; private set; }

    public LootPresetForm(BotProfile profile, string suggestedName)
    {
        _profile = profile;
        Text = "Kayıtlı eşya ayarları";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(520, 400);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        Theme.Label(this, "Toplanacak / alınmayacak / atılacak eşyalar, Topla-Alma-At listesi, efsun listesi, fiyat sınırı ve " +
            "satış ayarı bir isimle kaydedilir. Diğer botlarda aynı isim seçilip 'Yükle'ye basılınca hepsi o karaktere aynen gelir. " +
            "Dövüş, iksir ve alan ayarları değişmez.",
            new Rectangle(12, 8, 496, 58), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        Theme.Label(this, "KAYITLI AYARLAR", new Rectangle(12, 72, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        _list = new ListBox
        {
            Bounds = new Rectangle(12, 94, 330, 210),
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f),
        };
        Controls.Add(_list);
        var load = Theme.Button(this, "📂  Yükle", new Rectangle(354, 94, 154, 32), Theme.Primary, Theme.PrimaryHover);
        var delete = Theme.Button(this, "🗑  Sil", new Rectangle(354, 134, 154, 32), Theme.Secondary, Theme.SecondaryHover);

        Theme.Label(this, "ŞU ANKİ AYARLARI KAYDET", new Rectangle(12, 314, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        Theme.Label(this, "İsim", new Rectangle(12, 338, 40, 26), 9f, Theme.Muted);
        _name = new TextBox
        {
            Bounds = new Rectangle(56, 338, 286, 26),
            Text = suggestedName,
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_name);
        var save = Theme.Button(this, "💾  Kaydet", new Rectangle(354, 336, 154, 30), Theme.Primary, Theme.PrimaryHover);
        _note = Theme.Label(this, "", new Rectangle(12, 370, 496, 24), 9f, Theme.Text);

        Fill();
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedItem is string chosen) _name.Text = chosen; };
        _list.DoubleClick += (_, _) => Load();
        load.Click += (_, _) => Load();
        save.Click += (_, _) => Save();
        delete.Click += (_, _) =>
        {
            if (_list.SelectedItem is not string chosen)
            {
                _note.Text = "Önce listeden bir kayıt seç.";
                return;
            }
            if (MessageBox.Show(this, $"\"{chosen}\" kaydı silinsin mi? (Botların şu anki ayarları değişmez.)", "Sil",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            LootPresets.Delete(chosen);
            Fill();
            _note.Text = $"\"{chosen}\" silindi.";
        };
    }

    private void Fill(string? select = null)
    {
        _list.Items.Clear();
        foreach (var name in LootPresets.All())
            _list.Items.Add(name);
        if (select != null && _list.Items.IndexOf(select) is >= 0 and var at)
            _list.SelectedIndex = at;
    }

    private void Save()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _note.Text = "Bir isim yaz (ör. \"KAiZER ayarı\").";
            return;
        }
        if (LootPresets.Exists(name) && MessageBox.Show(this, $"\"{name}\" adında bir kayıt var; şu anki ayarlarla değiştirilsin mi?",
                "Kaydet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            LootPresets.Save(name, _profile);
            Fill(name);
            _note.Text = $"✓ \"{name}\" kaydedildi. Diğer botlarda bu pencereden yükleyebilirsin.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _note.Text = "Kaydedilemedi: " + ex.Message;
        }
    }

    private void Load()
    {
        if (_list.SelectedItem is not string chosen)
        {
            _note.Text = "Önce listeden bir kayıt seç.";
            return;
        }
        if (!LootPresets.Load(chosen, _profile))
        {
            _note.Text = $"\"{chosen}\" okunamadı.";
            return;
        }
        Loaded = chosen;
        DialogResult = DialogResult.OK;
        Close();
    }
}
