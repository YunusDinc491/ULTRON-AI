// Blazor'un kolayca yapamadığı küçük tarayıcı işleri.
window.ultron = {
    scrollToBottom(el) {
        if (el) el.scrollTop = el.scrollHeight;
    },

    // Ctrl+V ile görsel: panodaki görsel base64 olarak Blazor'a verilir (soruya eklenir). Yazı yapıştırma olduğu gibi kalır.
    bindPaste(dotnet) {
        if (window.__ultronPaste) return;
        window.__ultronPaste = true;
        document.addEventListener("paste", e => {
            const item = [...(e.clipboardData?.items ?? [])].find(i => i.type.startsWith("image/"));
            if (!item) return;
            e.preventDefault();
            const file = item.getAsFile();
            const reader = new FileReader();
            reader.onload = () => dotnet.invokeMethodAsync("OnImagePasted", String(reader.result).split(",")[1], file?.name ?? "");
            reader.readAsDataURL(file);
        });
    },

    // Cevaptaki $...$ formüller (Markdig bunları <span class="math">\(…\)</span> / <div class="math">\[…\]</div> yapar)
    // KaTeX ile matematik yazı tipinde çizilir. Çizilen öğe işaretlenir; Blazor metni değiştirirse yeni öğe yeniden çizilir.
    renderMath() {
        if (!window.katex) return;
        document.querySelectorAll(".md .math:not([data-k])").forEach(el => {
            const display = el.tagName === "DIV";
            const tex = el.textContent.trim().replace(/^\\[(\[]/, "").replace(/\\[)\]]$/, "").trim();
            el.dataset.k = "1";
            try {
                katex.render(tex, el, { displayMode: display, throwOnError: false, strict: "ignore" });
            } catch {
                el.textContent = tex;
            }
        });
    },

    focus(el) {
        if (el) el.focus();
    },

    // Gönderimden sonra yazı kutusunu tek satıra döndür (Blazor değeri kodla temizleyince "input" olayı oluşmaz).
    resetComposer(el) {
        if (el) el.style.height = "";
    },

    // Enter: gönder, Shift+Enter: yeni satır. Yazı kutusu içeriğe göre büyür (en fazla 6 satır).
    bindComposer(textarea, sendButton) {
        if (!textarea || textarea.dataset.bound) return;
        textarea.dataset.bound = "1";
        const grow = () => {
            textarea.style.height = "auto";
            textarea.style.height = Math.min(textarea.scrollHeight, 6 * 22 + 20) + "px";
        };
        textarea.addEventListener("input", grow);
        textarea.addEventListener("keydown", e => {
            if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                sendButton.click();
            }
        });
    },
};

// Açılış ekranında görünür bir giriş kutusu yok: herhangi bir harfe basınca ya da boşluğa tıklayınca
// gizli yazı kutusu odaklanır ve belirir (CSS :focus-within).
document.addEventListener("keydown", e => {
    // F11: çerçevesiz tam ekran (WPF penceresini .NET tarafı yönetir).
    if (e.key === "F11") {
        e.preventDefault();
        DotNet.invokeMethodAsync("Ultron", "ToggleFullScreen");
        return;
    }

    // İş arayüzü (Faz 16) açıkken HUD'un gizli yazı kutusu odak çalmasın.
    if (document.querySelector(".studio")) return;
    const prompt = document.getElementById("prompt");
    // Başka bir yazı alanındaysa (takip sorusunda serbest cevap, hafıza paneli) odak çalınmaz.
    if (!prompt || document.activeElement?.matches("input, textarea, select")) return;
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    if (e.key.length === 1 || e.key === "Enter") prompt.focus();
});

document.addEventListener("click", e => {
    if (e.target.closest("a, button, textarea, input, select, .panel, .hud-panel, .studio")) return;
    document.getElementById("prompt")?.focus();
});

