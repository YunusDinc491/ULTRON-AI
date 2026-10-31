---
title: Satır işlem menüsü (üç nokta ⋮: düzenle, kopyala, sil)
tags: kebab menu, üç nokta, işlem menüsü, context menu, bağlam menüsü, satır menüsü, sil, düzenle
description: Liste satırının sağındaki ⋮ düğmesiyle açılan küçük işlem menüsü; "Sil" kırmızı ve ayrı grupta; her satırın menüsü ayrı açılır.
---
```jsx
import React, { useEffect, useState } from 'react';

const files = ['Sunum-final.pdf', 'Bütçe 2026.xlsx', 'Logo taslakları.fig'];

export default function RowActions() {
  const [openRow, setOpenRow] = useState(null);
  useEffect(() => {
    const close = (e) => { if (!e.target.closest('[data-menu]')) setOpenRow(null); };
    const onKey = (e) => e.key === 'Escape' && setOpenRow(null);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', onKey); };
  }, []);
  return (
    <div className="mx-auto max-w-xl p-6">
      <ul className="divide-y divide-slate-100 rounded-2xl border border-slate-200 bg-white shadow-sm">
        {files.map((f, i) => (
          <li key={f} className="flex items-center justify-between gap-3 px-4 py-3">
            <span className="truncate text-sm font-medium text-slate-800">{f}</span>
            <div className="relative" data-menu>
              <button
                type="button"
                className="rounded-lg p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
                aria-label={`${f} için işlemler`}
                aria-haspopup="menu"
                aria-expanded={openRow === i}
                onClick={() => setOpenRow(openRow === i ? null : i)}
              >
                <svg viewBox="0 0 20 20" className="h-5 w-5" fill="currentColor" aria-hidden="true"><circle cx="10" cy="4" r="1.6" /><circle cx="10" cy="10" r="1.6" /><circle cx="10" cy="16" r="1.6" /></svg>
              </button>
              {openRow === i && (
                <div role="menu" className="absolute right-0 z-50 mt-1 w-40 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
                  {['Düzenle', 'Kopyala', 'Paylaş'].map((a) => (
                    <button key={a} type="button" role="menuitem" className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-slate-50">{a}</button>
                  ))}
                  <div className="my-1 h-px bg-slate-100" />
                  <button type="button" role="menuitem" className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-rose-600 hover:bg-rose-50">Sil</button>
                </div>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
```
