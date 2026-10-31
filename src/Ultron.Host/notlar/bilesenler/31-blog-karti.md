---
title: Blog yazısı kartları (kategori, başlık, özet, yazar ve okuma süresi)
tags: card, blog kartı, makale kartı, yazı listesi, yazar, okuma süresi, kategori etiketi
description: Üstte renkli kapak alanı, kategori rozeti, iki satırla sınırlı başlık ve özet, altta yazar baş harfi, tarih ve okuma süresi; kartın tamamı tıklanabilir.
---
```jsx
import React from 'react';

const posts = [
  { title: 'Tailwind ile tasarım sistemi kurmak', excerpt: 'Renk, boşluk ve yazı ölçülerini baştan belirlemek tutarlı arayüzün yarısı.', tag: 'Tasarım', author: 'Elif Demir', date: '12 Eki', read: 6, color: 'from-indigo-500 to-violet-500' },
  { title: 'React’te gereksiz render’ları azaltmak', excerpt: 'useMemo ve useCallback’i ne zaman kullanmalı, ne zaman kullanmamalı?', tag: 'React', author: 'Mert Can', date: '9 Eki', read: 8, color: 'from-sky-500 to-cyan-500' },
  { title: 'Mobil öncelikli düşünmek', excerpt: 'Küçük ekrandan başlayıp büyütmek neden daha az kod demek?', tag: 'Web', author: 'Ayşe Yılmaz', date: '3 Eki', read: 5, color: 'from-amber-500 to-rose-500' },
];

export default function BlogCards() {
  return (
    <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
      <h2 className="text-2xl font-semibold tracking-tight text-slate-900">Son yazılar</h2>
      <div className="mt-6 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
        {posts.map((p) => (
          <a key={p.title} href="#" className="group flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition duration-200 hover:-translate-y-0.5 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500">
            <div className={`h-36 bg-gradient-to-br ${p.color}`} aria-hidden="true" />
            <div className="flex flex-1 flex-col p-5">
              <span className="w-fit rounded-full bg-indigo-50 px-2.5 py-0.5 text-xs font-medium text-indigo-700">{p.tag}</span>
              <h3 className="mt-3 line-clamp-2 font-semibold text-slate-900 group-hover:text-indigo-600">{p.title}</h3>
              <p className="mt-2 line-clamp-2 text-sm text-slate-600">{p.excerpt}</p>
              <div className="mt-auto flex items-center gap-3 pt-5 text-xs text-slate-500">
                <span className="grid h-7 w-7 place-items-center rounded-full bg-slate-100 font-semibold text-slate-700">{p.author.split(' ').map((w) => w[0]).join('')}</span>
                <span className="font-medium text-slate-700">{p.author}</span>
                <span aria-hidden="true">·</span>
                <span>{p.date}</span>
                <span aria-hidden="true">·</span>
                <span>{p.read} dk</span>
              </div>
            </div>
          </a>
        ))}
      </div>
    </section>
  );
}
```
