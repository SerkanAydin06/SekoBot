# SekoBot

Metin2 için tek dosyalı Windows farm botu. Kurulum gerekmez, açılışta kendini otomatik günceller.

**[SekoBot.exe — indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)** · Windows 10 (2004+) / 11, x64

> Eski bir SekoBot.exe kullanıyorsan bu sürümü **bir kez** elle indir. Bundan sonra her açılışta yeni sürüm sessizce indirilir, SHA-256 ile doğrulanır ve uygulama kendini yeniden başlatır. Bot çalışırken güncelleme yapılmaz.

## Kullanım

1. Oyunu (Mobile2 Global) pencere modunda aç, SekoBot.exe'yi **yönetici olarak** çalıştır (oyun yönetici olarak çalıştığı için belleğini okumak buna bağlı). Oyun açılınca SekoBot kendiliğinden bağlanır; sağ üstte **● Mobile2 Global'e bağlı** yazar. Birden fazla oyun penceresi varsa **Oyunu seç ▾** ile birini seç.
2. **Her şey oyunun belleğinden okunur:** HP/MP, karakterin konumu, kamera, etraftaki moblar (isim, seviye, can), seçili hedef ve canı, öldün mü. Kalibrasyon, bar işaretleme ya da "Koordinat bul" gerekmez; oyunu kapatıp açınca da hiçbir şey yeniden bulunmaz.
3. **▶ BAŞLAT (F10)** ile başlat, **■ DURDUR (F11)** her zaman acil durdurur. (F12'yi Windows ayırdığı için kısayol F11'dir.) Üstteki satır botun ne yaptığını gösterir (ör. "Saldırıyor · Alanın içinde").

**Arayüz:** Solda menü: **Panel** ve ayar sayfaları (Saldırı, Hedef, Eşya / Envanter, İksir / Beceri, Alan, Bildirim, Tuşlar, Gelişmiş); her sayfanın başlığı ve kısa açıklaması var, altta her zaman **Günlük**. Panelde canlı görüntü (sarı halka = bulunan mob), sağda **OTURUM**: süre, öldürülen mob, saatte mob, kazanılan EXP, saatte EXP, seviye (yüzde ve seviye atlamaya kalan tahmini süre), kazanılan yang, iksir (HP ve MP ayrı ayrı: envanterde kalan · içilen), ölüm. Ayarlarda her değişiklik hemen kaydedilir:
- **Saldırı:** saldırı mesafesi, Z ile toplama sıklığı (yalnız yürüyüp toplama kapalıyken), Kalabalığa git, kalabalık için en az mob.
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

Bot oyunun kendi pencerelerinin (Esc menüsü, envanter, karakter, beceriler, ayarlar, etkinlik, arkadaşlık istekleri, …) açık olup olmadığını bellekten okur. Bot başladığında kapalı olan bir oyun penceresi açılırsa (ör. level atlayınca çıkan reklam) yürümeyi ve saldırıyı bırakır ve **bir kez Esc**'ye basar; pencere 3 sn sonra hâlâ açıksa tekrar basar. Seviye atlayınca çıkan reklam da (oyunda `popup_C`, ekrana sonradan eklenen pencereler listesinde) tanınır. Hangi pencereyi gördüğünü günlüğe adıyla yazar ("Açık pencere (bellekten): popup_C"). Kapatmak için `profile.json` içinde `"ClosePopups": false`. Not: bot çalışırken envanter gibi bir pencereyi kendin açarsan onu da kapatır.

## Alan sınırı ve takılma

**Alan sınırı (metre)** (varsayılan 40, 0 = kapalı): Bot başladığı yeri bellekten okuyup kaydeder. Başlangıçtan bu mesafeden fazla uzaklaşınca geri yürür (yanındaki mobla ve 5 m içinde ona vuran moblarla önce savaşır — onlar zaten peşinden gelip yolda vururdu). Alanın dışında duran moblar hedef olarak **hiç seçilmez** (yürüyüp yarı yoldan dönmesin); bize saldıranlar yine kesilir, yarı mesafeye gelince avlanmaya devam eder. Hangi tuşun geri götürdüğünü kameranın baktığı yönden (yine bellekten) hesaplar; harita açmaz.

