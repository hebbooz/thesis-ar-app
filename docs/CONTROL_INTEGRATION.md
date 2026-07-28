# CONTROL_INTEGRATION.md — wiring this app to the orchestration server

**Read this before writing any OSC code.** It is the complete brief for Phase 7 of
the installation build: everything this Unity project needs to become a subscriber
to the control system, including the parts that are easy to get wrong and expensive
to debug on a device.

Companion documents:

| Document | Role |
|---|---|
| `docs/PROTOCOL.md` (this repo) | The wire contract. **Mirror** — refreshed 2026-07-27 from the control repo. |
| `CLAUDE.md` §9 (this repo) | Why this app is a passive subscriber, and the rules it must follow. |
| `docs/PROGRESS.md` (this repo) | What already exists on the Unity side. Read before instructing any setup. |

**Source of truth for the contract is the control repository**, at
`/Users/HeberdenN21/Documents/2026 University/Thesis/code` —
specifically `docs/PROTOCOL.md`, `docs/ARCHITECTURE.md`, `CLAUDE.md` and
`config.yaml`. If the contract changes there, re-copy `docs/PROTOCOL.md` here.
Never edit the mirror to resolve a disagreement.

---

## 1. What this app is inside the system

A Python orchestration server owns one number. It reads the temperature of a water
bath containing a second, physical coral, derives a discrete `state` (0–3) and a
continuous `intensity` (0.0–1.0), and unicasts both over OSC/UDP **5×/second** to
every subscriber: this AR app, a projected reef, Ableton, and a lamp.

This app is a **passive subscriber**. It receives `state`/`intensity`/`temp` and
sends exactly one thing back: `/client/hello`, so the server knows where to send.
It has no control authority, derives no state locally, and holds nothing but the
last values received.

**Visitors change the coral by pressing physical buttons at the water bath, not by
moving the iPad.** That is the thesis argument — agency lives at the artefact, and
every screen in the room shows the same consequence at once.

---

## 2. The wire contract (exact)

### Inbound — server → this app, 5 Hz

Delivered as **one OSC bundle** containing three messages:

| Address | Type | Range | Use |
|---|---|---|---|
| `/coral/state` | int32 | 0–3 | **Switch** on it (which appearance regime) |
| `/coral/intensity` | float32 | 0.0–1.0 | **Interpolate** on it (the continuous driver) |
| `/coral/temp` | float32 | °C | Display / diagnostics only |

