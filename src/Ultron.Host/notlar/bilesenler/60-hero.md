---
title: Hero bölümü (açılış): büyük başlık, alt metin, iki buton, sağda ürün görseli yerine gradyan pano
tags: hero, açılış bölümü, landing page, karşılama, başlık, cta, ana sayfa üstü, gradyan
description: Solda rozet, büyük başlık (vurgulu kelime gradyan), açıklama, "Ücretsiz başla" ve "Demoyu izle" butonları, güven satırı; sağda pano görünümünü taklit eden gradyan kart; mobilde alt alta.
---
```jsx
import React from 'react';

export default function Hero() {
  return (
    <section className="relative overflow-hidden bg-white">
      <div className="pointer-events-none absolute -top-40 right-0 h-96 w-96 rounded-full bg-indigo-200/50 blur-3xl" aria-hidden="true" />
      <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-2 lg:px-8 lg:py-24">
        <div>
          <span className="inline-flex items-center gap-2 rounded-full border border-indigo-100 bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
            <span className="h-1.5 w-1.5 rounded-full bg-indigo-600" aria-hidden="true" /> Yeni: yapay zekâ destekli raporlar
          </span>
          <h1 className="mt-6 text-4xl font-semibold tracking-tight text-slate-900 sm:text-5xl lg:text-6xl">
            Ekibinin işini <span className="bg-gradient-to-r from-indigo-600 to-violet-600 bg-clip-text text-transparent">tek ekranda</span> yönet
          </h1>
          <p className="mt-6 max-w-xl text-lg leading-relaxed text-slate-600">Görevleri, belgeleri ve konuşmaları bir araya getir. Kurulum yok, dakikalar içinde başla.</p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <a href="#" className="rounded-lg bg-indigo-600 px-6 py-3 text-center text-sm font-medium text-white shadow-sm hover:bg-indigo-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2">Ücretsiz başla</a>
            <a href="#" className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-6 py-3 text-sm font-medium text-slate-700 hover:bg-slate-50">
              <svg viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor" aria-hidden="true"><path d="M8 5v14l11-7z" /></svg> Demoyu izle
            </a>
          </div>
          <p className="mt-8 text-sm text-slate-500">4.000’den fazla ekip kullanıyor · Kredi kartı gerekmez</p>
        </div>
        <div className="relative rounded-2xl border border-slate-200 bg-gradient-to-br from-slate-50 to-indigo-50 p-4 shadow-lg" role="img" aria-label="Uygulama panosunun önizlemesi">
          <div className="flex gap-1.5 pb-3" aria-hidden="true"><span className="h-2.5 w-2.5 rounded-full bg-rose-300" /><span className="h-2.5 w-2.5 rounded-full bg-amber-300" /><span className="h-2.5 w-2.5 rounded-full bg-emerald-300" /></div>
          <div className="grid grid-cols-3 gap-3" aria-hidden="true">
            <div className="col-span-3 h-24 rounded-xl bg-gradient-to-r from-indigo-500 to-violet-500" />
            <div className="h-20 rounded-xl bg-white shadow-sm" /><div className="h-20 rounded-xl bg-white shadow-sm" /><div className="h-20 rounded-xl bg-white shadow-sm" />
            <div className="col-span-2 h-28 rounded-xl bg-white shadow-sm" /><div className="h-28 rounded-xl bg-white shadow-sm" />
          </div>
        </div>
      </div>
    </section>
  );
}
```
