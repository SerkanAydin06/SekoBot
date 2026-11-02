# SekoBot Yol Haritası

## v0.2 — Godot masaüstü temeli
- [x] Eski Python/PySide6 dosyalarını kaldır
- [x] Godot 4.5 .NET projesi ve GDScript arayüzü
- [x] Win32 ile oyun penceresi keşfi, HWND/PID takibi
- [x] GDI/BitBlt ile oyun alanı ekran yakalama
- [x] Canlı önizleme, FPS seçimi, yerel PNG kaydı, durum/günlük
- [ ] Gerçek Windows + Metin2 istemcisiyle uçtan uca çalışma testi

## v0.3 — Yakalama kalitesi
- [ ] Pencere örtülmesi tespiti ve uyarı
- [ ] Windows Graphics Capture backend'i (desteklenen pencerelerde)
- [ ] Ana iş parçacığı dışına aktarılmış ekran yakalama ve FPS ölçümü
- [ ] Çoklu monitör / farklı DPI testleri
- [ ] Ekran görüntüsünde bölge seçimi ve tanıma kutuları

## v0.4 — Salt-okunur görsel analiz
- [ ] Can ve mana çubuğu tespiti
- [ ] Hedef/Metin taşı şablon karşılaştırma
- [ ] Ölüm ve bağlantı kaybı durumu tespiti
- [ ] Oyun sürümü/sunum arayüzü ayar profilleri

## v0.5 — Kullanıcı kontrollü farm
- [ ] Giriş komutları için ayrı ve durdurulabilir Windows kontrol modülü
- [ ] Güvenli durdurma tuşu, önizleme ve kullanıcı onayı
- [ ] Hedef seçimi, saldırı ve toplanma durum makinesi
- [ ] İstatistik ve olay kaydı

## Windows üzerinde doğrulama
1. Godot 4.5 .NET ve .NET 8 SDK ile proje Build (C#) başarılı mı?
2. F5 ile ana kontrol paneli açılıyor mu?
3. Metin2 pencere başlığından bulunuyor mu? Birden fazla oyun penceresi seçilebiliyor mu?
4. Pencere taşındığında ve boyut değiştirildiğinde önizleme doğru mu?
5. Simge durumuna küçültülmüş veya kapatılmış pencere için doğru uyarı görünüyor mu?
6. Ekran görüntüsü geçerli PNG olarak Pictures/SekoBot içine kaydediliyor mu?
7. GPU/DirectX modu siyah görüntüye yol açıyor mu? (Bilinen GDI sınırlaması.)
