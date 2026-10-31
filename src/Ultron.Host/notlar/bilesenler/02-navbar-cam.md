---
title: Navbar · cam efektli (bulanık, yarı saydam), Apple tarzı sade
tags: navbar, cam efekti, glassmorphism, backdrop blur, şeffaf, sade, apple, header
description: Kaydırınca içerik arkasından bulanık görünen yarı saydam üst çubuk; ince yazılar, ortada kısa linkler, mobilde hamburger.
---
```jsx
import React, { useState } from 'react';

const links = ['Mağaza', 'Mac', 'iPad', 'Destek'];

export default function GlassNavbar() {
  const [open, setOpen] = useState(false);
  return (
    <div className="min-h-[420px] bg-gradient-to-br from-indigo-200 via-sky-100 to-rose-100">
      <header className="sticky top-0 z-40 border-b border-white/40 bg-white/60 backdrop-blur-xl">
        <nav className="mx-auto flex h-12 max-w-5xl items-center justify-between px-4" aria-label="Ana menü">
          <a href="#" className="text-sm font-semibold tracking-tight text-slate-900">Ultron</a>
          <ul className="hidden gap-7 sm:flex">
            {links.map((l) => (
              <li key={l}><a href="#" className="text-xs text-slate-700 transition duration-200 hover:text-slate-950">{l}</a></li>
            ))}
          </ul>
          <button
            type="button"
            className="rounded-full p-2 text-slate-700 hover:bg-white/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 sm:hidden"
            aria-label={open ? 'Menüyü kapat' : 'Menüyü aç'}
            aria-expanded={open}
            aria-controls="cam-menu"
            onClick={() => setOpen(!open)}
          >
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
              {open ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 9h16M4 15h16" />}
            </svg>
          </button>
          <a href="#" className="hidden rounded-full bg-slate-900 px-3 py-1 text-xs font-medium text-white sm:inline-block">Satın al</a>
        </nav>
        {open && (
          <ul id="cam-menu" className="space-y-1 px-4 pb-4 sm:hidden">
            {links.map((l) => (
              <li key={l}><a href="#" className="block rounded-xl px-3 py-2 text-lg font-semibold text-slate-900 hover:bg-white/60">{l}</a></li>
            ))}
          </ul>
        )}
      </header>
      <section className="mx-auto max-w-3xl px-4 py-20 text-center">
        <h1 className="text-4xl font-semibold tracking-tight text-slate-900 sm:text-5xl">Sade. Hızlı. Senin.</h1>
        <p className="mt-4 text-slate-600">Çubuk yarı saydam: arkadaki renk bulanık görünür.</p>
      </section>
    </div>
  );
}
```
