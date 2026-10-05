# Seed dosyaları

Her bot için bu klasöre `{botKey}.json` adıyla bir seed dosyası konur. API açılırken buradaki dosyaları okur:
veritabanında olmayan botu oluşturur ve botun yalnızca boş olan bölümlerini (cevaplar, menü) doldurur.
Veritabanında zaten olan içeriğe dokunmaz; asıl kaynak veritabanıdır.

Örnek dosyalar: `samples/*/Chatbot.Api/Data/Seed/`
