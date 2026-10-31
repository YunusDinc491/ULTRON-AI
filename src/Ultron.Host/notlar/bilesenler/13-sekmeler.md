---
title: Sekmeler (alt çizgili tabs), ok tuşlarıyla gezilebilir
tags: tabs, sekmeler, sekme, tablist, alt çizgi, panel, klavye, erişilebilir sekme
description: Alt çizgili sekme çubuğu ve içerik panelleri; role="tablist/tab/tabpanel", sağ/sol ok tuşlarıyla sekme değişir; mobilde yatay kaydırılır.
---
```jsx
import React, { useRef, useState } from 'react';

const tabs = [
  { id: 'genel', label: 'Genel bakış', body: 'Projenin özeti, hedefler ve son durum.' },
  { id: 'gorevler', label: 'Görevler', body: '12 açık görev, 4 tanesi bu hafta bitmeli.' },
  { id: 'dosyalar', label: 'Dosyalar', body: 'Paylaşılan 38 dosya.' },
  { id: 'ayarlar', label: 'Ayarlar', body: 'Bildirimler ve izinler.' },
];

export default function Tabs() {
  const [active, setActive] = useState(0);
  const refs = useRef([]);
  const onKey = (e) => {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
    const next = (active + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length;
    setActive(next);
    refs.current[next]?.focus();
  };
  return (
    <div className="mx-auto max-w-2xl p-6">
      <div role="tablist" aria-label="Proje sekmeleri" className="flex gap-6 overflow-x-auto border-b border-slate-200" onKeyDown={onKey}>
        {tabs.map((t, i) => (
          <button
            key={t.id}
            ref={(el) => (refs.current[i] = el)}
            role="tab"
            id={`sekme-${t.id}`}
            aria-selected={active === i}
            aria-controls={`panel-${t.id}`}
            tabIndex={active === i ? 0 : -1}
            onClick={() => setActive(i)}
            className={`-mb-px whitespace-nowrap border-b-2 py-3 text-sm font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${active === i ? 'border-indigo-600 text-indigo-600' : 'border-transparent text-slate-500 hover:border-slate-300 hover:text-slate-700'}`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tabs.map((t, i) => (
        <div key={t.id} role="tabpanel" id={`panel-${t.id}`} aria-labelledby={`sekme-${t.id}`} hidden={active !== i} className="pt-5 text-sm text-slate-600">
          {t.body}
        </div>
      ))}
    </div>
  );
}
```
