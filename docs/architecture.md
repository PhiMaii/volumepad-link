# VolumePad Link Architecture

## Purpose

This document defines the proposed architecture for **VolumePad Link**, the Windows companion app for the VolumePad device.

It is written for:
- developers
- future contributors
- AI agents working on the repo
- firmware and desktop engineers who need a shared system view

This document focuses on:
- process architecture
- IPC and message contracts
- USB protocol shape
- audio session control
- device settings sync
- event/action flow
- display and LED streaming
- responsibilities of each subsystem
- unresolved questions and design decisions

---

## Product Summary

VolumePad consists of:
- a **Windows desktop companion app**
- a **background agent**
- a **USB-connected hardware device** with:
  - encoder
  - buttons
  - display
  - 32 indicator LEDs
  - haptics / detents

The system must support:

1. **Audio control**
   - list all active audio sessions
   - list master output
   - mute/unmute sessions
   - change session volume
   - change master volume
   - receive live level data for peak / RMS display

2. **Device configuration**
   - detent count
   - detent strength
   - haptic behavior
   - LED/display settings
   - other firmware parameters

3. **Device events**
   - button presses/releases
   - encoder turns
   - encoder button presses
   - gestures if added later

4. **Realtime device output**
   - display content updates
   - LED meter updates for 32 indicator LEDs
   - optional icons / labels / app state

---

## High-Level Architecture

The recommended architecture uses **one GitHub repo** with **three projects**:

- `VolumePadLink.UI`
- `VolumePadLink.Agent`
- `VolumePadLink.Contracts`

### Process model

There are **two executables**:

1. `VolumePadLink.exe`
   - WinUI desktop UI
   - user-facing windows and settings

2. `VolumePadLink.Agent.exe`
   - long-running background process
   - owns tray icon
   - owns USB communication
   - owns Windows audio integration
   - stays alive when UI closes

### Why two processes

The device and system integration concerns are long-lived and should not depend on the UI window.

Keeping the backend separate provides:
- stable USB ownership
- stable tray lifecycle
- stable audio session monitoring
- UI can be closed/reopened
- UI crash does not kill the backend
- cleaner architecture boundaries

---

## Repository Structure

```text
volumepad-link/
│
├─ src/
│   ├─ VolumePadLink.UI/
│   ├─ VolumePadLink.Agent/
│   └─ VolumePadLink.Contracts/
│
├─ docs/
│   └─ architecture.md
│
├─ tests/
│
├─ .github/
│   └─ workflows/
│
├─ VolumePadLink.sln
└─ README.md
```

---

## Project Responsibilities

## 1. VolumePadLink.UI

The UI process is responsible for:
- showing active audio sessions
- showing master volume
- letting the user mute/unmute sessions
- letting the user change session/master volume
- showing device connection state
- editing device settings
- editing mappings from hardware events to actions
- previewing live device events
- requesting display/LED previews if needed
- talking to the backend over IPC

The UI should **not**:
- open the serial port directly
- control Core Audio directly
- own the tray icon
- contain protocol parsing logic for device transport

The UI should talk only to the backend through a typed client API.

---

## 2. VolumePadLink.Agent

The agent is responsible for:
- tray icon
- startup at login
- single-instance backend ownership
- USB CDC communication with the device
- device discovery/reconnect
- Windows audio session enumeration
- master and app volume control
- level metering collection
- routing hardware events into desktop actions
- sending display frames / LED frames / settings to the device
- exposing IPC to the UI
- storing current app/device state

The agent is the main orchestration layer.

---

## 3. VolumePadLink.Contracts

This project contains shared types only:
- IPC envelopes
- command DTOs
- response DTOs
- event DTOs
- enums
- constants
- versioned protocol model classes where shared with UI

This project must contain **no business logic**.

---

## Architectural Layers

## UI process

```text
WinUI Views
   ↓
ViewModels
   ↓
IBackendClient
   ↓
Named Pipe IPC
```

## Agent process

```text
Pipe Server
   ↓
Command Router / Application Layer
   ↓
Domain Services
   ├─ Audio Service
   ├─ Device Service
   ├─ Action Mapping Service
   ├─ Display Service
   ├─ Led Streaming Service
   ├─ Tray Service
   └─ Settings Store
```

## Device side

```text
USB CDC Transport
   ↓
Firmware Message Parser
   ↓
Device State + Action Logic
   ├─ Haptics / Detents
   ├─ Display Rendering
   ├─ LED Rendering
   └─ Input Event Reporting
```

---

## Core Subsystems

## A. IPC between UI and Agent

### Transport

Use **Named Pipes** for local IPC on Windows.

### Why Named Pipes
- local only
- no network ports
- fast enough
- good fit for desktop app + local agent
- request/response and event streaming are both possible

