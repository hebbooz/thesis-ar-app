# Protocol Contract — AR Client

The messaging contract this AR application must honour. **Mirrored from the control repository (`<url-of-control-repo>`), which is the source of truth.** If the two ever disagree, the control repo wins — update this copy.

Only the parts relevant to an AR client are reproduced here. The full protocol (smart plugs, sensor ingestion, buttons, display) lives in the control repo.

---

## 1. What this app receives

The orchestration server broadcasts an OSC bundle over UDP at a fixed rate (default **5 Hz**, i.e. every 200 ms) to every registered client.

| Address | Type | Range | Use in this app |
|---|---|---|---|
| `/coral/state` | int32 | 0–3 | Discrete selector — which material set; whether polyp footage is alive |
| `/coral/intensity` | float32 | 0.0–1.0 | Continuous driver — material vividness, colour blend, fade factors |
| `/coral/temp` | float32 | °C | Live water temperature. Informational; not normally rendered by this app |

**Listen port:** UDP **9001** (configurable; must match the server's `broadcast.client_port`).

### Applying the values

- `intensity` is the primary driver for anything continuous. Lerp materials against it so changes read as smooth fades rather than steps.
- `state` selects discrete behaviour only.
- Values arrive repeatedly and unchanged by design — application must be idempotent.
- On a missed message, hold the last known values. Never treat absence as an error.

---

## 2. What this app sends

Exactly one message, and nothing else:

| Address | Type | Frequency | Meaning |
|---|---|---|---|
| `/client/hello` | string (device id) | every **5 s** | "I'm here, send broadcasts to me" |

**Send to:** the server's IP on UDP **9000** (configurable).

The server records the sender's IP from the UDP packet — the payload only needs a stable, unique identifier for the device (e.g. `phone-a`, or a device name). Registry entries idle for more than **15 s** are pruned; sending `hello` again re-registers automatically.

This app has **no other outbound messages** and no control authority over the installation.

---

## 3. State semantics

| State | Name | Entered when | AR presentation |
|---|---|---|---|
| 0 | Natural | Water at ~26 °C, no bleach latch | Healthy orange coral material; magnifier reveals lively polyp footage |
| 1 | Fluorescent | Temperature rising above 26.2 °C | Material lerps toward vivid fluorescence with `intensity`; polyps still alive |
| 2 | Bleached | ≥27.8 °C sustained 10 s — **latches** | Bleached white material; polyp footage fades to ghostly stillness or absence |
| 3 | Recovery | Cooling began, after a ~30 s lag | Gradually heals back toward healthy; life returns |

**State 2 is latched on the server.** Once bleached, the installation stays bleached regardless of temperature until the server initiates recovery — and recovery only begins after a deliberate delay. This app must never implement local recovery, shortcut the lag, or attempt to "fix" the appearance.

The transition from 0→1 is fully reversible; only bleaching is a one-way door. That asymmetry is thematic, not a bug.

---

## 4. The magnifier

Device-to-target distance is used **only** for the magnifier effect — never to derive state.

- Moving the device closer progressively reveals embedded polyp video through a masked blend over the coral-surface material.
- In **state 2**, the footage is desaturated to stillness or absent entirely. A visitor who leans in expecting life and finds none is the installation's key emotional beat; preserve it.
- In **state 3**, life returns gradually as the coral heals.

---

## 5. Lifecycle requirements

| Situation | Required behaviour |
|---|---|
| App launches before the server exists | Boot to state 0 / intensity 0.0; wait silently; converge on first broadcast |
| Server restarts mid-session | Hold last values; reconverge within one broadcast interval |
| Device sleeps and wakes | Resume sending `hello`; resync on next broadcast |
| Network drops briefly | Hold last values; no error state shown to the visitor |
| Three devices running at once | Each operates independently; no coordination between them |

Startup order must never matter. There is no handshake, no acknowledgement, and no retry logic — the continuously repeated broadcast is itself the recovery mechanism.

---

## 6. Configuration

Keep these as editable settings, not hard-coded values:

| Setting | Default |
|---|---|
| Server IP | (set per installation) |
| Server port (for `hello`) | 9000 |
| This device's listen port | 9001 |
| Device id | unique per device |
| Hello interval | 5 s |

---

## 7. Reference implementation notes

- **extOSC** (free Unity package) handles both receive and send.
- Register an `OSCReceiver` on the listen port with bindings for the three `/coral/*` addresses.
- Drive a single `OSCTransmitter` on a 5-second repeating invoke for `hello`.
- Keep the received values in one small state object that materials and the magnifier read from — don't scatter OSC handling through the scene.