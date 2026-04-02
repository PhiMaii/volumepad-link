# VolumePad Link v1

Windows companion app for VolumePad hardware.

## Projects

- `src/VolumePadLink.Contracts`: shared v2 protocol envelopes, DTOs, and settings validation.
- `src/VolumePadLink.Agent`: backend runtime (IPC, serial/simulator device transport, audio, ring pipeline, settings, debug, tray).
- `src/VolumePadLink.UI`: minimal WinUI client using named-pipe IPC.
- `tests/VolumePadLink.Tests`: unit and integration tests.

## Build

```powershell
dotnet build VolumePadLink.slnx
```

## Test

```powershell
dotnet test tests/VolumePadLink.Tests/VolumePadLink.Tests.csproj
```

## Protocol Docs

- [Minimal app scope](docs/app-summary.md)
- [Implementation guide](docs/app_implementation.md)
- [Protocol v2](docs/protocol_v2.md)
- [Stream Deck protocol (post-V1)](docs/streamdeck_protocol.md)
