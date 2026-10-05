# Eğitim verileri

Her bot için bu klasörde `{botKey}` adlı bir alt klasör açılır:

```
Data/{botKey}/training.tsv     modelin eğitildiği cümleler
Data/{botKey}/regression.tsv   eğitimde kullanılmayan, ölçüm için ayrılmış cümleler
```

Dosyalar UTF-8'dir; ilk satır başlıktır (`Text<TAB>Label`), sonraki her satır `Metin<TAB>Intent` biçimindedir.
Aynı cümle iki intent'e atanırsa, eğitimde yinelenen cümle olursa ya da bir regression cümlesi eğitim setinde de
bulunursa Trainer eğitimi başlatmaz.

Örnek veri setleri: `samples/*/Chatbot.Trainer/Data/`
