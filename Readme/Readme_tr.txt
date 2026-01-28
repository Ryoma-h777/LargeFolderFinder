Large Folder Finder
====================
Klasör hiyerarşilerini hızlı bir şekilde analiz etmek ve listelemek için bir araç.
Boyut koşulları ve filtreler (joker karakterler, düzenli ifadeler) kullanarak klasör analizi için kullanışlıdır.
NAS gibi birden fazla kullanıcı tarafından kullanılan büyük verilerde sorunun nedenini belirlemeye yardımcı olur.


■ Nasıl kullanılır
--------------------
  1. İncelemek istediğiniz klasörü seçin.
  2. Aramayı başlatmak için "▶" (Tara) düğmesine basın.
  3. Sonuçlar Windows Gezgini'ne benzer bir formatta görüntülenir.
  4. Görüntüleme koşullarını belirtin: çıkarılacak minimum boyut, filtre, sıralama, klasörleri daralt.
  5. Görüntüleme sonuçlarını panoya kopyalamak için sağ üstteki kopyala düğmesine basın.
  6. Geçmişi koruyarak yeni bir tarama başlatmak için sekmenin sağındaki "+" düğmesine basın.
    Geçmiş, uygulama kapatıldıktan sonra bile korunur.
※ Uygulamayı yönetici ayrıcalıklarıyla çalıştırmak, C sürücüsündeki yönetici ayrıcalıklı klasörleri analiz etmenizi sağlar.
※ Menü/Görünüm'den dili değiştirebilir veya düzeni değiştirebilirsiniz.
※ Menü/Görünüm/Gelişmiş Ayarları Aç(S)'den ayarları değiştirebilirsiniz. Ayrıntılar aşağıda açıklanmıştır.


■ Görüntüleme özellikleri hakkında
-------------------
1. Sırala
  Görüntüleme sırasını sıralamak için her etikete (Ad, Boyut, Değiştirilme Tarihi, Tür) tıklayın.
  Artan/azalan arasında geçiş yapmak için tekrar tıklayın.
2. Dosyaları da göster
  Dosyaları da görüntülemek için bunu işaretleyin.
3. Minimum Boyut
  Görüntülenecek klasörlerin veya dosyaların minimum boyutunu belirtin. Ayarlanan değere eşit veya daha büyük öğeler görüntülenecektir.
  Her şeyi görüntülemek istiyorsanız 0 girin.
  Birimler Byte'tan TB'ye kadar seçilebilir.
4. Filtre
Joker karakter: Windows Gezgini ile aynı davranış.
  * Herhangi bir dizeyle eşleşmeye izin verir. Örnek) *.txt Herhangi bir adla tüm txt dosyaları. Örnek 2) *veri* Adında "veri" olan tüm dosyalar.
  ? Herhangi bir tek karakterle eşleşmeye izin verir. Örnek) 202?yıl → 2020yıl~2029yıl vb. (rakam olmayanlarla da eşleşir)
  ~ (* veya ?) karakterlerini aramak için önüne yerleştirin. Örnek) ~?.txt → ?.txt'yi arar
Düzenli ifade: Gelişmiş filtre özelliği (mühendisler vb. tarafından kullanılır)
  Joker karakterlerin yapamayacağı şeyleri yapabilir. Yalnızca sayıları, küçük harfleri, büyük harfleri eşleştirme, yalnızca eşleşmeyen öğeleri çıkarma vb.
  Karmaşıktır, bu nedenle ayrı olarak "düzenli ifadeler nasıl kullanılır" araması yapın.
  Aramanızın doğru çalışıp çalışmadığını doğrulamak için düzenli ifade kontrol araçları da mevcuttur.
5. Boşluk/Sekme
  Kopyala düğmesine basıldığında ad ve boyut arasındaki boşluğu boşluk veya sekmelerle doldurmayı belirtin.


