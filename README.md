# SekoBot · Windows Masaüstü Uygulaması

**Metin2 pencere bağlantısı için bağımsız SekoBot.exe**. Godot editörü, Python, PowerShell yardımcı işlemi veya bilgisayarda .NET kurulumu gerektirmez.

## İndir ve çalıştır

**[SekoBot.exe — doğrudan indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)**

Alternatif: [SekoBot Releases](https://github.com/SerkanAydin06/SekoBot/releases/tag/preview) sayfasındaki **Assets → SekoBot.exe**.

Dosya, GitHub'daki **Windows Build** iş akışı başarılı olduğunda oluşur. Derleme devam ediyorsa veya başarısızsa indirme bağlantısı henüz çalışmayabilir: [Derlemeyi kontrol et](https://github.com/SerkanAydin06/SekoBot/actions/workflows/build-windows.yml).

1. İnen `SekoBot.exe` dosyasını normal bir klasöre kaydet.
2. Metin2'yi pencere modunda aç.
3. SekoBot.exe'yi çift tıkla.
4. Açık pencere listesinden oyun penceresini seç ve **Pencereye bağlan** düğmesine bas.
5. Önizlemeyi kontrol et; **Ekran görüntüsü kaydet** ile PNG oluştur.

Uygulama **Windows 10/11 x64** içindir. İlk çalıştırmada Windows SmartScreen, dijital imza olmadığından uyarı gösterebilir; çalıştırmadan önce dosyanın bu depodan geldiğini doğrula.

## Özellikler

- Küçük, koyu temalı, çerçevesiz WinForms arayüzü (940×595)
- Başlıktan sürükleyerek taşıma, küçültme, kapatma
- Windows API üzerinden **doğrudan** açık pencere arama, gerçek oyun alanı boyutu
- HWND + PID kimliğiyle hedef pencere takibi; kapanma/yeniden açılma denetimi
- Canlı görüntü (5/10/15 FPS), FPS/çözünürlük/kare sayaçları
- Windows ekranındaki **görünür** pencere alanını yakalama; ayrı PowerShell hizmeti ve geçici JSON/PNG alışverişi yok
- PNG kayıtları: Kullanıcı profilindeki `Pictures\SekoBot`
- Pencere bağlantısı, tarama ve yakalama hatalarını açıklayan sistem günlüğü

Bu aşamada **otomatik farm / oyun girdisi gönderme yoktur**. Önce canlı görüntünün sorunsuz yakalanması hedefleniyor.

## Yakalama sınırlamaları

İlk sürüm ekrandaki görünür pikselleri `CopyFromScreen` ile yakalar. Oyun penceresi **simgede olmamalı**, SekoBot dahil başka pencereler oyun alanını örtmemelidir. DirectX/özel tam ekran veya ekran korumalı pencerelerde siyah görüntü oluşabilir. Metin2'yi **pencere modunda** kullan ve SekoBot penceresini oyunun yanına taşı. Gerekirse sonraki sürümde Windows Graphics Capture eklenecektir.

Uygulama oyun belleğini okumaz, oyuna kod enjekte etmez ve görüntüleri herhangi bir sunucuya yüklemez.

## Kaynak kod ve geliştirme

- `SekoBot.Windows.csproj`: Windows Forms / .NET 10 projesi
- `src/WindowService.cs`: Win32 pencere listesi, HWND/PID ve istemci alanı koordinatları
- `src/ScreenCapture.cs`: GDI+ canlı görüntü yakalama
- `src/DashboardForm.cs`: Kontrol paneli, canlı önizleme ve kayıt
- `.github/workflows/build-windows.yml`: GitHub üzerinde tek dosyalı bağımsız EXE derleme

Kod geliştirmek için .NET 10 SDK kullanılabilir. Ancak **hazır EXE kullanıcısının hiçbir SDK veya dil kurmasına gerek yoktur**.

Yeni kod `main` dalına gönderildiğinde GitHub aynı **preview** sürümündeki SekoBot.exe dosyasını günceller; her seferinde yeni bir sürüm adresi açılmaz.

## Test durumu

Derleme GitHub Actions üzerindeki Windows derleyicisi tarafından doğrulanır. Metin2 istemcisi üzerindeki gerçek görüntü yakalama testi ayrıca Windows bilgisayarda yapılmalıdır.
