# SekoBot · Godot Desktop

Metin2 penceresi için **Godot 4.5 .NET** tabanlı Windows masaüstü arayüzü. Python, PySide6 ve otomatik GitHub Releases süreçleri kaldırılmıştır. Geliştirme sırasında doğrudan Godot editöründen çalışır.

## Kurulum (bir defalık)

> **Godot'un standart sürümü yeterli değildir.** Windows pencerelerini listelemek/yakalamak için C# Win32 köprüsü vardır.

- [Godot 4.5 .NET editor (Windows x64)](https://godotengine.org/download/windows/) — indirme sayfasından **.NET** sürümünü seç.
- [.NET 8 SDK (Windows x64)](https://dotnet.microsoft.com/download/dotnet/8.0) — yalnızca Godot içinde C# kodunu derlemek için gerekir. Python gerekmez.

1. Depoyu klonla veya **Code → Download ZIP** ile indirip çıkar.
2. Godot **.NET editörü** üzerinden **Import** seç ve bu klasördeki **project.godot** dosyasını aç.
3. Üst menüde **Build** düğmesini bir kez çalıştır (C# köprüsü derlenir).
4. **F6** (ana sahne açıksa) veya **F5** ile SekoBot'u çalıştır.
5. Metin2'yi pencere modunda aç. SekoBot penceresini listeler; tek Metin2 varsa otomatik bağlanır. Gerekirse listeden seçip **Pencereye bağlan** butonuna bas.

Her kod değişikliğinde GitHub'a push/pull yapıp Godot'ta **Build** (C# değiştiyse) ve **F5** yeterlidir. **Releases, Python ve her güncelleme için EXE gerekmez.** Son sürüm hazır olduğunda Godot > Project > Export üzerinden Windows EXE üretilebilir.

## Şu anda çalışan özellikler

- Koyu temalı Godot arayüzü ve bağlantı durum paneli.
- Açık Windows pencerelerini listeleme; Metin2 başlıklarını öne alma.
- Seçilen pencereye **HWND + PID** ile bağlanma ve eski/değişmiş pencereyi reddetme.
- Pencerenin **başlık çubuğu hariç yalnızca oyun alanını** yakalama.
- Pencere konumu ve boyutu değiştiğinde her karede güncel koordinatları kullanma.
- 5 / 10 / 15 FPS canlı önizleme ve çözünürlük/FPS/kare sayacı.
- **Ekran görüntüsü kaydet**: varsayılan olarak \`%USERPROFILE%\Pictures\SekoBot\`.
- Bağlantıyı durdurma; simge durumuna küçültme/hata senaryolarında uyarı.
- FPS ayarını \`user://settings.cfg\` içerisinde hatırlama.

## Proje yapısı

\`\`\`text
SekoBot/
├── project.godot               # Godot proje giriş dosyası
├── SekoBot.csproj              # Godot .NET 8 projesi
├── scenes/
│   └── main.tscn              # Ana Godot sahnesi
├── scripts/
│   ├── main.gd                # GDScript kontrol paneli
│   └── WindowCaptureBridge.cs # C# / Win32 pencere keşfi ve görüntü yakalama
└── docs/
    └── ROADMAP.md             # Sonraki modüller ve test planı
\`\`\`

## Bilinmesi gerekenler

- **Bu sürüm bir ekran izleyicisidir**; otomatik farm, hareket, saldırı veya klavye/fare komutu henüz yoktur.
- Windows **BitBlt/GDI** ekran yakalama kullanılır. Metin2 **görünür ve pencere modunda** olmalıdır. Üstünü kapatan başka pencere görüntüye girer; küçültülmüş pencereden görüntü alınamaz.
- DirectX özel tam ekran veya donanım korumalı pencerelerde siyah görüntü oluşabilir. Önce pencere modunda deneyin; sonraki aşamada Windows Graphics Capture alternatifi eklenebilir.
- Kare yakalama şu anda **Godot ana iş parçacığında** gerçekleşir; 10 FPS başlangıç için makuldür. Oyun ve bilgisayara göre hız ayarlanabilir. Daha sonra worker thread'e alınabilir.
- Uygulama dışarıya veri göndermez. PNG'ler yerel diske kaydedilir.
- Bu ortamda Godot .NET/Windows GUI çalıştırma imkânı olmadığından **gerçek pencere yakalama testi Windows üzerinde yapılmalıdır**.

## Sorun giderme

- **C# script yüklenemedi:** Standart Godot yerine **.NET** sürümünü aç, .NET 8 SDK kur, editörde **Build** yap.
- **Pencere listesi boş:** Metin2'yi pencere modunda aç; GDScript'in Build sonrası güncellendiğini kontrol et.
- **Siyah/yanlış görüntü:** Tam ekranı kapat; oyun penceresinin başka pencere tarafından örtülmediğinden emin ol.
- **Kare hızı düşük:** Önizlemeyi 5 FPS yap; daha düşük oyun çözünürlüğü dene.

**Not:** Godot projeleri GDScript + C# ile hibrit çalışabilir. Arayüz sahneleri GDScript, Windows API erişimi C# olarak ayrılmıştır.
