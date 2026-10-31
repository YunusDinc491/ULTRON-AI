---
title: İstatistik kartları (KPI): değer, değişim yüzdesi ve küçük çizgi grafik
tags: stats, istatistik, kpi, dashboard kartı, metrik, gelir, değişim, sparkline, grafik
description: Panel üstü için 4 metrik kartı: başlık, büyük değer, yeşil/kırmızı değişim rozeti (ok ikonlu) ve SVG küçük çizgi grafik; duyarlı ızgara.
---
```jsx
import React from 'react';

const stats = [
  { label: 'Gelir', value: '₺248.900', change: 12.4, data: [4, 6, 5, 8, 7, 9, 11] },
  { label: 'Yeni müşteri', value: '1.284', change: 5.1, data: [3, 4, 4, 5, 6, 6, 7] },
  { label: 'İade oranı', value: '%2,4', change: -0.8, data: [6, 5, 5, 4, 4, 3, 3] },
  { label: 'Ort. sepet', value: '₺612', change: -3.2, data: [8, 7, 7, 6, 7, 6, 5] },
];

function Spark({ data, up }) {
  const max = Math.max(...data), min = Math.min(...data);
  const pts = data.map((v, i) => `${(i / (data.length - 1)) * 100},${28 - ((v - min) / (max - min || 1)) * 24}`).join(' ');
  return (
    <svg viewBox="0 0 100 30" className="h-8 w-24" preserveAspectRatio="none" aria-hidden="true">
      <polyline points={pts} fill="none" stroke={up ? '#10b981' : '#f43f5e'} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

export default function StatCards() {
  return (
    <div className="mx-auto grid max-w-7xl gap-4 p-6 sm:grid-cols-2 lg:grid-cols-4">
      {stats.map((s) => {
        const up = s.change >= 0;
        return (
          <div key={s.label} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p className="text-sm font-medium text-slate-500">{s.label}</p>
            <div className="mt-2 flex items-end justify-between gap-3">
              <p className="text-2xl font-semibold tracking-tight text-slate-900">{s.value}</p>
              <Spark data={s.data} up={up} />
            </div>
            <p className={`mt-3 inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${up ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-50 text-rose-700'}`}>
              <svg viewBox="0 0 20 20" className={`h-3 w-3 ${up ? '' : 'rotate-180'}`} fill="currentColor" aria-hidden="true"><path d="M10 4l6 7h-4v5H8v-5H4z" /></svg>
              {up ? '+' : ''}{s.change.toLocaleString('tr-TR')}% <span className="sr-only">{up ? 'artış' : 'düşüş'}</span> geçen haftaya göre
            </p>
          </div>
        );
      })}
    </div>
  );
}
```
