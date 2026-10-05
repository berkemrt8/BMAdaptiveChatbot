# tedu-demo

TED Üniversitesi öğrencilerinin sık sorduğu sorular için hazırlanmış örnek bot: akademik takvim, krediye göre sınıf,
kredi yükü, notlar ve ortalama, sınamalı durum, ders tekrarı ve çekilme, staj, çift anadal ve yandal, yaz okulu,
kayıt dondurma ve iletişim. Canlı veri kullanmaz.

## Kaynaklar

Cevaplar TED Üniversitesi'nin aşağıdaki resmi kaynaklarından derlenmiştir (Ekim 2026). Her cevabın altındaki
bağlantı ilgili kaynağa gider.

| Kaynak | Kullanıldığı konular |
|---|---|
| [2026-2027 Akademik Takvim](https://www.tedu.edu.tr/akademik-takvim) | Dönem tarihleri, ders kaydı, ekle-bırak, finaller, tatiller, yaz okulu, çekilme ve izin son tarihleri |
| [Lisans Eğitim-Öğretim Yönetmeliği](https://www.tedu.edu.tr/sites/default/files/docs/Resmi-Gazete-29-Haziran-2025-PAZAR.pdf) (Resmî Gazete, 29.06.2025) | Harf notları, ortalama, sınamalı, onur, ders tekrarı ve çekilme, itiraz, mezuniyet ve ek sınav, azami süre, izin |
| [Öğrenci İşleri, Akademik Konularla İlgili Özet Bilgiler](https://registrar.tedu.edu.tr/yonetmelik-yonerge-akademik-konularla-ilgili-ozet-bilgiler) | Krediye göre sınıf (29, 30-58, 59-96, 97+), kredi yükü, sınamalı eşikleri, çekilme kuralları |
| [Lisans Öğrencileri İçin Staj Yönergesi](https://career.tedu.edu.tr/sites/default/files/inline-files/KYS-YN-17_LisansOgrencileriIcinStajYonergesi.pdf) (Rev.4, 14.08.2025) | Staj süreleri, başvuru, sigorta, gönüllü staj, Erasmus stajı |
| [ÇAP](https://registrar.tedu.edu.tr/cap-programi-sikca-sorulan-sorular) ve [YAP](https://registrar.tedu.edu.tr/yap-programi-sikca-sorulan-sorular) sıkça sorulan sorular, [Yaz Okulu](https://registrar.tedu.edu.tr/yaz-okulu), [TEDÜ SSS](https://www.tedu.edu.tr/sikca-sorulan-sorular) | Çift anadal, yandal, yaz okulu, Erasmus, iletişim |

Takvime bağlı cevaplar (tarihler) 2026-2027 akademik yılı içindir ve her yıl güncellenmelidir. Asıl kaynak veritabanı
olduğu için güncelleme `KnowledgeArticles` tablosunda yapılır; seed dosyası yalnızca boş bir veritabanını doldurur.

## Ölçüm

Ölçüm, API ile aynı karar zincirini (`ChatbotService`) bellek içi veritabanıyla çalıştırarak yapıldı. Alias'lar
regression sonuçlarına bakılarak bir kez düzeltildiği için regression seti bu bot için bir doğrulama setidir. Bu yüzden
düzeltmelerden önce yazılıp hiç kullanılmayan 64 soruluk ayrı bir set (`holdout.tsv`) ile de ölçüldü. Trainer bu
dosyayı okumaz; yalnızca karşılaştırma için tutulur.

| | regression (192 soru) | holdout (64 soru, dokunulmamış) |
|---|---|---|
| Doğrudan doğru cevap | %71,4 | %75,0 |
| Doğru cevap seçeneklerde sunuldu | %21,4 | %15,6 |
| **Kullanıcı doğru cevaba ulaştı (toplam)** | **%92,8** | **%90,6** |
| Yanlış seçenek / ana menü | %1,0 | %1,6 |
| Yanlış cevap | %1,0 | %0,0 |
| Fallback | %5,2 | %7,8 |

Eğitim, regression ve holdout cümlelerinin hepsi aynı kişi tarafından yazıldığı için gerçek öğrenci soruları bu
sonuçlardan daha zor olabilir.
