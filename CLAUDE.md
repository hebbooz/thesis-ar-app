# CLAUDE.md — Coral Polyp AR: Project Context

> This file orients Claude Code (and any human) on what this project is,
> what we're building, and how the pieces fit. Read this first.

---

> **📍 CURRENT STATUS (2026-07) — read `docs/PROGRESS.md` before acting.**
> The project is through Phase 3 and took a **design pivot**: fluorescence is now
> rendered as **living tissue on the coral surface** (`Runtime/FluorescentTissue.shader`),
> **not** as discrete polyps — because _Goniastrea_ is a **cerioid** (shared-wall
> honeycomb) coral, so the accurate fluorescent view is a glowing honeycomb.
> As a result, `PolypPool.cs` + `FluorescentPolyp.shader` (the Stage 2/3 files in
> §5 below) are **shelved** (kept for a possible hybrid), and **§5–6 below describe
> the original plan and are partially superseded.** The coral is **already
> 3D-printed** at life-size. **Engine: Unity 6000.5.3f1.**
> **NEXT: installation integration — read `docs/CONTROL_INTEGRATION.md` FIRST.**
> This app is now a **passive subscriber** to the orchestration server (§9). The
> server owns the healthy→bleached arc; **proximity drives only the reveal and the
> magnification.** Wherever an older document says proximity bleaches the coral —
> §1 and §6 below, `docs/PROGRESS.md`, `docs/BUILD_PLAN.md` Phase 6,
> `docs/USER_STORIES.md` V3 — `docs/CONTROL_INTEGRATION.md` supersedes it.
> `docs/PROGRESS.md` remains the source of truth for **build status**, but not for
> what drives appearance.
>
> **⚠️ Before instructing ANY Unity setup, read PROGRESS.md's _"Current concrete
> state"_ section.** The working scene, the coral mesh, the **`CoralTissue`
> material** (with its textures + import settings), **HDR + a Bloom volume**, the
> **bake**, the **Phase-4 shader/controller code**, and a Vuforia **`coral-rendering`
> Model Target** ALL already exist and are wired. Re-instructing that setup (new
> material, re-importing textures, re-enabling bloom) is the main way to confuse the
> operator — check what's there first.

## 1. What the whole piece is

A **mobile AR art/research installation** about coral bleaching in distant reefs.

The physical setup:
- A **life-size 3D-printed coral** sits on a plinth as a physical anchor.
- A viewer holds an **iPad** up to it.
- As they **lean the iPad closer**, the app reveals tiny glowing **polyps**
  (the living animals that build coral) covering the surface.
- Whether those polyps appear **alive, fluorescing, or bleached white** is set by
  the wider installation (§9) — not by the phone. A visitor at the water bath
  presses a button, the water warms, and every screen in the room changes at once.

The phone acts like a **magnifying glass into a hidden, dying world.**
**Proximity IS the zoom** — distance from coral drives how deep into the
micro-scale content the viewer travels. It is *only* the zoom: proximity changes
how much you see, never the condition of what you are seeing. The healthy→dying
arc belongs to the orchestration server (§9, `docs/CONTROL_INTEGRATION.md` §3).

This is a **design study**, so **honest representation matters**: it's an
explicit *stylised impression*, not a fake photograph of science. Colours,
placement, and behaviour should be defensibly grounded in the real animal
even while stylised.

---

## 2. Tech stack

| Layer | Choice |
|---|---|
| Engine | Unity (URP — Universal Render Pipeline) |
| AR | AR Foundation + ARKit (iOS), targeting **iPad** |
| Tracking | **Vuforia Model Target** built from the coral's print mesh |
| Fallback tracking | Image marker at the coral's base |
| Reveal mechanism | A "loupe" region driven by a smoothed proximity value |

The tracked object is the **3D print**. Everything virtual is parented to the
Vuforia Model Target so it rides the tracked coral.

---

## 3. The specific coral (decided — do not re-litigate)

- **Anchor / print / tracking mesh:** the **Smithsonian _Goniastrea_
  (Astraea/Fissicella favistella) scan** — CC0 public domain, a dry skeleton,
  a massive/boulder coral with a field of cup-shaped corallites.
