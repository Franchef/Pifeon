# 🐦 Pifeon

[![License: GPL v3](https://shields.io)](https://gnu.org)
[![.NET 9](https://shields.io)](https://microsoft.com)
[![Platform](https://shields.io)]()

**Pifeon** (from *Pigeon* + *File*) is an open-source, cross-platform software designed for encrypted **Peer-to-Peer (P2P) file and folder transfer/synchronization**. It works directly between devices without cloud intermediaries, data logging, or mandatory user registration.

Just like the homing pigeons of the past, Pifeon delivers your data straight to its destination at the maximum speed allowed by your network.

---

## 🚀 Key Features

- **Zero Cloud & Zero Accounts:** No registration, no email required, and no centralized database. Files travel strictly between nodes.
- **Privacy-First & Copyleft:** Protected by the GNU GPLv3 license. End-to-End encrypted (AES-GCM / Seclink)—not even the signaling server can peek into your data.
- **Smart Hole Punching:** Seamlessly bypasses firewalls and home NATs (via STUN/ICE protocols) to establish direct connections anywhere.
- **Lightweight & High-Performance:** Written in modern C# and optimized for low RAM consumption, even when streaming multi-gigabyte folders.

---

## 🏗️ Solution Structure (.sln)

The project adopts a highly decoupled, modular architecture (Open/Closed Principle), allowing future extensions without rewriting the core network engine.

```text
src/
├── Pifeon.Core/          # 🧠 Central logic library (.NET Class Library)
│   ├── Networking/       # Sockets, WebRTC, Hole Punching (STUN) management
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

### Scenario A: One-Time Quick Transfer (WeTransfer Alternative)
1. **The Sender** drops a file/folder into Pifeon.
2. The client contacts `Pifeon.Server` and receives a temporary **6-digit code** (valid for 5 minutes).
3. **The Receiver** enters the 6-digit code into their Pifeon instance.
4. The server exchanges public IPs (NAT Traversal) and introduces the two PCs.
5. A direct P2P channel is established, the code is wiped from the server, and the file is streamed in encrypted chunks.

### Scenario B: Continuous Synchronization (Future-Proof Evolution)
Leveraging Dependency Injection, the `ISignalingService` can be extended with an authenticated module:
- Peers exchange a permanent asymmetric key once.
- The native .NET `FileSystemWatcher` monitors folder changes in real time.
- Clients silently connect in the background to sync only the modified chunks (*delta sync*) of the files.

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