### IPC Model

Use one logical API surface:
- UI sends commands/queries to agent
- agent sends responses
- agent pushes asynchronous events to UI

### IPC communication patterns

1. **Request / Response**
   - get audio sessions
   - set session volume
   - connect device
   - save mapping
   - update settings

2. **Push Events**
   - device connected/disconnected
   - new device event
   - audio sessions changed
   - live levels updated
   - backend errors/warnings

### Recommended message envelope

```json
{
  "type": "command",
  "id": "3e8f4f...",
  "name": "SetSessionVolume",
  "payload": {
    "sessionId": "spotify:1234",
    "value": 0.65
  }
}
```

Response:

```json
{
  "type": "response",
  "id": "3e8f4f...",
  "name": "SetSessionVolume",
  "payload": {
    "ok": true
  }
}
```

Event:

```json
{
  "type": "event",
  "name": "DeviceInputEvent",
  "payload": {
    "controlId": "encoder-main",
    "eventType": "rotate",
    "delta": 1,
    "timestampUtc": "2026-03-15T22:00:00Z"
  }
}
```

### IPC command groups

The UI-to-agent API should be grouped conceptually into:

- `Audio.*`
- `Device.*`
- `Mappings.*`
- `Display.*`
- `Leds.*`
- `App.*`
- `Diagnostics.*`

Example commands:
- `Audio.GetSessions`
- `Audio.SetMasterVolume`
- `Audio.SetSessionMute`
- `Device.GetStatus`
- `Device.Connect`
- `Device.UpdateSettings`
- `Mappings.GetAll`
- `Mappings.Save`
- `Display.PushPreview`
- `Leds.PushPreview`
- `App.ShowUi`
- `Diagnostics.GetLogs`

---

## B. Windows Audio subsystem

### Requirements

The agent must:
- enumerate active audio sessions
- identify master output endpoint
- expose master volume + mute
- expose per-session volume + mute
- expose session metadata:
  - process ID
  - process name
  - display name if available
  - icon if available
- observe session lifecycle changes
- provide meter data for UI/device visualization

### Audio model

Define three conceptual models:

#### 1. Master endpoint
Represents default output device control:
- volume
- mute
- endpoint name
- optional endpoint meter data

#### 2. Audio sessions
Each active app/session contains:
- stable internal ID
- process ID
- process name
- display label
- icon key or icon bytes
- current volume
- mute state
- availability / alive state
- optional peak meter value
- optional RMS approximation

#### 3. Audio graph state
A snapshot of:
- master endpoint
- active sessions
- active default device

### Audio service responsibilities

`AudioService`:
- enumerate sessions
- listen for changes
- set master volume
- set master mute
- set session volume
- set session mute
- publish session changes
- supply meter data to LED/display pipelines

### Important design decision: stable session identity

Per-session identity should not rely only on process name.

Recommended internal session key:
- endpoint ID + session instance identifier + process ID

UI may still show process name, but backend should keep a more stable key.

### Meter data cadence

The agent should sample levels at a bounded rate.

Recommended starting points:
- UI updates: 10 to 20 Hz
- LED peak meter updates to device: 20 to 60 Hz depending on USB bandwidth and firmware rendering cost

Do not tie meter transmission directly to every raw audio callback. Use a scheduler/coalescer.

---

## C. Device Communication subsystem

### Transport

Use **USB CDC** over a serial-like stream.

### Agent ownership

Only the agent should open the device port.

### Device service responsibilities

`DeviceService` owns:
- port discovery
- connect/disconnect
- reconnect
- protocol framing
- outgoing command queue
- incoming message parsing
- firmware version query
- capability query
- settings sync
- event forwarding

### Recommended internal layering

```text
DeviceService
  ├─ SerialTransport
  ├─ DeviceProtocolCodec
  ├─ DeviceConnectionState
  ├─ DeviceSettingsSync
  ├─ DisplayChannel
  └─ LedChannel
```

### Why separate protocol from transport

The serial port is only byte transport.

Separate layers make it easier to:
- test protocol without hardware
- support future transport changes
- log parsed messages
- version protocol cleanly

---

## Device Protocol Plan

A line-oriented JSON protocol is easy to debug, but may be too verbose for high-rate LED/display traffic.

Recommended approach:

### Hybrid protocol
Use:
- **structured control messages** for settings, events, acknowledgements
- **binary or compact framed payloads** for high-rate LED/display data if needed

### Practical phased plan

#### Phase 1: simple and debuggable
Use line-delimited JSON for everything.

Good for:
- bring-up
- firmware debugging
- settings sync
- event flow
- proving architecture

#### Phase 2: optimized channels
Keep JSON control messages, but add compact payload packets for:
- LED frame updates
- display bitmap/image data
- high-frequency meter data

