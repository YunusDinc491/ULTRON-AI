---
title: Gaz güç çevrimleri — Otto, Diesel, Brayton, ara soğutma
---

# Gaz güç çevrimleri (soğuk hava standardı varsayımları)

## Otto çevrimi (benzinli motor, buji ateşlemeli)
- 1-2 izantropik sıkıştırma, **2-3 SABİT HACİMDE ısı ekleme** (buji ile ani yanma), 3-4 izantropik genişleme, 4-1 sabit hacimde ısı atma.
- **η_Otto = 1 − 1 / r^(k−1)**, r = V_max/V_min sıkıştırma oranı. r artınca verim artar ama vuruntu (knock) sınırı yüzünden benzinli motorlarda r ≈ 8–12.

## Diesel çevrimi (sıkıştırma ateşlemeli)
- Hava sıkıştırılır, yakıt püskürtülür ve sıcak havada kendiliğinden tutuşur: **2-3 SABİT BASINÇTA ısı ekleme**.
- η_Diesel = 1 − (1 / r^(k−1)) · [(r_c^k − 1) / (k (r_c − 1))], r_c = V₃/V₂ kesme oranı.
- Aynı sıkıştırma oranında Otto daha verimlidir; ama Diesel motorlar çok daha yüksek sıkıştırma oranıyla (r ≈ 14–22) çalışabildiği için pratikte daha verimlidir.

## Brayton çevrimi (gaz türbini)
- İzantropik sıkıştırma (kompresör), sabit basınçta ısı ekleme (yanma odası), izantropik genişleme (türbin), sabit basınçta ısı atma.
- **η_Brayton = 1 − 1 / r_p^((k−1)/k)**, r_p = P₂/P₁ basınç oranı.
- Geri iş oranı (back work ratio) yüksektir: türbin işinin büyük kısmı kompresöre gider.

## Ara soğutma (intercooling) neden işi azaltır?
- Sürekli akışlı sıkıştırma işi **w = ∫ v dP**'dir. Soğuk gazın özgül hacmi küçük olduğu için aynı basınç artışı daha az iş ister.
- Çok kademeli sıkıştırmada kademeler arasında gazı soğutmak süreci izotermal sıkıştırmaya yaklaştırır; en az iş izotermal sıkıştırmada harcanır.
- Benzer şekilde türbinde ara ısıtma (reheat) işi artırır; rejenerasyon (egzoz ısısıyla kompresör çıkışını ısıtmak) verimi artırır.
