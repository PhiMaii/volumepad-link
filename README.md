# VolumePad Link

VolumePad Link is the Windows companion app for the VolumePad hardware controller.

## Projects

- `VolumePadLink.UI` - WinUI desktop app
- `VolumePadLink.Agent` - background agent for USB, audio, and tray
- `VolumePadLink.Contracts` - shared DTOs and contracts
- `VolumePadLink.Tests` - contract/protocol/service tests

## Features

- Control master and per-app volume
- Real Windows Core Audio backend with runtime mode switching
- Simulated audio backend fallback for development/testing
- Receive button and encoder events from the device
- Send device settings like detent count and strength
- Stream display data and LED meter updates to the device
- Built-in simulator mode for hardware-free device testing

## Simulator mode

Use simulator mode when no physical VolumePad is connected:

- In the UI, click `Connect Simulator`
- Or connect manually with port value `sim` (or any `sim*` value)

The simulator emits:

- `hello` and `capabilities` on connect
- periodic encoder rotate events
- periodic button press events
- `ack`/`nack` for control messages

## Audio mode

Audio mode is configurable in settings:

- `Real` - Windows Core Audio sessions/endpoints
- `Simulated` - synthetic audio sessions for development/testing

An optional config override is available through `Agent.AudioModeOverride` in `src/VolumePadLink.Agent/appsettings.json`.

## Architecture

- UI talks to the agent over local IPC
- Agent owns USB communication, audio control, mappings, and tray
- Device sends raw input events, agent resolves actions
- Display uses semantic updates plus optional framebuffer dirty-rectangle transfer

See `docs/architecture.md` for the detailed system design.