- **CRITICAL RULE:** the digital placement map and the physical print must be
  the **SAME coral**. A polyp placed in a cup that isn't physically there is
  the single worst failure at close ("loupe") range. Map == anchor, always.
- A separate **live _Diploastrea_ scan** (CC-BY, credit "QQman") exists ONLY as
  **colour/appearance reference** for living tissue. It is NOT the anchor and
  NOT a source of polyp models. Different species, different cups — it will not
  register onto the Goniastrea print.

**Colour caveat:** the Diploastrea scan shows *daylight reflective pigment*.
Real coral **fluorescence** (glow under blue/UV) is a different, usually more
saturated palette. Fluorescent colours must come from actual fluorescence
imagery of Goniastrea (or a close relative), NOT from the daylight scan.

---

## 4. The core technical problem

Generic polyp geometry must sit believably on a **specific, irregular coral
surface**. Naive approaches (warping/shrink-wrapping one polyp mesh over the
whole coral) are fragile.

**The insight that makes this tractable:** on a massive coral, each little
"star" on the surface is a **corallite** — a cup where exactly one polyp lives.
The scan's geometry therefore *already encodes where every polyp goes*. So this
is not a "fit polyps to a surface" problem; it's a **"detect the cups, drop one
polyp in each"** problem. The coral tells us the placement.

---

## 5. The pipeline (3 files = 3 stages)

Think of it as an assembly line: coral in one end, "correctly-placed glowing
polyps that can bleach" out the other.

```
  [Coral mesh]
       |
       v
  (1) BAKER  ──►  a saved LIST of polyp spots       (runs in editor, once)
       |          (position + facing + size per cup)
       v
  (2) POOL   ──►  places polyps into those spots     (runs on iPad, live)
       |          live, only where the loupe looks
       v
  (3) SHADER ──►  paints each polyp; one _Stress      (runs on iPad, per polyp)
                  dial drives healthy → bleached
```

- **Where** polyps go → the Baker.
- **Putting** them there → the Pool.
- **How they look / bleach** → the Shader.

The single wire between Pool and Shader: when the Pool places a polyp, it also
sets that polyp's `_Stress` value. So one system handles both placement and the
sickness state.

See `docs/FILE_DOCS.md` for per-file detail.

---

## 6. What exists now vs. what's next

**Built:**
- `CoralliteBaker.cs` — cup detection + bake (stage 1)
- `PolypScatterMap.cs` — the saved list format (handoff between stage 1 and 2)
- `PolypPool.cs` — runtime placement (stage 2)
- `FluorescentPolyp.shader` — appearance + bleach arc (stage 3)
- `ProximityRevealController.cs` — proximity → reveal + magnification (stage 4).
  Reads iPad-to-coral distance, smooths it, and drives the loupe
  (`PolypPool.SetLoupe`) plus the magnification arc. This is what makes the
  "magnifying glass" work. **Its second arc — proximity → `_Stress` → bleaching —
  is superseded** and is removed during installation integration: `_Stress` now
  comes off the broadcast (`docs/CONTROL_INTEGRATION.md` §3.1 and §4).

**Not yet built:**
- All core code stages are built. Remaining work is assets (below).

**Still needed as assets (not code):**
- The polyp **prefab** (geometry + material using the shader).
- The baked **PolypScatterMap asset** (run the Baker on the real mesh).
- The **occlusion depth mesh** (invisible copy of the coral that hides polyps
  behind real branches — noted in the design but not yet built).

See `docs/BUILD_PLAN.md` for the dependency-ordered, phase-by-phase plan to
finish these and stand the installation up, and `docs/USER_STORIES.md` for the
visitor/operator/developer/researcher stories with checkable acceptance criteria.

---

## 7. Known risks (keep these in view)

1. **Conform quality** — make-or-break. Polyps must sit *in* the cups. Verify
   via the Baker's gizmos before trusting anything downstream.
2. **Stylised, not scientific** — honest framing; real imagery could layer in
   later. Don't oversell realism.
3. **Fluorescence colour** — only convinces if colours track real wavelengths.
   Source from fluorescence imagery, not the daylight scan.
4. **Tracking stability at closest range** — the loupe pushes tracking to its
   limit; the plating (flat-ish boulder) shape helps here.
