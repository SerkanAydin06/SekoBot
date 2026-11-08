# SekoBot

Metin2 için tek dosyalı Windows farm botu. Kurulum gerekmez, açılışta kendini otomatik günceller.

**[SekoBot.exe — indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)** · Windows 10 (2004+) / 11, x64

> Eski bir SekoBot.exe kullanıyorsan bu sürümü **bir kez** elle indir. Bundan sonra her açılışta yeni sürüm sessizce indirilir, SHA-256 ile doğrulanır ve uygulama kendini yeniden başlatır. Bot çalışırken güncelleme yapılmaz.

## Kullanım

1. Oyunu (Mobile2 Global) pencere modunda aç, SekoBot.exe'yi **yönetici olarak** çalıştır (oyun yönetici olarak çalıştığı için belleğini okumak buna bağlı). Oyun açılınca SekoBot kendiliğinden bağlanır; sağ üstte **● Mobile2 Global'e bağlı** yazar. Birden fazla oyun penceresi varsa **Oyunu seç ▾** ile birini seç.
2. **Her şey oyunun belleğinden okunur:** HP/MP, karakterin konumu, kamera, etraftaki moblar (isim, seviye, can), seçili hedef ve canı, öldün mü. Kalibrasyon, bar işaretleme ya da "Koordinat bul" gerekmez; oyunu kapatıp açınca da hiçbir şey yeniden bulunmaz.
3. **▶ BAŞLAT (F10)** ile başlat, **■ DURDUR (F11)** her zaman acil durdurur. (F12'yi Windows ayırdığı için kısayol F11'dir.) Üstteki satır botun ne yaptığını gösterir (ör. "Saldırıyor · Alanın içinde").

**Arayüz:** Ortada canlı görüntü (sarı halka = bulunan mob), sağda **OTURUM**: süre, öldürülen mob, saatte mob, kazanılan EXP, saatte EXP, seviye (yüzde ve seviye atlamaya kalan tahmini süre), kazanılan yang, iksir (1 ve 2 tuşundaki iksirden envanterde kalan adet, içilen sayısı), ölüm. Altta ayar sekmeleri, her değişiklik hemen kaydedilir:
- **Saldırı:** saldırı mesafesi, eşya toplama sıklığı, Kalabalığa git, kalabalık için en az mob.
- **Hedef:** en yüksek mob seviyesi, sadece şu moblar / şu mobları atla, metin taşları (atla / normal / önce onlar), önce bana saldıranı kes, yerdeki eşyaları topla.
- **İksir & Buff:** HP/MP iksiri eşiği, ölünce yeniden başla, F1–F5 buff süreleri.
- **Alan:** alan sınırı (metre), GM / envanter dolu / güvenli bölgede dur, bellekten okunan konum.
- **Tuşlar:** tüm tuşlar; tuşa tıkla, yeni tuşa bas.
- **Gelişmiş:** kuru çalışma (tuş göndermeden dener), kayıt / kaydı oynat, ayar klasörü, **Mobları göster**.

**Yürüme modu (varsayılan):** Bot başlarken **G**'ye basılı tutup kamerayı tepeden bakışa alır, **F** ile uzaklaştırır. Sonra bellekteki moblardan karaktere en yakın olanı seçer (kamerayla ekrandaki yerini hesaplar), **W/A/S/D** ile ona doğru yürür (çapraz için iki tuş), mob yanına gelince **Space**'e basılı tutar, saldırırken de mobu ortada tutmak için **W/A/S/D** ile yönlendirmeye devam eder **Z**'ye her zaman (yürürken de) 1,5 sn'de bir basarak eşya toplar. Kamera kaymasın diye 30 sn'de bir **G** ve **F**'ye tekrar basar; bunu yürümeyi ya da saldırıyı durdurmadan, aynı anda yapar. Yakında mob kalmayınca eşyayı toplayıp en yakın moba gider; ekranda hiç mob yoksa biraz yürüyüp kamerayı çevirerek bakar. 8 sn'de ulaşamadığı mobu bırakır. Ayarlar `profile.json` içinde: `CameraSetupHoldMs`, `CameraRefreshMs` (30000), `AttackRadius` (ekran yüksekliğinin oranı, 0.10; arayüzde "Saldırı mesafesi (%)"), `LootEveryMs`, `ExploreWalkMs`, `MoveStuckMs`; tuşlar `MoveLeft`/`MoveBack`/`MoveRight`/`CameraTop`/`ZoomOut`. Eski tıklama modu için `"NavigateMode": false`.

