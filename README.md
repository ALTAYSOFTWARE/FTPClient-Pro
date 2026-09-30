# FTP Client Pro v2.0

**Modern ve güçlü FTP dosya aktarım istemcisi**

## Özellikler

- 🚀 **Hızlı FTP Bağlantısı** - Klasik FTP protokolü desteği
- 📁 **Çift Panel Dosya Yönetimi** - Yerel ve uzak dosyaları yan yana yönetin
- ⬇️⬆️ **Dosya Transfer** - İndirme ve yükleme işlemleri
- 🗑️ **Dosya Silme** - Uzak sunucuda dosya silme
- 🌑 **Dark Mode UI** - Modern, göz yormayan WPF arayüzü
- 📊 **Transfer Takibi** - Aktif transferleri ve ilerleme durumunu izleyin
- 📝 **Gerçek Zamanlı Log** - Tüm işlemlerin ayrıntılı kaydı
- 🔄 **Dizin Yönetimi** - Klasörler arasında gezinme ve yönetim

## Sistem Gereksinimleri

- **Windows 7+**
- **.NET Framework 4.8**
- **Visual Studio 2019+** (geliştirme için)

## Kurulum

### Yöntemi 1: Derlenmiş Executable
```bash
# Release klasöründen FTPClient.exe'yi çalıştırın
bin\Release\FTPClient.exe
```

### Yöntemi 2: Kaynaktan Derleme
```bash
# Repository'yi klonlayın
git clone https://github.com/ALTAYSOFTWARE/FTPClient-Pro.git
cd FTPClient-Pro

# working-ftpclient branch'ine geçin
git checkout working-ftpclient

# Visual Studio'da FTPClient.sln dosyasını açın
# Derleyin: Build > Build Solution (Ctrl+Shift+B)

# Çalıştırın: F5
```

## Kullanım

### 1. Bağlantı Kurma
```
1. Toolbar'da aşağıdaki bilgileri girin:
   - Host: ftp.example.com (veya IP adresi)
   - Port: 21 (varsayılan FTP portu)
   - Kullanıcı: (opsiyonel, boş bırakılırsa anonymous)
   - Şifre: (opsiyonel)

2. "Bağlan" butonuna tıklayın
3. Başarılı bağlantı sonrası uzak dosyalar otomatik yüklenecektir
```

### 2. Dosya Yönetimi
```
• Yerel Dosyalar (Sol Panel):
  - Dosya seçin
  - "Yükle" butonuna tıklayın
  - Veya drag-drop yapın (yakında)

• Uzak Dosyalar (Orta Panel):
  - Dosya seçin
  - "İndir" butonuna tıklayın
  - Transfer "Aktif Transferler" panelinde izlenir

• Klasör Gezinme:
  - Klasöre çift tıklayın veya Enter basın
  - ".." ile bir üst klasöre çıkın
```

### 3. Transfer Takibi
```
• Sağ panelde aktif transferler görüntülenir
• İlerleme çubuğu ve transfer durumu gösterilir
• Log panelinde tüm işlemler kaydedilir
```

## Mimari

### Dosya Yapısı
```
FTPClient-Pro/
├── App.xaml              # Uygulama kaynakları
├── App.xaml.vb           # Uygulama başlatma
├── MainWindow.xaml       # Arayüz tasarımı
├── MainWindow.xaml.vb    # UI mantığı
├── FtpEngine.vb          # FTP işlemleri
├── Models.vb             # Veri modelleri
├── FTPClient.vbproj      # Proje dosyası
├── FTPClient.sln         # Solution dosyası
└── App.config            # Yapılandırma
```

### Sınıflar

**FtpEngine**
- FTP sunucusu bağlantısını yönetir
- Dosya yükleme/indirme işlemlerini gerçekleştirir
- Dizin listeleme
- Dosya silme

**Models**
- `FtpServerInfo` - Sunucu bilgileri
- `RemoteFile` - Uzak dosya
- `LocalFile` - Yerel dosya
- `TransferItem` - Transfer durumu

**MainWindow**
- UI yönetimi
- Kullanıcı olayları
- FtpEngine çağrıları

## Gelecek Özellikler (Roadmap)

- [ ] SFTP (SSH) desteği
- [ ] FTPS (SSL/TLS) şifrelemesi
- [ ] Site yönetimi ve kayıtlı bağlantılar
- [ ] Sürükle-bırak dosya yükleme
- [ ] Klasör yükleme/indirme
- [ ] Hız sınırlandırması
- [ ] Yeniden deneme mekanizması
- [ ] Düzenleme ve yeniden adlandırma
- [ ] Dosya izinleri (CHMOD)
- [ ] Taşıma ve kopyalama işlemleri
- [ ] Tarama ve arama özelliği
- [ ] Havuz yönetimi (connection pooling)

## Troubleshooting

### "Bağlantı başarısız" hatası
```
✓ Host adresini kontrol edin
✓ Port numarasını doğrulayın (genelde 21)
✓ Kullanıcı adı/şifre doğru mu?
✓ Güvenlik duvarı FTP portunu engellemiyor mu?
✓ Sunucu çalışıyor mu? (Terminalden test edin)
```

### "Erişim Reddedildi" hatası
```
✓ Kullanıcı izinleri kontrol edin
✓ Dosya/klasör sahibi kimdir?
✓ chmod ayarlarını kontrol edin (Unix sunucuları)
```

### Dosya transfer başarısız
```
✓ Ağ bağlantısı kontrol edin
✓ Diskte yeterli alan var mı?
✓ Dosya adında özel karakterler var mı?
✓ Log panelinde hata mesajını kontrol edin
```

## Lisans

MIT License - Detaylar için `LICENSE` dosyasını okuyun

## Geliştirici

**ALTAYSOFTWARE**
- GitHub: [@ALTAYSOFTWARE](https://github.com/ALTAYSOFTWARE)
- FTP Client Pro: [Repository](https://github.com/ALTAYSOFTWARE/FTPClient-Pro)

## Katkıda Bulunma

Katkılarınızı bekliyoruz! Lütfen:

1. Repository'yi fork edin
2. Feature branch'i oluşturun (`git checkout -b feature/AmazingFeature`)
3. Değişikliklerinizi commit edin (`git commit -m 'Add some AmazingFeature'`)
4. Branch'i push edin (`git push origin feature/AmazingFeature`)
5. Pull Request açın

## Destekler

- 💾 FTP (File Transfer Protocol)
- 📝 Metin tabanlı komutlar
- 🔌 TCP/IP socket bağlantısı
- 🌐 IPv4 desteği
- 🛡️ SSL/TLS (gelecek sürüm)

## Bilinen Sınırlamalar

- Klasör yükleme/indirme: Tek tek dosyalar transfer edilir
- IPv6 desteği: Planlıyor
- Bağlantı havuzu: Henüz uygulanmadı
- Maksimum dosya boyutu: Sistem belleğine bağlı

## Sürüm Geçmişi

### v2.0 (Mevcut - working-ftpclient)
- Tamamen yeniden yazılmış
- Temiz, bakımlanabilir kod yapısı
- Modern WPF UI
- Temel FTP işlemleri
- Real-time logging

### v1.0 (complete-project-structure branch)
- İlk deneme
- Karmaşık yapı
- Bağımlılık sorunları

---

**Son Güncelleme:** 30 Eylül 2026
**Durum:** Aktif Geliştirme 🚀