---
title: İkinci yasa — ısı makinesi, soğutucu, ısı pompası, Carnot
---

# Termodinamiğin ikinci yasası

## İki ifade
- **Kelvin–Planck:** Tek bir ısıl depodan ısı alıp bunun TAMAMINI işe çeviren çevrimsel bir makine yapılamaz (hiçbir ısı makinesinin verimi %100 olamaz).
- **Clausius:** Isı, dışarıdan iş verilmeden soğuk bir cisimden sıcak bir cisme kendiliğinden geçemez. Soğutucu ve ısı pompası bu yüzden iş (elektrik) harcar.
- İki ifade eşdeğerdir: biri çiğnenirse öteki de çiğnenir.

## Isı makinesi (heat engine)
- Sıcak depodan Q_H alır, W_net iş üretir, soğuk depoya Q_L atar: W_net = Q_H − Q_L.
- Isıl verim: **η = W_net / Q_H = 1 − Q_L / Q_H**.

## Soğutucu ve ısı pompası (refrigerator, heat pump)
- İkisi de AYNI çevrimdir (soğuk ortamdan ısı alıp sıcak ortama atar, iş harcar); fark AMAÇTADIR.
- Soğutucunun amacı soğuk ortamdan ısı çekmek: **COP_R = Q_L / W_net**.
- Isı pompasının amacı sıcak ortama ısı vermek: **COP_HP = Q_H / W_net**.
- Aynı çalışma koşullarında **COP_HP = COP_R + 1**. COP 1'den büyük olabilir (verim değildir).

## Carnot çevrimi ve sınırlar
- Tersinir (ideal) çevrim: iki izotermal + iki izantropik süreç.
- **Carnot verimi: η_C = 1 − T_L / T_H** (sıcaklıklar KELVİN). Ör. T_L = 300 K, T_H = 600 K → η = 1 − 300/600 = 0,50 = %50.
- Aynı depolar arasında hiçbir gerçek makine Carnot veriminden yüksek verime ulaşamaz.
- Carnot soğutucu: COP_R = T_L / (T_H − T_L); Carnot ısı pompası: COP_HP = T_H / (T_H − T_L).
- Verimi artırmak için T_H'yi artırmak ya da T_L'yi düşürmek gerekir.

## Tersinmezlikler (irreversibilities)
- Sürtünme, sonlu sıcaklık farkıyla ısı transferi, serbest genleşme, karışma, kimyasal tepkime, elektrik direnci.
- Gerçek süreçler tersinmezdir; tersinmezlik entropi üretir ve elde edilebilecek işi azaltır.