This gives both debuggability and performance.

---

## Proposed Device Message Categories

### 1. Agent → Device: settings/config
Examples:
- detent count
- detent strength
- mode
- brightness
- display settings
- LED behavior settings

Example JSON:

```json
{
  "type": "settings.apply",
  "requestId": "abc123",
  "payload": {
    "detentCount": 24,
    "detentStrength": 0.65,
    "snapPoint": 0.4,
    "ledBrightness": 0.8
  }
}
```

Device responds:

```json
{
  "type": "ack",
  "requestId": "abc123",
  "ok": true
}
```

### 2. Device → Agent: hardware events
Examples:
- encoder rotate
- button down
- button up
- long press
- hold
- device booted
- settings changed locally if ever supported

Example:

```json
{
  "type": "input.event",
  "payload": {
    "controlId": "encoder-main",
    "eventType": "rotate",
    "delta": 1,
    "position": 42,
    "timestampMs": 123456
  }
}
```

### 3. Agent → Device: display data
Examples:
- render text
- render icon
- render app name
- render current value
- render frame/bitmap

Possible phase-1 JSON:

```json
{
  "type": "display.text",
  "payload": {
    "title": "Spotify",
    "value": "68%",
    "subtitle": "Volume"
  }
}
```

Phase-2 binary frame:
- frame header
- encoding type
- width/height
- payload length
- pixel data

### 4. Agent → Device: LED data
Examples:
- set all 32 indicator LEDs
- meter frame
- static preview
- segment colors

Phase-1 JSON:

```json
{
  "type": "leds.frame",
  "payload": {
    "mode": "meter",
    "values": [0,0,1,1,1,1,2,2,2,2,1,1,0,0, ...]
  }
}
```

Phase-2 compact frame:
- mode byte
- count byte
- packed intensity/color bytes

### 5. Bidirectional: diagnostics / capabilities
Examples:
- protocol version
- firmware version
- supported display formats
- supported LED modes
- max frame rate
- max packet size

This is important to make the desktop app resilient to future firmware revisions.

---

## D. Settings Synchronization

### Device settings categories

Split device settings into conceptual groups:

#### Mechanical / haptics
- detent count
- detent strength
- endstop behavior
- snap strength
- idle friction
- center detent behavior

#### Input behavior
- encoder direction inversion
- button debounce options
- long press thresholds
- double-click thresholds

#### Display behavior
- brightness
- idle timeout
- theme or color mode
- render style

#### LED behavior
- brightness
- meter mode
- decay behavior
- smoothing
- peak hold behavior

#### Connectivity / protocol
- device name
- firmware compatibility flags
- protocol version
- debug logging enable

### Source of truth

Recommended source of truth:
- **desktop app / backend owns persisted user settings**
- device owns currently applied runtime settings
- on connect, agent pushes desired settings to device
- device acks applied values
- agent updates state accordingly

This is simpler than trying to make the device the long-term authority.

### Sync flow on connect

1. Device connects
2. Agent queries capabilities + firmware version
3. Agent loads saved user profile/settings
4. Agent sends `settings.apply`
5. Device validates and applies
6. Device responds with ack / normalized applied values
7. Agent stores effective runtime state

### Why return normalized applied values

The device may clamp or adjust values:
- detent count unsupported
- strength out of range
- brightness quantized
- firmware-specific limits

The agent should know the values actually applied.

---

## E. Hardware Event → Action Mapping

This is one of the most important layers.

### Goal

Map raw device events to desktop actions.

Examples:
- encoder rotate → change master volume
- encoder rotate → change Spotify session volume
- button press → toggle mute
- long press → switch profile
- button 2 → play/pause
- encoder press → toggle default target

### Raw input event model

A raw device event should contain:
- device ID
- control ID
- event type
- timestamp
- payload specific to type

Example:

```json
{
  "deviceId": "vp-001",
  "controlId": "button-1",
  "eventType": "press",
  "timestampUtc": "2026-03-15T22:05:00Z"
}
```

### Action model

Actions should be defined independently from transport.

Examples:
- `Audio.ChangeMasterVolume(delta)`
- `Audio.ToggleMasterMute`
- `Audio.ChangeSessionVolume(targetSession, delta)`
- `Audio.ToggleSessionMute(targetSession)`
- `Profile.Next`
- `App.ShowOverlay`
- `Device.SetLedMode(mode)`

### Mapping layer responsibilities

`ActionMappingService` should:
- load user mappings
- evaluate incoming events
- determine target action
- execute action
- report result/logging
- optionally update display/LED output after action

### Profiles

Mappings should be profile-based.

Example profiles:
- Master Volume
- Media
- Discord
- Spotify
- Dynamic Active App
- Custom

