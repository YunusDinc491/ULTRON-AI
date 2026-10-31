---
title: Navbar · uygulama üst çubuğu (arama kutusu, bildirim, profil fotoğrafı)
tags: navbar, app bar, arama çubuğu, search, bildirim zili, avatar, profil, dashboard header
description: Panel uygulamaları için üst çubuk: solda logo, ortada arama kutusu, sağda okunmamış rozetli bildirim düğmesi ve profil fotoğrafı.
---
```jsx
import React, { useState } from 'react';

export default function AppTopbar() {
  const [query, setQuery] = useState('');
  return (
    <header className="border-b border-slate-200 bg-white">
      <div className="mx-auto flex h-16 max-w-7xl items-center gap-4 px-4 sm:px-6 lg:px-8">
        <a href="#" className="flex shrink-0 items-center gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-indigo-600 text-sm font-bold text-white">P</span>
          <span className="hidden font-semibold tracking-tight text-slate-900 sm:inline">Panel</span>
        </a>
        <form role="search" className="relative min-w-0 flex-1 sm:max-w-md" onSubmit={(e) => e.preventDefault()}>
          <label htmlFor="ara" className="sr-only">Ara</label>
          <svg viewBox="0 0 20 20" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" fill="currentColor" aria-hidden="true"><path fillRule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 3.4 9.83l3.13 3.14a.75.75 0 1 0 1.06-1.06l-3.14-3.13A5.5 5.5 0 0 0 9 3.5zM5 9a4 4 0 1 1 8 0 4 4 0 0 1-8 0z" clipRule="evenodd" /></svg>
          <input
            id="ara"
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Proje, kişi ya da dosya ara…"
            className="w-full rounded-lg border border-slate-200 bg-slate-50 py-2 pl-9 pr-3 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-500 focus:bg-white focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
          />
        </form>
        <div className="ml-auto flex items-center gap-2">
          <button type="button" className="relative rounded-lg p-2 text-slate-600 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500" aria-label="Bildirimler (3 okunmamış)">
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true"><path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0" /></svg>
            <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-rose-500 ring-2 ring-white" />
          </button>
          <button type="button" className="rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2" aria-label="Profil menüsü">
            <span className="grid h-9 w-9 place-items-center rounded-full bg-gradient-to-br from-amber-400 to-rose-500 text-sm font-semibold text-white">YD</span>
          </button>
        </div>
      </div>
    </header>
  );
}
```
