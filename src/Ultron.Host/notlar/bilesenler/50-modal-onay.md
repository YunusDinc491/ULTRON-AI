---
title: Onay penceresi (modal / dialog): karartmalı arka plan, Esc ile kapanır, odak içeride
tags: modal, dialog, onay penceresi, açılır pencere, popup, silme onayı, overlay, focus trap
description: "Projeyi sil" butonu tehlike ikonlu onay penceresi açar; arka plan kararır, Esc ve karartmaya tıklama kapatır, açılınca odak İptal'e gider (role="dialog", aria-modal, aria-labelledby).
---
```jsx
import React, { useEffect, useRef, useState } from 'react';

export default function ConfirmModal() {
  const [open, setOpen] = useState(false);
  const [deleted, setDeleted] = useState(false);
  const cancel = useRef(null);
  useEffect(() => {
    if (!open) return;
    cancel.current?.focus();
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);
  return (
    <div className="grid min-h-[380px] place-items-center p-6">
      <div className="text-center">
        <button type="button" onClick={() => setOpen(true)} className="rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-rose-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-500 focus-visible:ring-offset-2">Projeyi sil</button>
        {deleted && <p role="status" className="mt-3 text-sm text-slate-600">Proje silindi.</p>}
      </div>
      {open && (
        <div className="fixed inset-0 z-50 grid place-items-center p-4">
          <div className="absolute inset-0 bg-slate-900/50" onClick={() => setOpen(false)} />
          <div role="dialog" aria-modal="true" aria-labelledby="modal-baslik" aria-describedby="modal-aciklama" className="relative w-full max-w-md rounded-2xl bg-white p-6 shadow-lg">
            <div className="flex gap-4">
              <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-rose-100 text-rose-600">
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M12 9v4m0 4h.01M10.3 3.9 2.6 17a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /></svg>
              </span>
              <div>
                <h2 id="modal-baslik" className="text-base font-semibold text-slate-900">Projeyi silmek istediğine emin misin?</h2>
                <p id="modal-aciklama" className="mt-2 text-sm text-slate-600">“Web sitesi yenileme” ve içindeki 38 dosya kalıcı olarak silinecek. Bu işlem geri alınamaz.</p>
              </div>
            </div>
            <div className="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
              <button ref={cancel} type="button" onClick={() => setOpen(false)} className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500">İptal</button>
              <button type="button" onClick={() => { setOpen(false); setDeleted(true); }} className="rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white hover:bg-rose-500">Evet, sil</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
```
