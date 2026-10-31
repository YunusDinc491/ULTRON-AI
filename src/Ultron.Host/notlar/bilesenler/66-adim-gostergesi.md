---
title: Adım göstergesi (stepper) ile çok adımlı form: ileri / geri
tags: stepper, adım göstergesi, progress steps, çok adımlı form, wizard, sihirbaz, ödeme adımları
description: Üstte numaralı adımlar (tamamlanan tikli, şu anki vurgulu, aria-current="step"), altında adımın içeriği ve Geri/İleri butonları; son adımda "Tamamla".
---
```jsx
import React, { useState } from 'react';

const steps = ['Hesap', 'Adres', 'Ödeme', 'Onay'];

export default function Stepper() {
  const [step, setStep] = useState(0);
  return (
    <div className="mx-auto max-w-2xl p-6">
      <ol className="flex items-center">
        {steps.map((s, i) => (
          <li key={s} className={`flex items-center ${i < steps.length - 1 ? 'flex-1' : ''}`} aria-current={i === step ? 'step' : undefined}>
            <div className="flex flex-col items-center gap-1">
              <span className={`grid h-9 w-9 place-items-center rounded-full text-sm font-semibold transition duration-200 ${i < step ? 'bg-indigo-600 text-white' : i === step ? 'border-2 border-indigo-600 bg-white text-indigo-600' : 'border-2 border-slate-200 bg-white text-slate-400'}`}>
                {i < step ? <svg viewBox="0 0 20 20" className="h-4 w-4" fill="currentColor" aria-hidden="true"><path fillRule="evenodd" d="M16.7 5.3a1 1 0 0 1 0 1.4l-8 8a1 1 0 0 1-1.4 0l-4-4a1 1 0 1 1 1.4-1.4L8 12.6l7.3-7.3a1 1 0 0 1 1.4 0z" clipRule="evenodd" /></svg> : i + 1}
              </span>
              <span className={`hidden text-xs font-medium sm:block ${i <= step ? 'text-slate-900' : 'text-slate-400'}`}>{s}</span>
            </div>
            {i < steps.length - 1 && <span className={`mx-2 mb-5 h-0.5 flex-1 rounded-full sm:mb-6 ${i < step ? 'bg-indigo-600' : 'bg-slate-200'}`} aria-hidden="true" />}
          </li>
        ))}
      </ol>
      <div className="mt-8 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h2 className="font-semibold text-slate-900">{step + 1}. {steps[step]}</h2>
        <p className="mt-2 text-sm text-slate-600">{['Ad ve e-postanı gir.', 'Teslimat adresini yaz.', 'Kart bilgilerini ekle.', 'Bilgileri kontrol edip onayla.'][step]}</p>
        <div className="mt-6 flex justify-between">
          <button type="button" disabled={step === 0} onClick={() => setStep(step - 1)} className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-40">Geri</button>
          <button type="button" onClick={() => setStep(Math.min(step + 1, steps.length - 1))} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500">{step === steps.length - 1 ? 'Tamamla' : 'İleri'}</button>
        </div>
      </div>
    </div>
  );
}
```