The active profile determines:
- which actions the controls trigger
- what the display shows
- what the LED meter represents

---

## F. Display pipeline

### Requirements
The device display should show:
- selected target
- current value
- mute state
- app name/icon if possible
- profile name
- transient action feedback
- optional menu/settings preview

### Display ownership

Recommended:
- agent owns semantic display content
- device owns final rendering of that content
- agent sends structured render commands or compact frame data

### Two possible strategies

#### Strategy 1: semantic commands
Agent sends:
- title
- subtitle
- value
- icon ID
- state flags

Device renders with its own UI system.

Pros:
- lower bandwidth
- simpler updates
- firmware can animate locally

Cons:
- desktop side has less pixel-perfect control

#### Strategy 2: full framebuffer/image push
Agent renders off-device and sends bitmap/frame

Pros:
- total desktop control

Cons:
- much higher bandwidth
- more complexity
- less firmware autonomy

### Recommendation

Use a desktop-side rendered framebuffer with dirty-area transfer from the beginning.

That means the display pipeline should support:
- full frame generation on the agent side
- partial dirty-rectangle updates
- selective refresh so unchanged screen areas are not resent

### Suggested display model

```json
{
  "screen": "target-status",
  "title": "Spotify",
  "valueText": "68%",
  "subtitle": "Volume",
  "iconRef": "spotify",
  "muted": false,
  "accent": "#00D26A"
}
```

The firmware can translate this into device UI.

---

## G. LED pipeline

### Requirements

The 32 LEDs should support:
- peak/RMS meter
- segmented level display
- mute indication
- profile color/state
- temporary animations for actions
- optional idle effects

### Recommended ownership model

The agent computes the current **semantic LED state**.
The device renders the actual LED output.

### Important design decision

Avoid streaming 32 RGB values at audio callback rate unless necessary.

Instead define LED modes such as:
- static
- segmented meter
- peak meter
- mute flash
- action pulse
- profile color

The agent can send either:
1. semantic meter values
2. or full LED frames

### Recommendation

Start with **semantic meter updates**.

Example:
- target: Spotify
- normalized level: 0.43
- peak: 0.62
- muted: false
- color theme: green → yellow → red

Then firmware renders the 32 LEDs itself.

This reduces bandwidth and jitter.

### Semantic meter message example

```json
{
  "type": "leds.meter",
  "payload": {
    "mode": "peak-rms",
    "rms": 0.43,
    "peak": 0.62,
    "muted": false,
    "theme": "audio-default"
  }
}
```

### Full frame fallback

For preview/testing or special modes, support a direct frame:

```json
{
  "type": "leds.frame",
  "payload": {
    "pixels": [
      {"r":0,"g":0,"b":0},
      {"r":0,"g":16,"b":0}
    ]
  }
}
```

Use full frame mode only when needed.

---

## H. State Management

The agent should maintain a central application state store.

### Suggested state domains
- backend lifecycle state
- audio graph state
- device connection state
- device capabilities
- active profile
- user mappings
- effective device settings
- live meter state
- latest display model
- latest LED model

### Why central state matters
It simplifies:
- UI snapshots
- reconnect behavior
- display refresh after action changes
- diagnostics
- event replay/logging
- testing

---

## I. Persistence

Persist the following on desktop:
- user profiles
- action mappings
- preferred audio target(s)
- device settings presets
- last active profile
- UI preferences
- known device associations if needed

Do not rely on the device to be the only persistence layer.

---

## Communication Flow Plans

## 1. UI opens and requests audio state

```text
UI → Agent: Audio.GetGraph
Agent → UI: master + sessions snapshot
UI renders master row and active session rows
```

### Response should include
- master endpoint
- session list
- mute states
- volumes
- icon keys / references
- current active profile if relevant

---

## 2. User changes a session volume in UI

```text
UI → Agent: Audio.SetSessionVolume(sessionId, value)
Agent → AudioService: apply value
AudioService → Agent: success / updated session state
Agent → UI: response + Audio.SessionUpdated event
```

If the changed session is currently the device target, the agent should also refresh:
- display semantic model
- LED meter target if relevant

---

## 3. User updates device settings in UI

```text
UI → Agent: Device.UpdateSettings(settingsPatch)
Agent validates patch
Agent updates persisted settings
If device connected:
    Agent → Device: settings.apply(...)
    Device → Agent: ack/applied-values
Agent updates effective runtime state
Agent → UI: Device.SettingsApplied event
```

If device is not connected:
- save desired settings
- mark as pending
- push on next connect

---

## 4. Device sends encoder turn event

