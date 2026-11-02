# SekoBot

Windows için Metin2 oyun penceresi izleme ve ileride geliştirilecek farm otomasyonu altyapısı.

**v0.1:** Modern PySide6 arayüzü, açık pencereleri listeleme, tek Metin2 penceresine otomatik bağlanma, HWND/PID takibi, 5/10/15 FPS canlı önizleme ve yerel PNG ekran görüntüsü kaydı.

## Çalıştırma

Windows 10/11 ve Python 3.11+ gereklidir.

1. Bu depoyu indir veya klonla.
2. `start.bat` dosyasını çift tıkla. Sanal ortam ve paketler ilk açılışta kurulur.
3. Metin2'yi pencere modunda aç; program otomatik bulur. Birden fazla pencere varsa listeden seç.
4. `Pencereye bağlan`, `Bağlantıyı durdur` ve `Ekran görüntüsü kaydet` kontrollerini kullan.

Alternatif PowerShell kurulum:

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m sekobot
```

PNG görüntüler `%USERPROFILE%\Pictures\SekoBot` klasörüne kaydedilir.

Test: `python -m pip install -e ".[dev]"`, ardından `python -m pytest -q`.

## Mimari
- `windowing.py`: Win32 pencere seçimi, fiziksel ekran koordinatları, HWND/PID doğrulaması.
- `capture.py`: Test edilebilir pencere görüntü yakalama motoru.
- `ui/worker.py`: Arka plan QThread yakalama döngüsü.
- `ui/main_window.py`: Masaüstü kontrol paneli.
- `ui/theme.py`: Tutarlı koyu arayüz teması.

## Sınırlar

İlk sürüm yalnızca **görüntü yakalar**. Farm/oyun kontrolü, bellek okuma veya tuş/fare gönderme içermez. MSS ekranda görünen pikselleri yakaladığından pencerenin üzeri örtülürse üstteki pencere görülebilir; simge durumunda görüntü alınamaz. Özel DirectX tam ekran modunda siyah görüntü oluşabilir. Pencere modu önerilir. İleride Windows Graphics Capture gibi ikinci bir backend eklenebilir.

Görüntüler yalnızca yerel diske kaydedilir. Herhangi bir sunucuya yükleme yapılmaz.