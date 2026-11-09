# Pifeon CLI

Use the CLI to establish a temporary signaling session and transfer files
directly between peers.

## Sender

```text
pifeon send <PATH> [--server <URL>]
```

- Creates a temporary session and displays its 10-character pairing code.
- Sends the selected file or folder directly to the connected receiver.
- `--server` overrides the signaling server URL.

## Receiver

```text
pifeon receive <DESTINATION> [CODE] [--server <URL>]
```

- Joins the temporary session identified by `CODE`; if omitted, the CLI prompts for it.
- Writes received files under `DESTINATION`.
- `--server` overrides the signaling server URL.
