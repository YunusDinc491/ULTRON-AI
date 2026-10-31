---
title: Açılır kullanıcı menüsü (profil fotoğrafı, ayarlar, çıkış)
tags: dropdown, açılır menü, kullanıcı menüsü, profil menüsü, avatar menü, hesap, çıkış yap
description: Profil fotoğrafına basınca açılan menü: üstte ad ve e-posta, altında Profil/Ayarlar/Faturalar, ayraçtan sonra kırmızı "Çıkış yap"; dışarı tıklayınca ve Esc ile kapanır, menü öğeleri role="menuitem".
---
```jsx
import React, { useEffect, useRef, useState } from 'react';

const items = [
  { label: 'Profilim', icon: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 20a8 8 0 0 1 16 0' },
  { label: 'Ayarlar', icon: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19 12l2-1-1-3-2 .3-1.5-1.5L17 5l-3-1-1 2h-2L10 4 7 5l.5 2.3L6 8.8 4 8.5 3 11.5 5 12v.1l-2 1 1 3 2-.3 1.5 1.5L7 19l3 1 1-2h2l1 2 3-1-.5-2.3 1.5-1.5 2 .3 1-3z' },
  { label: 'Faturalar', icon: 'M6 3h12v18l-3-2-3 2-3-2-3 2V3zM9 8h6M9 12h6' },
];

export default function UserMenu() {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  useEffect(() => {
    const onDown = (e) => ref.current && !ref.current.contains(e.target) && setOpen(false);
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('keydown', onKey); };
  }, []);
  return (
    <div className="flex min-h-[320px] justify-end bg-slate-50 p-6">
      <div className="relative" ref={ref}>
        <button
          type="button"
          className="flex items-center gap-2 rounded-full border border-slate-200 bg-white py-1 pl-1 pr-3 shadow-sm hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls="kullanici-menu"
          onClick={() => setOpen(!open)}
        >
          <span className="grid h-8 w-8 place-items-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-500 text-xs font-semibold text-white">ZK</span>
          <span className="text-sm font-medium text-slate-700">Zeynep</span>
          <svg viewBox="0 0 20 20" className={`h-4 w-4 text-slate-400 transition duration-200 ${open ? 'rotate-180' : ''}`} fill="currentColor" aria-hidden="true"><path d="M5.2 7.2a.75.75 0 0 1 1.06 0L10 10.94l3.74-3.74a.75.75 0 1 1 1.06 1.06l-4.27 4.27a.75.75 0 0 1-1.06 0L5.2 8.26a.75.75 0 0 1 0-1.06z" /></svg>
        </button>
        {open && (
          <div id="kullanici-menu" role="menu" className="absolute right-0 z-50 mt-2 w-60 origin-top-right rounded-2xl border border-slate-200 bg-white p-1.5 shadow-lg">
            <div className="px-3 py-2">
              <p className="text-sm font-semibold text-slate-900">Zeynep Kaya</p>
              <p className="truncate text-xs text-slate-500">zeynep@ornek.com</p>
            </div>
            <div className="my-1 h-px bg-slate-100" />
            {items.map((i) => (
              <a key={i.label} href="#" role="menuitem" className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 focus-visible:bg-slate-50 focus-visible:outline-none">
                <svg viewBox="0 0 24 24" className="h-4 w-4 text-slate-400" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={i.icon} /></svg>
                {i.label}
              </a>
            ))}
            <div className="my-1 h-px bg-slate-100" />
            <button type="button" role="menuitem" className="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm text-rose-600 hover:bg-rose-50">
              <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true"><path d="M15 12H4m0 0 3-3m-3 3 3 3M10 5h8a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-8" /></svg>
              Çıkış yap
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
```