// HUD: üst çubuktaki çekirdek modu ve saat (core3d.js her karede çağırır) + sol paneldeki gerçek sistem verileri
// (SystemMonitor saniyede bir gönderir). Her metriğin son 90 saniyesi küçük bir çizgi grafikte gösterilir.
window.ultronHud = (() => {
    let last = 0;
    const $ = id => document.getElementById(id);
    const clock = () => { const c = $("hud-clock"); if (c) c.textContent = new Date().toLocaleTimeString("tr-TR", { hour12: false }); };
    setInterval(clock, 1000);

    const HISTORY = 90;
    const history = {};
    const COLORS = { c: [34, 211, 238], o: [251, 146, 60], a: [252, 211, 77] };

    // 0-100 arası değerleri çizer; null (ölçülemeyen) noktalar boş kalır.
    function spark(key, value, max = 100) {
        const values = (history[key] ??= []);
        values.push(value == null ? null : Math.max(0, Math.min(1, value / max)));
        if (values.length > HISTORY) values.shift();

        const canvas = $("s-" + key);
        if (!canvas) return;
        const dpr = window.devicePixelRatio || 1;
        const w = canvas.clientWidth * dpr, h = canvas.clientHeight * dpr;
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
        const ctx = canvas.getContext("2d");
        ctx.clearRect(0, 0, w, h);
        const [r, g, b] = COLORS[canvas.dataset.color] ?? COLORS.c;
        const step = w / (HISTORY - 1);
        const x0 = w - (values.length - 1) * step;
        const y = v => h - 2 * dpr - v * (h - 4 * dpr);

        // Alt dolgu (yumuşak geçiş) + çizgi
        const grad = ctx.createLinearGradient(0, 0, 0, h);
        grad.addColorStop(0, `rgba(${r},${g},${b},0.28)`);
        grad.addColorStop(1, `rgba(${r},${g},${b},0)`);
        ctx.beginPath();
        let started = false, lastX = 0;
        values.forEach((v, i) => {
            if (v == null) return;
            const x = x0 + i * step;
            if (!started) { ctx.moveTo(x, h); ctx.lineTo(x, y(v)); started = true; } else ctx.lineTo(x, y(v));
            lastX = x;
        });
        if (!started) return;
        ctx.lineTo(lastX, h);
        ctx.closePath();
        ctx.fillStyle = grad;
        ctx.fill();

        ctx.beginPath();
        started = false;
        values.forEach((v, i) => {
            if (v == null) { started = false; return; }
            const x = x0 + i * step;
            if (!started) { ctx.moveTo(x, y(v)); started = true; } else ctx.lineTo(x, y(v));
        });
        ctx.strokeStyle = `rgba(${r},${g},${b},0.95)`;
        ctx.lineWidth = 1.5 * dpr;
        ctx.shadowColor = `rgba(${r},${g},${b},0.6)`;
        ctx.shadowBlur = 6 * dpr;
        ctx.stroke();
        ctx.shadowBlur = 0;
    }

    const text = (id, value) => { const el = $(id); if (el) el.textContent = value; };
    const gb = v => v.toFixed(1).replace(".", ",");

    return {
        metrics(m) {
            text("m-cpu", `%${Math.round(m.cpu)}`);
            spark("cpu", m.cpu);

            text("m-ram", `${gb(m.ramUsedGb)} / ${gb(m.ramTotalGb)} GB`);
            spark("ram", m.ramUsedGb, m.ramTotalGb);

            text("m-gpu", m.gpuUtil == null ? "ölçülemiyor" : `%${Math.round(m.gpuUtil)}`);
            spark("gpu", m.gpuUtil);

            text("m-vram", m.vramUsedGb == null ? "ölçülemiyor" : `${gb(m.vramUsedGb)} / ${gb(m.vramTotalGb)} GB`);
            spark("vram", m.vramUsedGb, m.vramTotalGb || 1);

            text("m-temp", m.gpuTemp == null ? "ölçülemiyor" : `${Math.round(m.gpuTemp)}°C`);
            spark("temp", m.gpuTemp, 100);
            const temp = $("m-temp");
            if (temp) temp.className = m.gpuTemp >= 80 ? "e" : "a";

            text("m-disk", `%${Math.round(m.diskActive)} · C: %${Math.round(m.diskUsedPercent)} dolu`);
            spark("disk", m.diskActive);

            text("m-battery", m.batteryPercent == null ? "pil yok" : `%${m.batteryPercent} · ${m.charging ? "şarjda" : "pilde"}`);
            spark("battery", m.batteryPercent);

            text("m-ultron", `${gb(m.ultronRamGb)} GB`);
            spark("ultron", m.ultronRamGb, m.ramTotalGb);
        },

        tick(scale, turb, level, state, t) {
            if (t - last < 0.08) return;
            last = t;
            clock.done ??= (clock(), true);

            const mode = $("hud-mode");
            if (mode) {
                const name = state === "asking" ? "GENİŞLEME" : state === "answering" ? "YOĞUNLAŞMA" : state === "error" ? "ÇEVRİMDIŞI" : "DENGE MODU";
                mode.textContent = `${name} (${scale.toFixed(2)}x)`;
                mode.className = state === "answering" ? "o" : "";
            }
            const reticle = $("hud-reticle");
            if (reticle) reticle.style.transform = `translate(-50%, -50%) scale(${(0.76 + (scale - 0.62) * 0.49).toFixed(3)})`;
        },
    };
})();