Tuşlar arayüzdeki **Tuşlar** sekmesinden değişir; diğer ince ayarlar `%LOCALAPPDATA%\SekoBot\profile.json` içinde (**Gelişmiş → Ayar klasörü**).

## Ölünce

Karakterin öldüğünü bot bellekten görür: tuşları bırakır, ölüm penceresinin 12 sn'lik geri sayımını da bellekten izler ve **"Burada yeniden başla"** tıklanabilir olunca ona tıklar. Dirilince HP %90'a gelene kadar (en fazla 30 sn) 1 tuşuyla iksir içer, kamerayı yeniden ayarlar ve avlanmaya devam eder. Bot düşük HP'de hiç durmaz (durmak, saldıran mobların karakteri öldürmesine yol açardı); iksir içmeye devam eder. Ayarlar: `ReviveHealTo` (0.90), `ReviveHealMaxMs` (30000).

## Açılır pencereler

Bot oyunun kendi pencerelerinin (Esc menüsü, envanter, karakter, beceriler, ayarlar, etkinlik, arkadaşlık istekleri, …) açık olup olmadığını bellekten okur. Bot başladığında kapalı olan bir oyun penceresi açılırsa (ör. level atlayınca çıkan reklam) yürümeyi ve saldırıyı bırakır ve **bir kez Esc**'ye basar; pencere 3 sn sonra hâlâ açıksa tekrar basar. Hangi pencereyi gördüğünü günlüğe adıyla yazar ("Açık pencere (bellekten): EscMenu"). Kapatmak için `profile.json` içinde `"ClosePopups": false`. Not: bot çalışırken envanter gibi bir pencereyi kendin açarsan onu da kapatır.

## Alan sınırı ve takılma

**Alan sınırı (metre)** (varsayılan 40, 0 = kapalı): Bot başladığı yeri bellekten okuyup kaydeder. Başlangıçtan bu mesafeden fazla uzaklaşınca geri yürür (yanındaki mobla yine savaşır), yarı mesafeye gelince avlanmaya devam eder. Hangi tuşun geri götürdüğünü kameranın baktığı yönden (yine bellekten) hesaplar; harita açmaz.

**Bellekten okuma:** Oyun Unreal Engine 4.27 ile yapılmış. Bot oyunun ana nesnesine exe içindeki tek bir sabit noktadan ulaşır, gerisini (HP, ölü mü, moblar, seçili hedef…) oyunun kendi alan isimleriyle bulur. Oyun güncellenip sabit noktalar kayarsa bot onları kendisi arar ve bulur (bir kez, birkaç saniye sürer; günlüğe yazar). Bellek yalnızca okunur, oyuna hiçbir şey yazılmaz. Bellek uzun süre (15 sn) okunamazsa bot durur; yükleme ekranı gibi kısa kesintilerde bekler.

**Mobları göster (Gelişmiş sekmesi):** Botun bellekte gördüğü mobları (isim, seviye, HP, uzaklık), seçili hedefi, karakterin HP/MP'sini ve etraftaki oyuncu sayısını günlüğe yazar. Bir şey ters giderse bu listeyi geliştiriciye gönder.

**Takılma:** W/A/S/D basılıyken karakterin konumu 0,7 sn değişmiyorsa karakter bir ağaca/kayaya takılmıştır (yürümeye başladıktan sonraki ilk 1 sn — vurma hareketinin bitmesi — ve mobun dibindeyken sayılmaz); bot yana kayarak dolanır (her seferinde diğer yana), sonra yoluna devam eder.

## Oturum istatistikleri

- **Öldürülen mob:** Karakterin 8 m yakınında canlı görülen bir mob ölür (ya da dünyadan kalkar) **ve** birkaç saniye içinde EXP'in artarsa sayılır. Başkasının kestiği mob (EXP gelmez) ve uzaktaki moblar sayılmaz.
- **EXP:** Oturumda kazanılan EXP (seviye atlayınca eski seviyenin kalanı + yeni seviyedeki EXP), saatte EXP, seviye yüzdesi ve bu hızla seviye atlamaya kalan süre.
- **İksir:** HP/MP iksir tuşunun (1, 2) hızlı slotundaki eşyanın envanterdeki toplam adedi; azaldıkça "içildi" sayılır. İksir bitince günlüğe uyarı yazılır, kutu 20'nin (MP 10'un) altında kırmızı olur.
- **Yang:** Oturumda kazanılan yang.

