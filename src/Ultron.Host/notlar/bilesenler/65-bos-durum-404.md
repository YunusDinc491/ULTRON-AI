---
title: Boş durum (henüz kayıt yok) ve 404 sayfası
tags: empty state, boş durum, kayıt yok, 404, sayfa bulunamadı, hata sayfası, ilk kullanım
description: Kesikli çerçeveli kutuda ikon, "Henüz projen yok" başlığı, açıklama ve "Yeni proje" butonu; altında büyük 404, açıklama ve "Ana sayfaya dön" / "Destekle iletişim" butonları.
---
```jsx
import React from 'react';

export default function EmptyAnd404() {
  return (
    <div className="mx-auto max-w-3xl space-y-12 p-6">
      <div className="flex flex-col items-center rounded-2xl border-2 border-dashed border-slate-200 px-6 py-12 text-center">
        <span className="grid h-14 w-14 place-items-center rounded-2xl bg-indigo-50 text-indigo-600">
          <svg viewBox="0 0 24 24" className="h-7 w-7" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7zM12 11v4m-2-2h4" /></svg>
        </span>
        <h2 className="mt-4 text-lg font-semibold text-slate-900">Henüz projen yok</h2>
        <p className="mt-2 max-w-sm text-sm text-slate-600">İlk projeni oluştur; görevlerini, dosyalarını ve ekibini tek yerde topla.</p>
        <button type="button" className="mt-6 inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-500">
          <span aria-hidden="true">+</span> Yeni proje
        </button>
      </div>
      <section className="py-10 text-center">
        <p className="text-sm font-semibold text-indigo-600">404</p>
        <h1 className="mt-3 text-4xl font-semibold tracking-tight text-slate-900 sm:text-5xl">Sayfa bulunamadı</h1>
        <p className="mt-4 text-slate-600">Aradığın sayfa taşınmış ya da hiç var olmamış olabilir.</p>
        <div className="mt-8 flex flex-col justify-center gap-3 sm:flex-row">
          <a href="#" className="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-indigo-500">Ana sayfaya dön</a>
          <a href="#" className="rounded-lg px-5 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-100">Destekle iletişim →</a>
        </div>
      </section>
    </div>
  );
}
```
