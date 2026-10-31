---
title: Giriş formu (e-posta, şifre göster/gizle, beni hatırla, hata mesajı, sosyal giriş)
tags: form, login, giriş formu, oturum aç, şifre göster gizle, beni hatırla, doğrulama, hata mesajı, google ile giriş
description: Ortalanmış kart içinde giriş formu; label'lı alanlar, şifreyi göster/gizle düğmesi, boş alanda kırmızı hata metni (aria-invalid, aria-describedby), gönderirken yükleniyor durumu, ayraçlı "Google ile devam et".
---
```jsx
import React, { useState } from 'react';

export default function LoginForm() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [show, setShow] = useState(false);
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(false);

  const submit = (e) => {
    e.preventDefault();
    const next = {};
    if (!/^\S+@\S+\.\S+$/.test(email)) next.email = 'Geçerli bir e-posta yaz.';
    if (password.length < 6) next.password = 'Şifre en az 6 karakter olmalı.';
    setErrors(next);
    if (Object.keys(next).length === 0) { setLoading(true); setTimeout(() => setLoading(false), 1200); }
  };
  const input = 'mt-1.5 block w-full rounded-lg border px-3 py-2 text-sm text-slate-900 shadow-sm placeholder:text-slate-400 focus:outline-none focus:ring-2';
  return (
    <div className="grid min-h-[560px] place-items-center bg-slate-50 p-4">
      <form onSubmit={submit} noValidate className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <h1 className="text-xl font-semibold tracking-tight text-slate-900">Tekrar hoş geldin</h1>
        <p className="mt-1 text-sm text-slate-500">Hesabına giriş yap.</p>
        <div className="mt-6">
          <label htmlFor="eposta" className="text-sm font-medium text-slate-700">E-posta</label>
          <input id="eposta" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="ornek@mail.com"
            aria-invalid={!!errors.email} aria-describedby={errors.email ? 'eposta-hata' : undefined}
            className={`${input} ${errors.email ? 'border-rose-400 focus:ring-rose-500/30' : 'border-slate-200 focus:border-indigo-500 focus:ring-indigo-500/20'}`} />
          {errors.email && <p id="eposta-hata" className="mt-1 text-xs text-rose-600">{errors.email}</p>}
        </div>
        <div className="mt-4">
          <div className="flex items-center justify-between">
            <label htmlFor="sifre" className="text-sm font-medium text-slate-700">Şifre</label>
            <a href="#" className="text-xs font-medium text-indigo-600 hover:text-indigo-500">Şifremi unuttum</a>
          </div>
          <div className="relative">
            <input id="sifre" type={show ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)}
              aria-invalid={!!errors.password} aria-describedby={errors.password ? 'sifre-hata' : undefined}
              className={`${input} pr-16 ${errors.password ? 'border-rose-400 focus:ring-rose-500/30' : 'border-slate-200 focus:border-indigo-500 focus:ring-indigo-500/20'}`} />
            <button type="button" onClick={() => setShow(!show)} aria-label={show ? 'Şifreyi gizle' : 'Şifreyi göster'}
              className="absolute inset-y-0 right-0 mt-1.5 px-3 text-xs font-medium text-slate-500 hover:text-slate-700">{show ? 'Gizle' : 'Göster'}</button>
          </div>
          {errors.password && <p id="sifre-hata" className="mt-1 text-xs text-rose-600">{errors.password}</p>}
        </div>
        <label className="mt-4 flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" className="h-4 w-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" /> Beni hatırla
        </label>
        <button type="submit" disabled={loading} className="mt-6 w-full rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 disabled:opacity-60">
          {loading ? 'Giriş yapılıyor…' : 'Giriş yap'}
        </button>
        <div className="my-6 flex items-center gap-3 text-xs text-slate-400"><span className="h-px flex-1 bg-slate-200" />ya da<span className="h-px flex-1 bg-slate-200" /></div>
        <button type="button" className="flex w-full items-center justify-center gap-2 rounded-lg border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50">
          <span className="grid h-5 w-5 place-items-center rounded-full bg-slate-900 text-[10px] font-bold text-white" aria-hidden="true">G</span> Google ile devam et
        </button>
        <p className="mt-6 text-center text-sm text-slate-500">Hesabın yok mu? <a href="#" className="font-medium text-indigo-600 hover:text-indigo-500">Kayıt ol</a></p>
      </form>
    </div>
  );
}
```
