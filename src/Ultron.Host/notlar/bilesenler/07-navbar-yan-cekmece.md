---
title: Navbar · mobilde soldan kayan çekmece menü (off-canvas), arka plan karartmalı
tags: navbar, çekmece, drawer, off-canvas, yan menü, mobil menü, overlay, kayan menü
description: Mobilde hamburgere basınca soldan kayan tam boy menü açılır; arka plan kararır, karartmaya ya da kapat düğmesine basınca veya Esc ile kapanır.
---
```jsx
import React, { useEffect, useState } from 'react';

const links = ['Keşfet', 'Koleksiyonlar', 'Yeni gelenler', 'İndirim', 'İletişim'];

export default function DrawerNavbar() {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);
  return (
    <header className="border-b border-slate-200 bg-white">
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <button type="button" className="rounded-lg p-2 text-slate-700 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 lg:hidden" aria-label="Menüyü aç" aria-expanded={open} aria-controls="cekmece" onClick={() => setOpen(true)}>
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 7h16M4 12h10M4 17h16" /></svg>
        </button>
        <a href="#" className="font-semibold tracking-tight text-slate-900">Atölye</a>
        <ul className="hidden gap-6 lg:flex">
          {links.map((l) => <li key={l}><a href="#" className="text-sm font-medium text-slate-600 hover:text-slate-900">{l}</a></li>)}
        </ul>
        <a href="#" className="text-sm font-medium text-indigo-600">Sepet</a>
      </nav>
      <div className={`fixed inset-0 z-50 lg:hidden ${open ? '' : 'pointer-events-none'}`} aria-hidden={!open}>
        <div className={`absolute inset-0 bg-slate-900/40 transition-opacity duration-300 ${open ? 'opacity-100' : 'opacity-0'}`} onClick={() => setOpen(false)} />
        <aside id="cekmece" className={`absolute inset-y-0 left-0 flex w-72 max-w-[85%] flex-col bg-white shadow-lg transition-transform duration-300 ${open ? 'translate-x-0' : '-translate-x-full'}`}>
          <div className="flex h-16 items-center justify-between border-b border-slate-200 px-4">
            <span className="font-semibold text-slate-900">Menü</span>
            <button type="button" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100" aria-label="Menüyü kapat" onClick={() => setOpen(false)}>
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
            </button>
          </div>
          <ul className="flex-1 space-y-1 overflow-y-auto p-3">
            {links.map((l) => <li key={l}><a href="#" className="block rounded-lg px-3 py-2.5 font-medium text-slate-700 hover:bg-slate-50">{l}</a></li>)}
          </ul>
          <div className="border-t border-slate-200 p-4">
            <a href="#" className="block rounded-lg bg-indigo-600 px-4 py-2.5 text-center text-sm font-medium text-white">Giriş yap</a>
          </div>
        </aside>
      </div>
    </header>
  );
}
```
