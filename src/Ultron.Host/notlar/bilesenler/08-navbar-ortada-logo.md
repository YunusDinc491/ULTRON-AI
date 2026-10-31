---
title: Navbar · logo ortada, linkler iki yanda (butik/marka sitesi)
tags: navbar, ortada logo, simetrik menü, marka, butik, moda, header, duyuru şeridi
description: Üstte ince duyuru şeridi; altında logonun ortada, linklerin iki yana bölündüğü simetrik menü; mobilde logo ortada, sol hamburger, sağ sepet.
---
```jsx
import React, { useState } from 'react';

export default function CenteredLogoNavbar() {
  const [open, setOpen] = useState(false);
  const left = ['Kadın', 'Erkek', 'Çocuk'];
  const right = ['Hikâyemiz', 'Mağazalar'];
  return (
    <header className="bg-white">
      <p className="bg-slate-900 px-4 py-2 text-center text-xs font-medium text-white">500 TL üzeri siparişlerde kargo ücretsiz</p>
      <nav className="mx-auto grid h-16 max-w-7xl grid-cols-3 items-center border-b border-slate-200 px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <div className="flex items-center gap-6">
          <button type="button" className="rounded-lg p-2 text-slate-700 hover:bg-slate-100 md:hidden" aria-label="Menüyü aç/kapat" aria-expanded={open} aria-controls="butik-menu" onClick={() => setOpen(!open)}>
            <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
          </button>
          {left.map((l) => <a key={l} href="#" className="hidden text-sm font-medium text-slate-700 hover:text-slate-900 md:inline">{l}</a>)}
        </div>
        <a href="#" className="justify-self-center text-xl font-semibold tracking-[0.3em] text-slate-900">ELİF</a>
        <div className="flex items-center justify-end gap-6">
          {right.map((l) => <a key={l} href="#" className="hidden text-sm font-medium text-slate-700 hover:text-slate-900 md:inline">{l}</a>)}
          <a href="#" className="rounded-lg p-2 text-slate-700 hover:bg-slate-100" aria-label="Sepet">
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 7h12l-1 13H7L6 7zM9 7a3 3 0 0 1 6 0" /></svg>
          </a>
        </div>
      </nav>
      {open && (
        <ul id="butik-menu" className="space-y-1 border-b border-slate-200 px-4 py-3 md:hidden">
          {[...left, ...right].map((l) => <li key={l}><a href="#" className="block rounded-lg px-3 py-2 text-slate-700 hover:bg-slate-50">{l}</a></li>)}
        </ul>
      )}
    </header>
  );
}
```
