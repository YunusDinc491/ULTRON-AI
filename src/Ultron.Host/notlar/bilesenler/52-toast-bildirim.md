---
title: Bildirim (toast) yığını: başarı/hata/bilgi, kendiliğinden kaybolur
tags: toast, bildirim, snackbar, notification, başarı mesajı, hata mesajı, aria-live
description: Sağ üstte (mobilde altta tam genişlik) üst üste dizilen kısa bildirimler; türüne göre renkli ikon, kapat düğmesi, 4 sn sonra kendiliğinden kaybolur; aria-live="polite".
---
```jsx
import React, { useState } from 'react';

const styles = {
  success: { icon: 'M5 12l5 5L19 7', color: 'text-emerald-600 bg-emerald-50', title: 'Kaydedildi' },
  error: { icon: 'M12 8v5m0 3h.01M4.9 19h14.2a2 2 0 0 0 1.7-3L13.7 4a2 2 0 0 0-3.4 0L3.2 16a2 2 0 0 0 1.7 3z', color: 'text-rose-600 bg-rose-50', title: 'Bir hata oluştu' },
  info: { icon: 'M12 16v-5m0-3h.01M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z', color: 'text-sky-600 bg-sky-50', title: 'Bilgi' },
};

export default function Toasts() {
  const [toasts, setToasts] = useState([]);
  const push = (type, text) => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, type, text }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000);
  };
  return (
    <div className="min-h-[360px] p-6">
      <div className="flex flex-wrap gap-3">
        <button type="button" onClick={() => push('success', 'Değişikliklerin kaydedildi.')} className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white">Başarı</button>
        <button type="button" onClick={() => push('error', 'Bağlantı kurulamadı, tekrar dene.')} className="rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white">Hata</button>
        <button type="button" onClick={() => push('info', 'Yeni bir sürüm hazır.')} className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white">Bilgi</button>
      </div>
      <div aria-live="polite" className="fixed inset-x-4 bottom-4 z-50 flex flex-col gap-3 sm:inset-x-auto sm:bottom-auto sm:right-4 sm:top-4 sm:w-80">
        {toasts.map((t) => {
          const s = styles[t.type];
          return (
            <div key={t.id} role="status" className="flex items-start gap-3 rounded-xl border border-slate-200 bg-white p-4 shadow-lg">
              <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-full ${s.color}`}>
                <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={s.icon} /></svg>
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-slate-900">{s.title}</p>
                <p className="text-sm text-slate-600">{t.text}</p>
              </div>
              <button type="button" aria-label="Bildirimi kapat" onClick={() => setToasts(toasts.filter((x) => x.id !== t.id))} className="rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600">
                <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
```
