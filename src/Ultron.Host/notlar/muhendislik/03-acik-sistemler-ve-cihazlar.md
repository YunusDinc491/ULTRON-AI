---
title: Açık sistemler — kararlı akış enerji denklemi ve cihazlar
---

# Açık sistemler (kontrol hacmi) ve mühendislik cihazları

## Kütlenin korunumu (conservation of mass)
- Kütle debisi: **ṁ = ρ A V** (kg/s); hacimsel debi: **V̇ = A V** (m³/s), ṁ = ρ V̇.
- Kararlı akışta giren toplam ṁ = çıkan toplam ṁ.
- Boru kesit alanı A = π D²/4 (ör. D = 0,1 m → A ≈ 0,00785 m²; V = 2 m/s → V̇ ≈ 0,0157 m³/s).

## Kararlı akış enerji denklemi (steady-flow energy equation)
**Q̇ − Ẇ = ṁ [ (h₂ − h₁) + (V₂² − V₁²)/2 + g (z₂ − z₁) ]** (tek giriş, tek çıkış).
Varsayımlar: kararlı akış (kontrol hacminde birikim yok), özellikler giriş ve çıkışta düzgün.

## Lüle ve difüzör (nozzle, diffuser)
- Lüle akışkanı hızlandırır (basınç düşer), difüzör yavaşlatır (basınç artar).
- Genellikle Q̇ ≈ 0, Ẇ = 0, Δz ≈ 0 → **h₁ + V₁²/2 = h₂ + V₂²/2**.
- Bu, Bernoulli denklemi DEĞİLDİR: entalpiyi içerir, sıkıştırılabilir akış ve gazlar için de geçerlidir.

## Türbin, kompresör, pompa (turbine, compressor, pump)
- Türbin akışkandan iş alır; kompresör (gaz) ve pompa (sıvı) iş harcar.
- Genelde ΔKE, ΔPE ihmal, iyi yalıtılmışsa Q̇ ≈ 0 → **Ẇ = ṁ (h₁ − h₂)** (türbinde pozitif çıkış).
- Pompa işi (sıkıştırılamaz sıvı, tersinir): w ≈ v (P₂ − P₁).

## Kısılma vanası (throttling valve)
- Akışkanın basıncını düşürür; iş yok, ısı ihmal, ΔKE ihmal → **h₁ = h₂ (izentalpik)**.
- Soğutma çevrimindeki genleşme vanası budur; sıvı kısılınca bir kısmı buharlaşır ve sıcaklık düşer.

## Karışma odası ve ısı değiştirici (enerji dengesi)
- Karışma odası: Σ ṁ_giriş h_giriş = Σ ṁ_çıkış h_çıkış (Q̇, Ẇ ≈ 0).
- Isı değiştirici: sıcak akışkanın verdiği ısı = soğuk akışkanın aldığı ısı (dış kayıp yoksa): ṁ_s c_s ΔT_s = ṁ_c c_c ΔT_c.

## Çözüm adımları
1. Kontrol hacmini çiz, giriş/çıkışları işaretle.
2. Varsayımları yaz (kararlı, ΔKE ≈ 0, adyabatik…).
3. Kütle ve enerji denklemini sadeleştir, tablodan h değerlerini bul, birimleri kontrol et.
