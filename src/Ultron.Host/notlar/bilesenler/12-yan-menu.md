---
title: Yan menü (sidebar) · panel uygulaması, ikonlu bölümler, mobilde açılır
tags: sidebar, yan menü, dashboard menüsü, panel, uygulama gezinmesi, ikonlu menü, admin
description: Masaüstünde solda sabit, ikonlu ve gruplu yan menü (aktif öğe vurgulu, sayı rozetleri); mobilde üst çubuktaki düğmeyle açılır kapanır; sağda içerik alanı.
---
```jsx
import React, { useState } from 'react';

const sections = [
  { title: 'Genel', items: [
    { label: 'Pano', icon: 'M4 13h6V4H4v9zm0 7h6v-5H4v5zm10 0h6v-9h-6v9zm0-16v5h6V4h-6z' },
    { label: 'Projeler', icon: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z', count: 12 },
    { label: 'Takvim', icon: 'M7 3v3m10-3v3M4 9h16M5 6h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1z' },
  ] },
  { title: 'Ekip', items: [
    { label: 'Mesajlar', icon: 'M4 5h16v11H8l-4 4V5z', count: 3 },
    { label: 'Ayarlar', icon: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z' },
  ] },
];

export default function Sidebar() {
  const [active, setActive] = useState('Pano');
  const [open, setOpen] = useState(false);
  const nav = (
    <nav aria-label="Yan menü" className="space-y-6">
      {sections.map((s) => (
        <div key={s.title}>
          <p className="px-3 text-xs font-semibold uppercase tracking-wide text-slate-400">{s.title}</p>
          <ul className="mt-2 space-y-1">
            {s.items.map((i) => (
              <li key={i.label}>
                <a
                  href="#"
                  aria-current={active === i.label ? 'page' : undefined}
                  onClick={(e) => { e.preventDefault(); setActive(i.label); setOpen(false); }}
                  className={`flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition duration-200 ${active === i.label ? 'bg-indigo-50 text-indigo-700' : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'}`}
                >
                  <svg viewBox="0 0 24 24" className="h-5 w-5 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" aria-hidden="true"><path d={i.icon} /></svg>
                  <span className="flex-1">{i.label}</span>
                  {i.count && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600">{i.count}</span>}
                </a>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  );
  return (
    <div className="flex min-h-[480px] bg-slate-50">
      <aside className="hidden w-64 shrink-0 border-r border-slate-200 bg-white p-4 lg:block">
        <p className="mb-6 px-3 font-semibold tracking-tight text-slate-900">Ultron Panel</p>
        {nav}
      </aside>
      <div className="min-w-0 flex-1">
        <header className="flex h-14 items-center gap-3 border-b border-slate-200 bg-white px-4 lg:hidden">
          <button type="button" className="rounded-lg p-2 text-slate-600 hover:bg-slate-100" aria-label="Menüyü aç/kapat" aria-expanded={open} aria-controls="mobil-yan" onClick={() => setOpen(!open)}>
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
          </button>
          <span className="font-semibold text-slate-900">Ultron Panel</span>
        </header>
        {open && <div id="mobil-yan" className="border-b border-slate-200 bg-white p-4 lg:hidden">{nav}</div>}
        <main className="p-6">
          <h1 className="text-xl font-semibold tracking-tight text-slate-900">{active}</h1>
          <p className="mt-2 text-sm text-slate-600">Seçili sayfanın içeriği burada.</p>
        </main>
      </div>
    </div>
  );
}
```
