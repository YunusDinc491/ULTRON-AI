---
title: Mobil alt gezinme çubuğu (bottom navigation) ve ortada büyük eylem düğmesi
tags: bottom navigation, alt menü, mobil gezinme, tab bar, mobil uygulama, alt sekmeler, ikonlu menü
description: Telefon ekranının altına sabit, 5 öğeli alt menü: ikon + kısa etiket, seçili öğe renkli (aria-current); ortadaki "+" yükseltilmiş yuvarlak düğme; güvenli alan boşluğu.
---
```jsx
import React, { useState } from 'react';

const items = [
  { id: 'ana', label: 'Ana sayfa', d: 'M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1v-9z' },
  { id: 'ara', label: 'Keşfet', d: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zm10 2-4.3-4.3' },
  { id: 'ekle', label: 'Ekle', d: 'M12 5v14M5 12h14', center: true },
  { id: 'mesaj', label: 'Mesajlar', d: 'M4 5h16v11H8l-4 4V5z' },
  { id: 'profil', label: 'Profil', d: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21a8 8 0 0 1 16 0' },
];

export default function BottomNav() {
  const [active, setActive] = useState('ana');
  return (
    <div className="relative mx-auto flex h-[560px] max-w-sm flex-col overflow-hidden border border-slate-200 bg-slate-50">
      <main className="flex-1 p-5">
        <h1 className="text-xl font-semibold tracking-tight text-slate-900">{items.find((i) => i.id === active)?.label}</h1>
        <p className="mt-2 text-sm text-slate-600">Alt menüdeki sekmeye dokun.</p>
      </main>
      <nav aria-label="Alt menü" className="border-t border-slate-200 bg-white pb-[env(safe-area-inset-bottom)]">
        <ul className="grid grid-cols-5">
          {items.map((i) => (
            <li key={i.id} className="flex justify-center">
              {i.center ? (
                <button type="button" aria-label={i.label} className="-mt-6 grid h-14 w-14 place-items-center rounded-full bg-indigo-600 text-white shadow-lg ring-4 ring-white hover:bg-indigo-500">
                  <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true"><path d={i.d} /></svg>
                </button>
              ) : (
                <a href="#" aria-current={active === i.id ? 'page' : undefined} onClick={(e) => { e.preventDefault(); setActive(i.id); }}
                  className={`flex w-full flex-col items-center gap-1 py-2.5 text-[11px] font-medium ${active === i.id ? 'text-indigo-600' : 'text-slate-500'}`}>
                  <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={i.d} /></svg>
                  {i.label}
                </a>
              )}
            </li>
          ))}
        </ul>
      </nav>
    </div>
  );
}
```
