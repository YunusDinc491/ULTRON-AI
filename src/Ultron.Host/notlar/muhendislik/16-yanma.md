---
title: Yanma — stokiyometri, hava-yakıt oranı, ısıl değerler, adyabatik alev sıcaklığı
---

# Yanma (combustion)

## Yanma denklemi ve teorik hava
- Yakıtın oksijenle kimyasal tepkimesi; kütle (atomlar) korunur, mol sayısı korunmayabilir.
- Hava ≈ %21 O₂ + %79 N₂ (mol): her 1 mol O₂ ile **3,76 mol N₂** gelir. N₂ genellikle tepkimeye girmez ama ısı taşır.
- **Teorik (stokiyometrik) hava:** yakıtı TAM yakmaya yetecek en az hava; ürünlerde serbest O₂ yoktur.
- Örnek, metan: CH₄ + 2 (O₂ + 3,76 N₂) → CO₂ + 2 H₂O + 7,52 N₂.
- **Fazla hava (excess air):** teorik havadan fazlası; tam yanmayı garantiler, alev sıcaklığını düşürür. %150 teorik hava = %50 fazla hava.
- Eksik havada CO ve yanmamış yakıt oluşur (eksik yanma).

## Hava-yakıt oranı
- **AF = m_hava / m_yakıt** (kütle bazında); mol bazında N_hava/N_yakıt × (M_hava/M_yakıt). M_hava ≈ 28,97 kg/kmol.
- Benzin için stokiyometrik AF ≈ 14,7.

## Oluşum entalpisi ve ısıl değer
- Standart referans hal 25 °C, 1 atm; kararlı elementlerin (O₂, N₂, C) oluşum entalpisi sıfırdır.
- Yanma entalpisi: h_R = H_ürünler − H_girenler (negatif, ısı açığa çıkar).
- **Isıl değer (heating value)** = −h_R (yakıt başına açığa çıkan ısı).
  - **Üst ısıl değer (HHV):** ürünlerdeki su SIVI.
  - **Alt ısıl değer (LHV):** ürünlerdeki su BUHAR. HHV − LHV = m_su h_fg.
- Açık sistem enerji dengesi: Q = Σ N_ürün (h°_f + h − h°)_ürün − Σ N_giren (h°_f + h − h°)_giren.

## Adyabatik alev sıcaklığı
- Isı kaybı ve iş yokken ürünlerin ulaşacağı en yüksek sıcaklık: H_ürünler = H_girenler.
- Teorik havayla ve tam yanmada en yüksektir; fazla hava ve eksik yanma düşürür. Malzeme sınırları yüzünden gaz türbinlerinde bilerek fazla hava kullanılır.
