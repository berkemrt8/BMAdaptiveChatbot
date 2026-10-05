# Örnek botlar

Bu klasörde platformu denemek için üç örnek bot bulunur. Her örnek, depodaki yerleriyle aynı klasör yapısındadır:
seed dosyası `Chatbot.Api/Data/Seed/` altına, veri seti `Chatbot.Trainer/Data/{botKey}/` altına kopyalanır.

| Bot | Konu | Intent | Eğitim / regression cümlesi | Canlı veri |
|---|---|---|---|---|
| `hotel-demo` | Kurgusal bir otel: odalar ve fiyatlar, giriş-çıkış, yeme-içme, kurallar | 27 | 1129 / 171 | Yok |
| `nexora-demo` | Nexora Tech Store e-ticaret sitesi: kargo, iade, hesap işlemleri, ürün soruları | 21 | 945 / 214 | Ürün fiyatı, stoku ve arama Nexora sitesinin API'sinden ([ayrıntılar](nexora-demo/README.md)) |
| `tedu-demo` | TED Üniversitesi öğrenci soruları: akademik takvim, kredi ve sınıf, notlar, staj, çift anadal/yandal | 32 | 1171 / 192 (+64 holdout) | Yok ([kaynaklar](tedu-demo/README.md)) |

## Bir örneği eklemek

Depo kök klasöründe (PowerShell), `{bot}` yerine örneğin adını yazarak:

```powershell
Copy-Item samples/{bot}/Chatbot.Api/Data/Seed/{bot}.json Chatbot.Api/Data/Seed/
Copy-Item -Recurse samples/{bot}/Chatbot.Trainer/Data/{bot} Chatbot.Trainer/Data/
dotnet run --project Chatbot.Trainer -- {bot}
```

Ardından API yeniden başlatılır; bot veritabanına eklenir ve test arayüzündeki listede görünür.

## Ölçüm sonuçları

Eğitimde kullanılmamış sorularla, API üzerinden:

| | hotel-demo (171 soru) | nexora-demo (214 soru) |
|---|---|---|
| Doğrudan doğru cevap | %51,5 | %88,8 |
| Doğru cevap seçeneklerde sunuldu | %26,9 | %2,8 |
| **Kullanıcı doğru cevaba ulaştı (toplam)** | **%78,4** | **%91,6** |
| Yanlış seçenek sunuldu | %4,1 | %0,0 |
| Yanlış cevap | %1,2 | %2,8 |
| Fallback | %16,4 | %5,6 |

Otel botunda doğrudan cevap oranının düşük olmasının sebebi, %97 doğruluk hedefinde modelin daha fazla soruda yeterince emin olamaması. Bot bu durumda yanlış cevap vermek yerine seçenek sunuyor.

`nexora-demo` sonuçlarında iki noktaya dikkat edilmelidir: regression setindeki ürün adları eğitim setindekilerle aynıdır, bu yüzden daha önce görülmemiş ürün adlarıyla sorulan sorular ölçülmemiş olur. Ayrıca ölçüm sırasında Nexora sitesi kapalıysa canlı veri soruları da (intent doğru bulunduğu için) doğru cevap sayılır.

`tedu-demo` sonuçları [kendi klasöründe](tedu-demo/README.md) anlatılmıştır.
