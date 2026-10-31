---
title: Navbar · açılır alt menülü (dropdown), dışarı tıklayınca ve Esc ile kapanır
tags: navbar, dropdown, açılır menü, alt menü, ürünler menüsü, header, klavye
description: "Ürünler" linkinin altında açıklamalı alt menü açılır; dışarı tıklayınca ya da Esc'e basınca kapanır; mobilde akordeon gibi açılan hamburger menü.
---
```jsx
import React, { useEffect, useRef, useState } from 'react';

const products = [
  { name: 'Analiz', desc: 'Trafiğini ve dönüşümlerini izle' },
  { name: 'Otomasyon', desc: 'Tekrarlayan işleri kendiliğinden yap' },
  { name: 'Entegrasyonlar', desc: '120+ araçla bağlan' },
];

export default function DropdownNavbar() {
  const [menu, setMenu] = useState(false);
  const [mobile, setMobile] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    const onClick = (e) => { if (ref.current && !ref.current.contains(e.target)) setMenu(false); };
    const onKey = (e) => { if (e.key === 'Escape') setMenu(false); };
    document.addEventListener('mousedown', onClick);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onClick); document.removeEventListener('keydown', onKey); };
  }, []);

  return (
    <header className="border-b border-slate-200 bg-white">
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8" aria-label="Ana menü">
        <a href="#" className="font-semibold tracking-tight text-slate-900">Akış</a>
        <ul className="hidden items-center gap-6 md:flex">
          <li className="relative" ref={ref}>
            <button
              type="button"
              className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-sm font-medium text-slate-600 hover:text-slate-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
              aria-expanded={menu}
              aria-controls="urunler-menu"
              onClick={() => setMenu(!menu)}
            >
              Ürünler
              <svg viewBox="0 0 20 20" className={`h-4 w-4 transition duration-200 ${menu ? 'rotate-180' : ''}`} fill="currentColor" aria-hidden="true"><path d="M5.2 7.2a.75.75 0 0 1 1.06 0L10 10.94l3.74-3.74a.75.75 0 1 1 1.06 1.06l-4.27 4.27a.75.75 0 0 1-1.06 0L5.2 8.26a.75.75 0 0 1 0-1.06z" /></svg>
            </button>
            {menu && (
              <div id="urunler-menu" className="absolute left-0 top-full z-50 mt-3 w-72 rounded-2xl border border-slate-200 bg-white p-2 shadow-lg">
                {products.map((p) => (
                  <a key={p.name} href="#" className="block rounded-lg p-3 hover:bg-slate-50">
                    <span className="block text-sm font-semibold text-slate-900">{p.name}</span>
                    <span className="block text-sm text-slate-600">{p.desc}</span>
                  </a>
                ))}
              </div>
            )}
          </li>
          <li><a href="#" className="text-sm font-medium text-slate-600 hover:text-slate-900">Fiyatlar</a></li>
          <li><a href="#" className="text-sm font-medium text-slate-600 hover:text-slate-900">Hakkımızda</a></li>
        </ul>
        <a href="#" className="hidden rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 md:inline-block">Demo iste</a>
        <button type="button" className="rounded-lg p-2 text-slate-700 hover:bg-slate-100 md:hidden" aria-label="Menüyü aç/kapat" aria-expanded={mobile} aria-controls="mobil" onClick={() => setMobile(!mobile)}>
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
        </button>
      </nav>
      {mobile && (
        <div id="mobil" className="space-y-1 border-t border-slate-200 px-4 py-3 md:hidden">
          <p className="px-3 pt-2 text-xs font-semibold uppercase tracking-wide text-slate-500">Ürünler</p>
          {products.map((p) => (
            <a key={p.name} href="#" className="block rounded-lg px-3 py-2 text-slate-700 hover:bg-slate-50">{p.name}</a>
          ))}
          <a href="#" className="block rounded-lg px-3 py-2 text-slate-700 hover:bg-slate-50">Fiyatlar</a>
          <a href="#" className="block rounded-lg px-3 py-2 text-slate-700 hover:bg-slate-50">Hakkımızda</a>
        </div>
      )}
    </header>
  );
}
```
