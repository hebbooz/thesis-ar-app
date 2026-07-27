# FILE_DOCS.md — Per-file documentation

Detailed reference for each source file. For the big picture, read `CLAUDE.md`
first; for current status and the roadmap, read `PROGRESS.md`. Files are
documented in pipeline order.

> **⚠️ Design pivot (2026-07):** the fluorescence is now rendered as **living
> tissue on the coral surface** (`Runtime/FluorescentTissue.shader`), not as
> discrete polyp instances — because _Goniastrea_ is a cerioid (shared-wall
> honeycomb) coral. `PolypPool.cs`, the polyp prefab, and `FluorescentPolyp.shader`
> below are **shelved** (kept for a possible hybrid), not on the active path.
> See `PROGRESS.md` for the full rationale.

---

## `Editor/CoralliteBaker.cs`  — Stage 1: WHERE polyps go

**Type:** Unity Editor tool (`EditorWindow`). Runs in the editor ONLY, never on
device. Wrapped in `#if UNITY_EDITOR` so it's excluded from builds.

**Purpose:** Look at the coral mesh, find every corallite cup, and write out a
vetted list of "put a polyp here" instructions.

**How to use:**
1. `Window > CoralPolyps > Corallite Baker`.
2. Drag the coral **mesh** into the *Coral Mesh* field (the mesh you print/track).
3. Tune sliders (start with defaults), click **Detect & Bake**.
4. It writes a `PolypScatterMap` asset to the *Output Asset* path.
5. **Verify:** select the object holding `PolypPool` and look at the gizmos
   (spheres in cups + normal rays). If spheres sit centered in cups, detection
   worked. This visual check is your make-or-break gate.

**How detection works (plain version):**
A corallite is a *concave pit*. For each vertex the tool measures how "sunken"
it is relative to its neighbours — cup floors score high, ridges score negative.
It picks the local high points of concavity as cup centers, removes duplicates
that are too close together (so one cup = one polyp, not a cluster), and rejects
cups on the underside and near the base.

**Key parameters:**
| Slider | What it does | Tune when… |
|---|---|---|
| `Concavity Threshold` | How deep a pit must be to count | too few cups → lower; noisy → raise |
| `Min Spacing` | Minimum gap between two polyps | polyps clustering in one cup → raise |
| `Underside Reject` | Rejects cups facing downward | polyps on the base/underside |
| `Base Cutoff` | Rejects cups in the bottom slice | polyps on the sawn base |
| `Global Scale` | Overall polyp size (slight >1 = honest exaggeration for legibility) | polyps too small/large |
| `Scale Jitter` | Per-polyp random size variation | field looks too uniform |

**Output:** a `PolypScatterMap` asset. Nothing else downstream re-runs detection.

**Tuning order if results are bad:** `Concavity Threshold` first, then
`Min Spacing`, then the mask sliders. Detection quality depends on a clean,
high-density mesh — if cups are too shallow to detect, the fix is a better mesh,
not more sliders.

---

## `Runtime/PolypScatterMap.cs`  — the handoff (list format)

**Type:** `ScriptableObject` (a Unity data asset). Not logic — just the saved
list that the Baker writes and the Pool reads. This is the seam between "editor
prep" and "runtime."

**Contents:** a list of `Corallite` entries. Each entry:
| Field | Meaning |
|---|---|
| `localPosition` | Where the polyp sits (coral local space) |
| `localNormal` | Which way it faces (so it grows outward) |
| `scale` | How big (from cup size / neighbour spacing) |
| `confidence` | Detection certainty 0–1 (runtime can mask low-confidence) |
| `randomSeed` | Per-polyp 0–1 for varying sway/rotation phase |

Also stores `sourceMeshName` (a safety check you baked from the right coral) and
`sourceBounds`.

**Why a ScriptableObject:** it's a normal Unity asset you can inspect, version,
and ship. Detection runs once, the answer lives here, the device just reads it.

---

## `Runtime/PolypPool.cs`  — Stage 2: PUTTING polyps there

**Type:** `MonoBehaviour`. Runs on device, every frame.

**Purpose:** Read the baked list and actually place polyp instances into the
cups the loupe is currently looking at.