// Pencere kısayolla öne gelince doğrudan yazmaya başlanabilsin.
window.addEventListener("focus", () => document.getElementById("prompt")?.focus());

// Dosya sürükle-bırak: pencereye dosya sürüklenince "BELGE EKLE" katmanı açılır. Katmandaki görünmez dosya seçici
// (Blazor InputFile) bırakılan dosyayı alır. Başka yere bırakılan dosya WebView'i o dosyaya götürmesin diye engellenir.
(() => {
    let depth = 0;
    // İş/Okul açıksa onların kendi katmanı (dosya o ekranın işine gider), yoksa ana ekranın "BELGE EKLE" katmanı (2026-10-31).
    const zone = () => document.querySelector(".studio .drop-zone") ?? document.getElementById("drop-zone");
    const hasFiles = e => [...(e.dataTransfer?.types ?? [])].includes("Files");
    const hide = () => { depth = 0; document.querySelectorAll(".drop-zone.on").forEach(z => z.classList.remove("on")); };

    window.addEventListener("dragenter", e => {
        if (!hasFiles(e)) return;
        depth++;
        zone()?.classList.add("on");
    });
    window.addEventListener("dragleave", e => {
        if (hasFiles(e) && --depth <= 0) hide();
    });
    window.addEventListener("dragover", e => {
        if (hasFiles(e)) e.preventDefault();
    });
    window.addEventListener("drop", e => {
        // Görünmez dosya seçiciye düşen bırakma varsayılan davranışını yapmalı (dosyayı seçiciye vermek).
        if (!(e.target instanceof HTMLInputElement && e.target.type === "file")) e.preventDefault();
        setTimeout(hide, 0);
    });
})();

// Üst çubuk = pencerenin başlık çubuğu: boş yerinden sürükle (büyük pencerede), çift tıkla panel ⇄ pencere.
(() => {
    const interactive = "button, a, input, textarea, select, label, .cmds, .sound-toggle, .winbtns, .brand-menu, .brand-menu-backdrop, .drag-scroll";
    document.addEventListener("mousedown", e => {
        const bar = e.target.closest?.("#topbar, #st-top");
        if (!bar || e.button !== 0 || e.target.closest(interactive)) return;
        if (e.detail === 2) {
            DotNet.invokeMethodAsync("Ultron", "TogglePanelMode");
            return;
        }
        e.preventDefault();
        DotNet.invokeMethodAsync("Ultron", "BeginDrag");
    });
})();

// Arayüzler arası geçiş (Faz 18, tasarım kararı "çekirdek açılır"): HUD'daki 3B çekirdeğin merkezinden ışık dairesi
// büyüyüp ekranı kaplar, yeni arayüz altında açılır, ışık söner; dönüşte ekran kaplanır ve daire çekirdeğe geri küçülür.
// Üst çubuğun alt kenarı → --topbar-h (İş, Okul, Gelişim onun altında açılır; çubuk her ekranda sabit, 2026-10-31).
(() => {
    let watched = null;
    const set = el => document.documentElement.style.setProperty("--topbar-h", Math.round(el.getBoundingClientRect().bottom) + "px");
    const observer = new ResizeObserver(entries => entries.forEach(e => set(e.target)));
    setInterval(() => {
        const el = document.getElementById("topbar");
        if (el && el !== watched) {
            if (watched) observer.unobserve(watched);
            watched = el;
            observer.observe(el);
            set(el);
        }
    }, 500);
    window.addEventListener("resize", () => watched && set(watched));
})();

