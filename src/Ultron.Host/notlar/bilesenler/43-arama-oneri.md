---
title: Öneri listeli arama kutusu (combobox): yazdıkça süzülür, ok tuşları ve Enter
tags: search, arama, autocomplete, otomatik tamamlama, combobox, öneriler, typeahead, klavye
description: Yazdıkça şehir listesini süzen arama kutusu; eşleşen kısım kalın; ↑/↓ ile gezilir, Enter seçer, Esc kapatır; role="combobox" + listbox; sonuç yoksa "Sonuç yok".
---
```jsx
import React, { useState } from 'react';

const cities = ['Adana', 'Ankara', 'Antalya', 'Bursa', 'Denizli', 'Diyarbakır', 'Eskişehir', 'Gaziantep', 'İstanbul', 'İzmir', 'Kayseri', 'Konya', 'Mersin', 'Samsun', 'Trabzon'];
const lower = (s) => s.toLocaleLowerCase('tr-TR');

export default function SearchCombobox() {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [selected, setSelected] = useState(null);
  const results = query ? cities.filter((c) => lower(c).includes(lower(query))) : cities.slice(0, 6);
  const choose = (c) => { setSelected(c); setQuery(c); setOpen(false); };
  const onKey = (e) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setActive((a) => Math.min(a + 1, results.length - 1)); }
    if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(a - 1, 0)); }
    if (e.key === 'Enter' && open && results[active]) { e.preventDefault(); choose(results[active]); }
    if (e.key === 'Escape') setOpen(false);
  };
  const highlight = (c) => {
    const i = lower(c).indexOf(lower(query));
    if (!query || i < 0) return c;
    return <>{c.slice(0, i)}<b className="font-semibold text-slate-900">{c.slice(i, i + query.length)}</b>{c.slice(i + query.length)}</>;
  };
  return (
    <div className="mx-auto max-w-sm p-8">
      <label htmlFor="sehir" className="text-sm font-medium text-slate-700">Şehir</label>
      <div className="relative mt-1.5">
        <input
          id="sehir"
          role="combobox"
          aria-expanded={open}
          aria-controls="sehir-liste"
          aria-autocomplete="list"
          aria-activedescendant={open && results[active] ? `sehir-${active}` : undefined}
          value={query}
          onChange={(e) => { setQuery(e.target.value); setOpen(true); setActive(0); }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 120)}
          onKeyDown={onKey}
          placeholder="Şehir ara…"
          className="block w-full rounded-lg border border-slate-200 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
        />
        {open && (
          <ul id="sehir-liste" role="listbox" className="absolute z-50 mt-1 max-h-60 w-full overflow-auto rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
            {results.length === 0 && <li className="px-3 py-2 text-sm text-slate-500">Sonuç yok</li>}
            {results.map((c, i) => (
              <li key={c} id={`sehir-${i}`} role="option" aria-selected={i === active} onMouseDown={() => choose(c)} onMouseEnter={() => setActive(i)}
                className={`cursor-pointer rounded-lg px-3 py-2 text-sm text-slate-600 ${i === active ? 'bg-indigo-50 text-indigo-700' : ''}`}>{highlight(c)}</li>
            ))}
          </ul>
        )}
      </div>
      {selected && <p className="mt-3 text-sm text-slate-600">Seçilen: <span className="font-medium text-slate-900">{selected}</span></p>}
    </div>
  );
}
```