```text
Device → Agent: input.event rotate delta=+1
Agent parses event
Agent → ActionMappingService: resolve mapping for current profile
Mapping returns action: Audio.ChangeTargetVolume(+0.02)
Agent → AudioService: apply target volume change
Agent updates state
Agent → UI: DeviceInputEvent + Audio.SessionUpdated / Audio.MasterUpdated
Agent → Device: display update + leds update if needed
```

### Important note
The device should not decide desktop-side audio logic.
It only sends raw input events.
The agent decides what those mean.

---

## 5. Device sends button press event to toggle mute

```text
Device → Agent: input.event button-1 press
Agent resolves mapping
Action = Audio.ToggleTargetMute
Agent applies action
Agent pushes UI updates
Agent sends semantic display/LED updates back to device
```

---

## 6. Agent streams live meter data to device

### Recommended loop
1. Audio service samples target meter data
2. Meter scheduler smooths/coalesces values
3. Agent generates semantic LED update
4. Agent sends LED meter update to device at capped rate

```text
AudioService → MeterScheduler → Device LedChannel → Device
```

### Suggested rate limits
- start at 20 Hz
- increase only if needed
- allow firmware smoothing so desktop side does not need very high rates

---

## 7. Agent updates display after target change

Example: active target switches from master to Spotify.

```text
Target changes
Agent computes new display model
Agent → Device: display.render(target-status)
Device updates screen
```

---

## Recommended Message Contracts

## IPC Contracts (UI ↔ Agent)

### Commands
- `Audio.GetGraph`
- `Audio.SetMasterVolume`
- `Audio.SetMasterMute`
- `Audio.SetSessionVolume`
- `Audio.SetSessionMute`
- `Audio.SetActiveTarget`
- `Device.GetStatus`
- `Device.Connect`
- `Device.Disconnect`
- `Device.GetCapabilities`
- `Device.UpdateSettings`
- `Device.PushDisplayPreview`
- `Device.PushLedPreview`
- `Mappings.GetAll`
- `Mappings.Save`
- `Profiles.GetAll`
- `Profiles.SetActive`
- `Diagnostics.GetStateSnapshot`

### Events
- `Audio.GraphChanged`
- `Audio.MasterChanged`
- `Audio.SessionAdded`
- `Audio.SessionUpdated`
- `Audio.SessionRemoved`
- `Device.Connected`
- `Device.Disconnected`
- `Device.InputEvent`
- `Device.SettingsApplied`
- `Device.CapabilitiesReceived`
- `Mappings.ActiveProfileChanged`
- `Diagnostics.Warning`
- `Diagnostics.Error`

---

## Device Protocol Contracts (Agent ↔ Device)

### Device → Agent
- `hello`
- `capabilities`
- `input.event`
- `ack`
- `nack`
- `diag.log`
- `state.report`

### Agent → Device
- `hello`
- `settings.apply`
- `display.render`
- `display.clear`
- `leds.meter`
- `leds.frame`
- `device.ping`
- `device.reset`
- `device.requestState`

---

## Suggested DTOs

### AudioSessionDto
- `SessionId`
- `ProcessId`
- `ProcessName`
- `DisplayName`
- `Volume`
- `Muted`
- `Peak`
- `Rms`
- `IconKey`
- `IsActive`

### MasterAudioDto
- `EndpointId`
- `EndpointName`
- `Volume`
- `Muted`
- `Peak`
- `Rms`

### DeviceSettingsDto
- `DetentCount`
- `DetentStrength`
- `SnapStrength`
- `LedBrightness`
- `DisplayBrightness`
- `EncoderInvert`
- `ButtonLongPressMs`

### DeviceInputEventDto
- `DeviceId`
- `ControlId`
- `EventType`
- `Delta`
- `Pressed`
- `Position`
- `TimestampUtc`

### DisplayModelDto
- `Screen`
- `Title`
- `Subtitle`
- `ValueText`
- `IconRef`
- `Muted`
- `Accent`

### LedMeterModelDto
- `Mode`
- `Rms`
- `Peak`
- `Muted`
- `Theme`
- `Brightness`

---

## Concurrency and Performance Guidance

## Audio meter updates
- should be decoupled from UI rendering
- should be rate-limited
- should be smoothed
- should not spam IPC or USB without coalescing

## USB writes
Use a write queue in the agent:
- preserve packet order
- avoid concurrent port writes
- allow priority channels if needed

Recommended write priorities:
1. settings/acks
2. display semantic updates
3. LED meter updates
4. diagnostics

LED meter frames are the most disposable and should be droppable if the queue backs up.

## Event storms
Encoder turns can arrive rapidly.

Recommended handling:
- do not debounce turns away entirely
- but coalesce where the action allows it
- keep action execution deterministic

Example:
- multiple small rotate events may be merged into one delta before applying volume if latency remains acceptable

---

## Error Handling Strategy

