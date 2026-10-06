const CHUNK_SIZE = 32 * 1024;

const fileInput = document.getElementById('fileInput');
const chooseFileButton = document.getElementById('chooseFileButton');
const cancelButton = document.getElementById('cancelButton');
const fileCard = document.getElementById('fileCard');
const fileName = document.getElementById('fileName');
const fileSize = document.getElementById('fileSize');
const sessionCard = document.getElementById('sessionCard');
const sessionCode = document.getElementById('sessionCode');
const progressCard = document.getElementById('progressCard');
const progressLabel = document.getElementById('progressLabel');
const progressValue = document.getElementById('progressValue');
const progressBar = document.getElementById('progressBar');
const statusCard = document.getElementById('statusCard');
const statusTitle = document.getElementById('statusTitle');
const statusMessage = document.getElementById('statusMessage');

const state = {
  socket: null,
  file: null,
  sessionCode: '',
  cancelled: false,
  finished: false,
  phase: 'idle',
  transferId: 0
};

const textEncoder = new TextEncoder();

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '0 B';
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  const fractionDigits = unitIndex === 0 ? 0 : value >= 10 ? 1 : 2;
  return `${value.toFixed(fractionDigits)} ${units[unitIndex]}`;
}

function setStatus(kind, message) {
  statusCard.classList.remove('hidden');
  statusTitle.textContent = kind;
  statusMessage.textContent = message;
}

function clearStatus() {
  statusCard.classList.add('hidden');
  statusTitle.textContent = '';
  statusMessage.textContent = '';
}

function setProgress(loaded, total, label) {
  progressCard.classList.remove('hidden');
  const percent = total > 0 ? Math.min(100, (loaded / total) * 100) : 0;
  progressBar.style.width = `${percent}%`;
  progressLabel.textContent = label;
  progressValue.textContent = `${formatBytes(loaded)} / ${formatBytes(total)} (${percent.toFixed(0)}%)`;
}

function clearProgress() {
  progressCard.classList.add('hidden');
  progressBar.style.width = '0%';
  progressLabel.textContent = '';
  progressValue.textContent = '';
}

function showFileInfo(file) {
  fileCard.classList.remove('hidden');
  fileName.textContent = file.name;
  fileSize.textContent = formatBytes(file.size);
}

function showSession(code) {
  sessionCard.classList.remove('hidden');
  sessionCode.textContent = code;
  cancelButton.classList.remove('hidden');
}

function hideSession() {
  sessionCard.classList.add('hidden');
  sessionCode.textContent = '';
  cancelButton.classList.add('hidden');
}

function resetTransferUi() {
  state.transferId += 1;
  state.socket = null;
  state.file = null;
  state.sessionCode = '';
  state.phase = 'idle';
  state.cancelled = false;
  state.finished = false;
  fileCard.classList.add('hidden');
  hideSession();
  clearProgress();
  chooseFileButton.disabled = false;
  chooseFileButton.textContent = 'Choose file';
  fileInput.value = '';
}

function buildWebSocketUrl(path) {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  return `${protocol}//${location.host}${path}`;
}

function bytesToBase64(bytes) {
  let binary = '';
  const chunk = 0x8000;

  for (let index = 0; index < bytes.length; index += chunk) {
    binary += String.fromCharCode(...bytes.subarray(index, index + chunk));
  }

  return btoa(binary);
}

function toBase64Utf8(text) {
  return bytesToBase64(textEncoder.encode(text));
}

function sendJson(socket, value) {
  socket.send(JSON.stringify(value));
}

