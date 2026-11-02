# SekoBot

Metin2 için tek dosyalı Windows farm botu. Kurulum gerekmez, açılışta kendini otomatik günceller.

**[SekoBot.exe — indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)** · Windows 10 (2004+) / 11, x64

> Eski bir SekoBot.exe kullanıyorsan bu sürümü **bir kez** elle indir. Bundan sonra her açılışta yeni sürüm sessizce indirilir, SHA-256 ile doğrulanır ve uygulama kendini yeniden başlatır. Bot çalışırken güncelleme yapılmaz.

## Kullanım

1. Metin2'yi pencere modunda aç, SekoBot.exe'yi çalıştır. Tek Metin2 penceresi varsa otomatik bağlanır.
2. **HP ve MP barları otomatik bulunur.** Önizlemede yeşil (HP) ve mavi (MP) çerçeve görünür, günlükte "HP barı otomatik bulundu ✓" yazar. Bir moba tıkladığında üstteki hedef barı da kendiliğinden bulunur. HP o an dolu değilse sorun değil; HP doldukça bot ölçüyü kendisi büyütür.
   - Bulamazsa 10 sn sonra `hp-bulunamadi.png` kaydedilir (yolu günlükte yazar). Bu resmi geliştiriciye gönder. Beklerken barı önizlemede fareyle seçip **HP barı** / **MP barı** düğmesine basarak elle işaretleyebilirsin.
   - Yanlış yer bulunduysa **Sıfırla / yeniden bul**.
3. **Kuru çalışma** açıkken **Başlat (F10)**: bot ne yapacağını yalnızca günlüğe yazar. Doğruysa kuru çalışmayı kapat.
4. **F12** her zaman acil durdurur. HP belirlenen eşiğin altına düşerse bot kendini durdurur.

**Yürüme modu (varsayılan):** Bot başlarken **G**'ye basılı tutup kamerayı tepeden bakışa alır, **F** ile uzaklaştırır. Sonra ekrandaki **kırmızı mob isimlerinden** karaktere en yakın olanı seçer, **W/A/S/D** ile ona doğru yürür (çapraz için iki tuş), mob yanına gelince **Space**'e basılı tutar, saldırırken de mobu ortada tutmak için **W/A/S/D** ile yönlendirmeye devam eder **Z**'ye her zaman (yürürken de) 1,5 sn'de bir basarak eşya toplar. Kamera kaymasın diye 30 sn'de bir **G** ve **F**'ye tekrar basar; bunu yürümeyi ya da saldırıyı durdurmadan, aynı anda yapar. Yakında mob kalmayınca eşyayı toplayıp en yakın kırmızı isme gider; ekranda hiç isim yoksa biraz yürüyüp kamerayı çevirerek bakar. 8 sn'de ulaşamadığı mobu bırakır. Ayarlar `profile.json` içinde: `CameraSetupHoldMs`, `CameraRefreshMs` (30000), `AttackRadius` (ekran yüksekliğinin oranı, 0.05; arayüzde "Saldırı mesafesi (%)"), `LootEveryMs`, `ExploreWalkMs`, `MoveStuckMs`; tuşlar `MoveLeft`/`MoveBack`/`MoveRight`/`CameraTop`/`ZoomOut`. Eski tıklama modu için `"NavigateMode": false`.

Tuşlar (Space saldırı, Z toplama, 1/2 iksir, E kamera, W yürüme) ve süreler `%LOCALAPPDATA%\SekoBot\profile.json` içinde; arayüzden **Klasör** ile açılır.

## Ölünce

Karakter ölünce ekrana gelen pencereyi bot tanır: tuşları bırakır, **"Burada yeniden başla"** düğmesinin yazısı griden beyaza dönüp tıklanabilir olunca ona tıklar. Dirilince HP %90'a gelene kadar (en fazla 30 sn) 1 tuşuyla iksir içer, kamerayı yeniden ayarlar ve avlanmaya devam eder. Bot düşük HP'de hiç durmaz (durmak, saldıran mobların karakteri öldürmesine yol açardı); iksir içmeye devam eder. Ayarlar: `ReviveHealTo` (0.90), `ReviveHealMaxMs` (30000).

