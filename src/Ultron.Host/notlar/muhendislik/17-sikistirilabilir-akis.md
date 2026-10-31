---
title: Sıkıştırılabilir akış — Mach sayısı, durma özellikleri, lülelerde boğulma, şok
---

# Sıkıştırılabilir akış (compressible flow)

## Ses hızı ve Mach sayısı
- İdeal gazda ses hızı: **c = √(k R T)** (T Kelvin). 20 °C havada c ≈ 343 m/s.
- **Mach sayısı: Ma = V / c.** Ma < 1 ses altı (subsonik), Ma = 1 sonik, Ma > 1 ses üstü (süpersonik), Ma > 5 hipersonik.
- Ma < ~0,3 olan gaz akışları yaklaşık sıkıştırılamaz kabul edilir.

## Durma (stagnation) özellikleri
- Akış izantropik olarak durdurulsaydı ulaşılacak hal: **h₀ = h + V²/2**; ideal gaz: T₀ = T + V²/(2 c_p).
- T₀/T = 1 + ((k − 1)/2) Ma²; P₀/P = (T₀/T)^(k/(k−1)).
- Adyabatik akışta (iş yokken) h₀ ve T₀ sabit kalır.

## Değişken kesitli izantropik akış (lüle ve difüzör)
- **Ses altı akışta** kesit daralınca hız ARTAR, basınç düşer (yakınsak lüle hızlandırır).
- **Ses üstü akışta** tersine: kesit GENİŞLEYİNCE hız artar.
- Ses üstü hıza çıkmak için **yakınsak–ıraksak lüle (de Laval)** gerekir; boğazda Ma = 1 olur.
- **Boğulma (choking):** boğazda Ma = 1'e ulaşınca, çıkış basıncı daha da düşürülse bile kütle debisi artmaz (maksimum debi). Kritik basınç oranı hava için P*/P₀ ≈ 0,528.

## Normal şok (normal shock)
- Ses üstü akışta ani, tersinmez sıçrama: şoktan sonra akış ses altı olur, basınç, sıcaklık ve yoğunluk artar, hız düşer.
- Şok boyunca durma sıcaklığı T₀ sabittir ama **durma basıncı P₀ düşer** ve entropi artar (tersinmezlik).
