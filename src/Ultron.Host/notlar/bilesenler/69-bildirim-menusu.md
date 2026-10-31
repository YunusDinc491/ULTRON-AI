---
title: Bildirim açılır menüsü (zil, okunmamış sayısı, tümünü okundu yap)
tags: notification dropdown, bildirim menüsü, zil, okunmamış, inbox, aktivite listesi, dropdown
description: Zil düğmesinde kırmızı sayı rozeti; basınca açılan panelde okunmamışlar mavi noktalı, zaman bilgili bildirim listesi, "Tümünü okundu işaretle" ve altta "Tümünü gör".
---
```jsx
import React, { useEffect, useRef, useState } from 'react';

const initial = [
  { id: 1, who: 'Elif', what: 'tasarımına yorum yaptı', when: '5 dk', unread: true },
  { id: 2, who: 'Mert', what: 'seni “Lansman” projesine ekledi', when: '1 sa', unread: true },
  { id: 3, who: 'Sistem', what: 'haftalık rapor hazır', when: 'Dün', unread: false },
];

export default function NotificationMenu() {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState(initial);
  const ref = useRef(null);
  const unread = items.filter((i) => i.unread).length;
  useEffect(() => {
    const onDown = (e) => ref.current && !ref.current.contains(e.target) && setOpen(false);
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('keydown', onKey); };
  }, []);
  return (
    <div className="flex min-h-[380px] justify-end p-6">
      <div className="relative" ref={ref}>
        <button type="button" aria-label={`Bildirimler, ${unread} okunmamış`} aria-expanded={open} aria-controls="bildirimler" onClick={() => setOpen(!open)}
          className="relative rounded-full p-2 text-slate-600 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500">
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true"><path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0" /></svg>
          {unread > 0 && <span className="absolute -right-0.5 -top-0.5 grid h-5 min-w-5 place-items-center rounded-full bg-rose-600 px-1 text-[11px] font-semibold text-white ring-2 ring-white">{unread}</span>}
        </button>
        {open && (
          <div id="bildirimler" className="absolute right-0 z-50 mt-2 w-80 max-w-[calc(100vw-2rem)] rounded-2xl border border-slate-200 bg-white shadow-lg">
            <div className="flex items-center justify-between px-4 py-3">
              <h2 className="text-sm font-semibold text-slate-900">Bildirimler</h2>
              <button type="button" disabled={unread === 0} onClick={() => setItems(items.map((i) => ({ ...i, unread: false })))} className="text-xs font-medium text-indigo-600 hover:text-indigo-500 disabled:text-slate-400">Tümünü okundu işaretle</button>
            </div>
            <ul className="max-h-72 divide-y divide-slate-100 overflow-y-auto border-y border-slate-100">
              {items.map((i) => (
                <li key={i.id} className={`flex gap-3 px-4 py-3 ${i.unread ? 'bg-indigo-50/40' : ''}`}>
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-semibold text-slate-700">{i.who[0]}</span>
                  <p className="flex-1 text-sm text-slate-600"><span className="font-medium text-slate-900">{i.who}</span> {i.what}<span className="mt-0.5 block text-xs text-slate-400">{i.when}</span></p>
                  {i.unread && <span className="mt-1.5 h-2 w-2 rounded-full bg-indigo-600" aria-label="okunmamış" />}
                </li>
              ))}
            </ul>
            <a href="#" className="block px-4 py-3 text-center text-sm font-medium text-slate-700 hover:bg-slate-50">Tümünü gör</a>
          </div>
        )}
      </div>
    </div>
  );
}
```