## Hedef seçimi ve eşya toplama

- **Önce bana saldıran mobu kes** (varsayılan açık): Bir mobun sana saldırdığını bot bellekten görür ve daha yakında başka mob olsa da önce onu keser.
- **En yüksek mob seviyesi** (0 = sınırsız): Üstündeki mobları atlar. **Sadece bu moblar / Bu mobları atla:** mob adlarını virgülle yaz (ör. "Kurt, Domuz"), adın bir kısmı yeter. Sana saldıran mob bu ayarlardan bağımsız her zaman kesilir.
- **Metin taşları:** *Atla* (hiç gitme), *Normal mob gibi* (varsayılan), *Önce onlara git* (taş her şeyden önce gelir, sana saldıran moblardan da; seviye ve isim filtreleri taşı atlamaz).
- **Yerdeki eşyalara yürüyüp topla** (varsayılan açık): Yanında mob yokken 6 m içindeki eşyaya (yerdeki eşyalar bellekte görünür, alınınca kaybolur) yürür ve toplama tuşuna basar. 5 sn'de alınamayan eşyayı (başkasının olabilir) bir dakika boyunca bırakır.

## Güvenlik: GM ve envanter

- **GM görünce dur** (Alan sekmesi, varsayılan açık): Yakında oyunun GM olarak işaretlediği (ya da adında [GM]/[GA]/[SA] olan) bir oyuncu görünürse bot uyarı sesi çıkarır, günlüğe adını ve uzaklığını yazar ve durur. Kapalıyken yalnızca uyarır.
- **Güvenli bölgede dur** (Alan sekmesi, varsayılan açık): Karakter şehre/güvenli bölgeye düşerse (ör. ölüp şehirde dirilirse) bot durur; güvenli bölgedeyken başlamaz.
- **Envanter dolunca dur** (Alan sekmesi, varsayılan açık): Envanterde boş yer kalmayınca bot uyarı sesiyle durur, toplanan eşyalar kaybolmaz. 5 boş yer kalınca günlüğe uyarı yazar. Boş yer Alan sekmesinde de görünür. Yalnızca açık (paralı olmayan) sayfalar sayılır; kılıç, yay, çift el silah ve zırh alt alta 2 yuva kaplar, emin olunmayan eşyalar 1 yuva sayılır (böylece bot boşuna durmaz).

## Kalabalığa git

**Kalabalığa git** işaretliyken (varsayılan) bot, yanında mob yoksa ekrandaki en iyi mob grubuna yürür (moblar bellekten): birbirine yakın en az 3 mob (`CrowdMinMobs`) bir grup sayılır; yakın ve kalabalık grup kazanır, uzaktaki grubun çok daha büyük olması gerekir. Grubun ortasına varınca Space ile saldırır. Yanındaki mobu bitirmeden gruba koşmaz, neredeyse ulaştığı tek mobu önce alır ve seçtiği gruptan kolay kolay vazgeçmez (zikzak yapmaz). Kapalıyken her zaman en yakın moba gider.

## Beceriler (otomatik)

**Becerileri otomatik kullan** (İksir & Beceri sekmesi, varsayılan açık): Bot hızlı slotlardaki (1–5 ve F1–F5) becerilerini ve oyunun onlar hakkında söylediklerini bellekten okur: ad, tür, seviye, mana, bekleme süresi, etki süresi. Sekmede hangi tuşta hangi becerinin olduğu ve ne işe yaradığı yazar.
- **Güçlendirmeler** (ör. Güçlü beden): süresi bitmeden ~5 sn önce yenilenir.
- **Alan saldırıları** (ör. Güçlü vuruş, Kılıç darbesi): etrafta en az 3 mob varken (`AreaSkillMinMobs`).
- **Tek hedef saldırıları** (ör. Şiddetli vuruş): yanında mob varken.
- Hepsi bekleme süresi dolunca ve MP yetiyorsa basılır; basarken bot 1 sn durur ki beceri kesilmesin, iki beceri arasında en az 1,5 sn olur.

