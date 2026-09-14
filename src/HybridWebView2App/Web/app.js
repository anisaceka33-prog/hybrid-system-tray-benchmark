const status = document.querySelector('#status'); const output = document.querySelector('#output'); const pending = new Map();
function send(type, payload) { const id = crypto.randomUUID(); const started = performance.now(); window.chrome.webview.postMessage({ id, protocolVersion: 1, type, payload }); return new Promise((resolve, reject) => pending.set(id, { resolve, reject, started })); }
window.chrome.webview.addEventListener('message', event => { const response = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; const item = pending.get(response.id); if (!item) return; pending.delete(response.id); const rttMs = performance.now() - item.started; if (!response.success) item.reject(new Error(response.error)); else item.resolve({ ...response, rttMs }); });
document.querySelector('#run').addEventListener('click', async () => { try { const input = document.querySelector('#input').value; const result = await send('operation', input); status.textContent = `Status: Ready (${result.rttMs.toFixed(3)} ms RTT)`; output.textContent = `${result.payload}\nPayload bytes: ${new TextEncoder().encode(input).byteLength}`; } catch (error) { status.textContent = 'Status: Bridge error'; output.textContent = error.message; } });
document.querySelector('#close').addEventListener('click', () => { void send('close-ui', 'user'); status.textContent = 'Status: Hidden in tray'; });

// Expose a small bench helper so the host can trigger an operation and the web
// side will return measured RTT and payload size. Returns an object.
window.__bench = {
  async runOperation(payload) {
    const started = performance.now();
    const res = await send('operation', payload);
    const rtt = performance.now() - started;
    const payloadBytes = new TextEncoder().encode(payload).length;
    return { rttMs: rtt, payloadBytes, responsePayload: res.payload, hostRttMs: res.rttMs };
  }
};

void send('frontend-ready', 'ready').catch(() => { status.textContent = 'Status: Bridge error'; });
