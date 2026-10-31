---
title: Breadcrumb (konum yolu) ve sayfalama (pagination)
tags: breadcrumb, konum yolu, ekmek kırıntısı, pagination, sayfalama, sayfa numaraları, önceki sonraki
description: Üstte "Ana sayfa / Ürünler / Kulaklık" konum yolu (aria-current), altta önceki/sonraki ve numaralı sayfalama; çok sayfada üç nokta; mobilde sadece "Sayfa 3/12".
---
```jsx
import React, { useState } from 'react';

export default function BreadcrumbPagination() {
  const [page, setPage] = useState(3);
  const total = 12;
  const pages = [1, 2, 3, 4, 5, '…', total].map((p, i) => (p === 5 && page > 5 ? page : p));
  return (
    <div className="mx-auto max-w-3xl space-y-10 p-6">
      <nav aria-label="Konum">
        <ol className="flex flex-wrap items-center gap-2 text-sm text-slate-500">
          <li><a href="#" className="hover:text-slate-900">Ana sayfa</a></li>
          <li aria-hidden="true">/</li>
          <li><a href="#" className="hover:text-slate-900">Ürünler</a></li>
          <li aria-hidden="true">/</li>
          <li aria-current="page" className="font-medium text-slate-900">Kulaklık</li>
        </ol>
      </nav>
      <nav aria-label="Sayfalama" className="flex items-center justify-between border-t border-slate-200 pt-4">
        <button type="button" disabled={page === 1} onClick={() => setPage(page - 1)} className="rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100 disabled:opacity-40">← Önceki</button>
        <ul className="hidden items-center gap-1 sm:flex">
          {pages.map((p, i) => (
            <li key={i}>
              {p === '…' ? (
                <span className="px-2 text-slate-400">…</span>
              ) : (
                <button
                  type="button"
                  aria-current={p === page ? 'page' : undefined}
                  aria-label={`Sayfa ${p}`}
                  onClick={() => setPage(p)}
                  className={`h-9 min-w-9 rounded-lg px-3 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${p === page ? 'bg-indigo-600 text-white' : 'text-slate-600 hover:bg-slate-100'}`}
                >
                  {p}
                </button>
              )}
            </li>
          ))}
        </ul>
        <span className="text-sm text-slate-600 sm:hidden">Sayfa {page}/{total}</span>
        <button type="button" disabled={page === total} onClick={() => setPage(page + 1)} className="rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100 disabled:opacity-40">Sonraki →</button>
      </nav>
    </div>
  );
}
```
