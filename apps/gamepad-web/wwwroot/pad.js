"use strict";

// Bit dei pulsanti: devono coincidere con l'enum GamepadButtons del server (Input/ControllerState.cs).
const BUTTONS = {
  A: 1 << 0, B: 1 << 1, X: 1 << 2, Y: 1 << 3,
  L1: 1 << 4, R1: 1 << 5, L2: 1 << 6, R2: 1 << 7,
  Share: 1 << 8, Options: 1 << 9, L3: 1 << 10, R3: 1 << 11, Home: 1 << 12,
  DpadUp: 1 << 13, DpadDown: 1 << 14, DpadLeft: 1 << 15, DpadRight: 1 << 16,
};

const TOKEN_KEY = "homecloud.pad.token";
const SEND_INTERVAL_MS = 16;   // ~60 controlli al secondo
const HEARTBEAT_MS = 250;      // reinvio dello stato anche se non cambia (il server azzera dopo 1 s di silenzio)
const CLOSE_BAD_CODE = 4001;
const CLOSE_FULL = 4003;

const params = new URLSearchParams(location.search);
const code = (params.get("c") || "").trim();

// Diagnostica: /pad?c=CODICE&debug=1 mostra gli ultimi eventi e i messaggi inviati
const debugEl = params.has("debug") ? document.getElementById("debug") : null;
const debugLines = [];
let t0 = performance.now();
function debug(text) {
  if (!debugEl) return;
  debugLines.unshift(`${Math.round(performance.now() - t0)} ms  ${text}`);
  debugLines.length = Math.min(debugLines.length, 14);
  debugEl.textContent = debugLines.join("\n");
}
if (debugEl) {
  debugEl.hidden = false;
  for (const type of ["pointerdown", "pointerup", "pointercancel", "touchstart", "touchend", "touchcancel"]) {
    document.addEventListener(type, (e) => debug(`${type} id=${e.pointerId ?? "-"} su ${e.target.dataset?.btn ?? e.target.id ?? e.target.tagName}`), { capture: true });
  }
}

// Stato inviato al server. Levette e grilletti 0..255, 128 = centro; per l'asse Y l'alto è positivo.
const state = { b: 0, lx: 128, ly: 128, rx: 128, ry: 128, l: 0, r: 0 };

const statusEl = document.getElementById("status");
const playerEl = document.getElementById("player");
const zone = document.getElementById("stick-zone");
const base = document.getElementById("stick-base");
const knob = document.getElementById("stick-knob");

let socket = null;
let lastSent = "";
let lastSentAt = 0;
let reconnectDelay = 500;

const setStatus = (text) => { statusEl.textContent = text; };

// ---------- Pulsanti (multi-touch: ogni dito è un pointerId diverso) ----------

// Un tocco veloce dura pochi ms: il tasto resta premuto almeno MIN_PRESS_MS,
// così il gioco (che legge l'input una volta per fotogramma) lo vede sempre.
const MIN_PRESS_MS = 60;

const pressedBy = new Map();    // pointerId -> pulsante premuto da quel dito
const pressedAt = new Map();    // pulsante -> istante della pressione
const pendingRelease = new Map(); // pulsante -> timer del rilascio ritardato

function setButton(el, down) {
  const bit = BUTTONS[el.dataset.btn];
  const analog = el.dataset.analog; // "l" o "r" per i grilletti analogici
  el.classList.toggle("pressed", down);
  if (down) state.b |= bit; else state.b &= ~bit;
  if (analog) state[analog] = down ? 255 : 0;
  sendState(); // invio immediato, senza aspettare il ciclo
}

document.querySelectorAll("[data-btn]").forEach((el) => {
  el.addEventListener("pointerdown", (e) => {
    e.preventDefault();
    pressedBy.set(e.pointerId, el);
    clearTimeout(pendingRelease.get(el));
    pendingRelease.delete(el);
    pressedAt.set(el, performance.now());
    setButton(el, true);
    navigator.vibrate?.(8);
  });
});

// Il rilascio si ascolta sull'intero documento: il dito può alzarsi anche fuori dal pulsante
function releasePointer(e) {
  const el = pressedBy.get(e.pointerId);
  if (!el) return;
  pressedBy.delete(e.pointerId);
  if ([...pressedBy.values()].includes(el)) return; // un altro dito lo tiene ancora premuto

  const wait = MIN_PRESS_MS - (performance.now() - (pressedAt.get(el) ?? 0));
  if (wait <= 0) {
    setButton(el, false);
  } else {
    pendingRelease.set(el, setTimeout(() => {
      pendingRelease.delete(el);
      setButton(el, false);
    }, wait));
  }
}

