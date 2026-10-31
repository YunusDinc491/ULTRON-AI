---
title: Buton grubu ve bölünmüş buton (ana işlem + açılır seçenekler)
tags: button group, buton grubu, split button, bölünmüş buton, araç çubuğu, toolbar, hizalama butonları
description: Birleşik kenarlı buton grubu (sola/ortaya/sağa hizala, seçili olan vurgulu) ve sağında ok olan bölünmüş "Yayınla" butonu (açılır: zamanla, taslak kaydet).
---
```jsx
import React, { useState } from 'react';

export default function ButtonGroups() {
  const [align, setAlign] = useState('sol');
  const [open, setOpen] = useState(false);
  const aligns = [
    { id: 'sol', label: 'Sola hizala', d: 'M4 6h16M4 10h10M4 14h16M4 18h10' },
    { id: 'orta', label: 'Ortala', d: 'M4 6h16M7 10h10M4 14h16M7 18h10' },
    { id: 'sag', label: 'Sağa hizala', d: 'M4 6h16M10 10h10M4 14h16M10 18h10' },
  ];
  return (
    <div className="flex min-h-[260px] flex-wrap items-start gap-8 p-8">
      <div className="inline-flex rounded-lg shadow-sm" role="group" aria-label="Metin hizalama">
        {aligns.map((a, i) => (
          <button
            key={a.id}
            type="button"
            aria-label={a.label}
            aria-pressed={align === a.id}
            onClick={() => setAlign(a.id)}
            className={`border border-slate-200 px-3 py-2 focus-visible:z-10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${i === 0 ? 'rounded-l-lg' : '-ml-px'} ${i === aligns.length - 1 ? 'rounded-r-lg' : ''} ${align === a.id ? 'bg-slate-100 text-slate-900' : 'bg-white text-slate-500 hover:bg-slate-50'}`}
          >
            <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d={a.d} /></svg>
          </button>
        ))}
      </div>
      <div className="relative inline-flex rounded-lg shadow-sm">
        <button type="button" className="rounded-l-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500">Yayınla</button>
        <button type="button" aria-label="Diğer yayın seçenekleri" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen(!open)}
          className="rounded-r-lg border-l border-indigo-500 bg-indigo-600 px-2 text-white hover:bg-indigo-500">
          <svg viewBox="0 0 20 20" className="h-4 w-4" fill="currentColor" aria-hidden="true"><path d="M5.2 7.2a.75.75 0 0 1 1.06 0L10 10.94l3.74-3.74a.75.75 0 1 1 1.06 1.06l-4.27 4.27a.75.75 0 0 1-1.06 0L5.2 8.26a.75.75 0 0 1 0-1.06z" /></svg>
        </button>
        {open && (
          <div role="menu" className="absolute right-0 top-full z-50 mt-1 w-44 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
            <button type="button" role="menuitem" className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-slate-50">Zamanla…</button>
            <button type="button" role="menuitem" className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-slate-50">Taslak kaydet</button>
          </div>
        )}
      </div>
    </div>
  );
}
```