## Device disconnect
On disconnect:
- agent updates state
- UI receives disconnect event
- pending LED/display sends are canceled
- reconnect loop starts
- last desired settings remain persisted

## Unsupported setting
If firmware does not support a setting:
- device returns `nack` with reason
- agent marks setting unsupported
- UI can disable/hide that field

## Audio session disappears
If a target app closes:
- backend marks session inactive/removed
- if it was the current device target:
  - fallback to master or configured fallback target
  - refresh display/LED state

## UI not running
Agent must continue to function:
- hardware events still control audio
- tray icon still works
- reconnect still works
- device still receives display/LED updates

---

## Security / Trust Boundaries

This is a local desktop app, but still define trust boundaries:

- UI is trusted but should still use validated IPC contracts
- device messages are not fully trusted and should be validated
- all incoming device payloads should be range-checked
- unknown message types should be logged and ignored safely

Do not let malformed device input crash the agent.

---

## Recommended Initial Implementation Order

### Phase 1: skeleton
- monorepo
- three projects
- agent single-instance
- UI ↔ agent pipe hello world
- tray icon
- basic device connect/disconnect
- basic audio enumeration

### Phase 2: control basics
- master volume + mute
- per-session volume + mute
- UI listing active sessions
- raw device events visible in UI
- simple action mapping

### Phase 3: settings sync
- detent settings UI
- persisted settings
- settings.apply protocol
- ack/nack + effective values

### Phase 4: display and LED semantics
- semantic display model
- semantic LED meter model
- target status screen
- peak/rms LED meter

### Phase 5: optimization
- compact device packets for LEDs/display if required
- smoothing and scheduler tuning
- richer profiles and mappings

---


## Clarified Decisions

The following decisions are now fixed for this project.

### 1. Audio target selection must remain flexible
The controlled target must be changeable later in software without requiring protocol redesign.

The system must support at least:
- master volume
- a fixed selected app/session
- cycling through available apps from the device
- selecting the target from the desktop UI
- selecting the target from a Stream Deck plugin

This means the action/mapping system must not hardcode a single target model.
Instead, target selection should be represented as a first-class concept in backend state.

Recommended target model:
- `Master`
- `SessionById`
- `SessionByLogicalApp`
- `CycleNextSession`
- `CyclePreviousSession`

The backend should expose a current **active target** abstraction.
All “change volume”, “toggle mute”, display rendering, and LED rendering logic should be able to reference that active target.

### 2. LED meter source must also be flexible
The 32-LED meter source must be switchable from:
- the device
- the desktop UI
- the Stream Deck plugin

The backend should therefore maintain a configurable **meter source** state independent from the active control target.

Recommended meter source model:
- `Master`
- `ActiveTarget`
- `SessionById`
- `SessionByLogicalApp`

This should allow use cases such as:
- knob controls Spotify but LEDs show master
- knob controls master but LEDs show Discord
- cycling meter source independently from target selection

### 3. Display transport uses full image transfers
The display pipeline should support **full image/frame transfers**, but use a **dirty area / dirty rectangle system** so only changed areas must be sent.

This means the display model should not be limited to semantic title/value/subtitle rendering.
Instead, the agent should be able to render or assemble a framebuffer/image and send only changed regions.

Recommended display approach:
- a desktop-side render pipeline produces the desired screen image
- a diffing layer compares current frame vs previous frame
- only changed rectangles are transmitted to the device
- the device blits those rectangles into its local framebuffer and refreshes the display

The protocol should therefore support:
- display init / format negotiation
- full frame transfer
- partial rectangle transfer
- clear/fill operations if useful
- ack/nack for frame chunks if reliability is needed

### 4. Single device only
The system only needs to support one VolumePad device.

This simplifies:
- state model
- backend ownership
- active device tracking
- target and settings sync

Multiple-device abstractions do not need to be implemented now.

### 5. Settings are desktop-app only
Settings are edited only in the desktop app.

The device is not a source of persisted settings authority.
The backend/desktop side remains the single source of truth for persisted configuration.

This simplifies settings sync to:
- desktop persists desired settings
- backend pushes them on connect
- device applies and acknowledges effective values

### 6. Stream Deck integration is a first-class external control surface
The Stream Deck plugin should be treated as another client that can:
- select active target
- cycle available targets
- change meter source
- toggle mute
- possibly request status for display/keys
- possibly trigger profile changes

The backend should therefore expose a stable external API suitable for both:
- WinUI desktop UI
- Stream Deck plugin

---

## Stream Deck Plugin Integration

### Goals

The Stream Deck plugin should be able to:
- select a target app/session
- cycle targets
- set active target to master
- toggle mute of current target
- optionally adjust volume of the current target
- change LED meter source
- request current state
- subscribe to state changes if needed for key feedback

