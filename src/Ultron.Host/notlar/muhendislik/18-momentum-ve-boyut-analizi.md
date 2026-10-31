---
title: Momentum denklemi, boyut analizi ve boyutsuz sayılar
---

# Momentum denklemi (kontrol hacmi)

## Doğrusal momentum
- Kararlı akışta: **ΣF = Σ ṁ V_çıkış − Σ ṁ V_giriş** (vektörel; her yön ayrı yazılır).
- Kuvvetler: basınç kuvvetleri (P A, gösterge basıncı kullanılabilir), ağırlık, mesnet/bağlantı kuvvetleri.
- Uygulamalar: jetin bir plakaya çarpma kuvveti (F = ṁ V, dik çarpma), dirsekteki akışkanın boruya uyguladığı kuvvet, roket itkisi.
- Momentum düzeltme katsayısı β: düzgün olmayan hız profilinde (laminer boruda β = 4/3, türbülanslıda ≈ 1).

## Açısal momentum
- ΣM = Σ r × ṁ V (çıkış) − Σ r × ṁ V (giriş); pompa ve türbin çarklarında Euler türbin denklemi: Ẇ = ṁ ω (r₂ V_t2 − r₁ V_t1).

# Boyut analizi (dimensional analysis)

## Buckingham Pi teoremi
- n değişken ve k temel boyut (M, L, T, Θ) varsa problem **n − k** boyutsuz Pi grubuyla ifade edilir.
- Amaç: deney sayısını azaltmak ve model–prototip benzerliği kurmak.

## Benzerlik (similarity)
- Geometrik (şekil), kinematik (hız oranları) ve dinamik (kuvvet oranları) benzerlik; dinamik benzerlik için ilgili boyutsuz sayılar model ve prototipte eşit olmalı.

## Sık kullanılan boyutsuz sayılar
- **Reynolds:** Re = ρ V L / μ — atalet / viskoz kuvvet (boru akışı, sürükleme).
- **Froude:** Fr = V / √(g L) — atalet / yerçekimi (serbest yüzeyli akış, gemiler, açık kanal).
- **Mach:** Ma = V / c — sıkıştırılabilirlik.
- **Euler / basınç katsayısı:** C_p = ΔP / (½ ρ V²).
- **Weber:** We = ρ V² L / σ — atalet / yüzey gerilimi (damlalar).
- **Prandtl (ısı transferi):** Pr = ν / α — momentum / ısıl yayınım. **Nusselt:** Nu = h L / k — taşınım / iletim.
- Sürükleme kuvveti: F_D = C_D (½ ρ V²) A.
