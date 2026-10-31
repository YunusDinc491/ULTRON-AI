---
title: Navbar · mega menü (ikonlu kategoriler ve öne çıkan kutu)
tags: navbar, mega menü, geniş açılır menü, kategoriler, e-ticaret menüsü, header
description: "Kategoriler"e basınca tam genişlikte açılan, ikonlu link grupları ve sağda öne çıkan kampanya kutusu olan menü; Esc ile kapanır.
---
```jsx
import React, { useEffect, useState } from 'react';

const groups = [
  { title: 'Elektronik', items: ['Telefon', 'Bilgisayar', 'Kulaklık', 'Akıllı saat'] },
  { title: 'Ev & Yaşam', items: ['Mobilya', 'Aydınlatma', 'Mutfak', 'Dekorasyon'] },
  { title: 'Spor', items: ['Koşu', 'Fitness', 'Kamp', 'Bisiklet'] },
];

export default function MegaMenu() {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);
  return (
    <header className="relative border-b border-slate-200 bg-white">
      <nav className="mx-auto flex h-16 max-w-7xl items-center gap-6 px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <a href="#" className="font-semibold tracking-tight text-slate-900">Pazar</a>
        <button
          type="button"
          className="inline-flex items-center gap-2 rounded-lg bg-slate-100 px-3 py-2 text-sm font-medium text-slate-800 hover:bg-slate-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
          aria-expanded={open}
          aria-controls="mega"
          onClick={() => setOpen(!open)}
        >
          <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><rect x="4" y="4" width="6" height="6" rx="1.5" /><rect x="14" y="4" width="6" height="6" rx="1.5" /><rect x="4" y="14" width="6" height="6" rx="1.5" /><rect x="14" y="14" width="6" height="6" rx="1.5" /></svg>
          Kategoriler
        </button>
        <a href="#" className="ml-auto text-sm font-medium text-slate-600 hover:text-slate-900">Sepet (2)</a>
      </nav>
      {open && (
        <div id="mega" className="absolute inset-x-0 top-full z-50 border-b border-slate-200 bg-white shadow-lg">
          <div className="mx-auto grid max-w-7xl gap-8 px-4 py-8 sm:px-6 md:grid-cols-4 lg:px-8">
            {groups.map((g) => (
              <div key={g.title}>
                <h3 className="text-sm font-semibold text-slate-900">{g.title}</h3>
                <ul className="mt-3 space-y-2">
                  {g.items.map((i) => (
                    <li key={i}><a href="#" className="text-sm text-slate-600 hover:text-indigo-600">{i}</a></li>
                  ))}
                </ul>
              </div>
            ))}
            <a href="#" className="rounded-2xl bg-gradient-to-br from-indigo-600 to-violet-600 p-5 text-white shadow-sm">
              <span className="text-xs font-semibold uppercase tracking-wide text-indigo-100">Kampanya</span>
              <span className="mt-2 block text-lg font-semibold">Kulaklıklarda %30 indirim</span>
              <span className="mt-4 inline-block text-sm font-medium underline underline-offset-4">Keşfet</span>
            </a>
          </div>
        </div>
      )}
    </header>
  );
}
```
