# SekoBot — Standart Godot 4

Metin2 penceresi izlemek için Godot 4 + GDScript tabanlı masaüstü arayüzü.

## Gereksinimler

- Windows 10/11 ve **Godot 4.5+ standart editörü**.
- Windows ile gelen Windows PowerShell 5.1.
- **Python ve .NET SDK kurmana gerek yok.**

Yerel PowerShell yardımcısı, `tools/WindowNative.cs` içindeki Win32 kodunu PowerShell'in `Add-Type` komutuyla çalışırken derler. Bu **Godot C# script'i değildir**; Godot sahnesi yalnızca GDScript yükler.

## Çalıştırma

1. GitHub'dan **Code → Download ZIP** ile güncel depoyu indirip klasöre çıkar veya mevcut projeyi güncelle.
2. Godot standart editörüyle `project.godot` dosyasını aç.
3. **F5** ile çalıştır — Build yapman gerekmez.
4. Metin2'yi pencere modunda aç. SekoBot pencere listesini tarar ve tek Metin2 bulursa bağlanmayı dener.
5. Önizleme geldikten sonra **Ekran görüntüsü kaydet** seçeneğini kullanabilirsin.

## Mimari

- `scripts/main.gd`: Koyu temalı Godot kontrol paneli, önizleme ve istatistikler.
- `scripts/WindowCaptureBridge.gd`: JSON/PNG üzerinden Windows yardımcı işlemine bağlanır.
- `tools/capture_helper.ps1`: Windows PowerShell yardımcı hizmeti.
- `tools/WindowNative.cs`: Win32 API pencere keşfi ve görünür ekran yakalama.
- `docs/ROADMAP.md`: Geliştirme planı.

## Özellikler ve sınırlamalar

Her zaman görünür kaydırılabilir pencere listesi, Metin2 otomatik seçimi, HWND/PID doğrulaması, 5/10/15 FPS önizleme, olay günlüğü ve PNG kayıtları bulunur. Pencere bulunamadığında tarama/Windows yardımcısı durumu arayüzde açıkça gösterilir. Yenile / Yeniden dene ile tekrar taranır. PNG'ler varsayılan olarak `%USERPROFILE%\Pictures\SekoBot` klasörüne kaydedilir. Godot arayüzü yardımcı sürecin oluşturduğu PNG karelerini okur.

**İlk sürüm otomatik farm yapmaz**; oyuna klavye/fare komutu göndermez. Yalnızca oyun ekranını yakalar.

Yakalama penceredeki görünür masaüstü piksellerini kullanır: Metin2 **pencere modunda, ekranda ve önü açık** olmalıdır. Simge durumunda görüntü alınamaz; pencerenin üstü örtülürse yanlış pikseller yakalanabilir. Bazı DirectX istemcileri siyah görüntü üretebilir. PNG üretimi CPU ve disk kullanır.

PowerShell'in `-ExecutionPolicy Bypass` seçeneği yalnızca yerel yardımcı sürecin başlatılması için kullanılır; Windows'un kalıcı yürütme politikasını değiştirmez. Görüntüler internete gönderilmez.

## Sorun giderme

- **WindowCaptureBridge.cs yüklenemedi:** Eski proje klasörü açıktır. Yeni sahne `WindowCaptureBridge.gd` dosyasını kullanır. Godot'u kapatıp güncel klasörden yeniden aç.
- **WINDOWS YARDIMCISI HATASI:** Sistem günlüğünü kontrol et. PowerShell'in çalıştırılmasına sistem politikası veya antivirüs engel oluyor olabilir.
- **Pencere bulunamadı:** Metin2 pencere modunda olmalı; 2–5 saniye sonra **Pencereleri yenile** seç.
- **Siyah görüntü:** Pencere moduna geç, üstündeki diğer pencereleri kaldır ve tekrar dene.

**Test:** GitHub dosyaları ve bağımlılıkları kontrol edildi. Gerçek Windows + Godot + Metin2 uçtan uca testi henüz yapılmadı.

## Kod kontrolü — EXE ve Releases yok

GitHub Actions, Godot sahnesinin açılmasını ve yerel Windows PowerShell/Win32 yardımcısının derlenmesini kontrol eder. EXE veya Release üretmez. Bu testler gerçek oyun istemcisindeki görüntü yakalamanın yerine geçmez.
