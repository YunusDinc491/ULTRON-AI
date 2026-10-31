---
title: Kayıt formu (ad soyad yan yana, şifre gücü göstergesi, koşulları kabul)
tags: form, register, kayıt formu, üye ol, şifre gücü, password strength, sözleşme onayı, iki sütun form
description: Ad ve soyad mobilde alt alta, geniş ekranda yan yana; şifre yazdıkça 4 parçalı güç çubuğu ve "Zayıf/Orta/Güçlü" metni; koşullar kabul edilmeden buton devre dışı.
---
```jsx
import React, { useState } from 'react';

function strength(p) {
  let s = 0;
  if (p.length >= 8) s++;
  if (/[A-ZÇĞİÖŞÜ]/.test(p)) s++;
  if (/\d/.test(p)) s++;
  if (/[^\w\s]/.test(p)) s++;
  return s;
}
const labels = ['Çok zayıf', 'Zayıf', 'Orta', 'İyi', 'Güçlü'];
const colors = ['bg-rose-500', 'bg-rose-500', 'bg-amber-500', 'bg-lime-500', 'bg-emerald-500'];

export default function RegisterForm() {
  const [form, setForm] = useState({ ad: '', soyad: '', eposta: '', sifre: '', kabul: false });
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value });
  const score = strength(form.sifre);
  const field = 'mt-1.5 block w-full rounded-lg border border-slate-200 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/20';
  return (
    <form className="mx-auto max-w-md space-y-4 p-6" onSubmit={(e) => e.preventDefault()}>
      <h1 className="text-xl font-semibold tracking-tight text-slate-900">Hesap oluştur</h1>
      <div className="grid gap-4 sm:grid-cols-2">
        <div><label htmlFor="ad" className="text-sm font-medium text-slate-700">Ad</label><input id="ad" autoComplete="given-name" value={form.ad} onChange={set('ad')} className={field} /></div>
        <div><label htmlFor="soyad" className="text-sm font-medium text-slate-700">Soyad</label><input id="soyad" autoComplete="family-name" value={form.soyad} onChange={set('soyad')} className={field} /></div>
      </div>
      <div><label htmlFor="k-eposta" className="text-sm font-medium text-slate-700">E-posta</label><input id="k-eposta" type="email" autoComplete="email" value={form.eposta} onChange={set('eposta')} className={field} /></div>
      <div>
        <label htmlFor="k-sifre" className="text-sm font-medium text-slate-700">Şifre</label>
        <input id="k-sifre" type="password" autoComplete="new-password" value={form.sifre} onChange={set('sifre')} aria-describedby="sifre-guc" className={field} />
        <div className="mt-2 flex gap-1" aria-hidden="true">
          {[0, 1, 2, 3].map((i) => <span key={i} className={`h-1.5 flex-1 rounded-full transition duration-200 ${i < score ? colors[score] : 'bg-slate-200'}`} />)}
        </div>
        <p id="sifre-guc" className="mt-1 text-xs text-slate-500">{form.sifre ? `Şifre gücü: ${labels[score]}` : 'En az 8 karakter, büyük harf, rakam ve sembol önerilir.'}</p>
      </div>
      <label className="flex items-start gap-2 text-sm text-slate-600">
        <input type="checkbox" checked={form.kabul} onChange={set('kabul')} className="mt-0.5 h-4 w-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
        <span><a href="#" className="font-medium text-indigo-600">Kullanım koşullarını</a> ve <a href="#" className="font-medium text-indigo-600">gizlilik politikasını</a> kabul ediyorum.</span>
      </label>
      <button type="submit" disabled={!form.kabul} className="w-full rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50">Kayıt ol</button>
    </form>
  );
}
```
