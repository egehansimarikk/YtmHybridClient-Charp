# YTM Lossless Hybrid Client

YouTube Music için bir Windows masaüstü istemcisi. Bir şarkının eşleşmesi başka bir kaynakta
(Jamendo, Audius, SoundCloud, kendi FLAC dosyaların) bulunursa onu çalar, bulunamazsa YouTube Music'e döner.
Üst çubuktaki rozet sesin nereden geldiğini her zaman gösterir.

🇬🇧 [English README](README.md)

## Özellikler

- YouTube Music gömülü WebView2 tarayıcıda (kendi hesabın, kendi kitaplığın)
- Parça değişimini algılar ve aynı şarkıyı diğer kaynaklarda arar
- Ses kaynakları: **Jamendo**, **Audius**, **SoundCloud**, **Yerel FLAC**, **JioSaavn** (deneysel, aşağıya bak)
- Kaynak rozeti: `🔍 Aranıyor`, `⏳ açılıyor`, `🎵 <kaynak>`, `▶ YouTube Music`, `🔇 Lossless bulunamadı`
- **Hızlı kaynak değiştirme**: rozete ya da 🔄 düğmesine tıklayarak o anki şarkıyı Jamendo → Audius → SoundCloud → YouTube Music arasında gezdir
- Yanlış şarkı çalmasın diye **katı eşleştirme**
- **WASAPI Exclusive / Shared** çıkış, başarısız olursa otomatik Shared'a geçiş
- **Ses çıkış aygıtı** seçimi
- **Harici kaynak ses takviyesi** (varsayılan +1,5 dB) ve yumuşak sınırlayıcı: Jamendo, Audius ve SoundCloud, YouTube Music kadar yüksek duyulur
- **Akıllı Yedekleme**: daha iyi bir kaynak bulunamazsa YouTube Music'in sesini aç (ya da sessiz kal)
- Türkçe ve İngilizce arayüz

## Gereksinimler

- Windows 10 veya 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (güncel Windows 10/11 sistemlerinin çoğunda zaten kurulu)

## Kurulum

1. [Releases](../../releases) sayfasından en son kurulum dosyasını (`.exe`) indirip çalıştır.
2. Kurulumdan sonra uygulamayı başlat.
3. Uygulama penceresinde YouTube Music'e giriş yap.
4. **Ayarlar**'ı açıp kaynaklarını seç.

## Kaynak koddan derleme

```powershell
git clone https://github.com/egehansimarikk/YtmHybridClient
cd YtmHybridClient
dotnet build -c Release
```

Ya da çözümü Visual Studio 2022 veya daha yenisinde (.NET 9 SDK) açıp **Release** olarak derle.

## Ses kaynakları

| Kaynak | Nedir | Notlar |
|---|---|---|
| **Jamendo** | Creative Commons lisanslı ücretsiz müzik | Ücretsiz bir API Client ID gerekir (aşağıya bak). Bağımsız sanatçılar, küçük katalog |
| **Audius** | Sanatçıların kendi yüklediği parçalardan oluşan açık müzik platformu | API anahtarı gerekmez. Çoğunlukla elektronik, hip-hop ve bağımsız müzik |
| **SoundCloud** | Müzik akış platformu | Resmi olmayan web API, hesap gerekmez. Sadece tam çalınabilen ve doğrudan MP3 akışı olan parçalar kullanılır (genelde 128 kbps, lossless **değil**). SoundCloud sitesini değiştirirse bozulabilir |
| **Yerel FLAC** | Seçtiğin klasördeki FLAC dosyaları | Sevdiğin şarkıları gerçekten lossless dinlemenin tek yolu |
| **JioSaavn** | Resmi olmayan topluluk API'leri | **Güvenilmez.** Herkese açık adresler hız sınırlı ya da kapalı, önerilmez |

> **Çok sayıda "YouTube Music" görmeyi bekle.** Jamendo ve Audius bağımsız kataloglardır. Ana akım
> şarkıların çoğu orada yoktur ve YouTube Music'ten çalar. Bu bilerek böyle tasarlandı.

### Jamendo Client ID

