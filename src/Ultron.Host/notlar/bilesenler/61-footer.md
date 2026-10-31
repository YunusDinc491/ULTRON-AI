---
title: Alt bilgi (footer): link sütunları, bülten kutusu, sosyal medya ikonları, telif satırı
tags: footer, alt bilgi, site altı, link sütunları, sosyal medya, bülten, copyright
description: Koyu zeminli footer; solda logo ve kısa tanıtım, ortada 3 link sütunu, sağda e-posta bülten formu; altta telif ve sosyal ikonlar (aria-label'lı); mobilde sütunlar alt alta.
---
```jsx
import React from 'react';

const columns = [
  { title: 'Ürün', links: ['Özellikler', 'Fiyatlar', 'Güncellemeler', 'Yol haritası'] },
  { title: 'Şirket', links: ['Hakkımızda', 'Kariyer', 'Basın', 'İletişim'] },
  { title: 'Destek', links: ['Yardım merkezi', 'Gizlilik', 'Koşullar', 'Durum'] },
];
const socials = [
  { name: 'X (Twitter)', d: 'M4 4l16 16M20 4 4 20' },
  { name: 'Instagram', d: 'M7 3h10a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4zm5 13a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM17.5 6.5h.01' },
  { name: 'LinkedIn', d: 'M4 9h4v11H4zM6 4a2 2 0 1 1 0 4 2 2 0 0 1 0-4zM11 9h4v2c.6-1.2 2-2 3.5-2 2.5 0 3.5 1.6 3.5 4.5V20h-4v-6c0-1.4-.5-2.2-1.7-2.2-1.4 0-2.3 1-2.3 2.5V20h-3z' },
];

export default function Footer() {
  return (
    <footer className="bg-slate-900 text-slate-400">
      <div className="mx-auto grid max-w-7xl gap-10 px-4 py-14 sm:px-6 lg:grid-cols-12 lg:px-8">
        <div className="lg:col-span-3">
          <p className="font-semibold tracking-tight text-white">Ultron</p>
          <p className="mt-3 text-sm leading-relaxed">Ekiplerin işini sadeleştiren çalışma alanı.</p>
        </div>
        <div className="grid grid-cols-2 gap-8 sm:grid-cols-3 lg:col-span-5">
          {columns.map((c) => (
            <div key={c.title}>
              <h3 className="text-sm font-semibold text-white">{c.title}</h3>
              <ul className="mt-4 space-y-2.5">{c.links.map((l) => <li key={l}><a href="#" className="text-sm hover:text-white">{l}</a></li>)}</ul>
            </div>
          ))}
        </div>
        <form className="lg:col-span-4" onSubmit={(e) => e.preventDefault()}>
          <h3 className="text-sm font-semibold text-white">Bültene katıl</h3>
          <p className="mt-2 text-sm">Ayda bir, sadece işe yarayan haberler.</p>
          <div className="mt-4 flex gap-2">
            <label htmlFor="bulten" className="sr-only">E-posta adresin</label>
            <input id="bulten" type="email" placeholder="ornek@mail.com" className="min-w-0 flex-1 rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-white placeholder:text-slate-500 focus:border-indigo-400 focus:outline-none" />
            <button type="submit" className="rounded-lg bg-indigo-500 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-400">Katıl</button>
          </div>
        </form>
      </div>
      <div className="border-t border-slate-800">
        <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 px-4 py-6 text-sm sm:flex-row sm:px-6 lg:px-8">
          <p>© 2026 Ultron. Tüm hakları saklıdır.</p>
          <div className="flex gap-3">
            {socials.map((s) => (
              <a key={s.name} href="#" aria-label={s.name} className="rounded-lg p-2 hover:bg-slate-800 hover:text-white">
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={s.d} /></svg>
              </a>
            ))}
          </div>
        </div>
      </div>
    </footer>
  );
}
```