Examples:
- key: “Select Spotify”
- key: “Cycle Next App”
- key: “Toggle Current Target Mute”
- key: “Set Meter Source = Active Target”
- key: “Set Meter Source = Master”

### Recommended communication architecture

Recommended approach:

```text
Stream Deck Plugin
    ↓
local IPC client
    ↓
VolumePadLink.Agent
```

The Stream Deck plugin should talk directly to the **backend agent**, not to the UI process.

Why:
- the UI may not be running
- the backend is already the source of truth
- the backend already owns audio and device state
- the plugin should work even when the UI is closed

### Recommended transport: loopback HTTP/WebSocket API

For the Stream Deck plugin, I recommend a **small localhost API exposed by the backend** rather than trying to reuse Named Pipes directly.

Recommended shape:
- HTTP for request/response commands
- WebSocket for pushed state updates if needed

Why this is the best fit:
- Stream Deck plugins are easiest to integrate with HTTP/WebSocket style communication
- easier debugging with Postman/curl/devtools
- easier multi-client support
- cleaner separation between internal desktop IPC and external plugin API
- plugin implementation is simpler than custom named pipe handling

### Why not Named Pipes for Stream Deck plugin
Named Pipes are excellent for WinUI ↔ backend on Windows.
But for a Stream Deck plugin, they are less convenient because:
- plugin runtime ergonomics are usually better with HTTP/WebSocket
- browser-style/plugin-side tooling is friendlier for HTTP/WebSocket
- external integrations are easier to expand later

### Recommended dual-API model

Use two APIs in the backend:

#### Internal API
- Named Pipes
- used by `VolumePadLink.UI`

#### External local API
- localhost HTTP + optional WebSocket
- used by Stream Deck plugin
- may later also be used by scripts/tools/debug clients

This avoids forcing the plugin to use the same IPC transport as the UI.

---

## Backend API Surface for Stream Deck

### Base design
Expose a local API from the agent, for example conceptually:

- `GET /state`
- `GET /audio/sessions`
- `POST /target/select`
- `POST /target/cycle-next`
- `POST /target/cycle-previous`
- `POST /audio/toggle-mute`
- `POST /audio/set-volume`
- `POST /meter-source/select`
- `POST /profiles/select`

The exact endpoint names can evolve, but the important part is that the plugin API remains a thin wrapper over backend domain actions.

### Example command payloads

Select master:

```json
{
  "targetType": "Master"
}
```

Select a session:

```json
{
  "targetType": "SessionById",
  "sessionId": "spotify:1234"
}
```

Select meter source:

```json
{
  "meterSourceType": "ActiveTarget"
}
```

### Recommended WebSocket events

If the Stream Deck plugin wants live key updates, the backend can publish events such as:
- `state.changed`
- `audio.sessions.changed`
- `activeTarget.changed`
- `meterSource.changed`
- `device.connected`
- `device.disconnected`

This allows keys on the Stream Deck to reflect current state:
- highlight active target
- show mute state
- show whether device is connected
- show whether meter source is master or active target

---

## Unified Domain Model for UI and Stream Deck

To keep the system clean, both UI and Stream Deck should operate through the same backend domain concepts.

### Target selection model
Define a shared target abstraction:
- `Master`
- `SessionById`
- `SessionByLogicalApp`

### Meter source model
Define a shared meter source abstraction:
- `Master`
- `ActiveTarget`
- `SessionById`
- `SessionByLogicalApp`

### Profile model
Profiles remain desktop/backend concepts and can also be selected from Stream Deck.

### Advantages
This avoids separate logic paths:
- UI uses the same domain operations as Stream Deck
- device actions can reference the same active target and meter source
- one source of truth for current state

---

## Revised Display Pipeline

Because full image transfer is required, the display system should be split into:

```text
DisplayModelComposer
    ↓
DisplayRenderer
    ↓
DirtyRegionTracker
    ↓
DisplayTransportEncoder
    ↓
Device
```

### Responsibilities

#### DisplayModelComposer
Builds the conceptual screen state from:
- active target
- mute state
- current value
- profile
- icons
- transient overlays

#### DisplayRenderer
Renders that conceptual state into a framebuffer/image.

#### DirtyRegionTracker
Compares current rendered frame to previous frame and produces a set of dirty rectangles.

#### DisplayTransportEncoder
Encodes dirty rectangles into protocol packets and sends them to the device.

### Dirty area strategy

Recommended dirty area flow:
1. render next frame
2. compare with previous frame
3. merge small nearby dirty rectangles where useful
4. cap total rectangle count
5. if dirty coverage is too large, send full frame instead

This prevents many tiny packet bursts and keeps bandwidth manageable.

