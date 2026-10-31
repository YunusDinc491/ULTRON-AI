---
title: Veri tablosu (sıralanabilir sütunlar, durum rozetleri, satır seçimi, mobilde kaydırma)
tags: table, tablo, veri tablosu, data table, sıralama, sort, durum rozeti, checkbox seçim, siparişler
description: Siparişler tablosu; başlığa basınca sıralanır (aria-sort), durum rozetleri renkli, satırlar onay kutusuyla seçilir ve seçili sayısı görünür; dar ekranda yatay kaydırılır.
---
```jsx
import React, { useState } from 'react';

const rows = [
  { id: 'S-1042', customer: 'Ayşe Kaya', date: '2026-10-21', total: 1250, status: 'Teslim edildi' },
  { id: 'S-1043', customer: 'Mehmet Öz', date: '2026-10-22', total: 480, status: 'Kargoda' },
  { id: 'S-1044', customer: 'Can Yılmaz', date: '2026-10-23', total: 3120, status: 'Hazırlanıyor' },
  { id: 'S-1045', customer: 'Deniz Ak', date: '2026-10-24', total: 760, status: 'İptal' },
];
const badge = { 'Teslim edildi': 'bg-emerald-50 text-emerald-700', Kargoda: 'bg-sky-50 text-sky-700', Hazırlanıyor: 'bg-amber-50 text-amber-700', İptal: 'bg-rose-50 text-rose-700' };
const cols = [['id', 'Sipariş'], ['customer', 'Müşteri'], ['date', 'Tarih'], ['total', 'Tutar'], ['status', 'Durum']];

export default function OrdersTable() {
  const [sort, setSort] = useState({ key: 'date', dir: 'desc' });
  const [selected, setSelected] = useState([]);
  const sorted = [...rows].sort((a, b) => (a[sort.key] > b[sort.key] ? 1 : -1) * (sort.dir === 'asc' ? 1 : -1));
  const toggle = (id) => setSelected(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  return (
    <div className="mx-auto max-w-5xl p-6">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Siparişler</h2>
        {selected.length > 0 && <span className="text-sm text-slate-600">{selected.length} seçili</span>}
      </div>
      <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-white shadow-sm">
        <table className="w-full min-w-[640px] text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th className="w-10 px-4 py-3"><input type="checkbox" aria-label="Tümünü seç" checked={selected.length === rows.length} onChange={(e) => setSelected(e.target.checked ? rows.map((r) => r.id) : [])} className="h-4 w-4 rounded border-slate-300 text-indigo-600" /></th>
              {cols.map(([key, label]) => (
                <th key={key} scope="col" aria-sort={sort.key === key ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'} className="px-4 py-3 font-medium">
                  <button type="button" onClick={() => setSort({ key, dir: sort.key === key && sort.dir === 'asc' ? 'desc' : 'asc' })} className="inline-flex items-center gap-1 hover:text-slate-900">
                    {label}<span aria-hidden="true">{sort.key === key ? (sort.dir === 'asc' ? '↑' : '↓') : '↕'}</span>
                  </button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {sorted.map((r) => (
              <tr key={r.id} className={selected.includes(r.id) ? 'bg-indigo-50/50' : 'hover:bg-slate-50'}>
                <td className="px-4 py-3"><input type="checkbox" aria-label={`${r.id} siparişini seç`} checked={selected.includes(r.id)} onChange={() => toggle(r.id)} className="h-4 w-4 rounded border-slate-300 text-indigo-600" /></td>
                <td className="px-4 py-3 font-medium text-slate-900">{r.id}</td>
                <td className="px-4 py-3 text-slate-600">{r.customer}</td>
                <td className="px-4 py-3 text-slate-600">{new Date(r.date).toLocaleDateString('tr-TR')}</td>
                <td className="px-4 py-3 text-slate-900 tabular-nums">{r.total.toLocaleString('tr-TR')} ₺</td>
                <td className="px-4 py-3"><span className={`rounded-full px-2.5 py-1 text-xs font-medium ${badge[r.status]}`}>{r.status}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```
