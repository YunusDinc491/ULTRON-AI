---
title: Buhar güç çevrimi (Rankine) ve buhar sıkıştırmalı soğutma çevrimi
---

# Rankine çevrimi (buhar santrali)

## Dört süreç (ideal)
1-2 pompada izantropik sıkıştırma (doymuş sıvı → sıkıştırılmış sıvı), 2-3 kazanda sabit basınçta ısı ekleme (kızgın buhara kadar), 3-4 türbinde izantropik genişleme, 4-1 kondenserde sabit basınçta ısı atma (doymuş sıvıya kadar).
- Verim: η = (w_türbin − w_pompa) / q_giren; w_türbin = h₃ − h₄, w_pompa ≈ v₁ (P₂ − P₁), q_giren = h₃ − h₂.
- Pompa işi türbin işinin yanında çok küçüktür (sıvının özgül hacmi küçük).

## Verimi artırma yolları
- **Kondenser basıncını düşürmek:** ısı daha düşük doyma sıcaklığında atılır, ortalama ısı atma sıcaklığı düşer → verim ve net iş artar. Bedeli: türbin çıkışında buharın **nemi artar** (kanat erozyonu), daha derin vakum gerekir, hava sızıntısı riski artar.
- **Kazan basıncını artırmak:** ortalama ısı ekleme sıcaklığı artar → verim artar; bedeli yine türbin çıkışında nem artışı.
- **Kızdırma (superheating):** buharı daha yüksek sıcaklığa ısıtmak verimi artırır ve çıkış nemini azaltır (malzeme sıcaklık sınırı var).
- **Ara ısıtma (reheat):** buharı türbinin ortasında yeniden ısıtmak nemi azaltır.
- **Rejenerasyon (besleme suyu ısıtıcıları):** türbinden ara buhar çekip besleme suyunu ısıtmak verimi artırır.

## Carnot neden pratik değil?
Islak (iki fazlı) karışımı pompayla sıkıştırmak çok zordur, türbinde çok nemli buhar kanatları aşındırır ve izotermal ısı ekleme kritik noktanın altındaki doyma bölgesiyle sınırlı kalır. Bu yüzden buharı tamamen yoğuşturup sıvıyı pompalayan Rankine çevrimi kullanılır.

# Buhar sıkıştırmalı soğutma çevrimi
- 1-2 kompresörde sıkıştırma (doymuş buhar → kızgın buhar), 2-3 kondenserde ısı atma (sıcak ortama), 3-4 **kısılma vanasında** genleşme (h₃ = h₄, sıcaklık düşer), 4-1 evaporatörde ısı alma (soğuk ortamdan).
- COP_R = (h₁ − h₄) / (h₂ − h₁); ısı pompası olarak COP_HP = (h₂ − h₃) / (h₂ − h₁).
- Kondenserin ısı atması zorlaşırsa (ör. bobinler tozlanırsa ya da ortam sıcaksa) yoğuşma sıcaklığı ve basıncı yükselir, kompresör işi artar, COP düşer, enerji tüketimi artar.
- Buzdolabı odaya, içeriden aldığı ısı + kompresör işi kadar ısı verir; kapısı açık bırakılırsa oda soğumaz, ısınır.