5. **All-day thermals** — the Pool's small active count exists to protect this.

---

## 8. Coordinate / space conventions

- The Baker stores all positions and normals in the **coral mesh's LOCAL
  space** — so the map stays valid wherever Vuforia places the coral in the
  world.
- The Pool is **parented to the Model Target**, so it works in that same local
  space and inherits tracking automatically.
- The loupe center/radius passed to `PolypPool.SetLoupe()` must be in the
  Pool transform's **local** space.

---


## 9. Part of a larger system

This Unity AR application is **one component of a multi-part installation**, not a standalone app. It is a **passive subscriber** in a centrally orchestrated system.

**Control repository:** `https://github.com/hebbooz/thesis-installation-control`
**Protocol contract:** see `docs/PROTOCOL.md` in this repo (mirrored from the control repository — that repo is the source of truth; update this copy if the contract changes).

### The system in one sentence

A Python orchestration server reads the temperature of a water bath containing a 3D-printed coral, derives a system `state` (0–3) and a continuous `intensity` (0.0–1.0), and broadcasts both over OSC/UDP ~5×/second. Every output — this AR app, a projected reef, a soundscape, a lamp — reacts to that single broadcast so the whole room behaves as one organism.

### This app's role

Visitors hold a device up to the physical coral artefact. The app overlays virtual living tissue on the print, and acts as a **magnifier**: moving closer reveals microscopic polyp footage, giving life to the coral. Its appearance is dictated entirely by the broadcast state — a visitor's actions at the buttons, not at the phone, change the coral's condition.

### Rules this app must follow

1. **Never derive state from anything local.** The server is the single source of truth. Do not infer state from device distance, elapsed time, or any other local signal. Distance controls *only* the magnifier reveal.
2. **Passive listener.** The only message this app ever sends is `/client/hello`. It has no control authority over the installation.
3. **Boot to state 0.** Assume Natural / intensity 0.0 on launch and converge silently on the first broadcast received. The server may not be running yet — that must not be an error condition.
4. **Idempotent application.** The same values arrive repeatedly by design (5×/second). Applying them must be harmless.
5. **Startup-order independent.** This app may launch before, during, or after the server. It must recover from the server restarting, the network dropping, or the device sleeping and waking — always within one broadcast interval, with no user action.
6. **Interpolate on `intensity`, switch on `state`.** Material vividness, colour and blend factors should follow `intensity` continuously so transitions read as smooth fades. Use `state` only for discrete changes (which material set, whether polyp footage is alive or dead).

### State meanings for this app

| State | Name | AR appearance | Magnifier |
|---|---|---|---|
| 0 | Natural | Healthy orange coral material | Reveals lively polyp footage |
| 1 | Fluorescent | Material lerps toward vivid fluorescence as `intensity` rises | Polyps still alive |
| 2 | Bleached | Bleached white material | **Footage fades to ghostly stillness or nothing** — finding the life gone is the installation's key emotional beat |
| 3 | Recovery | Gradually heals back toward healthy | Life gradually returns |

State 2 **latches** on the server — it will not un-bleach until the server says so. Do not implement any local recovery behaviour.

### Implementation notes

- OSC via the **extOSC** Unity package (free).
- Listen on the port defined in the protocol doc (default UDP 9001).
- Send `/client/hello` with a unique device id every 5 seconds. The server registers the sender's IP automatically; devices that go quiet for 15 s are pruned and re-register on return.
- Up to **three devices** run simultaneously. Each is independent — no coordination between them.
- Vuforia model target tracking is unchanged from the existing prototype; only what *drives* the material changes.

### Exhibition configuration

- Never-sleep enabled; app pinned (Guided Access on iOS, screen pinning on Android).
- Devices are tethered for charging or rotated on power banks.
- A separate constant spotlight lights the coral artefact so tracking survives the room light dimming in state 2. This is not controlled by the app.

### What NOT to do

- Don't add local state logic, timers, or "smart" fallbacks that guess at state.
- Don't send anything to the server beyond `hello`.
- Don't treat a missing broadcast as an error — hold the last value and wait.
- Don't hard-code IP addresses; keep the server address configurable.