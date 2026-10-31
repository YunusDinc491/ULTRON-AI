---
title: Hap şeklinde segment seçici (aylık/yıllık, liste/ızgara)
tags: segmented control, hap sekmeler, pill tabs, toggle group, aylık yıllık, görünüm seçici, radio group
description: Gri zemin içinde seçili seçeneğin beyaz "hap" olarak öne çıktığı seçici; role="radiogroup"; aylık/yıllık fiyat geçişi örneği ve indirim rozeti.
---
```jsx
import React, { useState } from 'react';

export default function SegmentedControl() {
  const [period, setPeriod] = useState('aylik');
  const options = [
    { id: 'aylik', label: 'Aylık' },
    { id: 'yillik', label: 'Yıllık', badge: '%20' },
  ];
  const price = period === 'aylik' ? 149 : 119;
  return (
    <div className="flex flex-col items-center gap-6 p-10">
      <div role="radiogroup" aria-label="Ödeme dönemi" className="inline-flex rounded-full bg-slate-100 p-1">
        {options.map((o) => (
          <button
            key={o.id}
            type="button"
            role="radio"
            aria-checked={period === o.id}
            onClick={() => setPeriod(o.id)}
            className={`flex items-center gap-2 rounded-full px-5 py-2 text-sm font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${period === o.id ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'}`}
          >
            {o.label}
            {o.badge && <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-semibold text-emerald-700">{o.badge}</span>}
          </button>
        ))}
      </div>
      <p className="text-slate-600"><span className="text-4xl font-semibold tracking-tight text-slate-900">{price} ₺</span> / ay</p>
    </div>
  );
}
```