**Bellekten okuma:** Oyun Unreal Engine 4.27 ile yapılmış. Bot oyunun ana nesnesine exe içindeki tek bir sabit noktadan ulaşır, gerisini (HP, ölü mü, moblar, seçili hedef…) oyunun kendi alan isimleriyle bulur. Oyun güncellenip sabit noktalar kayarsa bot onları kendisi arar ve bulur (bir kez, birkaç saniye sürer; günlüğe yazar). Bellek yalnızca okunur, oyuna hiçbir şey yazılmaz. Bellek uzun süre (15 sn) okunamazsa bot durur; yükleme ekranı gibi kısa kesintilerde bekler.

**Mobları göster (Gelişmiş sekmesi):** Botun bellekte gördüğü mobları (isim, seviye, HP, uzaklık), seçili hedefi, karakterin HP/MP'sini ve etraftaki oyuncu sayısını günlüğe yazar. Bir şey ters giderse bu listeyi geliştiriciye gönder.

**Takılma:** W/A/S/D basılıyken karakterin konumu 0,7 sn değişmiyorsa karakter bir ağaca/kayaya takılmıştır (yürümeye başladıktan sonraki ilk 1 sn — vurma hareketinin bitmesi — ve mobun dibindeyken sayılmaz); bot yana kayarak dolanır (her seferinde diğer yana), sonra yoluna devam eder.


