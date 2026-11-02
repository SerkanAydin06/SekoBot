# SekoBot

Windows için Metin2 oyun penceresi izleme ve ileride geliştirilecek farm otomasyonu altyapısı.

**v0.1:** Modern PySide6 arayüzü, açık pencereleri listeleme, tek Metin2 penceresine otomatik bağlanma, HWND/PID takibi, 5/10/15 FPS canlı önizleme ve yerel PNG ekran görüntüsü kaydı.

## Python kurmadan kullanma (önerilen)

SekoBot, GitHub Actions üzerinde Windows için **tek dosyalı taşınabilir SekoBot.exe** olarak derlenir. Kendi bilgisayarında Python kurulu olması gerekmez.

1. [Windows EXE Build](https://github.com/SerkanAydin06/SekoBot/actions/workflows/build-windows.yml) sayfasına git.
2. En son **başarıyla tamamlanan** çalıştırmayı aç (yeşil onay işaretli).
3. Sayfanın altındaki **Artifacts** bölümünden `SekoBot-Windows-x64` dosyasını indir. GitHub oturum açmanı isteyebilir.
4. İndirilen ZIP arşivini aç, içindeki `SekoBot.exe` dosyasını klasöre çıkar.
5. `SekoBot.exe` dosyasını çift tıklayarak çalıştır. Metin2 pencerede açık olduğunda program penceresini algılar.

**Önemli:** Bir iş akışı henüz tamamlanmamış veya başarısızsa kullanılabilir EXE dosyası oluşmuş değildir. `Code → Download ZIP` ile indirilen kaynak kodu doğrudan çalıştırılabilir program değildir. GitHub Actions dosyaları sınırlı süre saklanır; bu iş akışında süre 30 gündür.

Paketlenmiş EXE'nin ilk açılışı birkaç saniye sürebilir. Uygulama henüz dijital olarak imzalanmadığından Windows SmartScreen uyarısı gösterebilir. Güvenmediğin EXE dosyalarını çalıştırma.

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
