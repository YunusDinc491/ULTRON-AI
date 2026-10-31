const input = document.getElementById("code");
const status = document.getElementById("status");

function show(state) {
  status.innerHTML = state?.connected
    ? '<span class="ok">● Ultron\'a bağlı</span>'
    : '<span class="bad">● Bağlı değil</span>' + (state?.error ? "<br>" + state.error : "");
}

function refresh() {
  chrome.runtime.sendMessage({ type: "status" }, (state) => show(state));
}

chrome.storage.local.get("code").then(({ code }) => { if (code) input.value = code; });
document.getElementById("save").addEventListener("click", async () => {
  await chrome.storage.local.set({ code: input.value.trim().toUpperCase() });
  status.textContent = "Bağlanıyor…";
  setTimeout(refresh, 1500);
});
refresh();
setInterval(refresh, 3000);