### Suggested display protocol operations
- `display.beginFrame`
- `display.rect`
- `display.endFrame`
- `display.fullFrame`
- `display.clear`

Exact wire format can be defined later.

---

## Revised State Concepts

The backend state store must include at minimum:

- `ActiveTarget`
- `MeterSource`
- `AudioGraph`
- `DeviceConnectionState`
- `DeviceCapabilities`
- `EffectiveDeviceSettings`
- `ActiveProfile`
- `DisplayFrameState`
- `LedState`

These are needed so:
- UI can edit/view them
- Stream Deck plugin can control them
- device event mappings can reference them
- display and LEDs can update coherently

---

## Additional Recommended Commands

### IPC / UI commands
- `Target.GetActive`
- `Target.Select`
- `Target.CycleNext`
- `Target.CyclePrevious`
- `MeterSource.Get`
- `MeterSource.Set`

### Stream Deck / external API commands
- `Target.SelectMaster`
- `Target.SelectSession`
- `Target.CycleNext`
- `Target.CyclePrevious`
- `MeterSource.SetMaster`
- `MeterSource.SetActiveTarget`
- `MeterSource.SetSession`
- `Audio.ToggleTargetMute`
- `Audio.ChangeTargetVolume`
- `Profiles.SetActive`

---

## Additional Recommended Events

- `Target.ActiveChanged`
- `MeterSource.Changed`
- `Display.FrameSent`
- `Display.TransportWarning`
- `Led.SourceChanged`

---

## Recommendation Summary for Stream Deck Communication

### Recommended answer
Use:
- **Named Pipes** between UI and backend
- **localhost HTTP + optional WebSocket** between Stream Deck plugin and backend

### Why this combination
It gives the cleanest setup:
- WinUI gets efficient local Windows IPC
- Stream Deck plugin gets plugin-friendly API communication
- backend stays the single source of truth
- future integrations remain easy

### Strong recommendation
Do not make the Stream Deck plugin talk to the UI.
Do not make the device depend on the Stream Deck plugin.
All orchestration should remain in the backend agent.


## Open Questions / Clarifications Needed

The following points should be clarified before final implementation details are frozen.

### 1. Audio target model
Should the encoder/buttons always control:
- master volume by default,
- a user-selected fixed target app,
- the currently focused app,
- or a profile-defined target?

This affects the mapping engine and display model.

### 2. Session identity / persistence
If the user maps to Spotify, should that target survive process restarts by:
- process name,
- executable path,
- audio session identifier,
- or user-selected logical app target?

### 3. Meter source
For the 32 LEDs, should the meter represent:
- master output level,
- active target app level,
- microphone level,
- or profile-dependent source?

### 4. Display rendering strategy
Is semantic rendering enough for the device screen, or do you expect desktop-side image/frame pushing for normal operation?

### 5. LED rendering strategy
Should the firmware render meter behavior from semantic values, or do you want pixel-perfect desktop-side LED frame control most of the time?

### 6. Event granularity
For encoder rotation, should the device send:
- raw detent events,
- raw delta values,
- absolute position,
- or all of the above?

### 7. Action execution latency target
What latency is acceptable from:
device input → desktop action → feedback on LEDs/display?

This affects batching/coalescing strategy.

### 8. Multiple devices
Do you plan to support:
- exactly one VolumePad,
- or multiple simultaneously connected devices later?

### 9. Local hardware-side menus
Will some settings be editable on the device itself, or is the desktop app the only settings editor?

If editable on-device, settings sync becomes two-way.

### 10. Protocol format preference
For early development, do you prefer:
- easiest debugging with line-delimited JSON,
- or starting immediately with a framed binary protocol?

---

## Strong Recommendations

1. Keep the **agent** as the only owner of USB and audio.
2. Keep the **UI** as a pure control surface.
3. Keep **device events raw** and map them in the agent.
4. Start with **semantic display/LED updates**, not pixel/LED frames everywhere.
5. Use **JSON control messages first**, optimize only after measuring.
6. Build a **central state store** in the agent.
7. Treat **settings sync** as desktop-authoritative unless on-device editing is required.
8. Make **LED meter updates droppable** under load.
9. Build **capability negotiation** into the device protocol from the start.
10. Keep the contracts project small and clean.

---

## Final Summary

The cleanest architecture for VolumePad Link is:

- one repo
- three projects
- two executables
- UI talks only to agent
- agent owns audio, USB, tray, mappings, state
- device sends raw input events
- agent resolves actions
- agent sends semantic display/LED updates
- settings are persisted on desktop and synced to device on connect

This design is robust, testable, and flexible enough for:
- master/session volume control
- configurable detents/haptics
- display feedback
- realtime LED metering
- future profiles and richer device capabilities
