---
title: Entropi ve izantropik verim
---

# Entropi (entropy)

## Tanım
- **dS = (δQ / T)_tersinir**; birimi kJ/K, özgül entropi s: kJ/kg·K.
- Entropi bir hal özelliğidir; iki hal arasındaki değişimi yoldan bağımsızdır, ama hesap için tersinir bir yol düşünülür.
- **Entropi artışı ilkesi:** yalıtılmış bir sistemde S_üretim ≥ 0; tersinir süreçte 0, gerçek süreçlerde > 0. Entropi dengesi: ΔS_sistem = Σ Q/T + S_üretim.

## "Düzensizlik" benzetmesi ve sınırı
- İstatistiksel tanım: **S = k ln W** (W: sistemin aynı makro hale karşılık gelen mikro hal sayısı). Daha çok olası mikro hal = daha yüksek entropi; bu yüzden "düzensizlik" denir.
- Benzetme yanıltıcı olabilir: entropi gözle görülen "dağınıklık" değil, enerjinin ne kadar dağılmış ve işe dönüştürülemez olduğunun ölçüsüdür. Ör. bazı kendiliğinden kristalleşmelerde görünür düzen artar ama çevreye atılan ısıyla toplam entropi yine artar.

## İzantropik süreç
- Tersinir ve adyabatik süreç **izantropiktir** (s₁ = s₂).
- İdeal gaz, sabit özgül ısılar: T₂/T₁ = (P₂/P₁)^((k−1)/k) = (v₁/v₂)^(k−1).
- İdeal gaz entropi değişimi: Δs = c_p ln(T₂/T₁) − R ln(P₂/P₁).
- Sıkıştırılamaz madde: Δs = c ln(T₂/T₁).

## İzantropik verim (isentropic efficiency)
Gerçek cihazı aynı giriş hali ve aynı çıkış basıncındaki ideal (izantropik) cihazla karşılaştırır; 2s izantropik çıkış, 2a gerçek çıkış:
- **Türbin:** η_T = gerçek iş / izantropik iş = **(h₁ − h₂a) / (h₁ − h₂s)** (gerçek türbin daha AZ iş üretir, η < 1).
- **Kompresör ve pompa:** η_C = izantropik iş / gerçek iş = **(h₂s − h₁) / (h₂a − h₁)** (gerçek kompresör daha ÇOK iş ister). Oran türbinin TERSİDİR.
- **Lüle:** η_N = gerçek çıkış kinetik enerjisi / izantropik çıkış kinetik enerjisi.
- Tipik değerler: büyük türbin ~%85-90, kompresör ~%80-88.
