---
title: Navbar · yapışkan, linkler ortada, sağda giriş butonu, mobilde hamburger
tags: navbar, header, üst menü, gezinme, hamburger, mobil menü, yapışkan, sticky
description: Logo solda, sayfa linkleri ortada, sağda "Giriş yap" ve "Başla" butonları; mobilde hamburger düğmesiyle açılan dikey menü.
---
```jsx
import React, { useState } from 'react';

const links = [
  { label: 'Ürünler', href: '#' },
  { label: 'Çözümler', href: '#' },
  { label: 'Fiyatlar', href: '#' },
  { label: 'Blog', href: '#' },
];

export default function Navbar() {
  const [open, setOpen] = useState(false);
  return (
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white">
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <a href="#" className="flex items-center gap-2 rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-indigo-600 text-sm font-bold text-white">U</span>
          <span className="font-semibold tracking-tight text-slate-900">Ultron</span>
        </a>
        <ul className="hidden items-center gap-8 md:flex">
          {links.map((l) => (
            <li key={l.label}>
              <a href={l.href} className="text-sm font-medium text-slate-600 transition duration-200 hover:text-slate-900">{l.label}</a>
            </li>
          ))}
        </ul>
        <div className="hidden items-center gap-3 md:flex">
          <a href="#" className="text-sm font-medium text-slate-700 hover:text-slate-900">Giriş yap</a>
          <a href="#" className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition duration-200 hover:bg-indigo-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2">Başla</a>
        </div>
        <button
          type="button"
          className="inline-flex h-10 w-10 items-center justify-center rounded-lg text-slate-700 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 md:hidden"
          aria-label={open ? 'Menüyü kapat' : 'Menüyü aç'}
          aria-expanded={open}
          aria-controls="mobil-menu"
          onClick={() => setOpen(!open)}
        >
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            {open ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
          </svg>
        </button>
      </nav>
      {open && (
        <div id="mobil-menu" className="border-t border-slate-200 md:hidden">
          <ul className="space-y-1 px-4 py-3">
            {links.map((l) => (
              <li key={l.label}>
                <a href={l.href} className="block rounded-lg px-3 py-2 text-base font-medium text-slate-700 hover:bg-slate-50">{l.label}</a>
              </li>
            ))}
          </ul>
          <div className="flex gap-3 border-t border-slate-200 px-4 py-3">
            <a href="#" className="flex-1 rounded-lg border border-slate-200 px-4 py-2 text-center text-sm font-medium text-slate-700">Giriş yap</a>
            <a href="#" className="flex-1 rounded-lg bg-indigo-600 px-4 py-2 text-center text-sm font-medium text-white">Başla</a>
          </div>
        </div>
      )}
    </header>
  );
}
```