document.addEventListener("pointerup", releasePointer);
document.addEventListener("pointercancel", releasePointer);

// ---------- Levetta ----------

let stickPointer = null;
let centerX = 0;
let centerY = 0;

function moveStick(e) {
  const max = base.getBoundingClientRect().width / 2;
  let dx = e.clientX - centerX;
  let dy = e.clientY - centerY;
  const distance = Math.hypot(dx, dy);
  if (distance > max) {
    dx = (dx / distance) * max;
    dy = (dy / distance) * max;
  }
  knob.style.transform = `translate(${dx}px, ${dy}px)`;
  state.lx = Math.round(128 + (dx / max) * 127);
  state.ly = Math.round(128 - (dy / max) * 127); // sullo schermo la Y cresce verso il basso
}

function resetStick() {
  stickPointer = null;
  state.lx = 128;
  state.ly = 128;
  knob.style.transform = "";
}

zone.addEventListener("pointerdown", (e) => {
  if (stickPointer !== null) return;
  e.preventDefault();
  stickPointer = e.pointerId;
  zone.setPointerCapture(e.pointerId);
  const rect = base.getBoundingClientRect();
  centerX = rect.left + rect.width / 2;
  centerY = rect.top + rect.height / 2;
  moveStick(e);
});

zone.addEventListener("pointermove", (e) => {
  if (e.pointerId === stickPointer) moveStick(e);
});

for (const type of ["pointerup", "pointercancel"]) {
  zone.addEventListener(type, (e) => {
    if (e.pointerId === stickPointer) resetStick();
  });
}

// Se il telefono va in background o si blocca, nessun tasto deve restare premuto
function releaseAll() {
  state.b = 0;
  state.l = 0;
  state.r = 0;
  pressedBy.clear();
  pendingRelease.forEach((timer) => clearTimeout(timer));
  pendingRelease.clear();
  document.querySelectorAll(".pressed").forEach((el) => el.classList.remove("pressed"));
  resetStick();
}

document.addEventListener("visibilitychange", () => {
  if (document.hidden) releaseAll();
});

document.addEventListener("contextmenu", (e) => e.preventDefault());

// ---------- Collegamento al server ----------

function connect() {
  if (!code) {
    setStatus("Codice partita mancante: inquadra il QR sulla TV");
    return;
  }

  const token = localStorage.getItem(TOKEN_KEY) || "";
  const scheme = location.protocol === "https:" ? "wss" : "ws";
  socket = new WebSocket(`${scheme}://${location.host}/ws?c=${encodeURIComponent(code)}&t=${encodeURIComponent(token)}`);
  setStatus("Connessione…");

  socket.onopen = () => {
    reconnectDelay = 500;
    lastSent = "";
  };

  socket.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    if (msg.t === "welcome") {
      localStorage.setItem(TOKEN_KEY, msg.token);
      document.documentElement.style.setProperty("--player", msg.color);
      playerEl.textContent = `Giocatore ${msg.player}`;
      setStatus(msg.resumed ? "Riconnesso" : "Pronto");
    } else if (msg.t === "full") {
      setStatus("Partita piena: 4 giocatori già collegati");
    } else if (msg.t === "badcode") {
      localStorage.removeItem(TOKEN_KEY);
      setStatus("Codice non valido: inquadra di nuovo il QR");
    }
  };

  socket.onclose = (event) => {
    socket = null;
    if (event.code === CLOSE_BAD_CODE || event.code === CLOSE_FULL) return;
    setStatus("Connessione persa, riprovo…");
    setTimeout(connect, reconnectDelay);
    reconnectDelay = Math.min(reconnectDelay * 2, 5000);
  };
}

// Invio dello stato: subito quando cambia, comunque ogni HEARTBEAT_MS
function sendState() {
  if (!socket || socket.readyState !== WebSocket.OPEN) return;
  const message = JSON.stringify({ t: "s", ...state });
  const now = performance.now();
  if (message !== lastSent || now - lastSentAt > HEARTBEAT_MS) {
    socket.send(message);
    if (message !== lastSent) debug(`inviato b=${state.b}`);
    lastSent = message;
    lastSentAt = now;
  }
}

setInterval(sendState, SEND_INTERVAL_MS);

// Schermo intero e blocco in orizzontale (dove il browser lo permette, es. Android)
document.getElementById("fullscreen").addEventListener("click", async () => {
  try {
    await document.documentElement.requestFullscreen?.();
    await screen.orientation?.lock?.("landscape");
  } catch {
    // non supportato (es. Safari su iPhone): si usa "Aggiungi alla schermata Home"
  }
});

connect();
