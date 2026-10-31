// İş arayüzünün önizlemesi (Faz 16). İki yalıtılmış çerçeve: görünen önizleme ve gizli 375 px'lik kontrol çerçevesi
// (telefonda taşma, hata, erişilebilirlik). Kütüphaneler (React, Babel, Tailwind) Ultron'la gelir, çerçeveye gömülür:
// önizleme internetsiz çalışır.
window.ultronStudio = (() => {
    const files = [
        "lib/preview/react.production.min.js",
        "lib/preview/react-dom.production.min.js",
        "lib/preview/babel.min.js",
        "lib/preview/tailwind.js",
        "js/preview-harness.js",
    ];
    let html = null;
    let dotnet = null;
    let seq = 0;
    let pending = null;
    let timer = null;
    let dark = false;
    const frames = {};

    async function document_() {
        if (html) return html;
        const texts = await Promise.all(files.map(f => fetch(f).then(r => r.text())));
        const script = t => "<script>" + t.replace(/<\/script/gi, "<\\/script") + "<\/script>";
        html = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            + script(texts[0]) + script(texts[1]) + script(texts[2]) + script(texts[3])
            + "<style>html,body{margin:0;min-height:100%;font-family:ui-sans-serif,system-ui,'Segoe UI',sans-serif;background:#fff}</style></head>"
            + '<body><div id="root"></div>' + script(texts[4]) + "</body></html>";
        return html;
    }

    function make(key, host) {
        host.innerHTML = "";
        const frame = document.createElement("iframe");
        frame.setAttribute("sandbox", "allow-scripts");
        frame.setAttribute("title", key === "view" ? "Bileşen önizlemesi" : "Bileşen kontrolü");
        frame.className = "st-frame";
        frame.srcdoc = html;
        host.appendChild(frame);
        frames[key] = { el: frame, ready: false, queue: [] };
    }

    function send(key, message) {
        const frame = frames[key];
        if (!frame) return;
        if (!frame.ready) {
            if (message.type === "render") frame.queue = frame.queue.filter(q => q.type !== "render");
            frame.queue.push(message);
            return;
        }
        frame.el.contentWindow.postMessage(Object.assign({ __ultron: true }, message), "*");
    }

    window.addEventListener("message", e => {
        const m = e.data;
        if (!m || !m.__ultron) return;
        const key = Object.keys(frames).find(k => frames[k].el.contentWindow === e.source);
        if (!key) return;
        const frame = frames[key];
        if (m.type === "ready") {
            frame.ready = true;
            const queue = frame.queue;
            frame.queue = [];
            queue.forEach(q => send(key, q));
            if (key === "view") send("view", { type: "theme", dark });
            return;
        }
        if (!dotnet) return;
        if (key === "view" && m.type === "compile-error" && m.final) dotnet.invokeMethodAsync("OnPreviewError", m.message);
        if (key === "view" && m.type === "rendered") dotnet.invokeMethodAsync("OnPreviewRendered", m.seq, !!m.final);
        if (key === "check" && m.type === "checked") dotnet.invokeMethodAsync("OnPreviewChecked", m.seq, JSON.stringify(m.problems || []));
        if (key === "check" && m.type === "compile-error") dotnet.invokeMethodAsync("OnPreviewChecked", m.seq, JSON.stringify(["Derleme hatası: " + m.message]));
    });

    return {
        async mount(viewHost, checkHost, ref) {
            dotnet = ref;
            await document_();
            make("view", viewHost);
            make("check", checkHost);
        },

        // Yazılırken: en fazla ~0,5 sn'de bir son hali çizmeyi dener; yarım kod derlenmezse önceki görüntü kalır.
        live(code) {
            pending = code;
            if (timer) return;
            timer = setTimeout(() => {
                timer = null;
                if (pending) send("view", { type: "render", code: pending, seq: ++seq, final: false });
            }, 450);
        },

        // Bitmiş kod: görünür çerçevede çiz, gizli çerçevede telefon genişliğinde kontrol et (sonuç checkSeq ile döner).
        final(code, checkSeq) {
            pending = null;
            clearTimeout(timer);
            timer = null;
            send("view", { type: "render", code, seq: ++seq, final: true });
            if (checkSeq > 0) send("check", { type: "render", code, seq: checkSeq, final: true, check: true });
        },

        theme(isDark) {
            dark = !!isDark;
            send("view", { type: "theme", dark });
        },

        // Cihaz genişliği: 375 (telefon), 768 (tablet), 0 (tam).
        async copy(text) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch {
                return false;
            }
        },

        scrollToBottom(el) {
            if (el) el.scrollTop = el.scrollHeight;
        },
    };
})();