## Açılır pencereler

Level atlayınca çıkan reklam penceresi ve yanlışlıkla açılan Esc menüsü gibi oyunun üstüne açılan pencereleri bot görür (ekranın ortasındaki uzun, düz başlık çubuğundan ya da menünün arkasında bulanıklaşan oyundan), yürümeyi ve saldırıyı bırakır ve **bir kez Esc**'ye basar. Pencere 3 sn sonra hâlâ açıksa tekrar basar; böylece boşuna Esc basıp oyun menüsünü açmaz. Kapatmak için `profile.json` içinde `"ClosePopups": false`. Not: bot çalışırken envanter gibi bir pencereyi kendin açarsan onu da kapatır.

## Buff becerileri (F1–F5)

Bot kartında F1–F5 için "kaç saniyede bir" yazılır (0 = kapalı; varsayılan F1 ve F2 her 60 sn). Bot bunlara döngü halinde, aralarında en az 1,5 sn olacak şekilde basar; basarken W/A/S/D ve Space'i bırakıp 1 sn durur ki beceri kesilmesin, sonra devam eder.

## Arka planda çalışma

Tuşlar ve tıklamalar her zaman doğrudan oyun penceresine gönderilir; oyun başka pencerelerin arkasında kalabilir (simge durumuna küçültülmemeli, yoksa görüntü alınamaz).

## Kayıt ve tekrar oynatma

- **⏺ Kaydet:** Bot her turda okuduğu HP/MP/hedef değerlerini, aşamayı ve gönderdiği tuşları `%LOCALAPPDATA%\SekoBot\recordings\<tarih>\` klasörüne yazar. Saniyede bir kare PNG olarak saklanır; iksir ve durdurma anının karesi her zaman saklanır. Bot kapalıyken de kaydeder (örnek görüntü toplamak için). Kare aralığı ve boyut sınırı `profile.json` içinde: `RecordFrameIntervalMs` (varsayılan 1000), `RecordMaxMb` (varsayılan 1500).
- **▶ Kaydı oynat:** Bir kayıt klasörü seç. Kayıttaki kareler **şimdiki** kalibrasyon ve ayarlarla yeniden okunur ve bot mantığından geçirilir; oyuna hiçbir tuş gönderilmez. Klasöre `replay-report.txt` yazılır: kayıtta ne yapıldı → şimdiki kod ne yapardı, durma anı, kalibrasyonu tutmayan kareler.

Böylece yeni bir ayarı ya da kalibrasyonu oyunda denemeden önce eski bir oturumun üzerinde test edebilirsin.

## Nasıl çalışır

- **Yakalama:** Windows Graphics Capture (GPU); oyunun üstü örtülse de çalışır. Desteklenmezse GDI'ya düşer. Küçültülmüş pencere Windows tarafından çizilmez, görüntü alınamaz.
- **Algılama:** HP (kırmızı) ve MP (mavi) barları ekranın altındaki ince yatay renk şeritleri olarak, hedef barı üst ortadaki kırmızı şerit olarak otomatik bulunur; sonra bulunan renge göre okunur (kare başına <1 ms).
- **Karar:** Saldır → topla → dön döngüsü, iksir bekleme süreleri, kritik HP'de durma.
- **Girdi:** Tarama kodlu `SendInput`; oyun odakta değilse hiçbir şey gönderilmez ve basılı tuşlar bırakılır.

Uygulama oyun belleğini okumaz, koda enjekte etmez, görüntü yüklemez. Güncelleme yalnızca bu deponun Releases sayfasından yapılır.

> ⚠️ Bot kullanımı çoğu Metin2 sunucusunun kurallarına aykırıdır ve hesap kapatılmasına yol açabilir. Sorumluluk kullanıcıdadır.

## Geliştirme

```
src/SekoBot/            uygulama (Capture, Vision, Bot, Input, Config, Recording, Update, UI)
tests/SekoBot.Tests/    farm döngüsü, bar okuma, kayıt/oynatma testleri
```

.NET 10 SDK ile `dotnet test` ve `dotnet run --project src/SekoBot`. Her push GitHub Actions'ta test edilip derlenir; `main` dalı `preview` sürümünü günceller.