Default listen port: **UDP 9001** (`broadcast.client_port` in the server's config).

### Outbound — this app → server, every 5 s

| Address | Type | Payload |
|---|---|---|
| `/client/hello` | string | A stable unique id for this device |

Sent to the server's OSC listener, default **UDP 9000** (`broadcast.listen_port`).
The server registers `{id, ip, port, last_seen}` and prunes entries silent for
**15 s**. Registry is keyed by **id** — two devices sharing an id clobber each
other, so the id must be genuinely unique per device.

### The one rule that will silently break everything

> **`/client/hello` MUST be sent from the same socket the app listens on.**

The server replies to the **source `(ip, port)` of the hello packet** — not to a
fixed port, and not to the payload. A client that transmits hello from an ephemeral
port will register successfully, appear in the server log as connected, and **never
receive a single broadcast**. There is no error anywhere; it just sits at state 0
forever.

In extOSC this is one line, and the default is wrong:

```csharp
_receiver = gameObject.AddComponent<OSCReceiver>();
_receiver.LocalPort = 9001;
_receiver.Bind("/coral/state",     OnState);
_receiver.Bind("/coral/intensity", OnIntensity);
_receiver.Bind("/coral/temp",      OnTemp);
_receiver.Connect();

_transmitter = gameObject.AddComponent<OSCTransmitter>();
_transmitter.RemoteHost = cfg.serverHost;
_transmitter.RemotePort = cfg.serverPort;     // 9000
_transmitter.LocalPortMode  = OSCLocalPortMode.FromReceiver;  // NOT the default
_transmitter.SourceReceiver = _receiver;                      // share the socket
_transmitter.Connect();
```

`OSCTransmitter.LocalPortMode` defaults to `OSCLocalPortMode.Random`. Leaving it
there is exactly the failure above. The three enum members are `Random`,
`FromReceiver`, `Custom` — `FromReceiver` + `SourceReceiver` is the correct idiom
(`Custom` + `LocalPort = 9001` also works but duplicates the port in two places).

### extOSC facts already verified against its source — do not re-derive

- **Bundles are unpacked.** `Bind("/coral/state", cb)` fires for messages nested
  inside a bundle; the receiver recurses into `bundle.Packets`. No bundle-specific
  handling needed.
- **Callbacks run on the Unity main thread.** A background thread enqueues packets;
  `OSCReceiver.Update()` drains the queue (capped at 20 ms per frame). Touching
  materials and transforms inside a bind callback is safe.
- extOSC is **vendored as an embedded package** at `Packages/com.iam1337.extosc`
  (v1.21.0), *not* pulled from a registry. It arrives with its own `extOSC` /
  `extOSC.Editor` asmdefs — hence the `extOSC` entry in
  `CoralPolyps.Runtime.asmdef`. Pure networking code, URP-agnostic.

  **Why embedded, and do not "fix" this back to OpenUPM.** It was installed from
  OpenUPM first (scoped registry + `"com.iam1337.extosc": "1.21.0"`), which is the
  better default. But **extOSC 1.21.0 does not compile on Unity 6000.5**:
  `Scripts/Editor/OSCHierarchyIcon.cs` calls
  `EditorApplication.hierarchyWindowItemOnGUI` and
  `EditorUtility.InstanceIDToObject(int)`, which Unity 6000.5 promoted from
  obsolete-*warning* to obsolete-*error* (CS0619). The package is dated March 2025
  and predates that Editor; 1.21.0 is the latest release, so there is no upstream
  fix to wait for. A registry package lands in `Library/PackageCache/`, which
  Unity overwrites on every resolve, so it cannot be patched in place.

  **The patch:** `OSCHierarchyIcon.cs` is deleted. It only drew the extOSC icon
  beside OSC components in the Hierarchy window — nothing depends on it, and
  extOSC's custom inspectors (which *are* useful when wiring components) are
  untouched. `Examples~` was also dropped: Unity ignores `~` folders, so it was
  1.7 MB of pure repository weight.

  **If a future extOSC release supports Unity 6000.5**, delete
  `Packages/com.iam1337.extosc`, restore the scoped registry and the dependency
  line, and confirm the editor assembly compiles before committing.

### Contract rules (from PROTOCOL.md §1)

- Boot assuming **state 0 / intensity 0.0** and converge silently on the first
  message. The server may not be running yet; that is not an error.
- **Apply idempotently** — the same values arrive 5×/second by design.
- **Hold the last value** on silence. Never treat a gap as a signal.
- **Never re-derive state from temperature.** `/coral/temp` is for the HUD.
- Send nothing except `hello`.

---

## 3. The mapping — server `(state, intensity)` → what the app shows

The app renders **two layers**, and *both* are driven by the broadcast:

| Layer | What it is | Driven by |
|---|---|---|
| **Surface tissue** | Fluorescent living tissue on the coral mesh (`FluorescentTissue.shader`) | `state` + `intensity` → `_Stress` (§3.1) |
| **Magnifier** | Polyp footage revealed inside the loupe as the viewer leans in | `state` + `intensity` → which footage, at what weight (§3.2) |

**Proximity controls neither layer's appearance.** It controls only *how much of the
magnifier is revealed* — the mask and the magnification. What the revealed polyps
are *doing* — alive, fluorescing, dead — comes off the wire, exactly like everything
else in the room. A visitor leaning closer sees more; a visitor pressing the warm
button changes what there is to see.

### 3.1 Surface tissue → `_Stress`

This lands cleanly because `FluorescentTissue.shader` already exposes **one dial**:

```
_Stress  0 ──────────── _FluorPoint ──────────── 1
        natural       peak fluorescence       bleached (bare white skeleton)
```

The server's `intensity` is likewise one continuous dial. The rule mirrors the
projection player's ("state selects the regime, intensity sets the position within
it"), so all outputs in the room move as one:

```csharp
float TargetStress(int state, float intensity) => state switch
{
    0 or 1 => intensity * fluorPoint,   // natural ↔ peak fluorescence, fully reversible
    2      => 1f,                       // fully bleached; server pins intensity at 1.0
    3      => intensity,                // 1.0 → 0.0 heal, timed entirely by the server
    _      => 0f,
};
```

**Why each line:**

- **States 0/1** cover the whole reversible arc. Capping at `fluorPoint` means the
  app *cannot* bleach on its own — only the server's latch takes it past peak
  fluorescence. That is the thematic invariant made structural.
- **State 2** is latched on the server. `intensity` is pinned at 1.0 there, so it
  carries no information; hard-code 1.0 and let the slew (below) carry the beat.
- **State 3** is the heal. The server ramps `intensity` 1.0 → 0.0 across
  `recovery_ramp_s` (45 s, config), so passing it straight through means the heal
  **retimes from the server's config file with no rebuild and no reauthoring here**.

**Continuity check at every boundary** — the mapping is continuous everywhere
except the latch, which is meant to rupture:

| Transition | Target before | Target after | Behaviour |
|---|---|---|---|
| 1 → 2 (latch) | ~0.45 (intensity ≈ 0.9 × 0.5) | 1.0 | **Deliberate jump** — the beat. Slew it. |
| 2 → 3 | 1.0 | 1.0 (intensity = 1.0) | seamless |
| 3 → 0 | 0.0 (intensity = 0) | 0.0 | seamless |
| 3 → 2 (re-warm cancels recovery) | mid-heal | 1.0 | slews back up; **must be handled** |
| 2 → 1 (idle reset with hot water) | 1.0 | ~0.45 | slews down; **must be handled** |

Both backward transitions are real and reachable — do not assume the arc only runs
forwards.

### Slew: one rate, doing three jobs

Apply the target through `Mathf.MoveTowards` at a single configurable rate rather
than assigning it directly:

```csharp
_stress = Mathf.MoveTowards(_stress, TargetStress(state, intensity),
                            Time.deltaTime / crossfadeSeconds);
```

With `crossfadeSeconds ≈ 3`, the rate is ~0.33/s. Normal warming moves the target
at ~0.006/s (heat_rate 0.025 °C/s across a 2 °C span, halved by `fluorPoint`), so
the slew **never limits ordinary motion** — it only shapes the latch jump into a
~2 s drain to white, and softens the two backward transitions. One number, no
special cases.

**Cold start into state 2** — the app launches into an already-bleached room:
**snap, don't slew**, on the very first broadcast received. That rupture happened
before this app existed; replaying it would be a lie. (The projection player makes
the identical exception.)

### Emission during recovery

State 3 walks `_Stress` from 1.0 down through the `_FluorPoint` band to 0, which
would make the coral **flash fluorescent on its way out** — wrong: fluorescence is
a stress response, and recovery should heal bleached → healthy *directly*.

The shader already has the fix, and the current controller already uses it: hold
`_EmissionScale = 0` while `state == 3`, then restore it to 1 on entering state 0
(slew it over `crossfadeSeconds` so the glow doesn't pop back).

Tuning decision you own: this makes the entire 45 s heal a non-glowing dull gold.
That reads as "healing, not glowing" and is probably right, but check it on device —
the alternative is easing emission back in over the last ~25 % of the ramp.

### 3.2 Magnifier → polyp footage

**Confirmed design (2026-07-27):** the magnifier shows the polyps **alive when the
reef is alive, fluorescent when it is fluorescent, and dead when it is dead.** It
follows the installation state; it does not have a life of its own.

The footage has not arrived yet. **Build the whole path now against a placeholder
source** (§3.3) so that dropping the real clips in is a config edit, not an
integration.

Same rule as everywhere else — *state selects, intensity interpolates*:

| State | Magnifier shows |
|---|---|
| 0 Natural | `alive` — polyps extended, moving |
| 1 Fluorescent | `alive` → `fluorescent` cross-faded by `intensity` |
| 2 Bleached | `dead` — stillness. **The key emotional beat.** |
| 3 Recovery | `dead` → `alive` cross-faded by `intensity`, **never back through `fluorescent`** |

Three clips, not four. State 3 healing straight from `dead` to `alive` mirrors the
projection player exactly: fluorescence is a *stress response*, so the way out is
not the way in. The weights collapse to one expression:

```csharp
// intensity is pinned at 1.0 in state 2 and ramps 1.0 → 0.0 across state 3,
// so states 2 and 3 are the same blend — no special case for the latch.
(float wAlive, float wFluoro, float wDead) = state switch
{
    0 or 1 => (1f - intensity, intensity, 0f),
    2      => (0f, 0f, 1f),          // pinned, mirroring §3.1's `2 => 1f`
    _      => (1f - intensity, 0f, intensity),
};
```

State 2 pins `dead = 1` instead of deriving it, **deliberately duplicating the special
case in §3.1's tissue mapping**. In service the two forms are identical, because the
server holds `intensity` at 1.0 for the whole of state 2. The pin matters off-contract:
a state-2 message carrying `intensity 0.9` — from `tools/drive_projection`, or from any
future protocol slip — would otherwise bleach the tissue completely while the magnifier
still showed a tenth of a living polyp. The layers are seen together, one inside the
other, so they must not be *able* to disagree about what a latched bleach looks like.

Reuse the slew from §3.1 (`Mathf.MoveTowards` at `1/crossfadeSeconds`) on the
weights, and take the same **snap-don't-slew** exception on the first broadcast
after launch.

**The playhead invariant — copy this, it is hard-won:**

> **Playheads only ever advance. A clip is seeked only while its weight is 0.**

`intensity` drives **opacity, never a playhead**. Scrubbing a clip to
`intensity × duration` would run the polyps *backwards* when the reef cools before
the latch — instantly legible as an error in a way no dissolve ever is. All three
clips loop forever, untouched; only their weights move. See
`projection/README.md` in the control repo for the full argument.

**⚠️ Do not copy the projection player's compositor.** `ProjectionPlayer.cs` blends
in `OnRenderImage`, which **silently never fires under URP** — it is Built-In RP
precisely for that reason, and this project is URP. Blend in the **shader** instead:
pass both `RenderTexture`s plus the weight into the loupe material and `lerp` there.
That is the better fit anyway — the magnifier is a masked reveal on a tracked
surface, not a fullscreen effect. Copy the *weight logic* from `ProjectionPlayer.cs`
(`ChooseTargets` / `EaseWeights` / the pending-fire seek guard); write the
composite yourself.

**Sample the footage in OBJECT space, not screen space** (settled 2026-07-28, after a
first pass got it wrong). Screen-space sampling maps a given corallite to different
footage texels as the device moves, so the polyps slide across the coral — which
violates `USER_STORIES.md` V4 ("polyps stay registered to the same cups from different
angles") and inverts the metaphor: with a real magnifying glass the content is
attached to the *object* and the glass moves over it. Not mesh UVs either — the scan's
UVs were authored for the skeleton texture and smear the footage across the honeycomb.
Object space is rigidly attached to the mesh, so it survives tracking updates for free
and magnifies with the coral. Use **triplanar**: over a ~6 cm loupe on a ~10 cm dome a
single plane visibly stretches on the flanks; a sharpness exponent collapses it back
toward planar if that reads better. Verify with a static grid
(`"magnifier_source": "grid"`) — the grid must stay stuck to the coral as the device
moves. Compute the loupe mask first and `clip()`, so the triplanar fetches only cost
anything inside the window.

**iPad video budget.** Prepare all three `VideoPlayer`s at launch and leave them
decoding — a `Prepare()` mid-transition stalls for hundreds of milliseconds and the
blend visibly hitches. Three simultaneous decodes is the cost of never stalling; if
thermals or frame rate suffer during the endurance soak, drop to two players (A/B)
and swap the idle one while its weight is 0, which the invariant above already makes
safe. The footage lives inside a loupe mask, not fullscreen, so **encode it small** —
HEVC, and no larger than the on-screen mask ever gets. Blend in linear space (the
project is already Linear + HDR).

### 3.3 The placeholder seam — build now, footage later

Mirror the control repo's `temperature.py` idiom: one interface, two
implementations chosen by config, with everything above the seam identical.

```csharp
interface IMagnifierSource { Texture Alive { get; } Texture Fluorescent { get; } Texture Dead { get; } }
```

- `PlaceholderMagnifierSource` — three procedurally generated or solid-colour
  textures with an obvious visual signature per state (and a moving element in
  `alive`/`fluorescent`, a frozen one in `dead`, so the stillness beat is testable).
- `VideoMagnifierSource` — three `VideoPlayer`s rendering to `RenderTexture`s.

Select via config (`"magnifier_source": "placeholder" | "video"`). The placeholder
is **not throwaway scaffolding** — keep it permanently as the regression harness, so
the magnifier stays testable on a laptop with no footage and no device. That is
exactly the role `fake_rig.py` plays for the server, and it is why the control
system can be tested end-to-end with zero hardware.

**Contract for the footage when it arrives** — decide these now so the shoot/render
targets them:

| Property | Requirement |
|---|---|
| Clips | exactly 3: `alive`, `fluorescent`, `dead` |
| Looping | seamless — all three loop indefinitely and are never seeked |
| Framing | identical framing/scale across all three, so a cross-fade reads as *the same polyps changing*, not a cut between three shoots |
| Length | equal across the three, so their wraps coincide rather than landing at three different moments mid-fade |
| `dead` | must read as **stillness**, not a black frame — absence of life, not absence of image |
| Codec | HEVC, sized to the loupe mask, 30 or 60 fps |
| Location | `StreamingAssets/`, filenames in config |

---

## 4. What changes in the existing scripts

### `ProximityRevealController.cs` — split it

It currently implements the **whole narrative locally**: approach → peak
fluorescence → arm bleach → retreat bleaches → reset. Under the installation
architecture that is a direct violation — the server owns the arc.

**Delete** (the local state machine):
- `UpdateAppearanceCycle()`, `ResetCycle()`, and the fields `_hasPeaked`,
  `_resetting`, `_stress`
- `naturalDistance`, `peakDistance`, `bleachFullDistance`, `resetDistance`,
  `resetBlendTime`, `approachCurve`, `bleachCurve`, `fluorPoint`
- the four matching `OnValidate` warnings and their gizmo rings
- the `_Stress` / `_EmissionScale` writes

**Keep, unchanged** (proximity's remaining and only job):
- distance measurement, `measureFromSurface`, `distanceSmoothTime`
- the magnification arc: `magnifyStartDistance`, `magnifyFullDistance`,
  `maxMagnification`, `magnifyCurve`, `magnifyAnchor`, `ResetToBase()`
- the tracking-loss gate — but on target-lost, reset only the **transform**, never
  the appearance. Appearance is the server's, and the coral must still be showing
  the correct state when tracking re-acquires a second later.
- `ProximityTestRig` / `useManualDistance` (still the way to tune the zoom in-editor)

**Revive** (retired during the July pivot, needed again by §3.2): the loupe reveal —
the `_LOUPE_ON` shader keyword and `_LoupeCenter` / `_LoupeRadius`. The controller
currently forces `_LOUPE_ON` **off** so the whole coral shows. With footage back in
the design, proximity drives the reveal region again — but now it reveals *only*
where the polyps show, never *what condition they are in*. Loupe centre/radius must
be in the **Pool transform's local space** (`CLAUDE.md` §8).

**Result:** proximity drives the reveal and the magnification. Every appearance
decision, on both layers, comes off the wire. This is `CLAUDE.md` §9 rule 1,
enforced by construction rather than by discipline.

### New scripts

| Script | Responsibility |
|---|---|
| `CoralOscListener.cs` | Owns receiver + transmitter (shared socket), hello heartbeat, exposes `State`/`Intensity`/`Temp`/`SecondsSinceMessage`/`EverReceived`. No rendering knowledge. |
| `CoralAppearance.cs` | Reads the listener, applies §3.1's mapping + slew to the runtime material instance. No networking knowledge. |
| `CoralMagnifier.cs` | Reads the listener, applies §3.2's weights + slew, pushes the two top-weighted textures and the blend factor to the loupe material. |
| `IMagnifierSource` + `PlaceholderMagnifierSource` + `VideoMagnifierSource` | §3.3's seam. |
| `CoralConfig.cs` | JSON config load (§5). |
| `CoralHud.cs` | On-device diagnostic overlay (§7). |

There is a working reference implementation of the listener in the control repo at
`projection/Scripts/CoralOscListener.cs` — same package, same bindings, same
fail-soft posture. **It does not send hello** (the projection player is a static
subscriber), so the transmitter half of §2 is the part you add.

Keep the two halves separate. The listener must be testable with the renderer
absent, and the appearance must be testable by poking `State`/`Intensity` by hand.

---

## 5. Config, not code

The control system's hardest rule: **no hard-coded IPs, ports, thresholds, or
timings** (`CLAUDE.md`, control repo). At minimum:

```json
{
  "server_host": "192.168.8.10",
  "server_port": 9000,
  "listen_port": 9001,
  "client_id": "",
  "hello_interval_s": 5.0,
  "crossfade_s": 3.0,
  "fluor_point": 0.5,
  "suppress_emission_during_recovery": true,
  "magnifier_source": "placeholder",
  "magnifier_clips": {
    "alive": "polyps-alive.mp4",
    "fluorescent": "polyps-fluorescent.mp4",
    "dead": "polyps-dead.mp4"
  },
  "hud_enabled": true
}
```

**iOS has no "beside the .app" to drop a file into**, so the projection player's
pattern doesn't transfer. Use:

1. `Application.persistentDataPath/coral-ar.json` if present (editable over USB via
   the Files app — set `UIFileSharingEnabled` and
   `LSSupportsOpeningDocumentsInPlace` in Info.plist), else
2. `StreamingAssets/coral-ar.json` shipped in the build as the default, else
3. built-in defaults.

Fail soft at every step: a missing or malformed config logs loudly and falls back
to defaults. An exhibition device must always boot. Log which path was used.

`client_id`: leave `""` and generate a stable per-device id on first run
(`SystemInfo.deviceName` + a GUID persisted to `PlayerPrefs`), so three devices
never collide in the registry. Show it in the HUD — it's what appears in the
server's log line `CLIENT registered: <id>`.

**Also make the server host settable at runtime** (a field in the HUD is enough).
On exhibition morning the Mac's address is the one thing most likely to differ, and
you will not want to rebuild an iOS app to fix it.

---

## 6. iOS specifics that will cost you a day each

1. **Local Network permission (iOS 14+).** Sending or receiving UDP on the LAN
   requires `NSLocalNetworkUsageDescription` in Info.plist. Without it the socket
   opens, the sends "succeed", and **nothing ever arrives**. Unity does not expose
   this in Player Settings — add it from a `PostProcessBuild` hook. This project
   already has `Assets/CoralPolyps/Editor/BuildScript.cs`; put it there.

   ```xml
   <key>NSLocalNetworkUsageDescription</key>
   <string>Coral AR receives the installation's live coral state over the local network.</string>
   ```

   The prompt appears on first local-network access. If the visitor (or you) taps
   Deny, the only symptom is permanent state 0 — which is why the HUD in §7 is not
   optional. Denial is per-install; reinstalling re-prompts.

2. **Sleep/resume.** iOS tears the socket down when the app backgrounds. On
   `OnApplicationPause(false)`: reconnect the receiver **and send a hello
   immediately** rather than waiting up to 5 s for the next heartbeat. This is
   exactly the Phase 7 acceptance test "a device that sleeps and wakes resyncs
   itself".

3. **Never sleep:** `Screen.sleepTimeout = SleepTimeout.NeverSleep;` plus Guided
   Access for pinning. Devices are tethered or rotated on power banks.

4. **Router:** phones on the 5 GHz SSID, the Mac wired, plugs and sensor on 2.4 GHz
   (separate SSIDs — ESP devices fail to join combined-band SSIDs). **Client/AP
   isolation must be OFF** on the GL.iNet Opal, or the server's unicast to the phone
   is dropped at the access point and you will debug the app for hours over a router
   checkbox.

---

## 7. The diagnostic HUD (build it early, not last)

A toggleable overlay showing:

```
id=ipad-A  server=192.168.8.10:9000  listening=9001
state=2 Bleached   intensity=1.00   T=27.9C
stress=1.00   magnifier=dead 1.00 / alive 0.00   src=placeholder
last broadcast 0.2s ago   hello sent 1.4s ago   rx=1284
```

Showing the *applied* values (`stress`, magnifier weights) next to the *received*
ones is what separates a network fault from a mapping fault at a glance.

Every failure mode in this system is silent by design — a lost datagram, an
unregistered client, a denied permission, a wrong subnet and a stopped server all
look identical from the render side (a healthy coral that never changes). The
"seconds since last broadcast" line distinguishes all of them in one glance, and it
is the only practical triage tool once the iPad is on a plinth in a gallery.

`tools/fake_client.py` in the control repo prints exactly this status line — copy
its shape.

---

## 8. Testing without the physical installation

All of this runs on a laptop with zero installation hardware.

**Full system, driven from the keyboard** (control repo, three terminals):

```bash
python src/server.py          # the real server, simulated water
python tools/fake_rig.py      # keyboard: warm/cool, same path as the arcade buttons
python tools/fake_client.py   # a known-good reference client — compare against it
```

Point the iPad's config at the Mac's LAN IP (**not** `127.0.0.1`) and it joins as a
fourth subscriber. `fake_client.py` running alongside is the differential diagnosis:
if it updates and the iPad doesn't, the fault is in the app, not the server.

**Hold one state still for look-dev** — the single most useful tool for this app:

```bash
python tools/drive_projection.py --host <ipad-ip> --port 9001 --state 2
```

It unicasts the same `/coral/*` fan-out straight at the device, **bypassing the
server and the hello registry entirely**. That matters because tuning the bleached
look against the real print takes minutes of steady state, and the real server is
always drifting toward a target. Other modes: `--state 0|1|2|3`, `--arc` for the
full sequence at true pace, `--arc --speed 6` compressed.

Because it bypasses registration, it also **isolates the §2 socket-sharing bug**:
if the coral responds to `drive_projection.py` but not to the real server, your
hello is going out of the wrong port. That single test saves the worst debugging
session available here.

**Provoke the two backward transitions** (they take minutes of real water otherwise):
`--state 3` then `--state 2` for a cancelled recovery, and `--state 2` then
`--state 1` for the idle reset with hot water.

### Phase 7 acceptance test (control repo `docs/BUILD_ORDER.md`)

> Three devices around the artefact, all synced; magnifier reveals life at states
> 0–1 and absence at state 2; a device that sleeps and wakes resyncs itself.

Add to that, from `CLAUDE.md` §9: launch the app **before** the server and confirm
it sits at state 0 and converges silently when the server appears; kill the server
mid-arc and confirm the coral holds its last look rather than resetting or erroring.

---

## 9. Timings and thresholds you'll want on hand

From the server's `config.yaml` — **reference only, never duplicate these into
Unity code**. The app receives derived values; it must not know these numbers.

| Value | Setting |
|---|---|
| Broadcast rate | 5 Hz (200 ms) — also the heartbeat; there is no keep-alive |
| Hello interval / prune | 5 s / 15 s |
| `intensity` | `clamp((T − 26.0) / 2.0, 0, 1)` |
| State 1 engages | T > 26.2 °C (falls back to 0 only at ≤ 26.0 — hysteresis) |
| Bleach latch | T ≥ 27.8 °C sustained 10 s **while warming** |
| Recovery lag | 30 s visibly bleached after cool is pressed |
| Recovery ramp | 45 s heal (`intensity` 1.0 → 0.0) |
| Idle reset | 180 s with no button press |

Latch `intensity` at the moment of bleaching is ≈ **0.9**, not 1.0 — the mapping in
§3 must not assume the jump starts from a full-intensity 1.0.

---

## 10. Open items

**Magnifier — RESOLVED 2026-07-27.** The magnifier keeps the **polyp footage**
described in `ARCHITECTURE.md` §6.4 and `CLAUDE.md` §9, and that footage **follows
the installation state**: alive when the reef is alive, fluorescent when it is
fluorescent, dead when it is dead. See §3.2. The July pivot to surface tissue was
about *how the coral itself is rendered* (cerioid → glowing honeycomb, not discrete
polyp meshes); it did not replace the magnifier. The two layers coexist, and both
take their condition off the wire.

The control repo's documents are therefore correct as written and need no
amendment. `PROGRESS.md` in this repo, however, still describes proximity as the
thing that bleaches — update its snapshot once §4's split lands.

Still to settle: **whether the surface tissue and the magnifier footage should be
visibly the same organism at the same moment.** They will be seen together, one
inside the other. If the tissue is drained to white while the loupe still shows
green polyps for two seconds because their slews differ, the illusion breaks. The
simple fix is one shared `crossfade_s` and one shared slew — which is what §3
specifies. Confirm it holds on device.

**Repository cross-references — verified correct, no action needed.** This project
is `github.com/hebbooz/thesis-ar-app`; the control repo is
`github.com/hebbooz/thesis-installation-control`. Both repos' references to each
other match their actual remotes.

**Before §4's split:** removing serialized fields from `ProximityRevealController`
drops the tuned inspector values held on the scene's component — the *asset*
change is versioned, the lost values are not recoverable from it. Note the current
values from `PROGRESS.md` or the inspector before deleting anything, and commit the
scene beforehand.

---

## 11. Order of work

1. `CoralConfig` + `CoralOscListener` + `CoralHud`. **Nothing rendering.** Verify
   against `python src/server.py` + `fake_rig.py` that the HUD tracks
   `fake_client.py` value-for-value, including hello registration appearing in the
   server log, and pruning when the app is backgrounded.
2. iOS Local Network permission and the pause/resume reconnect — on device, before
   any appearance work. These are the two failures that masquerade as render bugs.
3. `CoralAppearance` with §3.1's mapping and slew, driven by `drive_projection.py`
   holding each state still.
4. Strip the local arc out of `ProximityRevealController`, leaving magnification.
5. `CoralMagnifier` + the §3.3 seam on `PlaceholderMagnifierSource`. Revive the
   loupe keyword. Verify all four states and both backward transitions with the
   placeholder — **the entire magnifier is finishable and testable before a single
   frame of footage exists**, which is the point of the seam.
6. Run the full arc end-to-end from the keyboard rig, then the Phase 7 acceptance
   test with three devices.
7. When the footage lands: encode to the §3.2 contract, flip `magnifier_source` to
   `"video"`, retune nothing. If that flip requires a code change, the seam was
   built wrong.

Step 1 before step 3 is not optional. A listener bug and a mapping bug produce the
same symptom — a coral stuck on the wrong look — and separating them after the fact
costs far more than the HUD does to build. Likewise step 5 before the footage
arrives: waiting on assets to start the integration is how a pending delivery turns
into a blocked build.
