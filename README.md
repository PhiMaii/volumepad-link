# VolumePad Desktop App (VolumePad Link)

Windows companion application for VolumePad hardware.

This repository is a submodule of the main VolumePad umbrella repo and provides:
- backend runtime services
- device communication
- audio control integration
- user interface

## Role In The Full Stack

In the full VolumePad architecture:
- firmware handles on-device behavior
- this desktop app is the host runtime and control plane
- Stream Deck plugin (optional) talks to this app via local API endpoints

## Projects

- `src/VolumePadLink.Contracts`: shared protocol envelopes, DTOs, and validation
- `src/VolumePadLink.Agent`: backend runtime (IPC, device transport, audio, ring pipeline, settings, debug, tray)
- `src/VolumePadLink.UI`: WinUI client
- `tests/VolumePadLink.Tests`: unit and integration tests

## Build And Test

```powershell
dotnet build VolumePadLink.slnx
dotnet test tests/VolumePadLink.Tests/VolumePadLink.Tests.csproj
```

## Documentation Policy

Global architecture/protocol/layout docs are maintained only in:
`D:\Daten\Programmieren\volumepad\docs`

Use this submodule docs area only for desktop-app-specific notes that do not redefine global contracts.