// Arayüzler arası piksel geçişi (2026-10-31, kullanıcı: "sağ alt köşeden navbarın altındaki sol üst köşeye piksel piksel").
// Üst çubuğun altını kaplayan bir tuvalde kareler sağ alttan sol üste doğru önce ekranı kapatır (cover), Blazor ekranı değiştirir,
// sonra kareler aynı yönde açılır (reveal). Toplam süre eski "çekirdek" geçişiyle aynı (~0,75 sn).
window.ultronTransition = (() => {
    const Cell = 22, CoverMs = 380, RevealMs = 380;
    const reduced = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    let canvas = null, cells = [], cols = 0, rows = 0;

    function setup() {
        canvas?.remove();
        const top = document.getElementById("topbar")?.getBoundingClientRect().bottom ?? 0;
        canvas = document.createElement("canvas");
        canvas.className = "pixel-wipe";
        canvas.style.top = Math.round(top) + "px";
        const w = window.innerWidth, h = Math.max(0, window.innerHeight - Math.round(top));
        const dpr = window.devicePixelRatio || 1;
        canvas.width = Math.ceil(w * dpr);
        canvas.height = Math.ceil(h * dpr);
        canvas.style.width = w + "px";
        canvas.style.height = h + "px";
        document.body.appendChild(canvas);
        const ctx = canvas.getContext("2d");
        ctx.scale(dpr, dpr);
        cols = Math.ceil(w / Cell);
        rows = Math.ceil(h / Cell);
        // Sıra: sağ alttan (0) sol üste (1); biraz rastgelelik kenarı "piksel piksel" gösterir.
        cells = [];
        for (let y = 0; y < rows; y++)
            for (let x = 0; x < cols; x++)
                cells.push({ x, y, at: Math.min(1, Math.max(0, ((cols - 1 - x) / cols + (rows - 1 - y) / rows) / 2 * 0.85 + Math.random() * 0.15)) });
        return ctx;
    }

    function run(ctx, ms, covering) {
        return new Promise(resolve => {
            if (reduced()) ms = 120;
            const start = performance.now();
            const frame = now => {
                const t = Math.min(1, (now - start) / ms);
                ctx.clearRect(0, 0, cols * Cell, rows * Cell);
                for (const c of cells) {
                    const shown = covering ? c.at <= t : c.at > t;
                    if (!shown) continue;
                    // Dalganın ön kenarındaki kareler camgöbeği parlar.
                    const edge = Math.abs(c.at - t) < 0.06;
                    ctx.fillStyle = edge ? "rgba(0, 240, 255, .55)" : "#03070e";
                    ctx.fillRect(c.x * Cell, c.y * Cell, Cell - (edge ? 1 : 0), Cell - (edge ? 1 : 0));
                }
                if (t < 1) requestAnimationFrame(frame);
                else resolve();
            };
            requestAnimationFrame(frame);
        });
    }

    let ctx = null;
    const cover = async () => { ctx = setup(); await run(ctx, CoverMs, true); return true; };
    const reveal = async () => {
        if (!ctx) return;
        await run(ctx, RevealMs, false);
        canvas?.remove();
        canvas = null;
        ctx = null;
    };
    // Eski adlar (enter/collapse) aynı geçişi kullanır.
    return { cover, enter: cover, reveal, collapse: reveal };
})();

// Görünüm (konuşarak değişir: "formülleri büyüt"): yüzde → kök CSS değişkenleri.
window.ultronAppearance = (formulaPercent, textPercent) => {
    const root = document.documentElement.style;
    root.setProperty("--formula-scale", String((formulaPercent || 100) / 100));
    root.setProperty("--text-scale", String((textPercent || 100) / 100));
};

// Yatay şeritler (.drag-scroll, ör. İş oturumları, 2026-10-31): fareyle basılı tutup sağa-sola sürükleyerek ya da tekerlekle kaydırılır.
// Sürükleme bitince bırakılan çipe yanlışlıkla "tıklandı" sayılmaz.
(() => {
    let strip = null, startX = 0, startLeft = 0, moved = false;
    document.addEventListener("mousedown", e => {
        const t = e.target.closest?.(".drag-scroll");
        if (!t || e.button !== 0) return;
        strip = t; startX = e.clientX; startLeft = t.scrollLeft; moved = false;
    });
    document.addEventListener("mousemove", e => {
        if (!strip) return;
        const dx = e.clientX - startX;
        if (!moved && Math.abs(dx) > 4) { moved = true; strip.classList.add("dragging"); }
        if (moved) { strip.scrollLeft = startLeft - dx; e.preventDefault(); }
    });
    document.addEventListener("mouseup", () => {
        strip?.classList.remove("dragging");
        strip = null;
        setTimeout(() => { moved = false; }, 0);
    });
    document.addEventListener("click", e => {
        if (moved && e.target.closest?.(".drag-scroll")) { e.stopPropagation(); e.preventDefault(); }
    }, true);
    document.addEventListener("wheel", e => {
        const t = e.target.closest?.(".drag-scroll");
        if (t && Math.abs(e.deltaY) > Math.abs(e.deltaX)) { t.scrollLeft += e.deltaY; e.preventDefault(); }
    }, { passive: false });
})();