■ Düzgün çalışmadığında
------------------------
※ Uygulamanın davranışını Menü/Görünüm/Günlükler'den kontrol edebilirsiniz.
※ Uygulama garip davranıyorsa, aşağıdaki klasördeki verileri silmek önbelleği sıfırlayabilir ve işlevselliği geri yükleyebilir.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Gelişmiş ayarlar hakkında (Config.txt)
--------------------
Yürütme dizinindeki "Config.txt"yi düzenleyerek daha ayrıntılı davranış ayarları mümkündür.
Kullanıcı arayüzündeki "⚙" düğmesine tıklayarak Not Defteri gibi bir metin düzenleyiciyle hemen açabilirsiniz.
Yapılandırma YAML formatını takip etmelidir. Kendi yorumlarınızı eklemek istiyorsanız, önüne # koyun.

    ▽ Yapılandırılabilir öğeler: (Varsayılan)
    UseParallelScan: true
        Tür: bool (true/false)
        Açıklama: Paralel işlemeyi etkinleştir
        Beklenen değer (true): NAS (ağ depolama) vb. için etkilidir. Yerel SSD'ler hızlıdır, bu nedenle paralelleştirme yükü daha büyük olabilir.

    SkipFolderCount: false
        Tür: bool (true/false)
        Açıklama: İlerleme görüntüsü için ön sayımı atlayıp taramayı hemen başlatıp başlatmayacağı
        True ise, toplam sayı bilinmediği için ilerleme yüzdesi görüntülenemez.

    MaxDepthForCount: 3
        Tür: int (doğal sayı)
        Açıklama: İlerleme yüzdesini belirlemek için klasörleri önceden saymak için maksimum hiyerarşi derinliği
        Belirtilen daha büyük hiyerarşi daha fazla zaman alabilir. Bunun yerine ilerleme doğruluğu artar.
        Beklenen değer (3): NAS: 3~6, Dahili PC: 7~

    UsePhysicalSize: true
        Tür: bool (true/false)
        Açıklama: Küme boyutunu dikkate alarak "diskte ayrılan boyutu" hesaplayıp hesaplamayacağı
        Beklenen değer (true): Genellikle true tutulması önerilir. Sonuçlar Windows özellik görüntülerine daha yakın olacaktır. False ise dosya boyutuna göre hesaplanır.
        Bunu ayarlamadan önce yönetici olarak çalıştırmanızı öneririz. Sistem dosyaları doğruluk için hesaplamalara dahil edilecektir.

    OldDataThresholdDays: 30
        Tür: int (Negatif olmayan tam sayı)
        Açıklama: Belirtilen gün sayısı geçtiyse, eski tarama verilerini belirtmek için sekmeyi sarı renkle vurgular.
        Beklenen Değer: Kullanıcı tercihi.

■ Dil dosyaları nasıl eklenir
--------------------
Bu araç birden fazla dili destekler ve yenilerini ekleyebilirsiniz.
1. Uygulama yürütülebilir dosyası (.exe) ile aynı hiyerarşideki "Languages" klasörünü açın.
2. "en.yaml" gibi mevcut bir dosyayı kopyalayın ve eklemek istediğiniz dilin kültür koduna yeniden adlandırın (örneğin, Fransızca için "fr.yaml").
   * Kültür kodlarının listesi için (örneğin: ja-JP / ja) aşağıya bakın:
   https://learn.microsoft.com/tr-tr/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. YAML dosyasındaki metni düzenleyin (UTF-8 formatında kaydedin).
4. Uygulamayı yeniden başlatın ve yeni dil "Language" menüsünde görünecektir.
※ Gerekirse, diğer dosyalara başvurarak Readme_<language_code>.txt oluşturun ve ekleyin.


■ Tam kaldırma (Ayarları ve günlükleri sil)
--------------------
Bu aracın ayarlarını ve yürütme günlüklerini tamamen kaldırmak için aşağıdaki klasörü manuel olarak silin:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Yukarıdaki yolu Gezgin adres çubuğuna yapıştırarak doğrudan açabilirsiniz)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
