# SekoBot Yol Haritası

## v0.3 — Bağımsız Windows EXE
- [x] Godot ve PowerShell yakalama zincirini kaldır
- [x] Tek dosyalı, self-contained Windows x64 EXE yapılandırması
- [x] Win32 ile pencere listesi ve istemci alanı boyutlarını okuma
- [x] C# GDI+ ile canlı pencere görüntüsü
- [x] Kompakt çerçevesiz kontrol paneli; 5/10/15 FPS
- [x] PNG kaydı, sistem günlüğü, HWND/PID doğrulaması
- [x] GitHub üzerinde sabit bağlantıdan EXE dağıtımı
- [ ] GitHub Windows derlemesinin başarılı olduğunu doğrulama
- [ ] Gerçek Windows 10/11 + Metin2 istemcisiyle görüntü yakalama testi

## Sonraki aşama
- [ ] Windows Graphics Capture ile ek yakalama yöntemi
- [ ] Arka plan / üstü örtülü pencere davranışı testi
- [ ] Görsel hedef/Metin taşı tanıma
- [ ] Kullanıcı kontrollü farm durum makinesi ve güvenli durdurma

## v0.3.1 — Otomatik güncelleme
- [x] Başlangıçta yeni GitHub Release sürümünü kontrol et
- [x] Derleme commit kimliği + SHA-256 manifesti yayınla
- [x] Yeni sürümü doğrulayarak indir ve kullanıcıya yeniden başlatma onayı ver
- [x] Eski EXE dosyasını uygulama kapandıktan sonra değiştir
- [ ] Gerçek Windows EXE üzerinde güncelleme testi

## v0.3.2 — Yakalama döngüsü güvenilirliği
- [x] Görüntü aktarımında UI iş parçacığını senkron beklemeyi kaldır
- [x] UI meşgulse yeni görüntü kuyruğunun büyümesini engelle
- [x] Yeniden bağlanmada eski oturumun yeni oturumu durdurmasını engelle
- [x] 30 FPS önizleme seçeneği
- [ ] Windows üzerinde gerçek FPS, CPU ve bellek kullanımını ölç
- [ ] Uzun süreli çalışmada pencere kapatma / tekrar bağlanma stres testi
- [ ] Windows Graphics Capture / DXGI desteği ve üstü örtülü pencere testi
- [ ] Kullanıcı onayı olmadan girdi göndermeyen bağımsız otomasyon katmanı
