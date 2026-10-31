---
title: İkonlu butonlar · sadece ikon (aria-label), ikon + metin, yuvarlak FAB
tags: icon button, ikon buton, ikonlu düğme, fab, yüzen buton, floating action button, beğen, paylaş, ekle
description: Sadece ikonlu kare/yuvarlak butonlar (her birinde aria-label), solunda ikon olan metinli butonlar ve sağ altta yüzen yuvarlak "+" butonu; beğen butonu basınca dolar.
---
```jsx
import React, { useState } from 'react';

const Icon = ({ d, className = 'h-5 w-5' }) => (
  <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={d} /></svg>
);
const icons = {
  heart: 'M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10z',
  share: 'M4 12v7a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-7M12 3v12M8 7l4-4 4 4',
  trash: 'M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13',
  plus: 'M12 5v14M5 12h14',
  download: 'M12 4v11m0 0-4-4m4 4 4-4M5 20h14',
};

export default function IconButtons() {
  const [liked, setLiked] = useState(false);
  return (
    <div className="relative min-h-[300px] space-y-6 p-8">
      <div className="flex items-center gap-2">
        <button type="button" aria-label={liked ? 'Beğeniyi geri al' : 'Beğen'} aria-pressed={liked} onClick={() => setLiked(!liked)}
          className={`rounded-lg p-2 transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${liked ? 'bg-rose-50 text-rose-600' : 'text-slate-500 hover:bg-slate-100'}`}>
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill={liked ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d={icons.heart} /></svg>
        </button>
        <button type="button" aria-label="Paylaş" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"><Icon d={icons.share} /></button>
        <button type="button" aria-label="Sil" className="rounded-lg p-2 text-slate-500 hover:bg-rose-50 hover:text-rose-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"><Icon d={icons.trash} /></button>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <button type="button" className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-500"><Icon d={icons.plus} className="h-4 w-4" />Yeni ekle</button>
        <button type="button" className="inline-flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 shadow-sm hover:bg-slate-50"><Icon d={icons.download} className="h-4 w-4" />İndir</button>
      </div>
      <button type="button" aria-label="Yeni not" className="absolute bottom-6 right-6 grid h-14 w-14 place-items-center rounded-full bg-indigo-600 text-white shadow-lg transition duration-200 hover:scale-105 hover:bg-indigo-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2">
        <Icon d={icons.plus} className="h-6 w-6" />
      </button>
    </div>
  );
}
```
