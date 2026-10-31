// Önizleme çerçevesinin içi (Faz 16): React + Tailwind bileşenini derler ve çizer, kontrol ister ise denetler.
// Çerçeve yalıtılmıştır (sandbox="allow-scripts"): Ultron'a sadece postMessage ile konuşur, dosya/ağ erişimi yok.
(function () {
    const post = m => parent.postMessage(Object.assign({ __ultron: true }, m), "*");
    const errors = [];
    window.addEventListener("error", e => errors.push(String(e.message || e)));
    window.addEventListener("unhandledrejection", e => errors.push(String((e.reason && e.reason.message) || e.reason)));
    const consoleError = console.error;
    console.error = (...args) => {
        const text = args.map(a => (a && a.message) || String(a)).join(" ");
        // React'in geliştirme uyarıları üretim yapısında gelmez; gelenler gerçek hata.
        errors.push(text.slice(0, 300));
        consoleError.apply(console, args);
    };
    if (window.tailwind) tailwind.config = { darkMode: "class" };

    class Boundary extends React.Component {
        constructor(props) { super(props); this.state = { err: null }; }
        static getDerivedStateFromError(err) { return { err }; }
        componentDidCatch(err) { errors.push(String((err && err.message) || err)); }
        render() {
            if (!this.state.err) return this.props.children;
            return React.createElement("pre", { style: { color: "#b91c1c", padding: "16px", whiteSpace: "pre-wrap", font: "12px ui-monospace, monospace" } },
                "Bileşen çalışırken hata verdi: " + ((this.state.err && this.state.err.message) || this.state.err));
        }
    }

    function compile(code) {
        const out = Babel.transform(code, {
            presets: [["react", { runtime: "classic" }]],
            plugins: ["transform-modules-commonjs"],
            filename: "Bilesen.jsx",
        }).code;
        const module = { exports: {} };
        const require = name => {
            if (name === "react") return React;
            if (name === "react-dom" || name === "react-dom/client") return ReactDOM;
            throw new Error("'" + name + "' önizlemede yok (sadece react)");
        };
        new Function("require", "module", "exports", "React", out)(require, module, module.exports, React);
        const component = module.exports.default || module.exports;
        if (typeof component !== "function") throw new Error("export default function bulunamadı");
        return component;
    }

    function check() {
        const problems = [];
        errors.slice(0, 3).forEach(e => problems.push("Çalışma hatası: " + e));
        const root = document.getElementById("root");
        if (!root.children.length) problems.push("Bileşen hiçbir şey çizmedi");
        const width = document.documentElement.scrollWidth;
        if (width > window.innerWidth + 2) problems.push(`Telefon genişliğinde (${window.innerWidth} px) yatay taşma: içerik ${width} px`);
        document.querySelectorAll("button, a[href], [role=button]").forEach(el => {
            const name = (el.getAttribute("aria-label") || el.getAttribute("title") || el.textContent || "").trim();
            const labelled = el.getAttribute("aria-labelledby") || el.querySelector("img[alt]:not([alt='']), svg title");
            if (!name && !labelled) problems.push("İsimsiz " + el.tagName.toLowerCase() + ": metin ya da aria-label yok");
        });
        document.querySelectorAll("img").forEach(el => { if (!el.hasAttribute("alt")) problems.push("alt metni olmayan görsel"); });
        // Küçük modelin sık hatası: "items-center gap-8" yazıp "flex"i unutmak (Faz 16 ölçümü: navbar linkleri alt alta çıktı).
        // Sadece önek almamış sınıflara bakılır (md:gap-8 gibi sınıflar dar ekranda bilerek etkisiz olabilir).
        const layoutToken = /^(gap(-[xy])?-\S+|items-(start|end|center|baseline|stretch)|justify-(start|end|center|between|around|evenly))$/;
        document.querySelectorAll("#root [class]").forEach(el => {
            const tokens = String(el.className.baseVal ?? el.className).split(/\s+/).filter(t => layoutToken.test(t));
            if (!tokens.length || el.children.length < 2) return;
            const display = getComputedStyle(el).display;
            if (display === "none" || /flex|grid|contents/.test(display)) return;
            problems.push(`<${el.tagName.toLowerCase()}> öğesinde "${tokens.join(" ")}" var ama "flex" ya da "grid" yok: öğeler yan yana dizilmez`);
        });
        document.querySelectorAll("input:not([type=hidden]):not([type=submit]):not([type=button]), select, textarea").forEach(el => {
            const id = el.id;
            const labelled = el.getAttribute("aria-label") || el.getAttribute("aria-labelledby") || el.closest("label")
                || (id && document.querySelector('label[for="' + CSS.escape(id) + '"]'));
            if (!labelled) problems.push("Etiketsiz form alanı (" + (el.name || el.type || el.tagName.toLowerCase()) + "): label ya da aria-label yok");
        });
        return [...new Set(problems)].slice(0, 8);
    }

    let root = null;
    window.addEventListener("message", e => {
        const m = e.data;
        if (!m || !m.__ultron) return;
        if (m.type === "theme") {
            document.documentElement.classList.toggle("dark", !!m.dark);
            document.body.style.background = m.dark ? "#0f172a" : "#ffffff";
            return;
        }
        if (m.type !== "render") return;
        let component;
        try {
            component = compile(m.code);
        } catch (err) {
            post({ type: "compile-error", seq: m.seq, final: m.final, message: String((err && err.message) || err).slice(0, 400) });
            return;
        }
        errors.length = 0;
        if (!root) root = ReactDOM.createRoot(document.getElementById("root"));
        try {
            root.render(React.createElement(Boundary, { key: m.seq }, React.createElement(component)));
        } catch (err) {
            errors.push(String((err && err.message) || err));
        }
        post({ type: "rendered", seq: m.seq, final: m.final });
        // Tailwind sınıfları gözlemciyle eklenir: ölçümden önce kısa bekle.
        if (m.check) setTimeout(() => post({ type: "checked", seq: m.seq, problems: check() }), 800);
    });

    post({ type: "ready" });
})();
