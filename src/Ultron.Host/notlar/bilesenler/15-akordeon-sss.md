---
title: Akordeon · sıkça sorulan sorular (SSS)
tags: accordion, akordeon, sss, faq, sıkça sorulan sorular, açılır kapanır, collapse
description: Soruya basınca cevabı açılan, artı işareti döndüren SSS listesi; aynı anda tek soru açık; aria-expanded ve aria-controls ile erişilebilir.
---
```jsx
import React, { useState } from 'react';

const faqs = [
  { q: 'Ücretsiz deneme süresi var mı?', a: 'Evet, 14 gün boyunca bütün özellikleri kredi kartı olmadan deneyebilirsin.' },
  { q: 'Planımı sonradan değiştirebilir miyim?', a: 'İstediğin zaman yükseltebilir ya da düşürebilirsin; fark bir sonraki faturaya yansır.' },
  { q: 'Verilerim nerede saklanıyor?', a: 'Avrupa’daki veri merkezlerinde, şifrelenmiş olarak.' },
  { q: 'İptal edersem ne olur?', a: 'Dönem sonuna kadar erişimin sürer, verilerini dışa aktarabilirsin.' },
];

export default function FaqAccordion() {
  const [open, setOpen] = useState(0);
  return (
    <section className="mx-auto max-w-2xl px-4 py-12">
      <h2 className="text-2xl font-semibold tracking-tight text-slate-900">Sıkça sorulan sorular</h2>
      <div className="mt-6 divide-y divide-slate-200 border-y border-slate-200">
        {faqs.map((f, i) => {
          const isOpen = open === i;
          return (
            <div key={f.q}>
              <h3>
                <button
                  type="button"
                  className="flex w-full items-center justify-between gap-4 py-4 text-left text-base font-medium text-slate-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
                  aria-expanded={isOpen}
                  aria-controls={`sss-${i}`}
                  onClick={() => setOpen(isOpen ? -1 : i)}
                >
                  {f.q}
                  <svg viewBox="0 0 24 24" className={`h-5 w-5 shrink-0 text-slate-400 transition duration-200 ${isOpen ? 'rotate-45' : ''}`} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
                </button>
              </h3>
              <div id={`sss-${i}`} hidden={!isOpen} className="pb-4 text-sm leading-relaxed text-slate-600">{f.a}</div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
```
