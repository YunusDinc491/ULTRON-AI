---
title: İpucu (tooltip) ve yükleniyor iskeleti (skeleton)
tags: tooltip, ipucu, açıklama balonu, hover, skeleton, iskelet, yükleniyor, loading placeholder, shimmer
description: Üzerine gelince ve odaklanınca görünen ipucu balonu (role="tooltip", aria-describedby); altta kart listesi yüklenirken nabız atan gri iskelet, 2 sn sonra gerçek içerik.
---
```jsx
import React, { useEffect, useState } from 'react';

function Tooltip({ id, text, children }) {
  const [show, setShow] = useState(false);
  return (
    <span className="relative inline-flex" onMouseEnter={() => setShow(true)} onMouseLeave={() => setShow(false)} onFocus={() => setShow(true)} onBlur={() => setShow(false)}>
      {React.cloneElement(children, { 'aria-describedby': id })}
      <span id={id} role="tooltip" className={`pointer-events-none absolute bottom-full left-1/2 z-50 mb-2 -translate-x-1/2 whitespace-nowrap rounded-lg bg-slate-900 px-2.5 py-1 text-xs text-white shadow-lg transition duration-150 ${show ? 'opacity-100' : 'opacity-0'}`}>
        {text}
      </span>
    </span>
  );
}

export default function TooltipSkeleton() {
  const [loading, setLoading] = useState(true);
  useEffect(() => { const t = setTimeout(() => setLoading(false), 2000); return () => clearTimeout(t); }, []);
  return (
    <div className="mx-auto max-w-xl space-y-10 p-8">
      <div className="flex gap-4 pt-8">
        <Tooltip id="ipucu-kaydet" text="Ctrl+S ile de kaydedebilirsin">
          <button type="button" className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white">Kaydet</button>
        </Tooltip>
        <Tooltip id="ipucu-yardim" text="Yardım merkezini aç">
          <button type="button" aria-label="Yardım" className="grid h-9 w-9 place-items-center rounded-full border border-slate-200 text-slate-500">?</button>
        </Tooltip>
      </div>
      <ul className="space-y-4" aria-busy={loading}>
        {[0, 1, 2].map((i) => (
          <li key={i} className="flex gap-4 rounded-2xl border border-slate-200 p-4">
            {loading ? (
              <>
                <div className="h-12 w-12 animate-pulse rounded-full bg-slate-200" />
                <div className="flex-1 space-y-2 py-1">
                  <div className="h-3 w-1/2 animate-pulse rounded bg-slate-200" />
                  <div className="h-3 w-5/6 animate-pulse rounded bg-slate-200" />
                </div>
              </>
            ) : (
              <>
                <span className="grid h-12 w-12 place-items-center rounded-full bg-indigo-100 font-semibold text-indigo-700">{['AK', 'BY', 'CE'][i]}</span>
                <div>
                  <p className="font-medium text-slate-900">{['Ahmet Kaya', 'Burcu Yıldız', 'Cem Erdem'][i]}</p>
                  <p className="text-sm text-slate-500">Son görülme {i + 2} saat önce</p>
                </div>
              </>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
```
