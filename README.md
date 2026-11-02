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

Döngü: **mob ara → tıkla → ölene kadar vur → eşya topla → tekrar.** Moblar önce **kırmızı isimlerinden** bulunur: bot ekrandaki kırmızı yazıları arar ve ismin altındaki mobun gövdesine tıklar (altında hareket eden bir şey varsa tam ona). İsim yoksa kamera dururken **hareket eden** şeyler mob sayılır. Adaylar önizlemede sarı halkalarla gösterilir. Bot karaktere en yakın olana tıklar; üstte hedef barı çıkarsa karakter koşup kendisi saldırır, bot **Space'e basmaz** (günlükte "Mob bulundu ve seçildi ✓"). Hedefin HP'si 3 sn düşmezse bot moba yeniden tıklar (mob yürüdüyse yeni yerine); 3 denemeden sonra başka mob arar. Tıklanan yerde hedef barı çıkmazsa o nokta 15 sn denenmez. Mob bulunamazsa iki kez kamerayı çevirir, sonra biraz yürür. Hedefteyken Space de basılsın istersen `profile.json` içinde `"UseAttackKeyOnTarget": true`. İlk tıklamalarda hedef barı henüz bilinmediği için ilk tur körlemesine olabilir, bu normal. Eski düz döngü için `profile.json` içinde `"AutoTarget": false`. Tuşlar (Space saldırı, Z toplama, 1/2 iksir, E kamera, W yürüme) ve süreler `%LOCALAPPDATA%\SekoBot\profile.json` içinde; arayüzden **Klasör** ile açılır.

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
