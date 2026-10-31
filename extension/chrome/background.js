// Ultron Chrome eklentisi (Faz 22). Sadece bu bilgisayardaki Ultron'a (ws://127.0.0.1:5199/eklenti) bağlanır; eşleştirme kodu
// ayarlardan bir kez girilir. Ultron'dan gelen komutları uygular: YouTube kontrolü, sekme açma, açık sekmeyi okuma.
// Yorum/mesaj gönderme, form doldurma, satın alma bu eklentide YOK (kullanıcının kuralı: okuma/oynatma serbest, gönderme onaylı).

const ULTRON = "ws://127.0.0.1:5199/eklenti";
let socket = null;
let pingTimer = null;
let state = { connected: false, error: "" };

function setBadge(text, color = "#c2410c") {
  chrome.action.setBadgeText({ text });
  if (text) chrome.action.setBadgeBackgroundColor({ color });
}

async function connect() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) return;
  const { code } = await chrome.storage.local.get("code");
  if (!code) {
    state = { connected: false, error: "Eşleştirme kodu girilmedi (eklentinin simgesine tıkla)." };
    setBadge("KOD");
    return;
  }
  try {
    socket = new WebSocket(ULTRON);
  } catch (e) {
    state = { connected: false, error: "Ultron'a ulaşılamadı." };
    setBadge("✕");
    return;
  }
  socket.onopen = () => socket.send(JSON.stringify({ type: "hello", code, version: chrome.runtime.getManifest().version }));
  socket.onmessage = async (event) => {
    let msg;
    try { msg = JSON.parse(event.data); } catch { return; }
    if (msg.type === "welcome") {
      state = { connected: true, error: "" };
      setBadge("");
      startPing();
      return;
    }
    if (msg.type === "rejected") {
      state = { connected: false, error: msg.reason || "Kod kabul edilmedi." };
      setBadge("KOD");
      return;
    }
    if (msg.type === "pong" || !msg.id) return;
    try {
      const data = await handle(msg.cmd, msg.args || {});
      socket?.send(JSON.stringify({ id: msg.id, ok: true, data }));
    } catch (e) {
      socket?.send(JSON.stringify({ id: msg.id, ok: false, error: String(e?.message || e) }));
    }
  };
  socket.onclose = () => {
    stopPing();
    if (state.connected) state = { connected: false, error: "Ultron kapalı ya da bağlantı koptu; birazdan yeniden denenecek." };
    setBadge("✕");
    socket = null;
  };
  socket.onerror = () => {
    state = { connected: false, error: "Ultron'a ulaşılamadı (Ultron açık mı?)." };
  };
}

// Açık bağlantıda 20 sn'de bir mesaj: hem bağlantı canlı kalır hem Chrome arka plan betiğini uyutmaz.
function startPing() {
  stopPing();
  pingTimer = setInterval(() => socket?.readyState === WebSocket.OPEN && socket.send(JSON.stringify({ type: "ping" })), 20000);
}
function stopPing() {
  if (pingTimer) clearInterval(pingTimer);
  pingTimer = null;
}

chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create("ultron-baglan", { periodInMinutes: 0.5 });
  connect();
});
chrome.alarms.onAlarm.addListener((alarm) => alarm.name === "ultron-baglan" && connect());
chrome.storage.onChanged.addListener((changes) => {
  if (changes.code) {
    socket?.close();
    socket = null;
    setTimeout(connect, 300);
  }
});
chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg?.type === "status") {
    connect();
    reply(state);
  }
  return false;
});
connect();

// ---- Komutlar ----

async function handle(cmd, args) {
  switch (cmd) {
    case "tabs.open": {
      const tab = await chrome.tabs.create({ url: args.url, active: true });
      await chrome.windows.update(tab.windowId, { focused: true });
      await waitComplete(tab.id);
      const done = await chrome.tabs.get(tab.id);
      return { url: done.url, title: done.title };
    }
    case "tabs.list": {
      const tabs = await chrome.tabs.query({ windowType: "normal" });
      return tabs.map((t) => ({ title: t.title, url: t.url, active: t.active }));
    }
    case "tabs.read": {
      const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
      if (!tab || !/^https?:/.test(tab.url || "")) throw new Error("Açık sekme okunamaz (Chrome'un kendi sayfası ya da boş sekme).");
      const [result] = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => ({ title: document.title, url: location.href, text: (document.body?.innerText || "").replace(/\n{3,}/g, "\n\n").slice(0, 20000) }),
      });
      return result.result;
    }
    case "yt.play":
      return await youtubePlay(args.query);
    case "yt.control":
      return await youtubeControl(args);
    default:
      throw new Error("Bilinmeyen komut: " + cmd);
  }
}

