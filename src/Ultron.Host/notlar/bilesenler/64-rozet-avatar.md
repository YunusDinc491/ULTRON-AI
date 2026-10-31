---
title: Rozetler (badge), etiketler (silinebilir chip) ve avatar grubu
tags: badge, rozet, etiket, chip, tag, avatar, avatar grubu, çevrimiçi durumu, kullanıcı yığını
description: Renkli durum rozetleri (noktalı), silinebilir etiket çipleri ve üst üste binen avatar grubu (+3 sayacı), çevrimiçi yeşil nokta.
---
```jsx
import React, { useState } from 'react';

const people = [
  { name: 'Ali Veli', color: 'from-sky-400 to-indigo-500', online: true },
  { name: 'Buse Er', color: 'from-amber-400 to-rose-500' },
  { name: 'Cenk Tan', color: 'from-emerald-400 to-teal-500', online: true },
  { name: 'Dilan Ay', color: 'from-fuchsia-400 to-violet-500' },
];

export default function BadgesAvatars() {
  const [tags, setTags] = useState(['React', 'Tailwind', 'Tasarım', 'Mobil']);
  return (
    <div className="space-y-8 p-8">
      <div className="flex flex-wrap gap-2">
        {[['Aktif', 'bg-emerald-50 text-emerald-700', 'bg-emerald-500'], ['Beklemede', 'bg-amber-50 text-amber-700', 'bg-amber-500'], ['Kapalı', 'bg-slate-100 text-slate-600', 'bg-slate-400'], ['Yeni', 'bg-indigo-50 text-indigo-700', 'bg-indigo-500']].map(([t, c, d]) => (
          <span key={t} className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${c}`}><span className={`h-1.5 w-1.5 rounded-full ${d}`} aria-hidden="true" />{t}</span>
        ))}
      </div>
      <ul className="flex flex-wrap gap-2" aria-label="Etiketler">
        {tags.map((t) => (
          <li key={t} className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white py-1 pl-3 pr-1 text-sm text-slate-700">
            {t}
            <button type="button" aria-label={`${t} etiketini kaldır`} onClick={() => setTags(tags.filter((x) => x !== t))} className="rounded-md p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600">
              <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
            </button>
          </li>
        ))}
      </ul>
      <div className="flex items-center">
        <div className="flex -space-x-3">
          {people.map((p) => (
            <span key={p.name} className="relative" title={p.name}>
              <span className={`grid h-10 w-10 place-items-center rounded-full bg-gradient-to-br ${p.color} text-xs font-semibold text-white ring-2 ring-white`} role="img" aria-label={`${p.name}${p.online ? ', çevrimiçi' : ''}`}>{p.name.split(' ').map((w) => w[0]).join('')}</span>
              {p.online && <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full bg-emerald-500 ring-2 ring-white" aria-hidden="true" />}
            </span>
          ))}
          <span className="grid h-10 w-10 place-items-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600 ring-2 ring-white">+3</span>
        </div>
        <p className="ml-4 text-sm text-slate-600">7 kişi bu projede</p>
      </div>
    </div>
  );
}
```
