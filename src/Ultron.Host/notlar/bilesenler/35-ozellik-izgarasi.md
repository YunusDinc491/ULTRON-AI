---
title: Özellikler bölümü (ikonlu özellik ızgarası)
tags: features, özellikler, özellik ızgarası, ikonlu kutular, landing page bölümü, neden biz
description: Başlık ve alt başlığın altında 3 sütunlu (mobilde tek) ikonlu özellik kutuları: renkli ikon kutusu, başlık ve kısa açıklama.
---
```jsx
import React from 'react';

const features = [
  { title: 'Işık hızında', desc: 'Sayfalar 1 saniyenin altında açılır.', d: 'M13 3 4 14h7l-1 7 9-11h-7l1-7z' },
  { title: 'Güvenli', desc: 'Uçtan uca şifreleme ve iki aşamalı giriş.', d: 'M12 3l7 3v6c0 4.5-3 7.5-7 9-4-1.5-7-4.5-7-9V6l7-3z' },
  { title: 'Ekip dostu', desc: 'Gerçek zamanlı ortak çalışma ve yorumlar.', d: 'M17 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M10 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM21 20v-2a4 4 0 0 0-3-3.9M16 2.1a4 4 0 0 1 0 7.8' },
  { title: 'Akıllı raporlar', desc: 'Önemli değişiklikler sana kendiliğinden gelir.', d: 'M4 20V10m6 10V4m6 16v-7m4 7H2' },
  { title: 'Her cihazda', desc: 'Telefon, tablet ve masaüstünde aynı deneyim.', d: 'M7 3h10a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zM11 18h2' },
  { title: '7/24 destek', desc: 'Türkçe destek ekibi her zaman yanında.', d: 'M4 13a8 8 0 1 1 16 0v4a2 2 0 0 1-2 2h-1v-6h3M4 13v4a2 2 0 0 0 2 2h1v-6H4' },
];

export default function FeatureGrid() {
  return (
    <section className="bg-white px-4 py-16 sm:px-6">
      <div className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold text-indigo-600">Neden Ultron?</p>
        <h2 className="mt-2 text-3xl font-semibold tracking-tight text-slate-900">İhtiyacın olan her şey, tek yerde</h2>
        <p className="mt-4 text-slate-600">Kurulum gerektirmez; dakikalar içinde ekibinle çalışmaya başla.</p>
      </div>
      <div className="mx-auto mt-12 grid max-w-6xl gap-x-8 gap-y-10 sm:grid-cols-2 lg:grid-cols-3">
        {features.map((f) => (
          <div key={f.title} className="flex gap-4">
            <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-indigo-50 text-indigo-600">
              <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={f.d} /></svg>
            </span>
            <div>
              <h3 className="font-semibold text-slate-900">{f.title}</h3>
              <p className="mt-1 text-sm leading-relaxed text-slate-600">{f.desc}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
```
