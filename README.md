# VolumePad Link

VolumePad Link is the Windows companion app for the VolumePad hardware controller.

## Projects

- `VolumePadLink.UI` — WinUI desktop app
- `VolumePadLink.Agent` — background agent for USB, audio, and tray
- `VolumePadLink.Contracts` — shared DTOs and contracts

## Features

- Control master and per-app volume
- Receive button and encoder events from the device
- Send device settings like detent count and strength
- Stream display data and LED meter updates to the device
- Integrate external controls such as a Stream Deck plugin

## Architecture

- UI talks to the agent over local IPC
- Agent owns USB communication, audio control, mappings, and tray
- Device sends raw input events, agent resolves actions
- Display uses full image transfer with dirty-rectangle updates

See `docs/architecture.md` for the detailed system design.
