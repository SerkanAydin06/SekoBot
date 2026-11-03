# SekoBot

Metin2 için tek dosyalı Windows farm botu. Kurulum gerekmez, açılışta kendini otomatik günceller.

**[SekoBot.exe — indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)** · Windows 10 (2004+) / 11, x64

> Eski bir SekoBot.exe kullanıyorsan bu sürümü **bir kez** elle indir. Bundan sonra her açılışta yeni sürüm sessizce indirilir, SHA-256 ile doğrulanır ve uygulama kendini yeniden başlatır. Bot çalışırken güncelleme yapılmaz.

## Kullanım

1. Oyunu (Mobile2 Global) pencere modunda aç, SekoBot.exe'yi çalıştır. Oyun açılınca SekoBot kendiliğinden bağlanır; sağ üstte **● Mobile2 Global'e bağlı** yazar. Birden fazla oyun penceresi varsa **Oyunu seç ▾** ile birini seç.
2. **HP ve MP doğrudan oyun belleğinden okunur.** Sağdaki KARAKTER alanındaki doluluk çubukları bu verilere göre gösterilir. Hedef yaratığın HP barı görüntüyle tespit edilir; gerekirse Gelişmiş sekmesinden yalnızca hedef HP barını kalibre edebilirsin.
3. **▶ BAŞLAT (F10)** ile başlat, **■ DURDUR (F11)** her zaman acil durdurur. (F12'yi Windows ayırdığı için kısayol F11'dir.) Üstteki satır botun ne yaptığını gösterir (ör. "Saldırıyor · Alanın içinde").

**Arayüz:** Ortada canlı görüntü (sarı halka = bulunan mob), sağda **OTURUM** (süre, öldürülen mob, saatte mob, ölüm, iksir, alan durumu). Altta ayar sekmeleri, her değişiklik hemen kaydedilir:
- **Saldırı:** saldırı mesafesi, eşya toplama sıklığı, Kalabalığa git, kalabalık için en az mob.
- **İksir & Buff:** HP/MP iksiri eşiği, ölünce yeniden başla, F1–F5 buff süreleri.
- **Alan:** alan sınırı ve haritaya bakma sıklığı.
- **Tuşlar:** tüm tuşlar; tuşa tıkla, yeni tuşa bas.
- **Gelişmiş:** kuru çalışma (tuş göndermeden dener), bar işaretleme, kayıt / kaydı oynat, ayar klasörü.

**Yürüme modu (varsayılan):** Bot başlarken **G**'ye basılı tutup kamerayı tepeden bakışa alır, **F** ile uzaklaştırır. Sonra ekrandaki **kırmızı mob isimlerinden** karaktere en yakın olanı seçer, **W/A/S/D** ile ona doğru yürür (çapraz için iki tuş), mob yanına gelince **Space**'e basılı tutar, saldırırken de mobu ortada tutmak için **W/A/S/D** ile yönlendirmeye devam eder **Z**'ye her zaman (yürürken de) 1,5 sn'de bir basarak eşya toplar. Kamera kaymasın diye 30 sn'de bir **G** ve **F**'ye tekrar basar; bunu yürümeyi ya da saldırıyı durdurmadan, aynı anda yapar. Yakında mob kalmayınca eşyayı toplayıp en yakın kırmızı isme gider; ekranda hiç isim yoksa biraz yürüyüp kamerayı çevirerek bakar. 8 sn'de ulaşamadığı mobu bırakır. Ayarlar `profile.json` içinde: `CameraSetupHoldMs`, `CameraRefreshMs` (30000), `AttackRadius` (ekran yüksekliğinin oranı, 0.05; arayüzde "Saldırı mesafesi (%)"), `LootEveryMs`, `ExploreWalkMs`, `MoveStuckMs`; tuşlar `MoveLeft`/`MoveBack`/`MoveRight`/`CameraTop`/`ZoomOut`. Eski tıklama modu için `"NavigateMode": false`.

Tuşlar arayüzdeki **Tuşlar** sekmesinden değişir; diğer ince ayarlar `%LOCALAPPDATA%\SekoBot\profile.json` içinde (**Gelişmiş → Ayar klasörü**).

## Ölünce

Karakter ölünce ekrana gelen pencereyi bot tanır: tuşları bırakır, **"Burada yeniden başla"** düğmesinin yazısı griden beyaza dönüp tıklanabilir olunca ona tıklar. Dirilince HP %90'a gelene kadar (en fazla 30 sn) 1 tuşuyla iksir içer, kamerayı yeniden ayarlar ve avlanmaya devam eder. Bot düşük HP'de hiç durmaz (durmak, saldıran mobların karakteri öldürmesine yol açardı); iksir içmeye devam eder. Ayarlar: `ReviveHealTo` (0.90), `ReviveHealMaxMs` (30000).

