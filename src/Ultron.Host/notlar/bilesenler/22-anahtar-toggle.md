---
title: Açma/kapama anahtarı (toggle switch) ve ayar satırları
tags: toggle, switch, anahtar, açma kapama, ayarlar, bildirim ayarları, checkbox alternatifi
description: role="switch" ve aria-checked kullanan kaydırmalı anahtar; başlık ve açıklamalı ayar satırları içinde; tıklayınca ve boşlukla değişir.
---
```jsx
import React, { useState } from 'react';

function Switch({ checked, onChange, label }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 rounded-full transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 ${checked ? 'bg-indigo-600' : 'bg-slate-200'}`}
    >
      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition duration-200 ${checked ? 'left-[22px]' : 'left-0.5'}`} />
    </button>
  );
}

export default function SettingsToggles() {
  const [settings, setSettings] = useState({ email: true, push: false, weekly: true });
  const rows = [
    { key: 'email', title: 'E-posta bildirimleri', desc: 'Yeni yorum ve bahsetmelerde e-posta al.' },
    { key: 'push', title: 'Anlık bildirimler', desc: 'Telefonuna bildirim gönder.' },
    { key: 'weekly', title: 'Haftalık özet', desc: 'Her pazartesi kısa bir rapor.' },
  ];
  return (
    <div className="mx-auto max-w-xl p-6">
      <div className="divide-y divide-slate-100 rounded-2xl border border-slate-200 bg-white shadow-sm">
        {rows.map((r) => (
          <div key={r.key} className="flex items-center justify-between gap-4 p-4">
            <div>
              <p className="text-sm font-medium text-slate-900">{r.title}</p>
              <p className="text-sm text-slate-500">{r.desc}</p>
            </div>
            <Switch label={r.title} checked={settings[r.key]} onChange={(v) => setSettings({ ...settings, [r.key]: v })} />
          </div>
        ))}
      </div>
    </div>
  );
}
```
