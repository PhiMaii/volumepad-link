# VolumePad Link Stream Deck Protocol (Post-V1)

This document defines the Stream Deck endpoint contract for a later phase.
It is intentionally not part of V1 implementation.

## 1. Scope

Planned Stream Deck capabilities:

- display current master volume on a key
- display speaker icon state (normal vs crossed when muted)
- toggle master mute on key press
- increase/decrease master volume by configurable step `x`
- expose device connection health to Stream Deck
- change app/device settings (including detents/endstops) through a backend-managed apply path

## 2. Transport

- Localhost HTTP + WebSocket
- JSON request/response/event payloads
- Loopback-only bind (`127.0.0.1`)

## 3. State Model

`streamdeck.state` payload:

```json
{
  "master": {
    "volume": 0.64,
    "muted": false
  },
  "deviceConnection": {
    "state": "connected",
    "portName": "COM3"
  },
  "capturedAtUtc": "2026-04-01T18:00:00.0000000Z"
}
```

`deviceConnection.state` values:

- `connecting`
- `connected`
- `disconnected`
- `reconnecting`
- `error`

## 4. HTTP Endpoints

### 4.1 Get Current State

- `GET /api/v1/streamdeck/state`

Returns full `streamdeck.state`.

### 4.2 Master Mute Toggle

- `POST /api/v1/streamdeck/actions/master/mute/toggle`

Response returns updated `streamdeck.state`.

### 4.3 Increase Volume by X

- `POST /api/v1/streamdeck/actions/master/volume/increase`

Request:

```json
{
  "step": 0.02
}
```

### 4.4 Decrease Volume by X

- `POST /api/v1/streamdeck/actions/master/volume/decrease`

Request:

```json
{
  "step": 0.02
}
```

Validation:

- `step` range: `0.001..0.20`

Responses for 4.3/4.4 return updated `streamdeck.state`.

### 4.5 Get Effective Settings

- `GET /api/v1/streamdeck/settings`

Response:

```json
{
  "effective": {
    "detentCount": 24,
    "detentStrength": 0.65,
    "lowEndstopEnabled": true
  },
  "state": {
    "master": {
      "volume": 0.64,
      "muted": false
    },
    "deviceConnection": {
      "state": "connected",
      "portName": "COM3"
    },
    "capturedAtUtc": "2026-04-01T18:00:00.0000000Z"
  }
}
```

### 4.6 Update Settings (Partial Patch Supported)

- `POST /api/v1/streamdeck/settings/update`

Request example:

```json
{
  "detentCount": 28,
  "detentStrength": 0.75,
  "lowEndstopEnabled": true,
  "lowEndstopPosition": -0.90,
  "lowEndstopStrength": 0.80,
  "highEndstopEnabled": true,
  "highEndstopPosition": 0.90,
  "highEndstopStrength": 0.80
}
```

Rules:

- payload fields are optional (partial patch merge over current effective settings)
- final apply uses the same backend validation/normalization as `settings.update`
- response returns normalized/effective settings and updated `streamdeck.state`

## 5. WebSocket Events

- Endpoint: `/api/v1/streamdeck/ws`

Event types:

- `state.snapshot` (sent on connect)
- `state.changed` (sent whenever master volume/mute or connection health changes)

Envelope:

```json
{
  "type": "state.changed",
  "payload": {
    "state": {
      "master": {
        "volume": 0.64,
        "muted": false
      },
      "deviceConnection": {
        "state": "connected",
        "portName": "COM3"
      },
      "capturedAtUtc": "2026-04-01T18:00:00.0000000Z"
    }
  },
  "utcNow": "2026-04-01T18:00:00.0000000Z"
}
```

## 6. Key Rendering Guidance

For the main master key:

- key text: current volume percentage (for example `64%`)
- icon:
  - speaker icon when unmuted
  - crossed speaker icon when muted
- press action: call mute toggle endpoint

For optional extra keys:

- `Volume +` key calls `volume/increase` with configured step
- `Volume -` key calls `volume/decrease` with configured step

Connection-health handling:

- plugin should reflect `deviceConnection.state` to user
- when `error` or `disconnected`, key should show degraded/offline visual state

## 7. Error Model

Error response:

```json
{
  "ok": false,
  "error": {
    "code": "out_of_range",
    "message": "step must be between 0.001 and 0.20"
  }
}
```

Suggested error codes:

- `unknown_method`
- `invalid_payload`
- `out_of_range`
- `not_connected`
- `internal_error`

## 9. Debug Traffic Monitor Endpoints

To simplify plugin development, the Stream Deck host also exposes endpoint-level traffic diagnostics.

- `GET /api/v1/debug/streamdeck/traffic?sinceSeq=0&limit=200`
- `DELETE /api/v1/debug/streamdeck/traffic`
- `POST /api/v1/debug/streamdeck/traffic/clear`
- `GET /api/v1/debug/streamdeck/endpoints`

Traffic snapshot response:

```json
{
  "latestSeq": 53,
  "entries": [
    {
      "seq": 52,
      "utcNow": "2026-04-01T18:00:00.0000000Z",
      "direction": "in",
      "transport": "http",
      "name": "POST /api/v1/streamdeck/actions/master/mute/toggle",
      "statusCode": null,
      "note": null,
      "payload": {}
    }
  ]
}
```

## 8. Architecture Requirement

Even though this is post-V1, backend architecture should already support:

- a single authoritative master state source
- a publish/subscribe event pipeline for state updates
- command endpoints for mute toggle and volume-step adjustments
- a unified device connection-health state model reusable by UI, tray, and Stream Deck
