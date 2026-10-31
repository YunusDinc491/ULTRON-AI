---
title: Fiyatlandırma kartları · üç plan, ortadaki öne çıkan, aylık/yıllık geçişi
tags: pricing, fiyat tablosu, fiyatlandırma, plan kartları, abonelik, en popüler, aylık yıllık, özellik listesi
description: Üç plan kartı (Başlangıç, Pro, Kurumsal); ortadaki "En popüler" rozetli, renkli çerçeveli ve gölgeli; tik ikonlu özellik listesi; üstte aylık/yıllık seçici.
---
```jsx
import React, { useState } from 'react';

const plans = [
  { name: 'Başlangıç', monthly: 0, desc: 'Kişisel projeler için', features: ['3 proje', 'Temel analiz', 'Topluluk desteği'] },
  { name: 'Pro', monthly: 249, desc: 'Büyüyen ekipler için', features: ['Sınırsız proje', 'Gelişmiş analiz', 'Öncelikli destek', 'Özel alan adı'], featured: true },
  { name: 'Kurumsal', monthly: 799, desc: 'Büyük kuruluşlar için', features: ['Her şey Pro’da', 'Tek oturum açma (SSO)', 'Sözleşmeli SLA', 'Özel yönetici'] },
];

export default function Pricing() {
  const [yearly, setYearly] = useState(false);
  return (
    <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <div className="text-center">
        <h2 className="text-3xl font-semibold tracking-tight text-slate-900">Sana uygun planı seç</h2>
        <div role="radiogroup" aria-label="Ödeme dönemi" className="mt-6 inline-flex rounded-full bg-slate-100 p-1">
          {[['Aylık', false], ['Yıllık · %20 indirim', true]].map(([label, value]) => (
            <button key={label} type="button" role="radio" aria-checked={yearly === value} onClick={() => setYearly(value)}
              className={`rounded-full px-4 py-1.5 text-sm font-medium transition duration-200 ${yearly === value ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500'}`}>{label}</button>
          ))}
        </div>
      </div>
      <div className="mt-10 grid gap-6 lg:grid-cols-3 lg:items-center">
        {plans.map((p) => {
          const price = yearly ? Math.round(p.monthly * 0.8) : p.monthly;
          return (
            <div key={p.name} className={`relative rounded-2xl border bg-white p-6 ${p.featured ? 'border-indigo-600 shadow-lg lg:py-10' : 'border-slate-200 shadow-sm'}`}>
              {p.featured && <span className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-indigo-600 px-3 py-1 text-xs font-semibold text-white">En popüler</span>}
              <h3 className="text-lg font-semibold text-slate-900">{p.name}</h3>
              <p className="mt-1 text-sm text-slate-500">{p.desc}</p>
              <p className="mt-6"><span className="text-4xl font-semibold tracking-tight text-slate-900">{price === 0 ? 'Ücretsiz' : `${price} ₺`}</span>{price > 0 && <span className="text-sm text-slate-500"> / ay</span>}</p>
              <ul className="mt-6 space-y-3">
                {p.features.map((f) => (
                  <li key={f} className="flex gap-3 text-sm text-slate-600">
                    <svg viewBox="0 0 20 20" className="h-5 w-5 shrink-0 text-indigo-600" fill="currentColor" aria-hidden="true"><path fillRule="evenodd" d="M16.7 5.3a1 1 0 0 1 0 1.4l-8 8a1 1 0 0 1-1.4 0l-4-4a1 1 0 1 1 1.4-1.4L8 12.6l7.3-7.3a1 1 0 0 1 1.4 0z" clipRule="evenodd" /></svg>
                    {f}
                  </li>
                ))}
              </ul>
              <a href="#" className={`mt-8 block rounded-lg px-4 py-2.5 text-center text-sm font-medium transition duration-200 ${p.featured ? 'bg-indigo-600 text-white hover:bg-indigo-500' : 'border border-slate-200 text-slate-700 hover:bg-slate-50'}`}>
                {p.monthly === 0 ? 'Ücretsiz başla' : 'Planı seç'}
              </a>
            </div>
          );
        })}
      </div>
    </section>
  );
}
```