[developer.jamendo.com](https://developer.jamendo.com/) adresinde ücretsiz bir uygulama oluştur ve
Client ID'ni **Ayarlar → Jamendo API Client ID** alanına yapıştır. Kendi ID'ni kullanmak ortak
hız sınırlarından kaçınmanı sağlar.

## Ayarlar

| Ayar | Açıklama |
|---|---|
| Dil | Türkçe veya İngilizce |
| Birincil / İkincil kaynak | Kaynakların denenme sırası |
| Harici kaynak ses takviyesi | Jamendo, Audius ve SoundCloud için dB cinsinden takviye (0 = kapalı, varsayılan +1,5). Yerel FLAC'e uygulanmaz. Kaydırıcıyı oynatırken anında uygulanır |
| Ses çıkış aygıtı | Harici kaynakların çıkış aygıtı. Boşsa *Sistem varsayılanı* |
| Yerel FLAC klasörü | Yerel FLAC kaynağının tarayacağı klasör |
| Jamendo API Client ID | Jamendo API anahtarın |
| Katı eşleştirme | Başlık **ve** sanatçı eşleşmeli. Kapatırsan daha çok eşleşme olur ama yanlış şarkı riski artar |
| WASAPI Exclusive | Aygıtı kilitleyen bit-perfect çıkış. Başarısız olursa Shared'a düşer |
| Akıllı Yedekleme | Lossless bulunamazsa YouTube Music'in sesini aç. Kapalıysa o şarkılar sessiz kalır |

Ayarlar `%AppData%\YtmHybridClient\appsettings.json` dosyasında saklanır.
Tarayıcı profili (oturum, çerezler) `%AppData%\YtmHybridClient\WebView2Profile` içindedir.

Çıkış aygıtı ya da WASAPI modu değişikliği, harici kaynaktan çalan şarkıya hemen uygulanır. "YouTube Music"
yedeği olarak çalan ses her zaman Windows'un varsayılan aygıtını kullanır.

## Sorun giderme

- **Rozet "Aranıyor..."da kalıyor** – v1.2.0 veya daha yeni bir sürüme güncelle.
- **Kaynak hiç görünmüyor** – Şarkının o katalogda olup olmadığına bak. Katı eşleştirmeyi kapatmayı dene.
- **Hiç ses yok** – Akıllı Yedekleme kapalı olabilir ya da seçili çıkış aygıtı başka bir uygulama
  tarafından Exclusive modda kullanılıyor olabilir. Exclusive'i kapatmayı dene.
- **Çıkış aygıtı değişmiyor** – Aygıt ayarı sadece bu uygulamanın çaldığı şarkılar için geçerlidir (Jamendo,
  Audius, SoundCloud, Yerel FLAC). Rozette "▶ YouTube Music" yazan şarkılar tarayıcı bileşeninden çalar ve
  Windows'un varsayılan aygıtını kullanır. Onları başka bir aygıta yönlendirmek için Windows
  **Ayarlar → Sistem → Ses → Ses karıştırıcısı**'nda bu uygulama / *Microsoft Edge WebView2* için çıkış aygıtını seç.
- **Günlükler** – Visual Studio'dan **Debug** derlemesini çalıştırınca Output penceresinde `[Resolver]`,
  `[Jamendo]`, `[Audius]`, `[SoundCloud]`, `[AudioEngine]` gibi satırlar her kararı açıklar.
  Release derlemeleri bu günlükleri yazmaz.

## Sorumluluk reddi

- Bu proje Google, YouTube, Jamendo, Audius veya JioSaavn ile **bağlantılı değildir**.
- Uygulama YouTube Music sayfasındaki reklamları gizler ve filtreler. Bu, YouTube'un Hizmet Şartları ile
  çelişebilir. Kullanım riski sana aittir.
- Jamendo parçaları Creative Commons lisanslıdır; kişisel dinleme dışındaki kullanımlar için her parçanın
  lisansına bak. Audius, uygulamaların sanatçılara atıf yapmasını ister.
- SoundCloud kaynağı, herkese açık siteden okunan bir client ID ile SoundCloud'un resmi olmayan web API'sini
  kullanır. Bu, SoundCloud'un API şartları kapsamında değildir ve herhangi bir zamanda çalışmayı bırakabilir.
  Önizlemeler ve kısıtlı parçalar atlanır.
- JioSaavn kaynağı resmi olmayan API'lere dayanır ve içeriği lisanssız kullanıyor olabilir. Sadece uyumluluk
  için tutulur, önerilmez.

## Lisans

Yayınlamadan önce seçeceğin bir `LICENSE` dosyası ekle (örneğin MIT).
