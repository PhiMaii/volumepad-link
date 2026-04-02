# Stream Deck Plugin Summary (Post-V1)

This document describes the planned Stream Deck plugin behavior based on the current app and protocol docs.
Stream Deck is a post-V1 feature, but backend architecture should already support these flows.

## 1. Plugin Scope

Implement these 4 key types:

1. `Volume`
2. `Change Volume`
3. `Connection Status`
4. `Change Settings`

## 2. Endpoint Constants (Hard-Coded)

The plugin should hard-code backend endpoints (no endpoint field in property inspector for `Volume` key).

- Base URL: `http://127.0.0.1:5820`
- WebSocket: `ws://127.0.0.1:5820/api/v1/streamdeck/ws`
- State: `GET /api/v1/streamdeck/state`
- Mute toggle: `POST /api/v1/streamdeck/actions/master/mute/toggle`
- Volume increase: `POST /api/v1/streamdeck/actions/master/volume/increase`
- Volume decrease: `POST /api/v1/streamdeck/actions/master/volume/decrease`
- Get settings: `GET /api/v1/streamdeck/settings`
- Update settings: `POST /api/v1/streamdeck/settings/update`

## 3. Shared Runtime Behavior

- On plugin start:
  - connect WebSocket
  - fetch initial state via `GET /state`
  - fetch effective settings via `GET /settings` for settings-key defaults
- Keep one live state cache with:
  - master volume
  - muted state
  - device connection state
- Update key visuals from `state.snapshot` and `state.changed` events.
- If endpoint is unavailable:
  - show offline visual state
  - retry with backoff

## 4. Key Definitions

## 4.1 Volume Key

Behavior:

- Icon: speaker symbol when unmuted, crossed speaker when muted.
- Show current master volume percentage above the key title area.
- On key press: call `POST /actions/master/mute/toggle`.
- Re-render immediately from returned/streamed state.

Property inspector:

- none (endpoint is hard-coded).

## 4.2 Change Volume Key

Behavior:

- Icon depends on direction:
  - increase icon when mode is `Increase`
  - decrease icon when mode is `Decrease`
- On key press:
  - `Increase` -> `POST /actions/master/volume/increase`
  - `Decrease` -> `POST /actions/master/volume/decrease`
  - request body contains configured `step`

Property inspector:

- `direction` (enum): `Increase | Decrease`
- `step` (number): `0.001..0.20`

## 4.3 Connection Status Key

Behavior:

- Full-key color by connection state:
  - `connected` -> green
  - `connecting` -> yellow
  - `reconnecting` -> yellow
  - `disconnected` -> red
  - `error` -> red
- Optional short state label on key (`Connected`, `Connecting`, etc).
- No press action required in MVP scope.

Property inspector:

- optional color overrides for each state (default mapping above).

## 4.4 Change Settings Key

Behavior:

- Icon: gear.
- On key press:
  - read selected setting + entered value from property inspector
  - type-validate in plugin before sending
  - send partial patch to `POST /settings/update`
  - on success, show confirmation checkmark feedback
  - refresh with returned effective settings

- This key should be able to be used in multi-actions

Property inspector:

- `settingKey` dropdown with all supported settings.
- `value` input field, type depends on selected setting:
  - bool toggle
  - numeric input (with range clamp)
  - enum dropdown
  - color picker/text for hex color

## 5. Setting Dropdown Catalog (for Change Settings Key)

Use these keys from the current docs:

- `autoReconnectOnError` (bool)
- `autoConnectOnStartup` (bool)
- `volumeStepSize` (number, `0.001..0.20`)
- `detentCount` (int, `0..128`)
- `detentStrength` (number, `0.0..1.0`)
- `snapStrength` (number, `0.0..1.0`)
- `encoderInvert` (bool)
- `ledBrightness` (number, `0.0..1.0`)
- `meterMode` (enum: `ring_fill | vu_peak_hold | peak_indicator`)
- `meterColor` (color hex `#RRGGBB`)
- `meterBrightness` (number, `0.0..1.0`)
- `meterGain` (number, `0.10..8.0`)
- `meterSmoothing` (number, `0.0..1.0`)
- `meterPeakHoldMs` (int, `0..3000`)
- `meterMuteRedDurationMs` (int, `50..3000`)
- `lowEndstopEnabled` (bool)
- `lowEndstopPosition` (number, `-1.0..1.0`)
- `lowEndstopStrength` (number, `0.0..1.0`)
- `highEndstopEnabled` (bool)
- `highEndstopPosition` (number, `-1.0..1.0`)
- `highEndstopStrength` (number, `0.0..1.0`)

## 6. Feedback and Error UX

- Success for `Change Settings` key: show checkmark overlay for a short duration.
- API errors:
  - show warning/error badge on key
  - keep previous rendered value/state
  - optionally show short error subtitle where supported
- Validation errors should be blocked client-side before sending.

## 7. Implementation Notes

- Prefer rendering all key states from one shared state cache to avoid inconsistent visuals.
- Keep endpoint calls idempotent where possible and tolerate repeated key presses.
- For settings changes, always trust backend `effective` response values for final UI state.