function waitForNextFrame() {
  return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

async function failTransfer(title, message) {
  state.finished = false;
  state.cancelled = true;

  if (state.socket && state.socket.readyState === WebSocket.OPEN) {
    try {
      state.socket.close(1000, title);
    } catch {
      // Ignore close failures while unwinding the UI.
    }
  }

  setStatus(title, message);
  resetTransferUi();
}

async function completeTransfer(message) {
  state.finished = true;

  if (state.socket && state.socket.readyState === WebSocket.OPEN) {
    try {
      state.socket.close(1000, 'Transfer complete');
    } catch {
      // Ignore close failures while unwinding the UI.
    }
  }

  setStatus('Complete', message);
  resetTransferUi();
}

async function sendFile(socket, file, transferId) {
  state.phase = 'sending';
  setStatus('Sending', 'Receiver connected. Uploading the file now.');
  clearProgress();

  const manifest = {
    totalFiles: 1,
    totalSizeBytes: file.size,
    items: [
      {
        relativePath: file.name,
        fileSize: file.size
      }
    ]
  };

  sendJson(socket, {
    type: 'Manifest',
    sequenceNumber: 0,
    payload: toBase64Utf8(JSON.stringify(manifest))
  });

  let sentBytes = 0;
  let sequenceNumber = 1;

  while (sentBytes < file.size) {
    if (transferId !== state.transferId || state.cancelled) {
      throw new Error('Transfer cancelled.');
    }

    const chunk = file.slice(sentBytes, sentBytes + CHUNK_SIZE);
    const chunkBytes = new Uint8Array(await chunk.arrayBuffer());

    if (transferId !== state.transferId || state.cancelled) {
      throw new Error('Transfer cancelled.');
    }

    sendJson(socket, {
      type: 'ChunkData',
      sequenceNumber,
      payload: bytesToBase64(chunkBytes)
    });

    sentBytes += chunkBytes.length;
    sequenceNumber += 1;
    setProgress(sentBytes, file.size, file.name);
    await waitForNextFrame();
  }

  await completeTransfer('The file was sent successfully.');
}

function attachSocketHandlers(socket, transferId) {
  socket.addEventListener('message', async (event) => {
    if (socket !== state.socket || transferId !== state.transferId || state.cancelled || state.finished) {
      return;
    }

    const raw = typeof event.data === 'string'
      ? event.data
      : event.data instanceof Blob
        ? await event.data.text()
        : new TextDecoder().decode(event.data);

    let message;
    try {
      message = JSON.parse(raw);
    } catch {
      return;
    }

    if (message.type === 'CODE_CREATED') {
      state.sessionCode = message.code ?? '';
      state.phase = 'waiting';
      showSession(state.sessionCode);
      setStatus('Waiting', 'Share the code with the receiver.');
      return;
    }

    if (message.type === 'RECEIVER_JOINED') {
      if (!state.file) {
        return;
      }

      showSession(state.sessionCode);
      setStatus('Connected', 'Receiver joined. Preparing the file transfer.');

      try {
        await sendFile(socket, state.file, transferId);
      } catch (error) {
        if (!state.cancelled && socket === state.socket && transferId === state.transferId) {
          await failTransfer('Failed', error instanceof Error ? error.message : 'The transfer failed.');
        }
      }
      return;
    }

    if (message.type === 'ERROR') {
      await failTransfer('Failed', message.message ?? 'The server returned an error.');
    }
  });

  socket.addEventListener('close', () => {
    if (socket !== state.socket || transferId !== state.transferId || state.cancelled || state.finished) {
      return;
    }

    if (state.phase === 'waiting') {
      void failTransfer('Expired', 'The session expired. Choose a file again to create a new session.');
      return;
    }

    if (state.phase === 'sending') {
      void failTransfer('Failed', 'The connection was lost while sending the file.');
      return;
    }

    void failTransfer('Disconnected', 'The session ended before it could start.');
  });

  socket.addEventListener('error', () => {
    if (socket !== state.socket || transferId !== state.transferId || state.cancelled || state.finished) {
      return;
    }

    void failTransfer('Failed', 'The session could not be established.');
  });
}

async function startTransfer(file) {
  resetTransferUi();
  const transferId = state.transferId;
  state.file = file;
  chooseFileButton.disabled = true;
  chooseFileButton.textContent = 'Creating session...';
  showFileInfo(file);
  setStatus('Starting', 'Creating a new session.');

  const socket = new WebSocket(buildWebSocketUrl('/ws/session/create'));
  state.socket = socket;
  attachSocketHandlers(socket, transferId);
}

chooseFileButton.addEventListener('click', () => {
  if (!chooseFileButton.disabled) {
    fileInput.click();
  }
});

cancelButton.addEventListener('click', () => {
  state.cancelled = true;
  state.finished = false;

  if (state.socket && state.socket.readyState === WebSocket.OPEN) {
    try {
      state.socket.close(1000, 'Cancelled by user');
    } catch {
      // Ignore close failures while canceling.
    }
  }

  setStatus('Cancelled', 'The session was cancelled. Choose another file to start again.');
  resetTransferUi();
});

fileInput.addEventListener('change', async () => {
  const [file] = fileInput.files ?? [];
  if (!file) {
    return;
  }

  await startTransfer(file);
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {
    // Offline support is best-effort.
  });
}

resetTransferUi();
setStatus('Ready', 'Choose a file to create a session.');