**Setup:**
1. Put this on a GameObject that is a **child of the Vuforia Model Target**
   (so it inherits tracking and shares the coral's local space).
2. Assign the baked `PolypScatterMap` to `map`.
3. Assign your polyp **prefab** to `polypPrefab` (geometry + the fluorescence
   material).
4. Set `poolSize` (default 300 — keep small for thermals).

**How it works ("pool" explained):**
It pre-makes `poolSize` polyp objects once, then reuses them. Each frame it asks
"which baked cups are inside the loupe right now?" and moves pooled polyps into
those cups (position, facing via the normal, scale, random yaw). Polyps outside
the loupe are switched off. Like re-placing a fixed set of chess pieces instead
of owning thousands. This is the thermal-safety mechanism.

**Key public members:**
| Member | Purpose |
|---|---|
| `SetLoupe(localCenter, localRadius)` | Called each frame by the proximity controller. Center/radius in THIS transform's local space. |
| `CloseLoupe()` | Hide everything (loupe shut). |
| `stress` (0–1) | Global bleaching state; pushed to each polyp's shader `_Stress`. |
| `minConfidence` | Extra runtime mask — hide polyps below this detection confidence. |

**The one wire to the shader:** in `LateUpdate`, for each active polyp it sets
`_Stress` on the renderer via a `MaterialPropertyBlock`. That's how placement
and sickness state stay in one place.

**Editor gizmos:** `OnDrawGizmosSelected` draws every baked cup (sphere colored
by confidence + a normal ray) so you can verify the Baker's output visually.

**Not yet wired:** something must CALL `SetLoupe()` / `stress` each frame. That's
the proximity reveal controller (next piece to build).

---

## `Runtime/FluorescentPolyp.shader`  — Stage 3: HOW polyps look & bleach

**Type:** URP HLSL shader. Runs per-polyp on the GPU.

**Purpose:** Make each polyp glow *from within* (fluorescence, not reflected
light), and drive the whole healthy → bleached story from one dial.

**The one dial:** `_Stress`, 0 → 1.
| `_Stress` | State | Look |
|---|---|---|
| 0.0–0.4 | Healthy | steady cyan-green, full emission |
| 0.4–0.7 | Colourful bleaching | emission surges + saturates, hue shifts warm, slow pulse |
| 0.7–1.0 | Bleached | emission desaturates, drains toward white, dims |

(The 0.4–0.7 "colourful bleaching" surge mirrors a real photoprotective-pigment
response before bleaching — a defensible detail for the study.)

**How to use:**
1. Create a Material from this shader.
2. Put it on the polyp prefab.
3. Drag `_Stress` in the inspector to scrub the arc; at runtime the Pool drives it.

**Key properties:**
| Property | Purpose |
|---|---|
| `_HealthyColor` (HDR) | Healthy emission — **set from real fluorescence imagery of the species** |
| `_StressColor` (HDR) | Colourful-bleach emission (warm) |
| `_EmissionStrength` | Base glow; **lift when viewed through the dimmer AR camera feed** |
| `_SurgeBoost` | How bright the mid-arc surge gets |
| `_PulseSpeed` / `_PulseDepth` | Pulsing during stress |
| `_FresnelPower` | Rim glow so it reads as volumetric, not flat |

**Important notes:**
- `_Stress` lives in the shader's **instancing buffer**, matching the Pool's
  per-instance `MaterialPropertyBlock.SetFloat("_Stress", ...)`. This lets
  polyps bleach at slightly different rates (looks natural). To make stress
  global instead, move `_Stress` into the `CBUFFER` and set it on the material.
- Emission colours are `[HDR]` — push above 1.0 for real bloom (needs a bloom
  post-process volume). Bloom is what sells fluorescence on screen.
- **Default colours are placeholders picked by convention, NOT from species
  imagery.** Replacing them from real Goniastrea fluorescence images is a
  required step, not optional polish (see risk #3 in CLAUDE.md).

---

## `Runtime/FluorescentTissue.shader`  — ACTIVE Stage 3: the fluorescent honeycomb

**Type:** URP HLSL shader. Runs on the **coral mesh** (not per-polyp). This is the
current, cerioid-accurate replacement for `FluorescentPolyp.shader`.

**Purpose:** render the living fluorescent tissue stretched over the shared-wall
honeycomb, and drive the whole healthy→bleached story from one `_Stress` dial.

**How it reads the coral's structure:**
- **Occlusion map** (`_AOMap`, the scan's AO): raised rims/ridges read light,
  sunken floors read dark. Drives the **ridge/floor two-tone colour split**
  (`_WallColor` vs `_FloorColor`), the **wall glow emphasis**, and the **depth
  shading** that shadows recessed floors so the cups read in 3D.
- **Normal map** (`_NormalMap`, imported as *Default*, sRGB off, unpacked
  manually): adds the fine, razor-sharp **septal detail** via tangent-space
  normal perturbation + directional relief. Needs tangents on the mesh.
- **Diffuse** (`_MainTex`): the bare skeleton shown as fluorescence drains.

**The `_Stress` arc (same shape as FluorescentPolyp, biologically grounded):**
| `_Stress` | Look |
|---|---|
| 0.0–0.4 | healthy two-tone honeycomb (green ridges / cyan floors) |
| 0.4–0.7 | **colourful bleach** — neon surge (`_StressColor`), saturates, pulses |
| 0.7–1.0 | fluorescence drains → bare (white) skeleton shows |

**Key knobs:** `_WallColor`/`_FloorColor` (HDR, set from real imagery),
`_ColorSplit`, `_AOContrast`, `_WallGlow`/`_FloorGlow`, `_Depth`,
`_ReliefStrength`, `_NormalStrength`, `_SurgeBoost`.

**Loupe reveal + AR transparency (Phase 4, built):**
- `_LoupeOn` (`[Toggle(_LOUPE_ON)]`): off = look-dev (fully revealed, opaque);
  on = AR reveal. The controller enables it on a runtime instance.
- `_LoupeCenter` (world), `_LoupeRadius`, `_LoupeSoftness`: a soft **world-space
  sphere**. `reveal = 1 - smoothstep(R - soft, R, distance(fragWS, centre))`.
  `_LoupeRadius = 0` ⇒ fully closed. Driven per-frame by the controller.
- **Alpha out** = `reveal * coverage * (1 - toWhite) * _TissueOpacity` — so *outside
  the loupe* AND *fully bleached* both go transparent → the real printed skeleton
  shows through. (`coverage` is a near-opaque tissue film, ridges slightly more
  opaque than recessed floors.)
- **Blend state is material-driven** (`_SrcBlend`/`_DstBlend`/`_ZWrite`, hidden) so
  ONE shader serves both: look-dev keeps the material's saved `One`/`Zero`/`On`
  (opaque, alpha ignored — unchanged), while the controller flips a runtime
  instance to `SrcAlpha`/`OneMinusSrcAlpha`/`Off`. SubShader is in the Transparent
  queue; `multi_compile_local _ _LOUPE_ON` keeps both variants so the runtime
  keyword toggle can't be stripped.

---

## `Editor/BuildScript.cs`  — headless build & fix harness

**Type:** Editor utility. CLI entry points (invoke via `-executeMethod`):
`BuildiOS` (build the Xcode project), plus `ReduceShaderVariants` and
`AssignRenderPipeline` — the Phase 0 fixes for the shader-variant explosion. The
URP asset must stay assigned across Graphics + all Quality levels or the iOS
build blows up on variants again.

---

## `Runtime/ProximityRevealController.cs`  — Stage 4: PROXIMITY drives everything

**Type:** `MonoBehaviour`. Runs on device, every frame. This is the piece that
makes the "magnifying glass" work end-to-end.

> **⚠️ TWO WARNINGS ON THIS SECTION.**
> **1. Superseded (2026-07-27).** Proximity no longer drives bleaching — the
> orchestration server does. The "bleach arc" below and its `_Stress` writes are
> removed during installation integration; see `docs/CONTROL_INTEGRATION.md` §4 for
> exactly what goes and what stays.
> **2. Stale field names.** The parameters below predate the 2026-07-14 redesign
> and **do not match the current source.** `ProximityRevealController.cs` today has
> `naturalDistance` / `peakDistance` / `bleachFullDistance` / `resetDistance` /
> `fluorPoint` / `magnify*` — not `revealStartDistance` / `loupeRadius*`. Read the
> source, not this table, until it is rewritten after the integration lands.

**Purpose:** Convert iPad-to-coral distance into the reveal + bleach behaviour by
driving the coral **`FluorescentTissue` material** — `_LoupeCenter` / `_LoupeRadius`
/ `_Stress`. (Retargeted in the Phase 4 pivot; it used to drive the shelved
`PolypPool`.)

**How it works (per frame):**
1. **Where is the viewer looking?** Raycasts from screen centre onto the coral
   collider (ideally the occlusion depth mesh). Falls back to the collider's
   nearest point, then the renderer origin if no collider is assigned. The hit
   point is the loupe centre; the camera→hit length is the raw distance.
2. **Smooth** that distance with `SmoothDamp` (`distanceSmoothTime`) so AR
   tracking jitter doesn't make the loupe twitch (risk #4).
3. **Two independent arcs** off the one smoothed distance:
   - **Reveal arc** (`revealStartDistance` → `revealFullDistance`): opens the
     loupe and grows its radius (`loupeRadiusFar` → `loupeRadiusNear`) as you
     lean in, writing `_LoupeCenter` (world) + `_LoupeRadius`. Below the reveal
     threshold it closes the loupe (sets `_LoupeRadius = 0`).
   - **Bleach arc** (`bleachStartDistance` → `bleachFullDistance`): kept *nearer*
     than reveal so the tissue appears healthy first, then sickens as you lean
     closer. Drives the material's `_Stress`.
   Each arc is shaped by its own `AnimationCurve`.
4. **Tracking gate:** if the anchor subtree goes inactive (tracking lost) it
   closes the loupe (`closeOnTrackingLoss`).

**Material handling:** on `Start` it takes a per-renderer `.material` instance
(auto-freed; never dirties the saved look-dev asset). With `configureMaterialForAR`
on it flips that instance into AR mode — enables the `_LOUPE_ON` keyword, sets
`SrcAlpha`/`OneMinusSrcAlpha`/`ZWrite Off`, and moves it to the Transparent queue —
then drives the loupe/stress floats on it each frame.

**Key parameters:**
| Param | Purpose |
|---|---|
| `coralRenderer` | The coral mesh renderer using the FluorescentTissue material (required) |
| `revealStartDistance` / `revealFullDistance` | Metres where the tissue begins/finishes appearing |
| `bleachStartDistance` / `bleachFullDistance` | Metres where bleaching begins/completes (set nearer than reveal) |
| `loupeRadiusFar` / `loupeRadiusNear` | Loupe radius in WORLD metres, lerped by reveal amount |
| `loupeEdgeSoftness` | Feathered loupe edge (world metres) |
| `revealCurve` / `bleachCurve` | Shape each arc |
| `distanceSmoothTime` | Jitter smoothing; higher = calmer but laggier |
| `coralCollider` / `coralRayMask` | Optional; centres the loupe on where the viewer looks |
| `configureMaterialForAR` | Auto-switch the runtime material to transparent + loupe on Start |

**Coordinate note:** the loupe test runs in **world space** in the shader, so both
distances and the loupe radius are world metres (the coral is life-size, so this is
just real-world centimetres). No local-space conversion needed.

**Editor gizmos:** draws the four distance thresholds as rings out along the
camera's forward, plus the current loupe centre — so you can dial in the beats.

**Threshold guard (`OnValidate`, editor-only):** warns in the console if the
distances get crossed — each arc's *full* must be nearer than its *start*, and
bleach must start nearer than reveal (so it reads healthy before it sickens).
Sensible ordering: `bleachFull < revealFull < bleachStart < revealStart`.

**Setup:** assign `coralRenderer`; leave `cam` empty to use `Camera.main`;
optionally assign `coralCollider`. It drives the material by reference, so it does
NOT need to be childed to the Model Target itself.

---

## Quick assembly checklist

- [ ] Run Baker on the real Goniastrea mesh → produces a `PolypScatterMap`.
- [ ] Verify detection via gizmos (spheres centered in cups).
- [ ] Make a polyp prefab: geometry + material using `FluorescentPolyp.shader`.
- [ ] Set `_HealthyColor` from real fluorescence imagery (not the daylight scan).
- [ ] Add `PolypPool` under the Vuforia Model Target; assign map + prefab.
- [x] Build the proximity reveal controller to drive `SetLoupe()` and `stress`.
      (`ProximityRevealController.cs` — assign the Pool + AR camera + optional coral collider.)
- [ ] (Later) add the occlusion depth mesh so real branches hide polyps behind them.
