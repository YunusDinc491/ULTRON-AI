---
title: Akışkanlar dinamiği — süreklilik, Bernoulli, Reynolds, boru kayıpları, kavitasyon
---

# Akışkanlar dinamiği

## Süreklilik (continuity)
- Sıkıştırılamaz akışta **A₁ V₁ = A₂ V₂** (hacimsel debi sabit); kesit daralınca hız artar.
- Genel: ṁ = ρ A V sabit.
- Dairesel borda A = π D²/4, yani **A ∝ D²**: çap yarıya inerse alan 1/4'e iner, hız **4 katına** çıkar (V₂ = V₁ (D₁/D₂)²). Örnek: 2 m/s, çap yarıya → 8 m/s.

## Bernoulli denklemi
- **P + ½ ρ V² + ρ g z = sabit** (aynı akım çizgisi boyunca); yük biçimi: P/(ρg) + V²/(2g) + z = sabit.
- Varsayımlar: **kararlı akış, sıkıştırılamaz akışkan, sürtünmesiz (viskoz kayıp yok), aynı akım çizgisi, arada pompa/türbin (iş) ve ısı transferi yok.**
- Hız artınca statik basınç düşer (ör. lüle, venturi, uçak kanadı sezgisi).
- Gerçek borularda sürtünme kayıpları eklenir: P₁/ρg + V₁²/2g + z₁ + h_pompa = P₂/ρg + V₂²/2g + z₂ + h_türbin + h_kayıp.

## Reynolds sayısı
- **Re = ρ V D / μ = V D / ν** (μ dinamik, ν kinematik viskozite); atalet kuvvetlerinin viskoz kuvvetlere oranı, boyutsuz.
- Boru akışında yaklaşık: **Re < 2300 laminer**, 2300–4000 geçiş, **Re > 4000 türbülanslı**.
- Laminer akış düzenli katmanlar halinde, türbülanslı akış girdaplı ve karışımlıdır.

## Boru akışında basınç kaybı (Darcy–Weisbach)
- **h_L = f (L/D) (V² / 2g)**, ΔP = f (L/D) (ρ V² / 2).
- **Laminer:** f = 64/Re → ΔP = 32 μ L V / D² (Hagen–Poiseuille): **basınç kaybı hızla DOĞRU ORANTILIDIR**; hız iki katına çıkınca kayıp yaklaşık iki katına çıkar.
- **Türbülanslı:** f, Re ve bağıl pürüzlülüğe (ε/D) bağlıdır (Moody diyagramı, Colebrook); kayıp hızın yaklaşık **1,75–2. kuvvetiyle** artar; hız iki katına çıkınca kayıp yaklaşık 3,4–4 katına çıkar.
- Dirsek, vana, giriş gibi yerel (minor) kayıplar: h = K V²/2g.

## Pompa ve kavitasyon
- Pompa basma yüksekliği (head) h_p, gereken güç Ẇ = ρ g V̇ h_p / η_pompa.
- **Kavitasyon:** pompa girişinde yerel basınç sıvının buhar basıncının altına düşerse sıvı buharlaşır, kabarcıklar oluşur; bu kabarcıklar yüksek basınç bölgesine gelince ani çöker, mikro şoklarla çark yüzeyini aşındırır, gürültü, titreşim ve verim düşüşü yapar.
- Önlem: **NPSH_mevcut > NPSH_gerekli** olmalı (emme hattını kısa ve geniş tutmak, sıvı seviyesini yükseltmek, sıvı sıcaklığını düşürmek).

## Sınır tabaka ve sürükleme
- Sınır tabaka (boundary layer): duvar yakınında viskozitenin etkili olduğu ince bölge (duvarda hız sıfır, kaymama koşulu).
- Akış cismin arkasında ayrılırsa (flow separation) geniş bir iz bölgesi oluşur ve basınç sürüklemesi artar.
- Golf topundaki çukurlar sınır tabakayı türbülanslı yapar; türbülanslı sınır tabaka yüzeye daha uzun tutunur, ayrılma geriye kayar, iz küçülür, sürükleme azalır ve top daha uzağa gider.
