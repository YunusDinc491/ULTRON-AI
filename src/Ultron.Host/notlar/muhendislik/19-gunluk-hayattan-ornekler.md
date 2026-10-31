---
title: Günlük hayattan termodinamik, akışkanlar ve ısı transferi örnekleri — neden-sonuç
---

# Günlük hayattan örnekler (neden-sonuç)

Her örnekte önce kısa cevap, sonra fiziksel neden ve ilgili denklem.

## Genel ilkeler (yeni bir olayı açıklarken önce bunlara bak)
- **Gaz hızlı sıkıştırılırsa ısınır, hızlı genleşirse soğur** (yaklaşık adyabatik: verilen iş iç enerjiye döner, T₂/T₁ = (P₂/P₁)^((k−1)/k)). Bisiklet pompasının ucu bu yüzden ısınır; sürtünme küçük katkıdır. Sprey kutusu, gazın genleşmesi ve sıvının buharlaşması (gizli ısı) yüzünden soğur.
- **"Vakum çekmez", basınç farkı iter.** Pipette ağızdaki basınç düşürülür; sıvıyı yukarı iten dışarıdaki atmosfer basıncıdır (bu yüzden emerek su ~10 m'den yukarı çıkarılamaz: P_atm/(ρg) ≈ 10,3 m).
- **Kuvvet = momentum değişimi.** Bir yüzey akışkanı bir yöne saptırıyorsa akışkan yüzeyi ters yöne iter (Newton 3). Uçak kanadı havayı aşağı saptırır (hücum açısı + profil); üst yüzeyde basınç düşük, altta yüksektir. "Üstteki hava aynı anda arkaya varmak için hızlanır" (eşit zaman) açıklaması YANLIŞTIR.
- **Hareketli akış ortamın basıncını düşürür ve havayı sürükler.** Duşta su jeti ve yükselen sıcak hava perde içindeki havayı hareketlendirir, içerideki basınç biraz düşer; dışarıdaki durgun havanın basıncı perdeyi içeri iter.
- **Isı her zaman sıcaktan soğuğa akar; hissedilen, ısı akışının hızıdır** (iletkenlik, taşınım katsayısı h, temas).
- **Faz değişimi büyük enerji taşır:** buharlaşma serinletir (su için h_fg ≈ 2257-2450 kJ/kg), yoğuşma ısı bırakır. Soğuk yüzey havanın çiy noktasının altındaysa üzerinde yoğuşma (buğu, terleme) olur.
- **Basınç kaynama sıcaklığını belirler:** basınç artınca kaynama sıcaklığı artar (düdüklü tencere, basınçlı araç radyatörü ~120 °C), azalınca düşer (yüksek dağ).

## Buzdolabının kapısını açık bırakmak odayı serinletir mi?
- **Hayır, oda ısınır.** Buzdolabı bir soğutma makinesidir: içeriden Q_L çeker, arkadaki kondenserden odaya **Q_H = Q_L + W** atar.
- Kapı açıkken çekilen ve atılan ısı aynı odadadır; net etki odaya giren kompresör işi **W** kadar ısınmadır (enerjinin korunumu, birinci yasa).

## Düdüklü tencerede yemek neden çabuk pişer?
- Kapalı kapta buhar basıncı yükselir (~1 bar gösterge, ~2 bar mutlak). Suyun **doyma (kaynama) sıcaklığı basınçla artar**: 1 atm'de 100 °C, ~2 bar'da ~120 °C.
- Pişme hızı sıcaklığa bağlıdır; 120 °C'de kimyasal reaksiyonlar çok daha hızlıdır. Tersi: yüksek dağda basınç düşük, su ~90 °C'de kaynar, yemek geç pişer.

## Hortumun ucunu parmakla kısınca su neden uzağa fışkırır?
- **Süreklilik:** A₁V₁ = A₂V₂; çıkış kesiti küçülünce çıkış hızı artar.
- Şebeke basıncı yaklaşık sabittir; Bernoulli'ye göre basınç enerjisi kinetik enerjiye (½ρV²) döner. Debi biraz azalsa da çıkış hızı artar, menzil uzar.

## Metal kaşık neden tahta kaşıktan soğuk hissettirir?
- İkisi de **aynı sıcaklıktadır** (oda sıcaklığı). Fark ısıl iletkenliktedir: metal k ≈ 50-400 W/m·K, tahta k ≈ 0,1-0,2 W/m·K.
- Deri sıcaklık değil **ısı akısını** hisseder: metal elden ısıyı hızla çeker (q = −k dT/dx), temas yüzeyi soğur → "soğuk" algısı.

## Kalorifer peteğinin arkasına neden yansıtıcı folyo konur?
- Peteğin arkası dış duvara **ışınımla** ısı verir. Parlak folyonun yayıcılığı (ε) düşük, yansıtıcılığı yüksektir; ışınımı odaya geri yansıtır.
- Duvar daha az ısınır, duvardan dışarıya iletimle giden kayıp azalır (özellikle yalıtımsız dış duvarda).

## Termik santrallerde verim neden ~%35-45?
- **İkinci yasa:** ısının tamamı işe çevrilemez; üst sınır Carnot verimi η = 1 − T_L/T_H. Kazan/türbin sıcaklığı malzeme ile sınırlı (~800-900 K buhar), T_L çevre (~300 K).
- Kalan enerjinin çoğu **kondenserde** soğutma suyuna/soğutma kulesinden atmosfere atılır; bacadan sıcak gaz kaybı ve tersinmezlikler (sürtünme, sonlu sıcaklık farkıyla ısı geçişi) de verimi düşürür.
- Kombine çevrim (gaz türbini + buhar) atık ısıyı kullanarak ~%60'a çıkar.

## Kışın soğuk metal korkuluk / sıcak çay bardağı
- Isı her zaman sıcaktan soğuğa akar (ikinci yasa). Hissedilen sıcaklık, ısı geçiş hızına bağlıdır (iletkenlik ve temas).

## Ter ve rüzgâr neden serinletir?
- Buharlaşma gizli ısı (h_fg ≈ 2400 kJ/kg) çeker; rüzgâr taşınım katsayısı h'yi artırır ve nemli havayı uzaklaştırarak buharlaşmayı hızlandırır. Bağıl nem yüksekse buharlaşma yavaşlar, serinleme azalır.

## Termos neden sıcağı/soğuğu korur?
- Çift cidar arası **vakum**: iletim ve taşınım neredeyse yok; gümüş kaplama ışınımı yansıtır. Üç ısı geçiş yolu da kısılır.
