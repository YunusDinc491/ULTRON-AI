---
title: Çerez izni bandı (alt köşede kart, tercihleri yönet)
tags: cookie banner, çerez, kvkk, gizlilik, izin, onay bandı, consent
description: Sol altta (mobilde tam genişlik) çerez kartı: kısa açıklama, "Tümünü kabul et", "Reddet" ve "Tercihler" (açılınca analiz/pazarlama anahtarları); seçim sonrası kaybolur.
---
```jsx
import React, { useState } from 'react';

export default function CookieBanner() {
  const [visible, setVisible] = useState(true);
  const [prefs, setPrefs] = useState(false);
  const [choice, setChoice] = useState({ analiz: false, pazarlama: false });
  if (!visible) return <div className="p-6 text-sm text-slate-600">Tercihin kaydedildi. <button type="button" className="font-medium text-indigo-600" onClick={() => setVisible(true)}>Tekrar göster</button></div>;
  return (
    <div className="relative min-h-[380px]">
      <section role="dialog" aria-labelledby="cerez-baslik" className="fixed inset-x-4 bottom-4 z-50 rounded-2xl border border-slate-200 bg-white p-5 shadow-lg sm:inset-x-auto sm:left-4 sm:max-w-sm">
        <h2 id="cerez-baslik" className="font-semibold text-slate-900">Çerezleri kullanıyoruz</h2>
        <p className="mt-2 text-sm text-slate-600">Siteyi çalıştırmak için gerekli çerezler her zaman açık. Diğerleri için iznini istiyoruz.</p>
        {prefs && (
          <div className="mt-4 space-y-3">
            {[['analiz', 'Analiz çerezleri'], ['pazarlama', 'Pazarlama çerezleri']].map(([key, label]) => (
              <label key={key} className="flex items-center justify-between text-sm text-slate-700">
                {label}
                <input type="checkbox" checked={choice[key]} onChange={(e) => setChoice({ ...choice, [key]: e.target.checked })} className="h-4 w-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
              </label>
            ))}
          </div>
        )}
        <div className="mt-5 flex flex-wrap gap-2">
          <button type="button" onClick={() => setVisible(false)} className="flex-1 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500">{prefs ? 'Seçimi kaydet' : 'Tümünü kabul et'}</button>
          <button type="button" onClick={() => setVisible(false)} className="flex-1 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">Reddet</button>
          {!prefs && <button type="button" onClick={() => setPrefs(true)} aria-expanded={prefs} className="w-full rounded-lg px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100">Tercihleri yönet</button>}
        </div>
      </section>
    </div>
  );
}
```
