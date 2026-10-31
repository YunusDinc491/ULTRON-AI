---
title: İletişim formu ve iletişim bilgileri (iki sütunlu bölüm, konu seçimi, gönderildi mesajı)
tags: form, iletişim formu, contact, mesaj gönder, select, textarea, karakter sayacı, başarı mesajı
description: Solda iletişim bilgileri (adres, telefon, e-posta), sağda form: ad, e-posta, konu seçimi, karakter sayaçlı mesaj; gönderince yeşil "Mesajın alındı" kutusu (role="status").
---
```jsx
import React, { useState } from 'react';

export default function ContactSection() {
  const [message, setMessage] = useState('');
  const [sent, setSent] = useState(false);
  const field = 'mt-1.5 block w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/20';
  return (
    <section className="mx-auto grid max-w-5xl gap-10 px-4 py-12 sm:px-6 lg:grid-cols-5">
      <div className="lg:col-span-2">
        <h2 className="text-2xl font-semibold tracking-tight text-slate-900">Bize ulaş</h2>
        <p className="mt-3 text-slate-600">Sorun, önerin ya da iş birliği fikrin mi var? Bir gün içinde dönüyoruz.</p>
        <dl className="mt-8 space-y-4 text-sm">
          {[['Adres', 'Kadıköy, İstanbul'], ['Telefon', '+90 212 000 00 00'], ['E-posta', 'merhaba@ornek.com']].map(([k, v]) => (
            <div key={k}><dt className="font-medium text-slate-900">{k}</dt><dd className="text-slate-600">{v}</dd></div>
          ))}
        </dl>
      </div>
      <form className="space-y-4 rounded-2xl border border-slate-200 bg-slate-50 p-6 lg:col-span-3" onSubmit={(e) => { e.preventDefault(); setSent(true); }}>
        {sent && <p role="status" className="rounded-lg bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-700">Mesajın alındı, teşekkürler!</p>}
        <div className="grid gap-4 sm:grid-cols-2">
          <div><label htmlFor="i-ad" className="text-sm font-medium text-slate-700">Adın</label><input id="i-ad" required className={field} /></div>
          <div><label htmlFor="i-eposta" className="text-sm font-medium text-slate-700">E-posta</label><input id="i-eposta" type="email" required className={field} /></div>
        </div>
        <div>
          <label htmlFor="konu" className="text-sm font-medium text-slate-700">Konu</label>
          <select id="konu" className={field} defaultValue="genel">
            <option value="genel">Genel soru</option><option value="destek">Teknik destek</option><option value="is">İş birliği</option>
          </select>
        </div>
        <div>
          <label htmlFor="mesaj" className="text-sm font-medium text-slate-700">Mesajın</label>
          <textarea id="mesaj" rows={4} maxLength={500} value={message} onChange={(e) => setMessage(e.target.value)} aria-describedby="sayac" className={field} />
          <p id="sayac" className="mt-1 text-right text-xs text-slate-400">{message.length}/500</p>
        </div>
        <button type="submit" className="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-500">Gönder</button>
      </form>
    </section>
  );
}
```
