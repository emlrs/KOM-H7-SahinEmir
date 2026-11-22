# KOM-H7-SahinEmir — Üç Mekanik, Dört Oyun

Karmaşık Oyun Mekanikleri · Hafta 7 ödevi · Emir Şahin (20246000035)

Tek Unity projesinde dört ayrı sahne (dört ayrı oyun) vardır. Unity sürümü **6000.6.9f1** (URP 2D, Input System).

| Sahne | Oyun | Mekanik |
|---|---|---|
| `Assets/Scenes/A_TekTus.unity` | A · Tek Tuş — Son Savunma | tek girdi, zamanlama (kısa bas / basılı tut) |
| `Assets/Scenes/B_HareketHissi.unity` | B · Hareket Hissi — Lav Tırmanışı | ivme, fren, değişken zıplama, coyote time, tampon, iki profil |
| `Assets/Scenes/C_Carpisma.unity` | C · Çarpışma — Enerji Topu | sekme, yönlü parry, iki vuruşluk plaka, trigger alıcı |
| `Assets/Scenes/D_Birlesik.unity` | D · Birleşik Oyun — Kül Kulesi | üç mekaniğin bağlı kullanımı |

## Sahneler nasıl açılır

1. Unity Hub → **Add project from disk** → bu klasörü seçin (Unity 6000.6.9f1).
2. Project penceresinde `Assets/Scenes/` altındaki sahneyi çift tıklayın.
3. **Play**'e basın ve Game penceresine tıklayıp odaklayın. Game penceresinde 16:9 oranı önerilir.

A, B ve C için build gerekmez; Unity'de sahne açılıp Play'e basıldığında çalışır.

**D'nin Windows build'i:** `Build/D_Birlesik/D_Birlesik.exe`. Exe, yanındaki `D_Birlesik_Data`, `MonoBleedingEdge`, `D3D12` klasörleri ve DLL'lerle birlikte durmalıdır.

## Kontroller

### A · Tek Tuş

| Girdi | İşlev |
|---|---|
| **E** | Başlat |
| **E kısa bas-bırak** | Füzeyi parry et (bırakışta) |
| **E basılı tut** | Gard: lazeri engelle |
| **E** (sonuç ekranında, tuşu bırakıp yeniden bas) | Yeniden başlat |

Füzeyi gard durdurmaz, lazeri parry durdurmaz; uzun gardı bırakmak parry sayılmaz. Hedef 90 saniye hayatta kalmak; tek savunmasız isabet öldürür.

### B · Hareket Hissi

| Girdi | İşlev |
|---|---|
| **A / D** veya **sol / sağ ok** | İvmelenerek hareket, bırakınca frenleme |
| **Space** | Başlat; zıplama (kısa basış alçak, tutuş yüksek) |
| **E** | Akış ↔ Denge hareket profili |
| **R** | Ölüm veya başarı ekranında yeniden başlat |

Yükselen lava değmeden iki alt valfi açıp tepedeki çıkışa ulaşın.

### C · Çarpışma

| Girdi | İşlev |
|---|---|
| **A / D** veya **sol / sağ ok** | Hareket ve bakış yönü |
| **E kısa bas-bırak** | Başlat; bakılan tarafa parry |
| **R** | Ölüm veya başarı ekranında yeniden başlat |

Top hangi taraftan geliyorsa o tarafa bakıp parry yapın. Geri sayım bitince alıcı açılır; topu alıcıya gönderip açılan kapıdan çıkın. Normal plaka bir, zırhlı plaka iki darbede kırılır.

### D · Birleşik Oyun (Kül Kulesi)

| Girdi | İşlev |
|---|---|
| **Space** | Başlat (ilk basış yalnız başlatır); sonra değişken yükseklikte zıplama |
| **A / D** veya **sol / sağ ok** | İvmelenme, frenleme, bakış yönü |
| **E kısa bas-bırak** | Bakılan yöne parry (füze ve enerji topu) |
| **Durduktan sonra E basılı tut** | Gard (lazer); gard sırasında yürüme ve zıplama kilitlidir |
| **Gard sırasında A / D** | Yerinde yön değiştirme |
| **R** (sonuç ekranında) | Koşuyu baştan başlat |

Yükselen lavdan kaçarak tırmanın. Füzeyi ve enerji topunu parry edin, lazere karşı durup gard alın. Topu parry etmek 5 saniyelik saldırı kesintisi açar. Topu tepedeki enerji alıcısına gönderince kapı açılır.

## Klasörler

```
Assets/Scenes/          A_TekTus, B_HareketHissi, C_Carpisma, D_Birlesik
Assets/Scripts/A B C D/ her oyunun kodu (Editor/ alt klasörleri yalnız editör doğrulama araçlarıdır, build'e girmez)
Assets/Prefabs/C, D/    çoğaltılan plaka, füze, lazer, top ve oyuncu prefabları
Assets/Art/A B C D/     sade 2D şekil sprite'ları ve materyaller
Build/D_Birlesik/       D'nin Windows build'i
Belge/H7_TasarimBelgesi.pdf   tasarım belgesi
Belge/oynanis_D.mp4           D oynanış videosu (60 sn)
```

## Not

A, B, C ve D önce iki ayrı Unity projesinde geliştirildi, teslim için bu tek projede birleştirildi. Script GUID'leri korunduğu için sahne bağlantıları değişmedi.
