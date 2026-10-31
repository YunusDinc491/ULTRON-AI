---
title: Profil kartı (kapak, avatar, istatistikler, takip et butonu)
tags: card, profil kartı, kullanıcı kartı, avatar, takipçi, takip et, sosyal, ekip üyesi
description: Üstte gradyan kapak, ortada üstüne binen yuvarlak avatar, ad ve unvan, üç istatistik ve "Takip et / Takiptesin" geçişli buton + mesaj butonu.
---
```jsx
import React, { useState } from 'react';

export default function ProfileCard() {
  const [following, setFollowing] = useState(false);
  const stats = [{ label: 'Proje', value: 24 }, { label: 'Takipçi', value: following ? 1281 : 1280 }, { label: 'Takip', value: 312 }];
  return (
    <div className="grid min-h-[440px] place-items-center bg-slate-50 p-6">
      <article className="w-full max-w-sm overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div className="h-24 bg-gradient-to-r from-indigo-500 via-violet-500 to-fuchsia-500" aria-hidden="true" />
        <div className="-mt-10 flex flex-col items-center px-6 pb-6 text-center">
          <span className="grid h-20 w-20 place-items-center rounded-full bg-gradient-to-br from-amber-300 to-rose-400 text-2xl font-semibold text-white ring-4 ring-white" role="img" aria-label="Deniz Aksoy profil fotoğrafı">DA</span>
          <h2 className="mt-3 text-lg font-semibold text-slate-900">Deniz Aksoy</h2>
          <p className="text-sm text-slate-500">Ürün tasarımcısı · İstanbul</p>
          <dl className="mt-5 grid w-full grid-cols-3 divide-x divide-slate-100 rounded-xl bg-slate-50 py-3">
            {stats.map((s) => (
              <div key={s.label}>
                <dt className="text-xs text-slate-500">{s.label}</dt>
                <dd className="font-semibold text-slate-900">{s.value.toLocaleString('tr-TR')}</dd>
              </div>
            ))}
          </dl>
          <div className="mt-5 flex w-full gap-3">
            <button type="button" aria-pressed={following} onClick={() => setFollowing(!following)}
              className={`flex-1 rounded-lg px-4 py-2 text-sm font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 ${following ? 'border border-slate-200 bg-white text-slate-700' : 'bg-indigo-600 text-white hover:bg-indigo-500'}`}>
              {following ? 'Takiptesin' : 'Takip et'}
            </button>
            <button type="button" className="flex-1 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">Mesaj</button>
          </div>
        </div>
      </article>
    </div>
  );
}
```
