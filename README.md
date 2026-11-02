# SekoBot

Metin2 için tek dosyalı Windows farm botu. Kurulum gerekmez, açılışta kendini otomatik günceller.

**[SekoBot.exe — indir](https://github.com/SerkanAydin06/SekoBot/releases/download/preview/SekoBot.exe)** · Windows 10 (2004+) / 11, x64

> Eski bir SekoBot.exe kullanıyorsan bu sürümü **bir kez** elle indir. Bundan sonra her açılışta yeni sürüm sessizce indirilir, SHA-256 ile doğrulanır ve uygulama kendini yeniden başlatır. Bot çalışırken güncelleme yapılmaz.

## Kullanım

1. Metin2'yi pencere modunda aç, SekoBot.exe'yi çalıştır. Tek Metin2 penceresi varsa otomatik bağlanır.
2. **Kalibrasyon** (bir kez): önizlemede fareyle alan seç ve işaretle
   - **HP barı** / **MP barı** — barlar doluyken seç
   - **Hedef HP barı** — oyunda bir Metin taşına tıkla, ekranın üstündeki dolu hedef barını seç
   - **+ Metin şablonu** — bir Metin taşının etrafını seç (farklı taşlar/açılar için birkaç şablon ekle)
   - **Arama alanı** — isteğe bağlı; taşların aranacağı bölge (arayüzü hariç tut)
3. **Kuru çalışma** açıkken **Başlat (F10)**: bot ne yapacağını yalnızca günlüğe yazar. Önizlemedeki sarı kutular ve durum doğruysa kuru çalışmayı kapatıp gerçek moda geç.
4. **F12** her zaman acil durdurur. HP belirlenen eşiğin altına düşerse bot kendini durdurur.

Tuşlar (saldırı, toplama, iksir, kamera) ve süreler `%LOCALAPPDATA%\SekoBot\profile.json` içinde; arayüzden **Profil klasörü** ile açılır.

## Nasıl çalışır

```
Yakalama (WGC, GPU) ─► en son kare ─► Algılama ─► GameState ─► Durum makinesi ─► Girdi
   30 FPS önizleme        kuyruk yok     10 FPS                   Ara → Git →        yalnızca oyun
   GDI yedek                             HP/MP/hedef barı         Saldır → Topla     odaktayken
                                         Metin şablon eşleme
```

- **Yakalama:** Windows Graphics Capture; oyunun üstü örtülse de çalışır, yalnızca istemci alanını okur. Desteklenmezse GDI'ya düşer.
- **Algılama:** Barlar kalibre edilen renge göre okunur; Metin taşları yarım çözünürlükte, çok ölçekli şablon eşleme (OpenCV) ile bulunur.
- **Karar:** Her durumun zaman aşımı var. Ulaşılamayan taş geçici kara listeye alınır, takılma olmaz. Taş görünmüyorsa kamera döner, sonra ileri yürür.
- **Girdi:** Tarama kodlu `SendInput`; oyun penceresi odakta değilse hiçbir şey gönderilmez, durunca basılı tuş bırakılmaz.

Uygulama oyun belleğini okumaz, koda enjekte etmez, görüntü yüklemez. Güncelleme yalnızca bu deponun Releases sayfasından yapılır.

> ⚠️ Bot kullanımı çoğu Metin2 sunucusunun kurallarına aykırıdır ve hesap kapatılmasına yol açabilir. Sorumluluk kullanıcıdadır.

## Geliştirme

```
src/SekoBot/            uygulama (Capture, Vision, Bot, Input, Config, Update, UI)
tests/SekoBot.Tests/    durum makinesi ve görüntü testleri
```

.NET 10 SDK ile `dotnet test` ve `dotnet run --project src/SekoBot`. Her push GitHub Actions'ta test edilip derlenir; `main` dalı `preview` sürümünü günceller.
