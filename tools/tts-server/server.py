"""
Ultron · Faz 6 · Chatterbox Multilingual ses servisi (yalnızca bu bilgisayar: 127.0.0.1).

Model bir kez ekran kartına yüklenir, referans ses (kimin sesiyle konuşulacağı) bir kez hazırlanır;
sonra her istek tek cümleyi WAV'a çevirir. Ultron.Host bu servisi kendisi başlatır.

  GET  /health  -> {"ok": true, "device": "cuda", "voice": "..."}
  POST /tts     {"text": "...", "exaggeration": 0.6, "cfg_weight": 0.4, "temperature": 0.8} -> audio/wav (16 bit mono)

Çalıştırma: <venv>\\Scripts\\python.exe server.py --voice %USERPROFILE%\\Ultron\\voices\\referans.wav [--port 5201]
"""

import argparse
import io
import json
import sys
import threading
import time
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
import torch
from chatterbox.mtl_tts import ChatterboxMultilingualTTS

MAX_TEXT = 600

parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=5201)
parser.add_argument("--voice", default=None, help="Referans ses (WAV, 10-20 sn). Boşsa modelin kendi sesi.")
parser.add_argument("--language", default="tr")
parser.add_argument("--exaggeration", type=float, default=0.6, help="Duygu/vurgu yoğunluğu (0.25-2). Yüksek = daha canlı.")
parser.add_argument("--cfg-weight", type=float, default=0.4, help="Düşük = daha yavaş, daha oturaklı konuşma.")
parser.add_argument("--temperature", type=float, default=0.8)
args = parser.parse_args()

def clean_reference(path: str) -> str:
    """
    Laptop mikrofonuyla yapılan kayıtlar genelde kısık ve aralarında uzun boşluklar oluyor; Chatterbox bunu
    "cansız, kesik" bir konuşma tarzı olarak kopyalıyor. Kayıt temizlenir: 300 ms'den uzun sessizlikler 200 ms'ye iner,
    baş/son boşluk atılır, tepe seviye 0.9'a yükseltilir. Sonuç <ad>.temiz.wav olarak yanına yazılır (dinlenebilsin).
    """
    with wave.open(path) as w:
        rate, width, channels = w.getframerate(), w.getsampwidth(), w.getnchannels()
        if width != 2:
            return path
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    if channels > 1:
        x = x.reshape(-1, channels).mean(axis=1)
    frame = rate // 50  # 20 ms
    rms = np.array([np.sqrt((x[i:i + frame] ** 2).mean()) for i in range(0, len(x) - frame, frame)])
    if len(rms) == 0:
        return path
    active = rms > max(np.percentile(rms, 10) * 4, 0.004)
    keep, quiet = [], 0
    for i, is_speech in enumerate(active):
        quiet = 0 if is_speech else quiet + 1
        if is_speech or quiet <= 10:
            keep.append(x[i * frame:(i + 1) * frame])
    clean = np.concatenate(keep)
    start = int(np.argmax(np.abs(clean) > 0.01))
    end = len(clean) - int(np.argmax(np.abs(clean[::-1]) > 0.01))
    clean = clean[max(0, start - frame * 5):end + frame * 5]
    clean = clean / max(float(np.abs(clean).max()), 1e-6) * 0.9
    target = path[:-4] + ".temiz.wav" if path.lower().endswith(".wav") else path + ".temiz.wav"
    with wave.open(target, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes((clean * 32767).astype(np.int16).tobytes())
    print(f"Referans temizlendi: {len(x) / rate:.1f} sn -> {len(clean) / rate:.1f} sn ({target})", flush=True)
    return target


device = "cuda" if torch.cuda.is_available() else "cpu"
started = time.time()
model = ChatterboxMultilingualTTS.from_pretrained(device=device)
if args.voice:
    model.prepare_conditionals(clean_reference(args.voice), exaggeration=args.exaggeration)
print(f"Chatterbox hazır: {device}, {time.time() - started:.1f} sn, ses: {args.voice or 'varsayılan'}", flush=True)

# Model aynı anda tek iş yapar; istekler sıraya girer.
lock = threading.Lock()


def to_wav(samples: torch.Tensor, rate: int) -> bytes:
    pcm = (np.clip(samples.squeeze().detach().cpu().numpy(), -1.0, 1.0) * 32767).astype(np.int16)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())
    return buffer.getvalue()


class Handler(BaseHTTPRequestHandler):
    def _send(self, code: int, body: bytes, content_type: str) -> None:
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _json(self, code: int, value: dict) -> None:
        self._send(code, json.dumps(value, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

    def do_GET(self) -> None:
        if self.path == "/health":
            self._json(200, {"ok": True, "device": device, "voice": args.voice})
        else:
            self._json(404, {"error": "bulunamadı"})

    def do_POST(self) -> None:
        if self.path != "/tts":
            self._json(404, {"error": "bulunamadı"})
            return
        try:
            request = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
            text = str(request.get("text", "")).strip()
            if not text or len(text) > MAX_TEXT:
                self._json(400, {"error": "metin boş ya da çok uzun"})
                return
            started = time.time()
            with lock, torch.inference_mode():
                samples = model.generate(
                    text,
                    language_id=args.language,
                    exaggeration=float(request.get("exaggeration", args.exaggeration)),
                    cfg_weight=float(request.get("cfg_weight", args.cfg_weight)),
                    temperature=float(request.get("temperature", args.temperature)),
                )
            body = to_wav(samples, model.sr)
            seconds = (len(body) - 44) / 2 / model.sr
            print(f"TTS {len(text)} karakter -> {seconds:.1f} sn ses, {time.time() - started:.2f} sn", flush=True)
            self._send(200, body, "audio/wav")
        except Exception as ex:  # servis ayakta kalmalı; hata Host'a döner, Host Piper'a düşer
            print(f"TTS hatası: {ex}", file=sys.stderr, flush=True)
            self._json(500, {"error": str(ex)})

    def log_message(self, format, *args) -> None:  # her isteği ayrıca yazmaya gerek yok
        pass


ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
