---
title: Uyarı kutuları (alert / banner): bilgi, başarı, uyarı, hata; kapatılabilir
tags: alert, uyarı, banner, bilgi kutusu, hata kutusu, başarı kutusu, callout, duyuru
description: Sol kenarı renkli dört tür uyarı kutusu (ikon, başlık, açıklama, isteğe bağlı bağlantı) ve kapat düğmesi; hata kutusu role="alert", diğerleri role="status".
---
```jsx
import React, { useState } from 'react';

const kinds = {
  info: { box: 'border-sky-500 bg-sky-50', text: 'text-sky-800', icon: 'M12 16v-5m0-3h.01' },
  success: { box: 'border-emerald-500 bg-emerald-50', text: 'text-emerald-800', icon: 'M5 12l5 5L19 7' },
  warning: { box: 'border-amber-500 bg-amber-50', text: 'text-amber-800', icon: 'M12 8v5m0 3h.01' },
  error: { box: 'border-rose-500 bg-rose-50', text: 'text-rose-800', icon: 'M15 9l-6 6m0-6 6 6' },
};

function Alert({ kind, title, children, onClose }) {
  const k = kinds[kind];
  return (
    <div role={kind === 'error' ? 'alert' : 'status'} className={`flex gap-3 rounded-lg border-l-4 p-4 ${k.box}`}>
      <svg viewBox="0 0 24 24" className={`mt-0.5 h-5 w-5 shrink-0 ${k.text}`} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d={k.icon} /></svg>
      <div className={`min-w-0 flex-1 text-sm ${k.text}`}>
        <p className="font-semibold">{title}</p>
        <p className="mt-1 opacity-90">{children}</p>
      </div>
      <button type="button" onClick={onClose} aria-label={`${title} uyarısını kapat`} className={`h-fit rounded-md p-1 hover:bg-black/5 ${k.text}`}>
        <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
      </button>
    </div>
  );
}

export default function Alerts() {
  const [visible, setVisible] = useState(['info', 'success', 'warning', 'error']);
  const text = {
    info: ['Bakım çalışması', 'Pazar 03:00–04:00 arası kısa kesinti olacak.'],
    success: ['Ödeme alındı', 'Faturan e-posta adresine gönderildi.'],
    warning: ['Depolama dolmak üzere', 'Kullanılan alan %90. Eski dosyaları temizleyebilirsin.'],
    error: ['Senkronizasyon başarısız', 'Sunucuya ulaşılamadı; internet bağlantını kontrol et.'],
  };
  return (
    <div className="mx-auto max-w-xl space-y-3 p-6">
      {visible.map((k) => <Alert key={k} kind={k} title={text[k][0]} onClose={() => setVisible(visible.filter((v) => v !== k))}>{text[k][1]}</Alert>)}
      {visible.length === 0 && <button type="button" onClick={() => setVisible(Object.keys(kinds))} className="text-sm font-medium text-indigo-600">Uyarıları geri getir</button>}
    </div>
  );
}
```
