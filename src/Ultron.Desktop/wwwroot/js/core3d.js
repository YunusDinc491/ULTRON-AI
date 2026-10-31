// Ultron nöral toz çekirdeği (Three.js r128, yerel kopya: lib/three.min.js).
// Katmanlar: katı küçük çekirdek + hale, 3 jiroskop halkası, toz kabuğu, iç çekirdek tozu, halkaları saran yörünge tozu, sinaptik çizgiler.
// Durumlar: idle (denge, 1.0x) · asking (kullanıcı yazıyor/konuşuyor: genişler 1.56x) · answering (düşünüyor/konuşuyor: küçülür 0.62x,
// sese göre titreşir) · error (söner). Ses seviyesi setLevel(0-1) ile gelir; gerçek mikrofon/hoparlör seviyesidir.
(() => {
    const SHELL = 5400, INNER = 2200, ORBITAL = 2400, FILAMENTS = 180;

    const shellBase = new Float32Array(SHELL * 3), shellPhase = new Float32Array(SHELL);
    const innerBase = new Float32Array(INNER * 3), innerPhase = new Float32Array(INNER);
    const orbAngle = new Float32Array(ORBITAL), orbRadius = new Float32Array(ORBITAL),
        orbTilt = new Float32Array(ORBITAL), orbSpeed = new Float32Array(ORBITAL);

    const s = {
        started: false, state: "idle", sound: true,
        scale: 1, targetScale: 1, turb: 1, targetTurb: 1, rot: 1, targetRot: 1, pulse: 0,
        level: 0, lvl: 0,
        mouseX: 0, mouseY: 0, dragX: 0, dragY: 0, dragging: false, prevX: 0, prevY: 0,
    };
    let scene, camera, renderer, group, clock;
    let dustGeo, innerGeo, orbitalGeo, filaments, solid, halo, rings = [];
    let container;

    const STATES = {
        idle: { scale: 1.0, turb: 1.0, rot: 1.0, pulse: 0 },
        asking: { scale: 1.56, turb: 2.65, rot: 2.4, pulse: 0.15 },
        answering: { scale: 0.62, turb: 1.95, rot: 3.1, pulse: 1.0 },
        error: { scale: 0.85, turb: 0.3, rot: 0.2, pulse: 0 },
    };

    // Yumuşak, turuncu parıltılı nokta dokusu (tozun her tanesi).
    function dustTexture() {
        const c = document.createElement("canvas");
        c.width = c.height = 64;
        const ctx = c.getContext("2d");
        const g = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
        g.addColorStop(0, "rgba(255,252,240,1)");
        g.addColorStop(0.28, "rgba(255,195,90,0.92)");
        g.addColorStop(0.65, "rgba(255,95,0,0.38)");
        g.addColorStop(1, "rgba(0,0,0,0)");
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, 64, 64);
        return new THREE.CanvasTexture(c);
    }

    function points(geo, size, sprite, opacity) {
        return new THREE.Points(geo, new THREE.PointsMaterial({
            size, map: sprite, vertexColors: true, transparent: true, opacity,
            depthWrite: false, blending: THREE.AdditiveBlending,
        }));
    }

    function build() {
        scene = new THREE.Scene();
        camera = new THREE.PerspectiveCamera(50, container.clientWidth / container.clientHeight, 0.1, 1000);
        camera.position.z = 310;
        renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: "high-performance" });
        renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        renderer.setSize(container.clientWidth, container.clientHeight);
        container.appendChild(renderer.domElement);

        group = new THREE.Group();
        scene.add(group);
        const sprite = dustTexture();
        const C = hex => new THREE.Color(hex);
        const dark = C(0xcc3300), burnt = C(0xff4500), fiery = C(0xff6a00), light = C(0xff8c00), amber = C(0xffaa00), golden = C(0xffcc44);
        const pick = (a, b) => a.clone().lerp(b, Math.random());

        // Katı küçük çekirdek ve plazma halesi
        solid = new THREE.Mesh(new THREE.SphereGeometry(12.5, 36, 36), new THREE.MeshBasicMaterial({ color: 0xffd27a, transparent: true, opacity: 0.96 }));
        halo = new THREE.Mesh(new THREE.SphereGeometry(17.5, 32, 32), new THREE.MeshBasicMaterial({
            color: 0xff6a00, transparent: true, opacity: 0.55, blending: THREE.AdditiveBlending, depthWrite: false,
        }));
        group.add(solid, halo);

        // Üç jiroskop halkası: parlak iç tüp + kalın ışık tüpü
        const ringsGroup = new THREE.Group();
        ringsGroup.name = "rings";
        group.add(ringsGroup);
        rings = [
            { radius: 66, tube: 2.35, color: 0xff7700, core: 0xffcc55, tilt: [0.62, 0.25, 0.1], speed: [0.008, 0.014, 0.004] },
            { radius: 74, tube: 2.15, color: 0xff5000, core: 0xffaa22, tilt: [-0.72, 0.45, 0.55], speed: [-0.011, 0.009, -0.006] },
            { radius: 82, tube: 1.95, color: 0xcc3700, core: 0xff8800, tilt: [1.18, -0.52, -0.35], speed: [0.006, -0.013, 0.009] },
        ].map(cfg => {
            const pivot = new THREE.Group();
            pivot.rotation.set(...cfg.tilt);
            for (const [tube, color, opacity] of [[cfg.tube * 0.55, cfg.core, 0.78], [cfg.tube * 1.65, cfg.color, 0.34]])
                pivot.add(new THREE.Mesh(new THREE.TorusGeometry(cfg.radius, tube, 20, 128), new THREE.MeshBasicMaterial({
                    color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false,
                })));
            pivot.userData = cfg;
            ringsGroup.add(pivot);
            return pivot;
        });

        // Toz kabuğu: koyu ve açık turuncu girdap
        dustGeo = new THREE.BufferGeometry();
        const pos = new Float32Array(SHELL * 3), col = new Float32Array(SHELL * 3);
        for (let i = 0; i < SHELL; i++) {
            const theta = Math.random() * Math.PI * 2, phi = Math.acos(2 * Math.random() - 1);
            let r = 78 + (Math.random() - 0.5) * 26;
            if (i % 5 === 0) r = 94 + Math.random() * 20; // dış sis
            if (i % 7 === 0) r = 48 + Math.random() * 22; // halkalar arası köprü
            const x = r * Math.sin(phi) * Math.cos(theta), y = r * Math.sin(phi) * Math.sin(theta), z = r * Math.cos(phi);
            shellBase.set([x, y, z], i * 3);
            pos.set([x, y, z], i * 3);
            shellPhase[i] = Math.random() * Math.PI * 2;
            const swirl = Math.sin(theta * 2 + phi * 3) * 0.5 + 0.5;
            const c = swirl > 0.48 || Math.random() < 0.45
                ? pick(light, Math.random() < 0.5 ? amber : golden)
                : pick(dark, Math.random() < 0.6 ? burnt : fiery);
            col.set([c.r, c.g, c.b], i * 3);
        }
        dustGeo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        dustGeo.setAttribute("color", new THREE.BufferAttribute(col, 3));
        group.add(points(dustGeo, 2.95, sprite, 0.9));

        // İç çekirdek tozu
        innerGeo = new THREE.BufferGeometry();
        const ip = new Float32Array(INNER * 3), ic = new Float32Array(INNER * 3);
        for (let i = 0; i < INNER; i++) {
            const theta = Math.random() * Math.PI * 2, phi = Math.acos(2 * Math.random() - 1);
            const r = 16 + Math.pow(Math.random(), 0.75) * 32;
            const v = [r * Math.sin(phi) * Math.cos(theta), r * Math.sin(phi) * Math.sin(theta), r * Math.cos(phi)];
            innerBase.set(v, i * 3);
            ip.set(v, i * 3);
            innerPhase[i] = Math.random() * Math.PI * 2;
            const c = Math.random() < 0.65 ? pick(fiery, golden) : pick(dark, amber);
            ic.set([c.r, c.g, c.b], i * 3);
        }
        innerGeo.setAttribute("position", new THREE.BufferAttribute(ip, 3));
        innerGeo.setAttribute("color", new THREE.BufferAttribute(ic, 3));
        group.add(points(innerGeo, 3.5, sprite, 0.94));

        // Halkaları saran yörünge tozu
        orbitalGeo = new THREE.BufferGeometry();
        const oc = new Float32Array(ORBITAL * 3);
        for (let i = 0; i < ORBITAL; i++) {
            const band = i % 3;
            orbAngle[i] = Math.random() * Math.PI * 2;
            orbRadius[i] = [66, 74, 82][band] + (Math.random() - 0.5) * 9.5;
            orbTilt[i] = [0.62, -0.78, 1.15][band];
            orbSpeed[i] = (0.45 + Math.random() * 0.55) * (band === 1 ? -1 : 1);
            const c = band === 0 ? golden.clone().lerp(fiery, Math.random() * 0.7) : band === 1 ? pick(burnt, amber) : pick(dark, light);
            oc.set([c.r, c.g, c.b], i * 3);
        }
        orbitalGeo.setAttribute("position", new THREE.BufferAttribute(new Float32Array(ORBITAL * 3), 3));
        orbitalGeo.setAttribute("color", new THREE.BufferAttribute(oc, 3));
        group.add(points(orbitalGeo, 3.4, sprite, 0.92));

        // Sinaptik çizgiler
        const lp = new Float32Array(FILAMENTS * 6), lc = new Float32Array(FILAMENTS * 6);
        for (let i = 0; i < FILAMENTS; i++) {
            const a = Math.floor(Math.random() * SHELL) * 3;
            const k = 0.62 + Math.random() * 0.45;
            const ax = shellBase[a], ay = shellBase[a + 1], az = shellBase[a + 2];
            lp.set([ax, ay, az, ax * k + (Math.random() - 0.5) * 22, ay * k + (Math.random() - 0.5) * 22, az * k + (Math.random() - 0.5) * 22], i * 6);
            const [c1, c2] = i % 2 === 0 ? [fiery, golden] : [dark, light];
            lc.set([c1.r, c1.g, c1.b, c2.r, c2.g, c2.b], i * 6);
        }
        const fg = new THREE.BufferGeometry();
        fg.setAttribute("position", new THREE.BufferAttribute(lp, 3));
        fg.setAttribute("color", new THREE.BufferAttribute(lc, 3));
        filaments = new THREE.LineSegments(fg, new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0.26, blending: THREE.AdditiveBlending }));
        group.add(filaments);

        clock = new THREE.Clock();
    }

    function resize() {
        if (!renderer) return;
        camera.aspect = container.clientWidth / container.clientHeight;
        // Küçük pencerede küre (özellikle 1.56x genişlemişken) ekrana sığsın diye kamera uzaklaşır:
        // dar ekranda (kenar paneli) genişliğe, alçak pencerede yüksekliğe göre.
        const byWidth = camera.aspect < 0.8 ? 0.8 / Math.max(camera.aspect, 0.45) : 1;
        const byHeight = Math.min(1.6, Math.max(1, 1000 / container.clientHeight));
        camera.position.z = 310 * Math.max(byWidth, byHeight);
        camera.updateProjectionMatrix();
        renderer.setSize(container.clientWidth, container.clientHeight);
    }

    function frame() {
        requestAnimationFrame(frame);
        if (document.hidden || !renderer) return;
        const t = clock.getElapsedTime();
        const lerp = (a, b, k) => a + (b - a) * k;

        s.scale = lerp(s.scale, s.targetScale, 0.055);
        s.turb = lerp(s.turb, s.targetTurb, 0.06);
        s.rot = lerp(s.rot, s.targetRot, 0.05);
        // Ses seviyesi hızlı yükselir, yavaş söner.
        s.lvl = s.level > s.lvl ? lerp(s.lvl, s.level, 0.5) : lerp(s.lvl, s.level, 0.12);

        let pulse;
        if (s.state === "answering") pulse = (Math.sin(t * 18) * 0.045 + Math.cos(t * 29) * 0.035) * s.pulse + s.lvl * 0.12;
        else if (s.state === "asking") pulse = Math.sin(t * 6) * 0.04 + s.lvl * 0.14;
        else pulse = Math.sin(t * 2.2) * 0.025;
        const k = s.scale + pulse;

        const sp = dustGeo.attributes.position.array;
        for (let i = 0; i < SHELL; i++) {
            const i3 = i * 3, bx = shellBase[i3], by = shellBase[i3 + 1], bz = shellBase[i3 + 2], ph = shellPhase[i];
            const amp = 3.2 * s.turb;
            const burst = s.state === "asking" ? 1 + Math.sin(ph * 3 + t * 3) * 0.16
                : s.state === "answering" ? 1 + Math.sin(ph * 5 + t * 14) * 0.07 : 1;
            sp[i3] = bx * k * burst + Math.sin(t * 1.4 * s.turb + ph + by * 0.04) * amp;
            sp[i3 + 1] = by * k * burst + Math.cos(t * 1.3 * s.turb + ph + bz * 0.04) * amp;
            sp[i3 + 2] = bz * k * burst + Math.sin(t * 1.6 * s.turb + ph + bx * 0.04) * amp;
        }
        dustGeo.attributes.position.needsUpdate = true;

        const ip = innerGeo.attributes.position.array;
        const ik = s.state === "answering" ? k * 0.85 + Math.abs(pulse) * 1.9 : k * 0.95;
        for (let i = 0; i < INNER; i++) {
            const i3 = i * 3, j = Math.sin(t * 3.2 + innerPhase[i]) * 2.2 * s.turb;
            ip[i3] = innerBase[i3] * ik + j;
            ip[i3 + 1] = innerBase[i3 + 1] * ik - j * 0.7;
            ip[i3 + 2] = innerBase[i3 + 2] * ik + j * 0.5;
        }
        innerGeo.attributes.position.needsUpdate = true;

        const op = orbitalGeo.attributes.position.array;
        for (let i = 0; i < ORBITAL; i++) {
            orbAngle[i] += 0.012 * orbSpeed[i] * s.rot;
            const a = orbAngle[i], r = orbRadius[i] * k, tilt = orbTilt[i];
            const x = r * Math.cos(a), z = r * Math.sin(a), y = Math.sin(a * 2 + t * 2) * 3.2 * s.turb;
            op[i * 3] = x;
            op[i * 3 + 1] = y * Math.cos(tilt) - z * Math.sin(tilt);
            op[i * 3 + 2] = y * Math.sin(tilt) + z * Math.cos(tilt);
        }
        orbitalGeo.attributes.position.needsUpdate = true;

        filaments.scale.setScalar(k);
        filaments.rotation.y = -t * 0.12 * s.rot;
        filaments.rotation.z = t * 0.08;

        const corePulse = 1 + Math.sin(t * (s.state === "answering" ? 20 : 4.5)) * 0.08 + s.lvl * 0.15;
        const coreScale = (s.state === "answering" ? 0.88 : s.state === "asking" ? 1.18 : 1) * corePulse;
        solid.scale.setScalar(coreScale);
        halo.scale.setScalar(coreScale * (1.22 + Math.sin(t * 7) * 0.1));

        group.getObjectByName("rings").scale.setScalar(k);
        for (const pivot of rings) {
            const [sx, sy, sz] = pivot.userData.speed;
            pivot.rotation.x += sx * s.rot;
            pivot.rotation.y += sy * s.rot;
            pivot.rotation.z += sz * s.rot;
        }

        group.rotation.y += 0.0032 * s.rot;
        group.rotation.x += (s.mouseY * 0.35 + s.dragX - group.rotation.x) * 0.05;
        group.rotation.z += (-s.mouseX * 0.25 + s.dragY - group.rotation.z) * 0.05;

        // Hata durumunda sönük
        renderer.domElement.style.opacity = s.state === "error" ? "0.35" : "1";
        renderer.render(scene, camera);
        window.ultronHud?.tick(k, s.turb, s.lvl, s.state, t);
    }

    // ---- HUD sesleri (genişleme / yoğunlaşma); "SES" düğmesi kapatır ----
    let audio = null;
    function hudSound(type) {
        if (!s.sound) return;
        try {
            audio ??= new AudioContext();
            if (audio.state === "suspended") audio.resume();
            const now = audio.currentTime, o1 = audio.createOscillator(), o2 = audio.createOscillator(), g = audio.createGain();
            o1.connect(g); o2.connect(g); g.connect(audio.destination);
            const [t1, t2, f1, f2, peak, len] = {
                asking: ["sine", "triangle", [160, 520], [240, 780], 0.06, 0.45],
                answering: ["sawtooth", "sine", [480, 130], [640, 195], 0.05, 0.4],
                idle: ["sine", "sine", [320, 440], [480, 660], 0.035, 0.28],
            }[type];
            o1.type = t1; o2.type = t2;
            o1.frequency.setValueAtTime(f1[0], now); o1.frequency.exponentialRampToValueAtTime(f1[1], now + len * 0.92);
            o2.frequency.setValueAtTime(f2[0], now); o2.frequency.exponentialRampToValueAtTime(f2[1], now + len * 0.92);
            g.gain.setValueAtTime(0.001, now);
            g.gain.linearRampToValueAtTime(peak, now + 0.08);
            g.gain.exponentialRampToValueAtTime(0.001, now + len);
            o1.start(now); o2.start(now); o1.stop(now + len + 0.02); o2.stop(now + len + 0.02);
        } catch { /* ses aygıtı yoksa sessiz geç */ }
    }

    window.ultronCore = {
        init(el) {
            container = el;
            if (s.started) return;
            s.started = true;
            build();
            window.addEventListener("resize", resize);
            new ResizeObserver(resize).observe(container);
            window.addEventListener("mousemove", e => {
                s.mouseX = (e.clientX / window.innerWidth) * 2 - 1;
                s.mouseY = -(e.clientY / window.innerHeight) * 2 + 1;
                if (s.dragging) {
                    s.dragY += (e.clientX - s.prevX) * 0.008;
                    s.dragX += (e.clientY - s.prevY) * 0.008;
                    s.prevX = e.clientX; s.prevY = e.clientY;
                }
            });
            container.addEventListener("mousedown", e => { s.dragging = true; s.prevX = e.clientX; s.prevY = e.clientY; });
            window.addEventListener("mouseup", () => s.dragging = false);
            requestAnimationFrame(frame);
        },
        setState(state) {
            if (!STATES[state] || state === s.state) return;
            if (state !== "error") hudSound(state);
            s.state = state;
            const g = STATES[state];
            s.targetScale = g.scale; s.targetTurb = g.turb; s.targetRot = g.rot; s.pulse = g.pulse;
        },
        setLevel(level) { s.level = Math.min(1, Math.max(0, level)); },
        setSound(on) { s.sound = on; },
    };
})();
