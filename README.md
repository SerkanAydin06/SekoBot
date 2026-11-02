# SekoBot

Windows için Metin2 oyun penceresi izleme ve ileride geliştirilecek farm otomasyonu altyapısı.

**v0.1:** Modern PySide6 arayüzü, açık pencereleri listeleme, tek Metin2 penceresine otomatik bağlanma, HWND/PID takibi, 5/10/15 FPS canlı önizleme ve yerel PNG ekran görüntüsü kaydı.

## Python kurmadan kullanma (önerilen)

SekoBot, GitHub Actions üzerinde Windows için **tek dosyalı taşınabilir SekoBot.exe** olarak derlenir. Kendi bilgisayarında Python kurulu olması gerekmez.

1. [SekoBot Releases](https://github.com/SerkanAydin06/SekoBot/releases/tag/preview) sayfasına git.
2. **Assets → SekoBot.exe** bağlantısına tıkla. ZIP olmadan doğrudan `.exe` indirilir.
3. İndirilen dosyayı çift tıkla. Metin2 pencerede açık olduğunda program onu otomatik algılamayı dener.

Başarılı bir derlemeden sonra doğrudan EXE adresi: [SekoBot.exe](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe).

**Önemli:** Release ancak GitHub Actions derlemesi **başarıyla tamamlanırsa** yayınlanır. Önizleme bağlantısı henüz açılmıyorsa [Windows EXE Build](https://github.com/SerkanAydin06/SekoBot/actions/workflows/build-windows.yml) sayfasından iş akışını kontrol et. `Code → Download ZIP` yalnızca kaynak kodunu içerir. `Actions → Artifacts` yedeği ZIP olarak iner, `Releases` ise doğrudan EXE sunar.

Her `main` güncellemesinde yeni sürüm mevcut `preview` sürümündeki EXE dosyasının yerini alır. Paketlenmiş EXE'nin ilk açılışı birkaç saniye sürebilir. Uygulama henüz dijital olarak imzalanmadığından Windows SmartScreen uyarısı gösterebilir. Güvenmediğin EXE dosyalarını çalıştırma.

## Geliştiriciler için Python ile çalıştırma (isteğe bağlı)

Windows 10/11 ve Python 3.11+:

1. Depoyu indir veya klonla.
2. `start.bat` dosyasını çalıştır: sanal ortam ve bağımlılıklar kurulur.
3. Ya da PowerShell'de:

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m sekobot
```

Test etmek için: `python -m pip install -e ".[dev]"` ve `python -m pytest -q`.

## Kullanım

- Metin2'yi pencere modunda aç. Tek oyun penceresi varsa SekoBot otomatik bağlantıyı dener.
- Birden çok Metin2 açıksa listeden pencere seçip **Pencereye bağlan**'a bas.
- Canlı oyun görüntüsünü kontrol panelinde gör; **Ekran görüntüsü kaydet** ile PNG oluştur.
- Görüntüler kullanıcı profilinin `Pictures\SekoBot` klasörüne kaydedilir.

## Mimari

- `windowing.py`: Win32 pencere seçimi, fiziksel ekran koordinatları ve HWND/PID doğrulaması.
- `capture.py`: Test edilebilir pencere görüntü yakalama motoru.
- `ui/worker.py`: Arka plan QThread yakalama döngüsü.
- `ui/main_window.py`: Masaüstü kontrol paneli.
- `ui/theme.py`: Tutarlı koyu arayüz teması.
- `.github/workflows/build-windows.yml`: Windows'ta testler ve tek dosyalı EXE derlemesi.

## Sınırlar

İlk sürüm yalnızca **görüntü yakalar**. Farm/oyun kontrolü, bellek okuma veya tuş/fare gönderme içermez. MSS ekranda görünen pikselleri yakaladığından pencerenin üzeri örtülürse üstteki pencere görülebilir; simge durumunda görüntü alınamaz. Özel DirectX tam ekran modunda siyah görüntü oluşabilir. Pencere modu önerilir. İleride Windows Graphics Capture gibi ikinci bir backend eklenebilir.

Görüntüler yalnızca yerel diske kaydedilir. Herhangi bir sunucuya yükleme yapılmaz.
