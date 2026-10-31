---
title: Dosya yükleme alanı (sürükle bırak, seçilen dosyalar listesi, ilerleme çubuğu)
tags: file upload, dosya yükleme, sürükle bırak, drag and drop, dropzone, ilerleme çubuğu, progress
description: Kesikli çerçeveli bırakma alanı (üzerine gelince renklenir), "dosya seç" bağlantısı; seçilen dosyalar boyutuyla listelenir, sahte ilerleme çubuğu dolar, her dosya kaldırılabilir.
---
```jsx
import React, { useRef, useState } from 'react';

const size = (b) => (b > 1e6 ? (b / 1e6).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1e3)) + ' KB');

export default function FileDropzone() {
  const [files, setFiles] = useState([]);
  const [over, setOver] = useState(false);
  const input = useRef(null);
  const add = (list) => {
    const next = [...list].map((f) => ({ name: f.name, size: f.size, progress: 0, id: f.name + f.size + Math.random() }));
    setFiles((prev) => [...prev, ...next]);
    next.forEach((f) => {
      const t = setInterval(() => setFiles((prev) => prev.map((p) => {
        if (p.id !== f.id) return p;
        const progress = Math.min(100, p.progress + 20);
        if (progress === 100) clearInterval(t);
        return { ...p, progress };
      })), 300);
    });
  };
  return (
    <div className="mx-auto max-w-lg p-6">
      <div
        onDragOver={(e) => { e.preventDefault(); setOver(true); }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => { e.preventDefault(); setOver(false); add(e.dataTransfer.files); }}
        className={`flex flex-col items-center rounded-2xl border-2 border-dashed px-6 py-10 text-center transition duration-200 ${over ? 'border-indigo-500 bg-indigo-50' : 'border-slate-300 bg-white'}`}
      >
        <svg viewBox="0 0 24 24" className="h-10 w-10 text-slate-400" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 16V4m0 0-4 4m4-4 4 4M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" /></svg>
        <p className="mt-3 text-sm text-slate-600">Dosyaları buraya sürükle ya da{' '}
          <button type="button" onClick={() => input.current?.click()} className="font-medium text-indigo-600 hover:text-indigo-500">bilgisayardan seç</button>
        </p>
        <p className="mt-1 text-xs text-slate-400">PNG, JPG, PDF · en fazla 10 MB</p>
        <input ref={input} type="file" multiple className="sr-only" aria-label="Dosya seç" onChange={(e) => add(e.target.files)} />
      </div>
      {files.length > 0 && (
        <ul className="mt-4 space-y-2">
          {files.map((f) => (
            <li key={f.id} className="rounded-xl border border-slate-200 bg-white p-3">
              <div className="flex items-center justify-between gap-3">
                <span className="truncate text-sm font-medium text-slate-800">{f.name}</span>
                <button type="button" aria-label={`${f.name} dosyasını kaldır`} onClick={() => setFiles(files.filter((x) => x.id !== f.id))} className="rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600">
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
                </button>
              </div>
              <div className="mt-2 flex items-center gap-3">
                <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-valuenow={f.progress} aria-valuemin={0} aria-valuemax={100} aria-label={`${f.name} yükleniyor`}>
                  <div className={`h-full rounded-full transition-all duration-300 ${f.progress === 100 ? 'bg-emerald-500' : 'bg-indigo-600'}`} style={{ width: `${f.progress}%` }} />
                </div>
                <span className="w-16 text-right text-xs text-slate-500">{f.progress === 100 ? size(f.size) : `%${f.progress}`}</span>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```