function waitComplete(tabId, timeoutMs = 15000) {
  return new Promise((resolve) => {
    const done = () => { chrome.tabs.onUpdated.removeListener(listener); clearTimeout(timer); resolve(); };
    const listener = (id, info) => id === tabId && info.status === "complete" && done();
    const timer = setTimeout(done, timeoutMs);
    chrome.tabs.onUpdated.addListener(listener);
    chrome.tabs.get(tabId).then((t) => t.status === "complete" && done()).catch(done);
  });
}

async function youtubeTab() {
  const tabs = await chrome.tabs.query({ url: ["*://*.youtube.com/*"] });
  return tabs.find((t) => t.audible) || tabs.find((t) => t.active && t.url.includes("/watch")) || tabs.find((t) => t.url.includes("/watch")) || tabs[0];
}

// "Tarkan Şımarık" → YouTube'da ara, ilk normal videoyu (reklam ve Shorts değil) aynı sekmede aç.
async function youtubePlay(query) {
  let tab = await youtubeTab();
  const direct = /youtu\.?be/.test(query) ? query : null;
  let url = direct;
  if (!url) {
    const search = "https://www.youtube.com/results?search_query=" + encodeURIComponent(query);
    tab = tab ? await chrome.tabs.update(tab.id, { url: search, active: true }) : await chrome.tabs.create({ url: search, active: true });
    await waitComplete(tab.id);
    const [found] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: async () => {
        for (let i = 0; i < 40; i++) {
          const link = [...document.querySelectorAll("ytd-video-renderer a#video-title[href*='/watch']")]
            .find((a) => !a.closest("ytd-ad-slot-renderer, ytd-reel-shelf-renderer"));
          if (link) return { href: link.href, title: link.textContent.trim() };
          await new Promise((r) => setTimeout(r, 200));
        }
        return null;
      },
    });
    if (!found.result) throw new Error("YouTube'da sonuç bulunamadı: " + query);
    url = found.result.href;
  }
  tab = tab ? await chrome.tabs.update(tab.id, { url, active: true }) : await chrome.tabs.create({ url, active: true });
  await chrome.windows.update(tab.windowId, { focused: true });
  await waitComplete(tab.id);
  await new Promise((r) => setTimeout(r, 1500));
  const done = await chrome.tabs.get(tab.id);
  return { url: done.url, title: (done.title || "").replace(/ - YouTube$/, "") };
}

// Oynatıcıyı sayfanın kendi dünyasında (MAIN) YouTube'un oynatıcı arayüzüyle kontrol eder; yoksa video öğesiyle.
async function youtubeControl(args) {
  const tab = await youtubeTab();
  if (!tab || !tab.url.includes("/watch")) throw new Error("Chrome'da açık bir YouTube videosu yok.");
  const run = async (a) => {
    const [result] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      world: "MAIN",
      args: [a],
      func: (a) => {
        const p = document.getElementById("movie_player");
        const v = document.querySelector("video");
        if (!v) return { error: "Sayfada video yok." };
        const now = () => (p?.getCurrentTime ? p.getCurrentTime() : v.currentTime);
        const seek = (s) => (p?.seekTo ? p.seekTo(s, true) : (v.currentTime = s));
        switch (a.action) {
          case "durdur": p?.pauseVideo ? p.pauseVideo() : v.pause(); break;
          case "devam": p?.playVideo ? p.playVideo() : v.play(); break;
          case "sar": {
            const s = Number(a.seconds) || 0;
            const duration = p?.getDuration ? p.getDuration() : v.duration;
            seek(a.mode === "git" ? s : a.mode === "geri" ? Math.max(0, now() - s) : Math.min(duration || 1e9, now() + s));
            break;
          }
          case "sonraki": p?.nextVideo ? p.nextVideo() : document.querySelector(".ytp-next-button")?.click(); break;
          case "ses": {
            const level = Math.max(0, Math.min(100, Number(a.volume)));
            if (p?.setVolume) { p.setVolume(level); level === 0 ? p.mute() : p.unMute(); } else { v.volume = level / 100; v.muted = level === 0; }
            break;
          }
        }
        return {
          title: document.title.replace(/^\(\d+\)\s*/, "").replace(/ - YouTube$/, ""),
          time: now(),
          duration: p?.getDuration ? p.getDuration() : v.duration,
          paused: v.paused,
          volume: p?.getVolume ? p.getVolume() : Math.round(v.volume * 100),
        };
      },
    });
    if (result.result?.error) throw new Error(result.result.error);
    return result.result;
  };
  const state = await run(args);
  if (args.action === "sonraki") {
    await new Promise((r) => setTimeout(r, 2500)); // yeni video yüklensin, başlığı doğru gelsin
    return await run({ action: "ne_caliyor" });
  }
  return state;
}