Kapalıyken eski usul F1–F5 "kaç saniyede bir" ayarları (`SkillEverySec`) kullanılır.

## İksir azalınca kaç

**Kaç HP iksiri kalınca kaç** (varsayılan 5, 0 = kapalı): HP iksiri bu sayıya düşünce bot saldırmayı bırakır ve moblardan uzaklaşır (yakındakilerden ters yöne, sana saldıranlar iki kat sayılır). Yakında mob kalmayıp 3 sn boyunca kimse vurmayınca orada durur. Bot başladığında iksir zaten bu kadar azsa yalnızca uyarır.

## Arka planda çalışma

Tuşlar ve tıklamalar her zaman doğrudan oyun penceresine gönderilir; oyun başka pencerelerin arkasında kalabilir (simge durumuna küçültülmemeli, yoksa görüntü alınamaz).

## Kayıt ve tekrar oynatma

- **⏺ Kaydet:** Bot her turda okuduğu HP/MP/hedef değerlerini, aşamayı ve gönderdiği tuşları `%LOCALAPPDATA%\SekoBot\recordings\<tarih>\` klasörüne yazar. Saniyede bir kare PNG olarak saklanır; iksir ve durdurma anının karesi her zaman saklanır. Bot kapalıyken de kaydeder (örnek görüntü toplamak için). Kare aralığı ve boyut sınırı `profile.json` içinde: `RecordFrameIntervalMs` (varsayılan 1000), `RecordMaxMb` (varsayılan 1500).
- **▶ Kaydı oynat:** Bir kayıt klasörü seç. Kayıttaki değerler **şimdiki** ayarlarla bot mantığından yeniden geçirilir; oyuna hiçbir tuş gönderilmez. Klasöre `replay-report.txt` yazılır: kayıtta ne yapıldı → şimdiki kod ne yapardı, durma anı.

Böylece yeni bir ayarı oyunda denemeden önce eski bir oturumun üzerinde test edebilirsin.

## Nasıl çalışır

- **Yakalama:** Windows Graphics Capture (GPU); oyunun üstü örtülse de çalışır. Desteklenmezse GDI'ya düşer. Küçültülmüş pencere Windows tarafından çizilmez, görüntü alınamaz.
- **Algılama:** Oyunla ilgili her şey bellekten salt okunur; ekran görüntüsü yalnızca canlı önizleme ve kayıt için alınır, karar vermek için kullanılmaz.
- **Karar:** Saldır → topla → dön döngüsü, iksir bekleme süreleri, kritik HP'de durma.
- **Girdi:** Tarama kodlu `SendInput`; oyun odakta değilse hiçbir şey gönderilmez ve basılı tuşlar bırakılır.

Uygulama oyun belleğini yalnızca okur; belleğe yazmaz, koda enjekte etmez, görüntü yüklemez. Güncelleme yalnızca bu deponun Releases sayfasından yapılır.

> ⚠️ Bot kullanımı çoğu Metin2 sunucusunun kurallarına aykırıdır ve hesap kapatılmasına yol açabilir. Sorumluluk kullanıcıdadır.

## Geliştirme

```
src/SekoBot/            uygulama (Capture, Vision, Bot, Input, Config, Recording, Update, UI)
tests/SekoBot.Tests/    farm döngüsü, bellek okuma (sahte oyun belleğiyle), alan sınırı, kayıt/oynatma testleri
```

.NET 10 SDK ile `dotnet test` ve `dotnet run --project src/SekoBot`. Her push GitHub Actions'ta test edilip derlenir; `main` dalı `preview` sürümünü günceller.


### İksir

HP/MP bellekten okunur. Okunamazsa HP/MP bilinmiyor kabul edilir ve otomatik pot gönderilmez; uydurma değer kullanılmaz. Okuma 2 sn'den uzun süre bozulursa günlüğe uyarı düşer, okuma geri gelince bot kendiliğinden devam eder.

**İksir & Buff** sekmesindeki HP/MP eşikleri, **Tuşlar** sekmesindeki pot tuşları, bekleme ve ölçüm onay süreleri korunur. Arka plan modu etkinse aynı oyun penceresine arka planda tuş gönderilir; normal modda oyun ön planda olmalıdır. Kuru çalışma modunda tuş gönderilmez.