**Engelleri öğrenme ve yükseklik:** ağaç, taş, duvar gibi engellerin şekli bellekten okunamıyor; bot onları öğrenir. Bir moba yürürken takıldığı (yandan dolandığı, moba ya da gruba ulaşamadığı) yeri ve gitmeye çalıştığı yönü 5 dakika hatırlar; yolu böyle bir yerden geçen mobları seçmez. Yükseklik bellekten okunur: düz zeminde mob karakterden ~60 birim aşağıda görünür; bundan çok yukarıda ya da aşağıda olan mob (yamaç, uçurum; uzaklığının yarısından ve 2,5 m'den fazla fark) seçilmez. Sana saldıran moblar her zaman kesilir.
## Oturum istatistikleri

- **Öldürülen mob:** Karakterin 8 m yakınında canlı görülen bir mob ölür (ya da dünyadan kalkar) **ve** birkaç saniye içinde EXP'in artarsa sayılır. Başkasının kestiği mob (EXP gelmez) ve uzaktaki moblar sayılmaz.
- **EXP:** Oturumda kazanılan EXP (seviye atlayınca eski seviyenin kalanı + yeni seviyedeki EXP), saatte EXP, seviye yüzdesi ve bu hızla seviye atlamaya kalan süre.
- **İksir:** HP/MP iksir tuşunun (1, 2) hızlı slotundaki eşyanın envanterdeki toplam adedi; azaldıkça "içildi" sayılır. Adetler oyunun **envanter penceresinden** okunur (karakter verisindeki liste, iksir içildikçe ya da eşya üst üste eklendikçe güncellenmiyor ve bazı eşyaları hiç göstermiyor). Aynı iksirin bütün yığınları sayılır (ör. 200 + 93 + 200 = 493); başka boyuttaki iksirler (orta, büyük) sayılmaz. Tuştaki yığın bitince oyun tuşu kendiliğinden aynı iksirin bir sonraki yığınına geçirir (görüldü: 42. slottan 43.'ye), tekrar koymak gerekmez. Tuş 30 sn boş kalırsa (çantada iksir varken) günlüğe + Telegram'a uyarı yazılır. İksir bitince günlüğe uyarı yazılır, kutu 20'nin (MP 10'un) altında kırmızı olur.
- **Yang:** Oturumda kazanılan yang.

## Hedef seçimi ve eşya toplama

- **Önce bana saldıran mobu kes** (varsayılan açık): Bir mobun sana saldırdığını bot bellekten görür ve daha yakında başka mob olsa da önce onu keser.
- **En yüksek mob seviyesi** (0 = sınırsız): Üstündeki mobları atlar. **Sadece bu moblar / Bu mobları atla:** mob adlarını virgülle yaz (ör. "Kurt, Domuz"), adın bir kısmı yeter. Sana saldıran mob bu ayarlardan bağımsız her zaman kesilir.
- **Metin taşları:** *Atla* (hiç gitme), *Normal mob gibi* (varsayılan), *Önce onlara git* (taş her şeyden önce gelir, sana saldıran moblardan da; seviye ve isim filtreleri taşı atlamaz).
- **Yürüyüp topla: Yang / İksir / Silah-zırh / Diğer** (Eşya / Envanter sayfası): yerdeki eşyanın ne olduğu bellekten okunur (eşya numarası ve adedi; yang = 1); bot yalnızca işaretli türlere yürür. Varsayılan: yang, iksir, diğer (malzeme, kitap…) açık; silah/zırh kapalı (bu seviyede çoğu işe yaramaz ve envanteri doldurur). Bot Z'ye yalnızca seçtiği eşyanın üstündeyken basar; birkaç saniyede bir Z'ye basma (yakındaki her şeyi, istenmeyenleri de toplar) yalnızca "Yerdeki eşyalara yürüyüp topla" kapalıyken çalışır.

**Eşya bilgisi:** oyun bütün eşyaların listesini bellekte tutar (m2data +0x500, eşya numarası → ad, tür, alt tür, satış fiyatı; canlı: 10 386 eşya). Bot bağlanınca bu listeyi bir kez okur; böylece yere düşen her eşya **almadan** tanınır (ör. 39006 "Cesaret Pelerini", 30038 "Kaplan Postu", 50007 "Gümüş Define Sandığı"), türü (iksir / silah-zırh / diğer) oyunun dediğine göre ayrılır ve Alan sekmesindeki konum satırında 10 m içinde yerde ne olduğu yazar ("Yerde: 107 yang, Kaplan Postu"). Liste bulunamazsa (oyun güncellemesi) eşyalar yine çantaya girdikçe öğrenilir (`%LOCALAPPDATA%\SekoBot\esya-bilgisi.json`).

**📊 Moblar** (Hedef sekmesi): bulunulan haritadaki mob türleri, oyunun bellekteki mob listesinden (loader.tempData1; c2'de 77 tür): seviye, EXP, yang aralığı, oyunun o mob için tuttuğu eşya (çoğu malzeme: Kaplan → Kaplan Postu, Siyah Ayı → Ayı Ayak Derisi) ve haritada kaç tane olduğu. Yanında **görülen düşenler**: oyunun tam düşme listeleri sunucuda olduğu için bot bunu izleyerek öğrenir — yakında ölen mobun yerine 4 sn içinde düşen eşyalar o moba yazılır (harita ve mob başına öldürme sayısı, yang, eşyalar; 20 öldürmeden sonra yüzde de). Kayıt saklanır (`%LOCALAPPDATA%\SekoBot\dusme-kaydi.json`), bot çalıştıkça dolar. Arama mob adında ve eşya adlarında yapılır ("Kaplan Postu" yazınca hangi mobdan düştüğü görünür). Etkinlik eşyaları (ör. Cesaret Pelerini) da görülünce buraya yazılır.

**📋 Eşya listesi** (Eşya / Envanter sayfası): oyunun bütün eşyaları (ad, tür, dükkân fiyatı) bir pencerede listelenir; adla ya da numarayla aranır, türe göre süzülür (Silah, Zırh, Takı, İksir, Malzeme, Sandık / hediye, Özel…). Her eşyanın yanında iki kutu: **Topla** = her zaman alınır (diğer toplama seçenekleri kapalı olsa da; 15 m'ye kadar, ekran dışında olsa bile ve mob saldırırken de saldırı bırakılıp hemen alınır, 20 sn denenir; düşünce günlüğe ve Telegram'a "🎁 Yere düştü: …" yazılır), **Alma** = hiç yürünmez (ör. çantayı dolduran ucuz malzeme), **At** = alınmaz ve envanter dolunca çantadan atılır. Bir eşyayı işaretleyince aynı adı taşıyanlar da işaretlenir (ör. 5 farklı Cesaret Pelerini). "Sadece işaretliler" kendi havuzunu gösterir. Seçimler profilde saklanır.

**Silah / zırh en az (yang)** (Eşya / Envanter sayfası, 0 = kapalı): silah/zırh toplama kapalıyken bile, dükkânın en az bu kadar yang verdiği silah ve zırhlara yürünür (fiyatı bilinenler; çöp ekipman yine alınmaz).

**Yerdekini önce topla:** izlemede botun dibindeki yang (1–4 m) bazen hiç alınmıyordu: savaşırken Z işe yaramıyor, savaş bitince de bot hemen bir sonraki grubu toplamaya gidiyordu. Artık kimse saldırmıyorken ve toplama sürmüyorken önce yakındaki istenen eşyalar alınır, sonra yeni gruba gidilir. Etrafta kesilecek mob yokken eşyaya giderken saldırı tuşu bırakılır (boşa vurmaz). Mob keserken dibinde istenen eşya varsa saldırı sürer ve arada Z'ye basılır (savaşı bırakmadan alır).
- **Yerdeki eşyalara yürüyüp topla** (varsayılan açık): Yanında mob yokken 6 m içindeki eşyaya (yerdeki eşyalar bellekte görünür, alınınca kaybolur) yürür ve toplama tuşuna basar. 5 sn'de alınamayan eşyayı (başkasının olabilir) bir dakika boyunca bırakır. Toplama tuşu oyunda en yakındaki her şeyi aldığı için dövüş bitince tuşa yalnızca yakında istenen bir eşya varken basılır (istenmeyen ucuz zırh böylece alınmaz).

## Güvenlik: GM ve envanter

- **GM görünce dur** (Alan sekmesi, varsayılan açık): Yakında oyunun GM olarak işaretlediği (ya da adında [GM]/[GA]/[SA] olan) bir oyuncu görünürse bot uyarı sesi çıkarır, günlüğe adını ve uzaklığını yazar ve durur. Kapalıyken yalnızca uyarır.
- **Güvenli bölgede dur** (Alan sekmesi, varsayılan açık): Karakter şehre/güvenli bölgeye düşerse (ör. ölüp şehirde dirilirse) bot durur; güvenli bölgedeyken başlamaz.
- **Yer azalınca 'At' işaretlileri … at** (Eşya / Envanter sayfası, varsayılan açık): Envanter dolunca — ya da 2 boş yer kalınca, ya da zırh / uzun silah için alt alta 2 boş yer kalmayınca — bot durmak yerine önce saldıranları keser, yeni grup toplamaz, moblardan uzak sakin bir yere çekilir (3 sn kimse yaklaşmayınca). Sonra oyunu öne getirip envanteri açar (**Envanter** tuşu, varsayılan **I**), eşya listesinde **At** işaretli eşyaları (ve "Silah / zırh" kapalı, **Silah/zırh ≥** fiyatı girilmişken botun çalışırken kendi aldığı daha ucuz silah, zırh ve takıları, ayrıca toplama tuşu yakındaki her şeyi aldığı için yanlışlıkla gelen ve toplama seçeneklerinin istemediği iksir / malzeme gibi eşyaları; senin eşyaların, "Topla" işaretliler ve giyilenler hariç — botun ne aldığı karakter başına `bot-aldiklari-<ad>.json` dosyasında saklanır, bot yeniden başlasa ya da güncellense de unutulmaz; çantada yer değiştirdiğin eşya sahibini korur) sayfa sayfa fareyle pencerenin dışına, yere sürükler ve "Evet" der; eşyanın gerçekten gittiğini bellekten denetler, envanteri kapatır ve kayıtlı alana dönüp devam eder. Bu sırada saldırı gelirse envanteri kapatıp savaşır, sonra yeniden dener. Atılamayan eşya iki denemeden sonra bırakılır; hiçbir şey atılamazsa bot eskisi gibi durur. Pencerenin, sayfa düğmelerinin, slotların ve "Evet" düğmesinin yeri ekrandan değil bellekten okunur. **🗑 Şimdi at** düğmesi (Eşya / Envanter sayfası; yanında çantanın canlı durumu: boş yer, zırh sığar mı, kaç eşya atılacak) envanterin dolmasını beklemeden aynı işi hemen yapar (denemek için).
- **Atılacak bir şey yoksa çanta dolunca dur** (Eşya / Envanter sayfası, varsayılan açık): Envanterde boş yer kalmayınca bot uyarı sesiyle durur, toplanan eşyalar kaybolmaz. 5 boş yer kalınca günlüğe uyarı yazar. Boş yer Alan sekmesinde de görünür. Yalnızca açık (paralı olmayan) sayfalar sayılır; kılıç, yay, çift el silah ve zırh alt alta 2 yuva kaplar, emin olunmayan eşyalar 1 yuva sayılır (böylece bot boşuna durmaz).

## Akıllı hedef seçimi

Bot her mobun oyundaki bilgisini bellekten okur: verdiği EXP, canı, en sert vuruşu, gövde büyüklüğü; karakterin saldırı menzili (130) ve saldırı gücü.
- **Değerli mob önce:** Her moba puan verir: EXP ÷ (yürüme süresi + öldürme süresi + 1 sn). Örneğin Siyah Ayı (624 can, 384 EXP), Aç Ayı'dan (305 can, 103 EXP) biraz uzakta olsa da önce seçilir. Canı azalmış mob daha çabuk öleceği için öne geçer. Canın %40'ın altındaysa sert vuran moblar daha az puan alır.
- **Yarım mobu bitir:** Dövüştüğü mob yaralı ve yakındaysa, daha değerli bir mob görünse de önce onu bitirir; başka moba geçmek için onun açıkça (1,3 kat) daha iyi olması gerekir (zikzak yapmaz).
- **Gerçek mesafe:** Saldırıya başlama mesafesi ekran yüzdesiyle değil, oyunun menziliyle (merkezden merkeze 120 birime kadar yaklaşır, dövüşürken 180'e kadar vurmaya devam eder) hesaplanır; büyük gövdeli moblarda (metin taşı) bu mesafe gövdeye göre büyür. Oyunda ölçüldü: vuruşlar 70–145 birimden geliyor. Kamera yakınlaşsa/uzaklaşsa da kaymaz.
- Sana saldıran moblar ve "önce" seçili metin taşları yine öncelikli; puan bunların kendi aralarında kullanılır.
- **Ekran dışındaki moblar da:** kamera yalnızca ~10 m gösterir; bot bellekte gördüğü 25 m içindeki bütün moblar arasından seçer ve ekran dışındakine yönünü kameradan hesaplayıp yürür. 8 m içinde vurulacak mob varken uzaktakilere gitmez.

## Yoğun bölge hafızası

Bot haritayı 10 m'lik karelere bölüp her karede ne kadar mob gördüğünü hatırlar (etrafını gördükçe tazeler; boşalan kare düşer, görülmeyen kareler yavaşça unutulur). Metin taşı olan kareler 5 mob sayılır. Ekranda mob kalmayınca rastgele dolaşmak yerine en çok mobun olduğu yakın kareye yürür ("Etrafta mob yok; mobların çok olduğu yere gidiliyor"). Alan sınırı açıksa sınırın dışındaki kareler seçilmez. Başka haritaya geçince hafıza sıfırlanır.

## Molalar ve oturum raporu

- **Ara sıra kısa mola ver** (Alan sekmesi, varsayılan açık): yaklaşık 30 dakikada bir (±%20), dövüş yokken ve kimse saldırmıyorken bot ~40 sn (±%25) durur; bir mob saldırırsa mola hemen biter. Toplama tuşu aralıkları da her seferinde biraz değişir. Ayarlar: `BreakEveryMin`, `BreakSeconds`.
- **Oturum raporu:** bot 1 dakikadan uzun çalışıp durduğunda özet günlüğe yazılır ve `%LOCALAPPDATA%\SekoBot\oturumlar.csv` dosyasına bir satır eklenir (başlangıç, bitiş, süre, harita, öldürülen, saatte mob, EXP, saatte EXP, seviye, yang, iksir, ölüm, durma sebebi). Excel'de açılır; **Gelişmiş → 📊 Oturum raporları**. Bot kendiliğinden durduysa özet Telegram mesajına da eklenir.

## Telegram bildirimi

**Bildirim** sekmesi: kendi Telegram botuna mesaj gönderir — GM görüldü, karakter öldü, seviye atladın, iksir bitti, bot durdu (sebebiyle; sen durdurursan mesaj gelmez). Aynı mesaj dakikada en fazla bir kez gelir.

**Fısıltı, istekler, sohbet** (Bildirim sayfası → "Neler bildirilsin", hepsi varsayılan açık): bot çalışırken bellekten okunur, günlüğe ve Telegram'a yazılır; acil olanlar uyarı sesiyle de:
- **Fısıltı (özel mesaj):** yeni gelen her mesaj, yazanın adı ve seviyesiyle ("💬 Fısıltı — Ali (Sv 40): bot musun?"). Bot başlamadan önceki mesajlar söylenmez.
- **Takas / grup / arkadaş isteği** ve oyunun Evet/Hayır sorduğu pencereler.
- **Sohbette adın geçince** (rakamsız hali de sayılır: "KAiZER06" → "kaizer") ya da **adı [GM]/[GA]/[SA] olan biri yazınca**.
- **Dağıtılmamış statü / beceri puanı** arttığında.
- **Etkinlik:** oyunun saatlik düşme etkinlikleri (ör. 22:00–23:00 Nadir Ekipman Sandığı) sol menünün altında görünür; seviyen yeten bir etkinlik başlayınca bildirilir. Listede görünen **bütün etkinliklerin ödülleri** (şu anki, sıradakiler ve yeni bitenler; seviyeye bakılmadan) düşünce **"Topla" gibi her zaman alınır**: 15 m'ye kadar, saldırı bırakılıp hemen ("Tüm etkinlik ödüllerini her zaman topla").

**Tehlikede kaç** (İksir / Beceri sayfası, varsayılan %30, 0 = kapalı): can bu yüzdenin altına düşer ve en az 3 mob saldırırsa bot vurmayı bırakıp moblardan uzaklaşır, iksir içmeye devam eder; can %60'ı geçince, 2 sn kimse saldırmayınca ya da en çok 20 sn sonra dövüşe döner (görülen: 9 aç mob canı 10 sn'de 1071'den 0'a indirdi).

**Uzaktaki "saldırgan":** oyun bazen 20 m ötede duran, hiç yaklaşmayan bir moba da "bana saldırıyor" der; 12 m'den uzaktaki böyle bir mob saldırgan sayılmaz (bot onun için alanından çıkıp yürümez).

**Ulaşılamayan hedef:** bir moba ya da Metin taşına 8 sn'de 1 m bile yaklaşılamazsa (kaya, duvar, yükseklik) 2 dakika bırakılır (görülen: Metin taşına giderken 70 sn bir kayanın çevresinde dönüldü).

**Mola** yakında (10 m) 3 ya da daha çok mob varken verilmez.

**Günlükte tekrar yok:** botun sürekli yaptığı işler ("Gruplar toplanıyor", "toplanan 3 mob kesiliyor"…) bir kez yazılır; aynı mesaj (sayıları farklı olsa da) 5 dakika içinde tekrar gelirse yazılmaz. Tehlike, ölüm ve uyarılar her zaman yazılır.

**Günlük dosyası:** günlük `%LOCALAPPDATA%\SekoBot\gunluk.txt` dosyasına da yazılır (2 MB'ı geçince `gunluk-eski.txt` olur).

**Dolu çantada boşuna yürümez:** çantaya girmeyecek eşyaya gidilmez. Çanta doluyken yalnızca yang ve çantadaki dolmamış bir yığına eklenecek eşyalar (ör. aynı iksir) toplanır; zırh / uzun silah için alt alta 2 boş yer yoksa onlara da gidilmez.

**Düşük seviye Metin taşları atlanır:** seviyesi karakterden 5'ten fazla düşük Metin taşına gidilmez (Sv 20'de 5 Savaş Metini'nden (Sv 15) hiçbir şey düşmedi; bir taş 1 dakikadan uzun sürüp 2000 EXP veriyor). Karakter seviye atladıkça sınır kendiliğinden yükselir.

**Kapanmayan pencere:** bot başladıktan sonra açılan bilinmeyen bir pencere Esc ile 7 saniyede kapanmazsa yok sayılır ve bot devam eder (sohbet geçmişi penceresi açık kalınca bot her Esc'de oyun menüsünü açıp kapatıyor ve duruyordu).

**Telegram'dan fısıltıya cevap:** Telegram'daki "💬 Fısıltı — İsim …" bildirimine **Yanıtla** ile yazdığın metni bot oyunda o kişiye gönderir; ya da botuna `yaz İsim mesaj` yaz. Bot dövüş bitince (en geç 30 sn) tutulan tuşları bırakır, soldaki **Mesaj** simgesiyle mesaj penceresini açar, kişinin konuşmasını seçer, yazı kutusuna yazıp **Gönder**'e basar, mesajın gittiğini denetler ve pencereyi kapatır; sonucu Telegram'a yazar. Yalnızca sana daha önce yazmış (konuşması pencerede olan) birine yazılabilir.

Fısıltı bildirimi artık mesaj penceresinin konuşma listesinden de okunur; mesaj metni okunamazsa "yeni mesaj yazdı" diye bildirilir.

**İki oyunda aynı anda bot:** iki oyun açıkken SekoBot'u iki kez aç. Her SekoBot başka bir oyuna kendiliğinden bağlanır (önce açılan oyun 1. SekoBot'a). 2. SekoBot kendi ayarlarını (`profile-2.json`, ilk açılışta 1.'nin ayarlarından kopyalanır) ve günlüğünü (`gunluk-2.txt`) kullanır; kısayolları **Ctrl+F10 / Ctrl+F11**. Telegram komutları ve otomatik güncelleme yalnızca 1. SekoBot'ta; bildirimler ikisinden de gelir ve başlarında karakter adı yazar. Pencere başlığında ve üst çubukta kaçıncı SekoBot olduğu ve karakter adı görünür.

**Sersemleme:** karakter sersemleyince (bellekte "stuned") bot bekler; o anı takılma ya da ulaşılamayan grup saymaz.

**Komutlar** (Bildirim sekmesinde **Komutlar (durum/dur/başla)**, varsayılan açık): kendi botuna Telegram'dan yaz, SekoBot cevap verir:
- **durum** — bot çalışıyor mu, seviye ve yüzdesi (seviyeye kalan süre), can/MP, oturum: süre, öldürülen mob, EXP ve saatte EXP, yang; kalan iksir; harita ve yakındaki oyuncu sayısı.
- **dur** — botu durdurur (cevapta son durum).
- **başla** — botu başlatır (BAŞLAT düğmesi gibi; olmazsa nedenini yazar).

Yalnızca kayıtlı sohbetten (Sohbet no) gelen mesajlara cevap verilir; SekoBot kapalıyken yazılanlar sonradan uygulanmaz. Büyük/küçük harf, başta "/" ve Türkçe harfler fark etmez (durum, /Durum, BAŞLA, basla).
Kurulum: Telegram'da @BotFather'a `/newbot` yaz, verdiği anahtarı **Bot anahtarı**na yapıştır; kendi botuna "merhaba" yaz ve **Sohbeti bul**'a bas; **Deneme mesajı** ile dene; kutucuğu işaretle. Anahtar yalnızca bu bilgisayarda (`profile.json`) saklanır, yalnızca api.telegram.org'a bağlanılır.

## En iyi farm alanı

**📍 En iyi alanı bul / 🔄 Alanı güncelle** (Alan sekmesi): bellek çevredeki mobları ~40 m'ye kadar gösterir. Bot önce durduğu yerden bakar, sonra dört yöne ~30'ar m kısa birer yürüyüş yapar (yaklaşık 1 dk; yakındaki moblar saldırabilir), gördüğü bütün mobları (Hedef sekmesindeki filtrelere uyanları) toplar. 5 m'lik ızgaradaki her nokta, 15 m çevresindeki moblarla **saatte kazanılacak EXP**'ye göre puanlanır: toplam EXP ÷ hepsini kesme süresi (her mob için öldürme süresi + 1 sn + bir sonrakine yürüme; mob azsa aralar uzun), en az mobların geri gelme süresi (bot ölçtükçe o seviyedeki mobların ölçülen süresi, ölçülmediyse 60 sn). Böylece 15 domuz (61 EXP) 4 Siyah Ayı'yı (384 EXP) geçemez; kalabalık yine artıdır çünkü daha az yürünür. Öncelikli metin taşları 3 kat sayılır. En iyi nokta kaydedilir. Sekmede kayıtlı alanın haritası, zamanı, mob sayısı ve en çok bulunan moblar yazar.
**Kayıtlı alanda başla** işaretliyse bot başlayınca önce o noktaya yürür (yolda can yarının altına düşerse ya da 2 dakikada varamazsa olduğu yerde başlar), alan sınırı ve yoğun bölge hafızası o noktaya göre işler. Kayıtlı alan başka haritadaysa ya da 300 m'den uzaksa kullanılmaz (günlüğe yazılır). İşaretli değilse bot durduğun yerde başlar.

**Mobların geri gelme süresi:** bot farm yaparken bellekten ölçer: yakında bir mob ölünce yeri hatırlanır, aynı yerin 12 m yakınında yeni bir mob (daha önce görülmemiş numara) çıkınca aradaki süre o mobun **seviyesine** yazılır. Karakter arada o yerden 32 m'den fazla uzaklaştıysa sayılmaz (ne zaman geldiği bilinmez); başlangıçta görülen ve uzaktan görüş alanına giren moblar da sayılmaz. Her seviye için son 30 ölçüm saklanır (`%LOCALAPPDATA%\SekoBot\dogma-sureleri.json`), en iyi alan hesabında ortanca değer kullanılır (ölçülmeyen seviyeye en yakın ölçülmüş seviye, 3 seviyeye kadar). Alan sekmesinde ölçülenler yazar (ör. "Sv 19: 48 sn (6)").

**Oyuncu kalınca yeni alan (dk)** (Alan sekmesi, varsayılan 3, 0 = kapalı): başka bir oyuncu (GM değil) bu kadar dakika 30 m içinde kalırsa ve o geldiğinden beri dakikadaki EXP, gelmeden önceki en az 2 dakikaya göre %40'tan fazla düştüyse (`AvoidPlayerExpDrop`), bot son 3 dakikada gördüğü mobların içinden en iyi alanı yeniden seçer — oyuncuların 25 m yakınında olmayan ve eski alandan en az 15 m ötede — ve oraya yürür (günlük + Telegram: kim, ne kadar düştü, yeni alan). Oyuncu kısa süre görünüp gittiyse ya da EXP düşmediyse bir şey yapmaz; yer değiştirdikten sonra 5 dk bekler.

## Grupları topla

**Grupları topla** (Saldırı sekmesi, varsayılan açık): oyun mobları üçlü gruplar halinde çıkarır ve birine vurunca grubun hepsi gelir. Grubu bellekten bulur: oyun mob numaralarını üçer üçer ve hep aynı hizadan verir (1877–79, 1880–82, 1883–85…); aynı üçlüde olup 10 m içinde duran moblar bir gruptur. Yan yana duran iki üçlü (ör. 1886–88 ve 1889–91) iki ayrı grup sayılır; hizayı bot moblardan kendisi çıkarır. Bot 15 m içindeki uyanmamış gruplardan **en değerlisiyle** başlar (EXP ÷ öldürme süresi, uzaklık az bir eksi), bir moba vurur; grup peşine takılınca yalnızca **ilk grubun çevresindeki** gruplara geçer (**Toplama alanı**, varsayılan 8 m) — böylece bir bölgede toplar, uzaklara sürüklenmez. Peşinde mob varken (ör. bir mob kendiliğinden gelip vurdu) yalnızca **çok yakındaki** gruplar toplanır (Toplama alanı içinde; o mob zaten peşinden gelir), uzaktaki gruplara gidilmez; peşindeki mob sayısı **Toplanacak en fazla mob**'a (varsayılan 9) ulaşınca, yakında başka grup kalmayınca ya da can **Toplarken can en az %**'nin (varsayılan 70) altına düşünce durup hepsini birlikte keser (alan becerileri hepsine vurur). 3 sn vurulup uyanmayan grup bir dakika bırakılır. 6 sn yürüyüp ulaşılamayan grup da (duvar, yamaç arkası) bir dakika bırakılır. Öncelikli metin taşı varken toplama yapılmaz. **Kaç mob toplanacağı kendini ayarlar:** birkaç mobla yapılan bir dövüşte can %40'ın altına düşerse sonraki toplamada 2 mob eksik toplanır (en az 3); can %70'in üstünde kalırsa 1 fazla (en çok ayardaki sayı). Değişiklik günlüğe yazılır.

Eski **Kalabalığa git** seçeneği bunun yerine kapatıldı (`PreferCrowds`, varsayılan kapalı).

## Beceriler (otomatik)

**Becerileri otomatik kullan** (İksir / Beceri sekmesi, varsayılan açık): Bot hızlı slotlardaki (1–5 ve F1–F5) becerilerini ve oyunun onlar hakkında söylediklerini bellekten okur: ad, tür, seviye, mana, bekleme süresi, etki süresi. Sekmede hangi tuşta hangi becerinin olduğu ve ne işe yaradığı yazar.
- **Güçlendirmeler** (ör. Güçlü beden): süresi bitmeden ~5 sn önce yenilenir.
- **Alan saldırıları** (ör. Güçlü vuruş, Kılıç darbesi): yalnızca bot **vururken** ve **3 m içinde en az 5 mob** varken (**Alan becerisi: en az mob**, varsayılan 5). Peşindeki moblar henüz gelmemişken, moba yürürken ya da grup toplarken kullanılmaz — boşa gitmesin.
- **Tek hedef saldırıları** (ör. Şiddetli vuruş): yalnızca bot yanındaki moba vururken.
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
