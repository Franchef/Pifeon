# 🐦 Pifeon

[![License: MIT](https://shields.io)](https://opensource.org)
[![.NET 9](https://shields.io)](https://microsoft.com)
[![Platform](https://shields.io)]()

**Pifeon** (da *Pigeon* + *File*) è un software open source e multipiattaforma progettato per il trasferimento e la sincronizzazione di file e cartelle in modalità **Peer-to-Peer (P2P) crittografata**, senza intermediari cloud e senza necessità di registrazione. 

Proprio come i piccioni viaggiatori del passato, Pifeon porta i tuoi dati direttamente a destinazione alla massima velocità consentita dalla tua rete.

---

## 🚀 Caratteristiche Principali

- **Zero Cloud & Zero Account:** Nessuna registrazione, nessuna email, nessun database centralizzato. I file viaggiano direttamente tra i nodi.
- **Privacy-First:** Crittografia End-to-End (AES-GCM / Seclink). Nemmeno il server di segnalazione può intercettare i tuoi dati.
- **Hole Punching Intelligente:** Supera firewall e NAT casalinghi (tramite protocolli STUN/ICE) per stabilire connessioni dirette ovunque.
- **Leggero e Performante:** Scritto in C# moderno e ottimizzato per consumare pochissima RAM anche con trasferimenti di file da centinaia di gigabyte.

---

## 🏗️ Struttura della Soluzione (.sln)

Il progetto adotta un'architettura modulare altamente disaccoppiata (Principio Open/Closed), permettendo di estendere le funzionalità senza riscrivere il motore di rete.

```text
src/
├── Pifeon.Core/          # 🧠 La libreria logica centrale (.NET Class Library)
│   ├── Networking/       # Gestione Socket, WebRTC, Hole Punching (STUN)
│   ├── Cryptography/     # Cifratura simmetrica/asimmetrica dei file
│   ├── IO/               # Scansione cartelle, calcolo Hash (SHA256) e gestione Stream
│   └── Signaling/        # Interfacce per l'accoppiamento dei client (ISignalingService)
│
├── Pifeon.Cli/           # 💻 Interfaccia a riga di comando (Console Application)
│   └── [Usa Spectre.Console per un'esperienza terminale avanzata e scriptabile]
│
├── Pifeon.Gui/           # 🎨 Interfaccia grafica nativa (AvaloniaUI)
│   └── [UI desktop cross-platform con rendering hardware in stile MVVM]
│
└── Pifeon.Server/        # 🌐 Mini-server di segnalazione (Signaling Server)
    └── [Hub ultra-leggero in memoria per l'accoppiamento iniziale tramite codice a 6 cifre]
```

---

## ⚙️ Come Funziona (Il Flusso)

### Scenario A: Invio Singolo "Al Volo" (Stile WeTransfer)
1. **Il Mittente** trascina un file/cartella su Pifeon.
2. Il client contatta il `Pifeon.Server` e riceve un **codice temporaneo a 6 cifre** (valido 5 minuti).
3. **Il Destinatario** inserisce il codice nella sua istanza di Pifeon.
4. Il server scambia gli IP pubblici (NAT Traversal) e mette in contatto diretto i due PC.
5. Il canale P2P si stabilisce, il codice viene eliminato dal server e il file viene trasmesso a blocchi cifrati.

### Scenario B: Sincronizzazione Continua (Evoluzione Future-Proof)
Sfruttando la Dependency Injection, l'interfaccia `ISignalingService` può essere estesa con un modulo autenticato:
- I PC scambiano una chiave asimmetrica permanente una sola volta.
- La classe `FileSystemWatcher` nativa di .NET monitora le modifiche alle cartelle in tempo reale.
- I client si connettono in background in modo silente e automatizzato per aggiornare solo i blocchi (*delta sync*) dei file modificati.

---

## 📦 Compilazione e Deploy

Per garantire la massima integrazione con la community open source e i sistemi operativi moderni, il deployment è progettato per eliminare qualsiasi dipendenza esterna.

### ⚡ Native AOT (Ahead-Of-Time)
Sia la versione CLI che la GUI sfruttano la compilazione **Native AOT** di .NET. Questo significa che il codice C# viene compilato direttamente in codice macchina nativo.
- **Vantaggi:** Nessuna necessità per l'utente di installare il .NET Runtime. Avvio istantaneo. File eseguibile singolo da pochi megabyte.

### 🏪 Distribuzione tramite Marketplace

L'applicazione viene pacchettizzata per rispettare le sandbox di sicurezza dei principali store:

#### 🪟 Windows (Microsoft Store)
Viene distribuita come pacchetto nativo **MSIX** (configurato tramite il *Windows Application Packaging Project*). Richiede l'attivazione dei permessi di rete (`internetClientServer`) nel manifesto per consentire le connessioni P2P in ingresso.

#### 🐧 Linux (Ubuntu Snap Store & Flathub)
Impacchettata tramite **Snapcraft** e **Flatpak**. Sfrutta le interfacce `network` e `home` per consentire al binario nativo di comunicare all'esterno e leggere i file da trasferire selezionati dall'utente, garantendo piena compatibilità con Ubuntu, Fedora e distribuzioni desktop.

---

## 📄 Licenza

Questo progetto è rilasciato sotto i termini della licenza **MIT**. Consulta il file [LICENSE](LICENSE) per maggiori dettagli.
