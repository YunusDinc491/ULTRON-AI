---
title: Navbar · koyu tema, aktif sayfa göstergesi
tags: navbar, koyu, dark, aktif link, seçili sayfa, alt çizgi göstergesi, header
description: Koyu zeminli üst çubuk; tıklanan link aktif olur ve altında renkli çizgi görünür (aria-current); mobilde hamburger.
---
```jsx
import React, { useState } from 'react';

const links = ['Anasayfa', 'Projeler', 'Ekip', 'Raporlar'];

export default function DarkNavbar() {
  const [active, setActive] = useState('Anasayfa');
  const [open, setOpen] = useState(false);
  return (
    <header className="bg-slate-900">
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <div className="flex items-center gap-8">
          <span className="font-semibold tracking-tight text-white">Gece</span>
          <ul className="hidden h-16 items-stretch gap-6 md:flex">
            {links.map((l) => (
              <li key={l} className="flex">
                <a
                  href="#"
                  aria-current={active === l ? 'page' : undefined}
                  onClick={(e) => { e.preventDefault(); setActive(l); }}
                  className={`flex items-center border-b-2 text-sm font-medium transition duration-200 ${active === l ? 'border-indigo-400 text-white' : 'border-transparent text-slate-400 hover:text-slate-200'}`}
                >
                  {l}
                </a>
              </li>
            ))}
          </ul>
        </div>
        <a href="#" className="hidden rounded-lg bg-indigo-500 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-400 md:inline-block">Yeni proje</a>
        <button type="button" className="rounded-lg p-2 text-slate-300 hover:bg-slate-800 md:hidden" aria-label="Menüyü aç/kapat" aria-expanded={open} aria-controls="koyu-menu" onClick={() => setOpen(!open)}>
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
        </button>
      </nav>
      {open && (
        <ul id="koyu-menu" className="space-y-1 border-t border-slate-800 px-4 py-3 md:hidden">
          {links.map((l) => (
            <li key={l}>
              <a href="#" aria-current={active === l ? 'page' : undefined} onClick={(e) => { e.preventDefault(); setActive(l); setOpen(false); }}
                className={`block rounded-lg px-3 py-2 text-base font-medium ${active === l ? 'bg-slate-800 text-white' : 'text-slate-300 hover:bg-slate-800'}`}>{l}</a>
            </li>
          ))}
        </ul>
      )}
    </header>
  );
}
```
