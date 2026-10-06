# 🐦 Pifeon

[![Build & Test Status](https://github.com/Franchef/Pifeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Franchef/Pifeon/actions/workflows/ci.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![P2P Transfer](https://img.shields.io/badge/P2P-Transfer-green.svg)](#)
[![End-to-End Encrypted](https://img.shields.io/badge/Encryption-E2E-darkgreen.svg)](#)

**Pifeon** (from *Pigeon* + *File*) is an open-source, cross-platform software designed for encrypted **Peer-to-Peer (P2P) file and folder transfer/synchronization**. It works directly between devices without cloud intermediaries, data logging, or mandatory user registration.

Just like the homing pigeons of the past, Pifeon delivers your data straight to its destination at the maximum speed allowed by your network.

---

## 🚀 Key Features

- **Zero Cloud & Zero Accounts:** No registration, no email required, and no centralized database. Files travel strictly between nodes.
- **Privacy-First & Copyleft:** Protected by the GNU GPLv3 license. Direct peer traffic uses ECDH P-256 key agreement and AES-256-GCM encryption. The signaling server is trusted to introduce the correct public keys.
- **Direct TCP:** Transfers work over LAN/reachable IPv4 endpoints. Inbound TCP must be allowed on the sender; automatic NAT traversal and relay fallback are not implemented.
- **Real-Time Progress Tracking:** Live transfer statistics showing bytes exchanged, transfer speed, and current file being transferred across all platforms (CLI/GUI).
- **Lightweight & High-Performance:** Written in modern C# and optimized for low RAM consumption, even when streaming multi-gigabyte folders.

---

## 🏗️ Solution Structure (.sln)

The project adopts a highly decoupled, modular architecture (Open/Closed Principle), allowing future extensions without rewriting the core network engine.

```text
src/
├── Pifeon.Core/          # 🧠 Central logic library (.NET Class Library)
│   ├── Networking/       # Direct TCP, binary framing, encrypted peer channels
│   ├── Cryptography/     # Symmetric/Asymmetric end-to-end encryption
│   ├── IO/               # Folder scanning, SHA256 hashing, and Stream handling
│   └── Signaling/        # Abstractions for client pairing (ISignalingService)
│
├── Pifeon.Cli/           # 💻 Command Line Interface (Console Application)
│   └── [Uses Spectre.Console for a rich, scriptable terminal experience]
│
├── Pifeon.Gui/           # 🎨 Native Graphical Interface (AvaloniaUI)
│   └── [Cross-platform desktop UI with hardware rendering using MVVM]
│
└── Pifeon.Server/        # 🌐 Ultra-lightweight Signaling Server
    └── [In-memory hub used exclusively for initial 6-digit code pairing]
```

---

## ⚙️ How It Works

See [Peer transfer flows](docs/FLOWS.md) for Mermaid diagrams of pairing,
secure connection setup, binary file transfer, and failure handling.

### Scenario A: One-Time Quick Transfer (WeTransfer Alternative)
1. **The Sender** drops a file/folder into Pifeon.
2. The client contacts `Pifeon.Server` and receives a temporary **6-digit code** (valid for 5 minutes).
3. **The Receiver** enters the 6-digit code into their Pifeon instance.
4. The peers exchange local IPv4 addresses, the sender's listening port, and ephemeral ECDH public keys through the server.
5. The receiver connects directly to the sender. Both peers confirm possession of their derived keys using encrypted handshake messages, then close signaling; the server removes the session.
6. **Both parties see live progress**, including cumulative bytes transferred, transfer speed, and the current file being transmitted.

#### Transfer protocol and security

- Pairing and endpoint/key announcements use JSON over WebSocket signaling.
- After key confirmation, the server is no longer involved in the transfer. The five-minute pairing timeout does not limit an established peer transfer.
- The manifest and peer control messages use JSON. Files are sent sequentially in chunks of up to 64 KiB; each chunk uses a binary type/sequence header followed by the original bytes, without JSON/base64 encoding.
- TCP packets have a four-byte big-endian length prefix and a bounded maximum size. Every peer packet (including manifest, acknowledgments, and handshake) is encrypted using the existing AES-GCM helper.
- HKDF derives separate sender/receiver keys from the ECDH secret and session code. Encrypted packet counters reject replay/out-of-order packets; GCM rejects tampering.
- Production signaling must use `wss://` with a trusted server certificate. The standard client permits `ws://` only for loopback development. Custom injected channels must provide their own trusted signaling transport and security.
- A six-digit code is a temporary pairing code, not an encryption key or proof of a human identity. A malicious/compromised signaling server can substitute keys; protection against that requires independent fingerprint verification.
- Connection failures are reported explicitly. UDP STUN discovery is not used to advertise a TCP endpoint, and there is no silent server-relay fallback.

### Scenario B: Continuous Synchronization (Future-Proof Evolution)
Leveraging Dependency Injection, the `ISignalingService` can be extended with an authenticated module:
- Peers exchange a permanent asymmetric key once.
- The native .NET `FileSystemWatcher` monitors folder changes in real time.
- Clients silently connect in the background to sync only the modified chunks (*delta sync*) of the files.
- Real-time progress tracking adapts to background operation patterns.

---

## 📦 Compilation & Deployment

To ensure maximum integration with the open-source community, deployment eliminates external runtime dependencies.

### ⚡ Native AOT (Ahead-Of-Time)
Both the CLI and GUI versions leverage .NET **Native AOT** compilation. The C# code compiles directly into native machine code.
- **Benefits:** Users don't need to install the .NET Runtime. Near-instant startup times. A single, self-contained executable of just a few megabytes.

### 🏪 Marketplace Distribution

The application is packaged to respect the security sandboxes of major app stores:

#### 🪟 Windows (Microsoft Store)
Distributed as a native **MSIX** package. It requests inbound/outbound network capabilities (`internetClientServer`) in its manifest to allow direct P2P connections.

#### 🐧 Linux (Ubuntu Snap Store & Flathub)
Packaged via **Snapcraft** and **Flatpak**. It utilizes the `network` and `home` interfaces, allowing the native binary to communicate externally and read selected files, ensuring full compatibility across Ubuntu, Fedora, and other desktop distros.

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0** - see the [LICENSE](LICENSE) file for details.
