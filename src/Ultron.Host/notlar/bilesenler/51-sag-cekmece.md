---
title: Sağdan açılan panel (drawer/sheet): sepet ya da filtreler
tags: drawer, sheet, sağ panel, yan panel, sepet paneli, filtre paneli, slide over, kayan pencere
description: "Sepeti aç" ile sağdan kayan panel; başlık, ürün listesi (adet artır/azalt), altta toplam ve ödeme butonu; karartma, kapat düğmesi ve Esc ile kapanır.
---
```jsx
import React, { useEffect, useState } from 'react';

export default function CartDrawer() {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState([
    { id: 1, name: 'Seramik kupa', price: 249, qty: 2 },
    { id: 2, name: 'Keten masa örtüsü', price: 699, qty: 1 },
  ]);
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);
  const change = (id, d) => setItems(items.map((i) => (i.id === id ? { ...i, qty: Math.max(1, i.qty + d) } : i)));
  const total = items.reduce((s, i) => s + i.price * i.qty, 0);
  return (
    <div className="min-h-[460px] p-6">
      <button type="button" onClick={() => setOpen(true)} aria-expanded={open} aria-controls="sepet" className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800">Sepeti aç ({items.length})</button>
      <div className={`fixed inset-0 z-50 ${open ? '' : 'pointer-events-none'}`} aria-hidden={!open}>
        <div className={`absolute inset-0 bg-slate-900/40 transition-opacity duration-300 ${open ? 'opacity-100' : 'opacity-0'}`} onClick={() => setOpen(false)} />
        <aside id="sepet" role="dialog" aria-modal="true" aria-labelledby="sepet-baslik"
          className={`absolute inset-y-0 right-0 flex w-full max-w-sm flex-col bg-white shadow-lg transition-transform duration-300 ${open ? 'translate-x-0' : 'translate-x-full'}`}>
          <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
            <h2 id="sepet-baslik" className="font-semibold text-slate-900">Sepetin</h2>
            <button type="button" onClick={() => setOpen(false)} aria-label="Sepeti kapat" className="rounded-lg p-1.5 text-slate-500 hover:bg-slate-100">
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
            </button>
          </div>
          <ul className="flex-1 divide-y divide-slate-100 overflow-y-auto px-5">
            {items.map((i) => (
              <li key={i.id} className="flex items-center gap-4 py-4">
                <span className="h-14 w-14 shrink-0 rounded-xl bg-gradient-to-br from-amber-100 to-orange-200" aria-hidden="true" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-slate-900">{i.name}</p>
                  <p className="text-sm text-slate-500">{i.price} ₺</p>
                </div>
                <div className="flex items-center rounded-lg border border-slate-200">
                  <button type="button" aria-label={`${i.name} adedini azalt`} onClick={() => change(i.id, -1)} className="px-2 py-1 text-slate-600 hover:bg-slate-50">−</button>
                  <span className="w-6 text-center text-sm" aria-live="polite">{i.qty}</span>
                  <button type="button" aria-label={`${i.name} adedini artır`} onClick={() => change(i.id, 1)} className="px-2 py-1 text-slate-600 hover:bg-slate-50">+</button>
                </div>
              </li>
            ))}
          </ul>
          <div className="border-t border-slate-200 p-5">
            <p className="flex justify-between text-sm font-medium text-slate-900"><span>Toplam</span><span>{total.toLocaleString('tr-TR')} ₺</span></p>
            <button type="button" className="mt-4 w-full rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-indigo-500">Ödemeye geç</button>
          </div>
        </aside>
      </div>
    </div>
  );
}
```