## Açılır pencereler

Level atlayınca çıkan reklam penceresi ve yanlışlıkla açılan Esc menüsü gibi oyunun üstüne açılan pencereleri bot görür (ekranın ortasındaki uzun, düz başlık çubuğundan ya da menünün arkasında bulanıklaşan oyundan), yürümeyi ve saldırıyı bırakır ve **bir kez Esc**'ye basar. Pencere 3 sn sonra hâlâ açıksa tekrar basar; böylece boşuna Esc basıp oyun menüsünü açmaz. Kapatmak için `profile.json` içinde `"ClosePopups": false`. Not: bot çalışırken envanter gibi bir pencereyi kendin açarsan onu da kapatır.

## Alan sınırı ve takılma

**Alan sınırı (harita %)** (varsayılan 6, 0 = kapalı): Bot başlarken **M** ile büyük haritayı açar, sarı oktan başlangıç yerini kaydeder ve kapatır; hemen ardından kısa bir yürüyüşle haritanın hangi yönünün ekranın üstü olduğunu ölçer. Sonra 20 sn'de bir (hiç saldırı ortasında değil) haritaya bakar; başlangıçtan haritanın genişliğinin %6'sından fazla uzaklaştıysa başlangıca geri yürür (yanındaki mobla yine savaşır). Bu yüzden bot kamerayı artık E ile çevirmez; mob ararken zikzak yürür.

**Takılma:** W/A/S/D basılıyken ekran 0,7 sn kımıldamıyorsa karakter bir ağaca/kayaya takılmıştır; bot yana kayarak dolanır (her seferinde diğer yana), sonra yoluna devam eder.

## Kalabalığa git

**Kalabalığa git** işaretliyken (varsayılan) bot, yanında mob yoksa ekrandaki en iyi mob grubuna yürür: birbirine yakın en az 3 mob (`CrowdMinMobs`) bir grup sayılır; yakın ve kalabalık grup kazanır, uzaktaki grubun çok daha büyük olması gerekir. Grubun ortasına varınca Space ile saldırır. Yanındaki mobu bitirmeden gruba koşmaz, neredeyse ulaştığı tek mobu önce alır ve seçtiği gruptan kolay kolay vazgeçmez (zikzak yapmaz). Kapalıyken her zaman en yakın moba gider.

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
- **Algılama:** Karakterin HP/MP'si yalnızca bellekten okunur, ekrandan hiç okunmaz. Ekranda yalnızca hedef barı (üst ortadaki kırmızı şerit) otomatik bulunur ve rengine göre okunur (kare başına <1 ms).
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


### Bellek tabanlı HP/MP ve arka planda otomatik iksir

Karakter HP, MP, maksimum HP ve maksimum MP verileri yalnızca `mobilemmo-Win64-Shipping.exe` işleminden salt okunur bellek erişimiyle alınır. Eski görüntü tabanlı HP/MP okuma ve kalibrasyon devre dışı bırakılmıştır; görüntü analizi yalnızca hedef yaratığın HP barı gibi diğer işlevlerde kalır. Bellek okunamazsa karakter HP/MP yüzdeleri bilinmiyor kabul edilir ve otomatik pot gönderilmez; uydurma değer veya görsel bar yedeği kullanılmaz. Okuma 2 sn'den uzun süre bozulursa günlüğe uyarı düşer ve üstteki durum satırı kırmızı **⚠ HP/MP okunamıyor, iksir içilmiyor** yazar; okuma geri gelince bot kendiliğinden devam eder.

**İksir & Buff** sekmesindeki HP/MP eşikleri, **Tuşlar** sekmesindeki pot tuşları, bekleme ve ölçüm onay süreleri korunur. Bot **BAŞLAT** durumunda iksirleri mevcut InputExecutor üzerinden gönderir: arka plan modu etkinse aynı oyun penceresine arka planda tuş gönderimi kullanılır; normal modda oyun ön planda olmalıdır. Arka planda girdi gönderimi oyunun ve Windows sürümünün davranışına bağlı olabilir. Kuru çalışma modunda tuş gönderilmez.

Bellek pointer'ları `Hp_Mp.CT` tablosuna dayanır. İstemci güncellemesi veya değişen karakter veri düzeni pointer'ları bozabilir. Maksimum HP/MP alanlarının farklı maksimum değerlerde doğrulanması önerilir. Gerekli işlem erişimi olmayan sistemlerde yönetici yetkisi gerekebilir; güvenlik mekanizmalarını aşma işlevi yoktur.
