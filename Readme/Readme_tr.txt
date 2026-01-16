Large Folder Finder
====================
Belirli bir boyuttan daha büyük klasörleri hızlı bir şekilde ayıklamak ve listelemek için bir araçtır.


■ Nasıl Kullanılır
--------------------
1. İncelemek istediğiniz klasörü seçin.
2. Ayıklamak istediğiniz minimum boyutu belirtin.
3. Aramayı başlatmak için "Scan" düğmesine basın.
4. Sonuçlar metin formatında görüntülenir.
5. Sonuçları panoya kopyalamak için sağ üstteki kopyalama düğmesine (📄 simgesi) basın.


■ Gelişmiş Ayarlar (Config.txt)
--------------------
Uygulama dizinindeki "Config.txt" dosyasını düzenleyerek ayrıntılı davranışları yapılandırabilirsiniz.
Not Defteri gibi bir metin düzenleyiciyle hemen açmak için kullanıcı arayüzündeki "⚙" düğmesine tıklayın.
Yapılandırma YAML formatına uygun olmalıdır. Kendi yorumlarınızı eklemek isterseniz, başlarına # koyun.

    ▽ Yapılandırılabilir öğeler: (Varsayılan)
    UseParallelScan: false
        Tür: bool (true/false)
        Açıklama: Paralel taramayı etkinleştir.
        Bağlam (false): HDD'ler (ve NAS'lar) fiziksel olarak döndüğü için paralel erişimde zayıftır, bu nedenle false olarak ayarlayın. Yalnızca SSD'ler için "true" önerilir.

    SkipFolderCount: false
        Tür: bool (true/false)
        Açıklama: İlerleme gösterimi için ön sayımın atlanıp atlanmayacağı ve taramanın hemen başlatılıp başlatılmayacağı.
        Eğer true olarak ayarlanırsa, toplam klasör sayısı bilinmediği için ilerleme yüzdesi görüntülenemez.

    MaxDepthForCount: 3
        Tür: int (doğal sayı)
        Açıklama: İlerleme yüzdesini belirlemek için klasörlerin ön sayımının yapılacağı maksimum hiyerarşi derinliği.
        Daha büyük değerler daha fazla zaman alabilir ancak ilerleme doğruluğunu artırır.
        Örnek (3): NAS: 3~6, Dahili PC: 7~

    UsePhysicalSize: true
        Tür: bool (true/false)
        Açıklama: Küme boyutunu dikkate alarak "diskteki ayrılmış boyutun" hesaplanıp hesaplanmayacağı.
        Örnek (true): Genellikle true tutulması önerilir. Sonuçlar Windows özellik ekranlarına daha yakın olacaktır. false ise, gerçek dosya boyutuna göre hesaplar.
        Bunu ayarlamadan önce, sistem dosyalarını hesaplamalara doğru bir şekilde dahil etmek için uygulamayı yönetici olarak çalıştırmanızı öneririz.


■ Dil Dosyaları Nasıl Eklenir
--------------------
Bu araç birden fazla dili destekler ve yeni diller ekleyebilirsiniz.
1. Yürütülebilir dosya (.exe) ile aynı dizindeki "Languages" klasörünü açın.
2. "en.yaml" gibi mevcut bir dosyayı kopyalayın ve adını eklemek istediğiniz dilin kültür koduna göre değiştirin (örneğin, Fransızca için "fr.yaml").
   * Kültür kodlarının listesi için Microsoft belgelerine bakın:
   https://learn.microsoft.com/tr-tr/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. YAML dosyasının içindeki metni düzenleyin (UTF-8 formatında kaydedin).
4. Uygulamayı yeniden başlatın, yeni dil "Language" menüsünde görünecektir.
* Gerekirse, diğer dosyaları referans alarak bir Readme_<code>.txt oluşturun ve ekleyin.


■ Temiz Kaldırma (Ayarları ve Logları Kaldır)
--------------------
Bu aracın ayarlarını ve yürütme günlüklerini tamamen kaldırmak için lütfen aşağıdaki klasörü manuel olarak silin:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Yukarıdaki yolu Gezgin adres çubuğuna yapıştırarak doğrudan açabilirsiniz)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
