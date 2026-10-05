# nexora-demo

Nexora Tech Store adlı bir e-ticaret sitesi için hazırlanmış örnek bot. Kargo, iade, ödeme, hesap işlemleri gibi
sabit cevaplı soruların yanında ürün fiyatı, stok durumu, teknik özellik ve ürün araması gibi canlı veri gerektiren
soruları da cevaplar. Canlı veri, depodaki `NexoraCatalogProvider` sınıfı aracılığıyla Nexora sitesinin entegrasyon
API'sinden (`/api/integration/v1/catalog/products`) okunur.

Nexora Tech Store sitesi bu deponun parçası değildir; ayrı bir projedir.

## Canlı veriyi açmak

Botu ve veri setini [genel adımlarla](../README.md#bir-örneği-eklemek) ekledikten sonra `Chatbot.Api/appsettings.json`
içindeki `ExternalData` bölümü aşağıdaki gibi doldurulur:

```json
"ExternalData": {
  "BotProviders": {
    "nexora-demo": "nexora-catalog"
  },
  "Providers": {
    "nexora-catalog": {
      "BaseUrl": "http://localhost:5248",
      "ApiKeyHeader": "X-Nexora-Integration-Key",
      "ApiKey": "<Nexora sitesindeki IntegrationApi:ApiKey ile aynı değer>",
      "TimeoutSeconds": 5
    }
  }
}
```

- `BotProviders` eşlemesi yapılmazsa bot yalnızca sabit cevaplı soruları cevaplar; ürün soruları için seed dosyasında
  cevap olmadığından fallback mesajı verilir.
- Eşleme yapıldığı halde Nexora sitesi kapalıysa ürün sorularına "Nexora ürün servisine şu anda bağlantı kurulamıyor."
  cevabı döner.
- Canlı ortamda `ApiKey` değeri `appsettings.json` yerine user-secrets ya da ortam değişkeninde tutulmalıdır.

## Denemek

`nexora-demo.http` dosyası sabit cevaplı ve canlı veri gerektiren soruların örnek isteklerini içerir. Visual Studio'da
dosyayı açıp her isteğin üstündeki "Send request" bağlantısıyla gönderebilirsiniz.

Ölçüm (uçtan uca test) yapılacaksa önce Nexora sitesinin çalıştığı kontrol edilmelidir
(`http://localhost:5248/api/integration/v1/health`).
