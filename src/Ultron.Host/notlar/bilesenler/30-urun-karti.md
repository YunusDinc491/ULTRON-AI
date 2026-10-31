---
title: Ürün kartı ızgarası (e-ticaret): görsel alanı, indirim rozeti, yıldız, fiyat, sepete ekle
tags: card, kart, ürün kartı, e-ticaret, product card, fiyat, indirim, sepete ekle, ızgara, grid
description: Duyarlı ızgarada (1-2-3 sütun) ürün kartları; görsel yerine renkli gradyan alan, indirim rozeti, puan, eski/yeni fiyat ve "Sepete ekle" butonu (eklenince "Eklendi ✓").
---
```jsx
import React, { useState } from 'react';

const products = [
  { id: 1, name: 'Kablosuz Kulaklık', price: 1899, old: 2399, rating: 4.6, color: 'from-sky-200 to-indigo-300' },
  { id: 2, name: 'Akıllı Saat', price: 3499, rating: 4.3, color: 'from-amber-200 to-rose-300' },
  { id: 3, name: 'Taşınabilir Hoparlör', price: 1299, old: 1599, rating: 4.8, color: 'from-emerald-200 to-teal-300' },
];
const tl = (n) => n.toLocaleString('tr-TR') + ' ₺';

export default function ProductGrid() {
  const [added, setAdded] = useState([]);
  return (
    <div className="mx-auto grid max-w-6xl gap-6 p-6 sm:grid-cols-2 lg:grid-cols-3">
      {products.map((p) => {
        const inCart = added.includes(p.id);
        return (
          <article key={p.id} className="group flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition duration-200 hover:shadow-md">
            <div className={`relative aspect-[4/3] bg-gradient-to-br ${p.color}`} role="img" aria-label={`${p.name} görseli`}>
              {p.old && <span className="absolute left-3 top-3 rounded-full bg-rose-600 px-2.5 py-1 text-xs font-semibold text-white">%{Math.round((1 - p.price / p.old) * 100)} indirim</span>}
            </div>
            <div className="flex flex-1 flex-col gap-2 p-4">
              <h3 className="font-semibold text-slate-900">{p.name}</h3>
              <p className="flex items-center gap-1 text-sm text-slate-500">
                <svg viewBox="0 0 20 20" className="h-4 w-4 text-amber-400" fill="currentColor" aria-hidden="true"><path d="M10 2l2.4 5 5.6.6-4.2 3.7 1.2 5.5L10 14l-5 2.8 1.2-5.5L2 7.6 7.6 7z" /></svg>
                {p.rating} puan
              </p>
              <p className="mt-auto flex items-baseline gap-2">
                <span className="text-lg font-semibold text-slate-900">{tl(p.price)}</span>
                {p.old && <span className="text-sm text-slate-400 line-through">{tl(p.old)}</span>}
              </p>
              <button
                type="button"
                onClick={() => setAdded(inCart ? added.filter((x) => x !== p.id) : [...added, p.id])}
                className={`mt-2 rounded-lg px-4 py-2 text-sm font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 ${inCart ? 'bg-emerald-50 text-emerald-700' : 'bg-indigo-600 text-white hover:bg-indigo-500'}`}
              >
                {inCart ? 'Eklendi ✓' : 'Sepete ekle'}
              </button>
            </div>
          </article>
        );
      })}
    </div>
  );
}
```
