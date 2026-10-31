---
title: Buton seti · birincil, ikincil, hayalet, tehlike; üç boyut; yükleniyor ve devre dışı durumları
tags: button, buton, düğme, primary, secondary, ghost, danger, boyutlar, loading, spinner, disabled, buton varyantları
description: Tek bir Button bileşeni (variant: primary/secondary/ghost/danger, size: sm/md/lg, loading, disabled) ve hepsinin vitrinini gösteren sayfa; yükleniyorken dönen ikon ve aria-busy.
---
```jsx
import React, { useState } from 'react';

const variants = {
  primary: 'bg-indigo-600 text-white shadow-sm hover:bg-indigo-500',
  secondary: 'border border-slate-200 bg-white text-slate-700 shadow-sm hover:bg-slate-50',
  ghost: 'text-slate-700 hover:bg-slate-100',
  danger: 'bg-rose-600 text-white shadow-sm hover:bg-rose-500',
};
const sizes = { sm: 'h-8 px-3 text-xs', md: 'h-10 px-4 text-sm', lg: 'h-12 px-6 text-base' };

function Button({ variant = 'primary', size = 'md', loading = false, disabled = false, children, ...rest }) {
  return (
    <button
      type="button"
      disabled={disabled || loading}
      aria-busy={loading}
      className={`inline-flex items-center justify-center gap-2 rounded-lg font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ${variants[variant]} ${sizes[size]}`}
      {...rest}
    >
      {loading && (
        <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="3" className="opacity-25" />
          <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
        </svg>
      )}
      {children}
    </button>
  );
}

export default function ButtonShowcase() {
  const [saving, setSaving] = useState(false);
  const save = () => { setSaving(true); setTimeout(() => setSaving(false), 1500); };
  return (
    <div className="space-y-6 p-8">
      <div className="flex flex-wrap items-center gap-3">
        <Button>Birincil</Button>
        <Button variant="secondary">İkincil</Button>
        <Button variant="ghost">Hayalet</Button>
        <Button variant="danger">Sil</Button>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button size="sm">Küçük</Button>
        <Button size="md">Orta</Button>
        <Button size="lg">Büyük</Button>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button loading={saving} onClick={save}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</Button>
        <Button variant="secondary" disabled>Devre dışı</Button>
      </div>
    </div>
  );
}
```
