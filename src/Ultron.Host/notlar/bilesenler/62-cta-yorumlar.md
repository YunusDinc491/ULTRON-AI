---
title: Müşteri yorumları (testimonials) ve harekete geçirici bant (CTA)
tags: testimonials, yorumlar, müşteri görüşleri, referanslar, cta, call to action, kayıt bandı, sosyal kanıt
description: Üç yorum kartı (tırnak işareti, yıldızlar, ad-unvan-baş harf avatarı) ve altında gradyan zeminli "Hemen başla" CTA bandı; mobilde kartlar alt alta.
---
```jsx
import React from 'react';

const reviews = [
  { text: 'Haftalık toplantılarımızı yarıya indirdik. Herkes neyin nerede olduğunu biliyor.', name: 'Selin Arslan', role: 'Ürün müdürü, Kargo.io' },
  { text: 'Kurulum gerçekten beş dakika sürdü. Destek ekibi de çok hızlı.', name: 'Burak Öztürk', role: 'Kurucu, Atölye 34' },
  { text: 'Raporlar sayesinde müşterilerimize her hafta net bir tablo sunuyoruz.', name: 'Ece Şahin', role: 'Ajans sahibi' },
];

export default function TestimonialsCta() {
  return (
    <section className="bg-slate-50 px-4 py-16 sm:px-6">
      <h2 className="text-center text-3xl font-semibold tracking-tight text-slate-900">Ekipler ne diyor?</h2>
      <div className="mx-auto mt-10 grid max-w-6xl gap-6 md:grid-cols-3">
        {reviews.map((r) => (
          <figure key={r.name} className="flex flex-col rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <div className="flex gap-0.5 text-amber-400" role="img" aria-label="5 üzerinden 5 yıldız">
              {[0, 1, 2, 3, 4].map((i) => <svg key={i} viewBox="0 0 20 20" className="h-4 w-4" fill="currentColor" aria-hidden="true"><path d="M10 2l2.4 5 5.6.6-4.2 3.7 1.2 5.5L10 14l-5 2.8 1.2-5.5L2 7.6 7.6 7z" /></svg>)}
            </div>
            <blockquote className="mt-4 flex-1 text-slate-700">“{r.text}”</blockquote>
            <figcaption className="mt-6 flex items-center gap-3">
              <span className="grid h-10 w-10 place-items-center rounded-full bg-indigo-100 text-sm font-semibold text-indigo-700">{r.name.split(' ').map((w) => w[0]).join('')}</span>
              <span><span className="block text-sm font-semibold text-slate-900">{r.name}</span><span className="block text-xs text-slate-500">{r.role}</span></span>
            </figcaption>
          </figure>
        ))}
      </div>
      <div className="mx-auto mt-14 flex max-w-6xl flex-col items-center justify-between gap-6 rounded-2xl bg-gradient-to-r from-indigo-600 to-violet-600 px-6 py-10 text-center shadow-lg sm:px-10 lg:flex-row lg:text-left">
        <div>
          <h3 className="text-2xl font-semibold tracking-tight text-white">Bugün denemeye başla</h3>
          <p className="mt-2 text-indigo-100">14 gün ücretsiz, istediğin zaman iptal et.</p>
        </div>
        <a href="#" className="rounded-lg bg-white px-6 py-3 text-sm font-semibold text-indigo-700 shadow-sm hover:bg-indigo-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white focus-visible:ring-offset-2 focus-visible:ring-offset-indigo-600">Hemen başla</a>
      </div>
    </section>
  );
}
```
