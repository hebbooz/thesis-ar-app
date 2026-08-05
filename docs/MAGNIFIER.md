# MAGNIFIER.md — the reveal, the arc, and the blur

_Written 2026-08-04, extended 2026-08-05 with §3b (corallite pinning)._
_Covers the magnifier rework from commit `c32038c` onward._

The decisions behind how the polyps emerge, why each one was made, and which are
still assumptions. Work on this was paused mid-refinement — **§8 is the list of what
was left undone**, and §7 is the manual setup step without which the blur does nothing.

For what the magnifier *is*, see `CLAUDE.md` §1 and `CONTROL_INTEGRATION.md` §3.2.
This document is about how it behaves and why.

---

## 1. The one invariant

> **Every arc is a pure function of one number: the effective distance `d`.
> Confidence acts on the INPUT, never on the OUTPUT.**

```
raw measure ─► plausibility gate ─► hold / release ─► One Euro ─► d ─► arcs
                                    ▲
                     all distrust is expressed HERE
```

Given the same `d` you get the same reveal, in both directions of travel. Nothing
animates, ramps, dwells, latches or rate-limits the output. Phone still ⇒ image
still. Phone moves ⇒ image moves with it, immediately and symmetrically.

### Why this needs stating

It was broken once, and the fix is counter-intuitive. A black-box evaluation found
flicker — ~45 unintended state changes in ten seconds at close range — and the
response was to make the **reveal** sticky: an entry dwell, exit hysteresis, a
minimum state age, a refractory period, a re-lock gate, and rate-limited retreat.

It suppressed the flicker. All of it was applied to the wrong variable. The thing
that actually flickered was the coral **mesh**, drawn at a bad solve for a frame or
two. Freezing the reveal instead bought that at the cost of the magnifying-glass
fiction:

- **Hold the iPad still and the polyps appear from nothing a second later.** `Meso`
  pinned `FullscreenReveal` to 0 regardless of distance until `minStateS` elapsed,
  then released it to its distance-derived value in a single frame.
- **Pull away and nothing happens for seconds, then it cuts.** The re-lock gate
  required frame-to-frame steadiness of 8 mm sustained for 0.75 s — a ceiling of
  ~0.5 m/s that no retreating hand can meet. The reveal held at full cover for the
  whole withdrawal (in `Micro` it actively re-inflated toward 1) and snapped only
  once the hand stopped.

Both were the same mistake: a positional quantity governed by a state machine.

### What that means in practice

| Situation | Behaviour |
|---|---|
| Pose not believable | `d` is **held**. Every arc holds with it and resumes with no pop, because the input never jumped. |
| Target lost > `takeoverHoldMaxS` (8 s) | `d` is driven **outward** to `loupeStartDistance`. The reveal closes through the arc it opened through — no bespoke fade. |
| Re-acquire after a hold | `d` reconciles over `reacquireReconcileS` (0.25 s). **The only motion in the file that is not the hand**, and it exists because for a moment we genuinely did not know where the hand was. |

**Removed and not to be reinstated:** the entry dwell, exit hysteresis, `minStateS`,
the refractory period, the re-lock gate, rate-limited retreat, and the steadiness
clock that could not tell a moving hand from a hunting tracker.

**Kept:** the mesh's own guard — pinned to the last pose taken while confidence was
high. A stale pose is invisible (the coral sits where it was); a wrong pose that
*moves* reads instantly as a fault. This is where the anti-flicker work belonged.

**`MagState` survives as a label** that describes the screen and drives nothing.
`MagState.Retreat` was deleted because in and out are now the same code path.

---

## 2. The arc

```
0.30 ──────────► 0.17 m     loupe opens on the coral      (object space, registered)
                 0.17 ────► 0.05 m   iris takes the screen (screen space, unregistered)
```

Two hard constraints pin these numbers. Both were violated by the original tuning.

### Constraint 1 — contiguous, no dead travel

`fullscreenStartDistance` must equal `loupeFullDistance`. The original left **7.5 cm**
between them (loupe done at 12 cm, takeover starting at 4.5 cm) in which the viewer
moved and nothing changed at all. That teaches them motion does nothing, and makes
the eventual onset read as an event that happened *to* them rather than something
they were driving.

`OnValidate` now warns if a gap is reintroduced.

### Constraint 2 — full cover before tracking dies

Vuforia gives up on a ~10 cm Model Target somewhere around **5 cm**. The takeover must
be total by then, or the dropout happens in plain view — that is the entire reason it
is invisible.

The original had the whole takeover arc (4.5 → 2 cm) sitting **inside** that range:
tracking was already gone before the footage started covering anything. A file comment
claimed the opposite. This is where much of the strobing at closest range came from.

**Which end to spend when the emergence feels too fast:**

- The **near end is not free**. Set `fullscreenFullDistance` inside the tracking limit
  and the reveal freezes partway when tracking dies — a stuck ~80% iris that leaning
  closer cannot finish, because there is no measurement left to finish it with.
- The **far end is free**. Move `fullscreenStartDistance` outward (with
  `loupeFullDistance` following) for as much travel as wanted, at no cost.

### The curve accelerates

`fullscreenCurve` is a quadratic-ish ease-in (zero outgoing tangent at 0, incoming
tangent 2.5 at 1), not the default ease-in-out. A lens should gain on approach, not
ease off as it arrives.

This matters more than it sounds because **the same curve drives the iris world
radius** (4 mm → 70 mm). On the old S-curve a tenth of the way along the arc had
already nearly tripled the magnified spot, so the first small lean did most of the
visible work. Now the opening holds near one corallite for the first half of the
travel — which is also the reading the piece wants: you are looking into a single cup
until you commit.

Values live in `SceneBuilder.cs`, not only the scene, because they are not free
parameters. It is a plain `AnimationCurve` and can be dragged in the Inspector.

---

## 3. The blur

### Why radial, and why Depth of Field cannot do it

The loupe's footage is painted on a **duplicate of the coral mesh, at identical depth
to the coral**. Depth of Field separates by distance from the camera, so it cannot
tell those two apart even in principle — it blurs the polyps along with the skeleton,
or leaves both sharp. No amount of tuning changes that.

What a magnifying glass does is **radial**: the disc you look through is sharp and
everything outside falls away. That is a screen-space mask, so it needs a fullscreen
pass with the loupe's projected circle handed to it.

`MagnifierRadialBlur.shader` + `MagnifierBlurFeature.cs` + `mode: "radial"` in
`MagnifierDefocus`. Twelve taps in two rings; radius ramped by **squared** distance
past the loupe rim, because a linear ramp puts a visible ring exactly where the viewer
is looking and a real lens has no hard focus boundary.

**This is the one `ScriptableRendererFeature` the project accepts.** The distinction
matters: `FullscreenMagnifier` explicitly declined to be one, because a single quad
gains nothing from it but a renderer-asset edit invisible in the scene diff. This case
has to *read* the finished colour buffer and write back a modified copy, which nothing
in the scene graph can do.

### The footage stays sharp for free

`FullscreenMagnifier` draws into a `ScreenSpaceOverlay` canvas, which uGUI composites
**after** the whole render pipeline. So the emerging footage is untouched by any
post-process. **Do not change that canvas to `ScreenSpaceCamera`** — it would start
being blurred along with everything else.

### Two traps found along the way (both silent)

**Gaussian DoF needs a depth texture.** It derives its circle of confusion from
`_CameraDepthTexture`. The camera was on `UsePipelineSettings` and `Mobile_RPAsset`
has `m_RequireDepthTexture: 0`. The failure is total and completely silent — no error,
a volume at full weight producing a pixel-identical frame. Now claimed on the **camera**
rather than the pipeline asset, so the cost stays attached to the feature and survives
a quality-level change. Set in the scene, in `SceneBuilder`, and asserted at runtime.

**Volume weight is the wrong handle for Bokeh.** Weight interpolates a volume's
parameters against the **stack defaults**, and URP's default DoF focuses at 10 metres.
The mode enum does not interpolate at all — enums cannot — so the instant the volume
carried any weight, the pass switched to Bokeh and a subject 20 cm away was already
many stops out of focus. The ramp existed; it ran between "sharp" and "very blurry"
with nothing in between. Bokeh now pins weight at 1 and drives the optics instead.
Gaussian still uses weight, where it blends correctly (its mode *is* the stack default,
so nothing snaps).

**Lerp the square, not the length.** Circle of confusion goes as focal length squared,
so a linear sweep is imperceptible for two thirds of its range then arrives all at
once — the same hard transition by a subtler route. Interpolating f² and taking the
root makes the result linear in the drive, which is the only reason
`magnifier_blur_onset` means what it says. Aperture is held constant; moving both
multiplies two ramps and the response stops being reasonable about.

### The blur is established early, not earned late

A magnifying glass has a shallow depth of field the entire time it is held close — the
surroundings are already gone while the magnified spot is still tiny. Ramping blur and
reveal together made the takeover read as a change of shot, because everything changed
at once. `magnifier_blur_onset` (0.35) spreads the softening over roughly the first
7 cm of the takeover and holds it from there.

`skipWhenFullyCovered` drops the blur to zero once the footage covers every pixel —
the same test that decides it is safe to hide the coral. Free, and it matters at the
range the device is hottest.

---

## 3b. Pinning the emergence to a corallite

**The problem.** `LoupeCenter` was re-derived every frame from a ray through the screen
centre — so it was never a crater, it was a cursor. Moving the iPad sideways while
leaning in tracked the emergence point *across* the coral, which reads as footage
sliding over the skeleton rather than something coming out of it.

**What it pins to.** `PolypScatterMap.asset` — 457 corallites in mesh-local space,
already baked, and until now loaded by nothing (`PolypPool` is shelved). Note the
shader's cup/ridge colour split is *not* usable for this: it is an AO texture read per
fragment, so it can shade a cup but cannot answer "where is one".

**Lifecycle.**

```
d > 0.17 m      FREE AIM   raycast through screen centre — the visitor explores
d crosses 0.17  LATCH      snap that hit to the nearest baked cup, store the INDEX
d < 0.17 m      PINNED     LoupeCenter = meshTransform.TransformPoint(cup.localPosition)
d > ~0.19 m     RELEASE    next approach chooses afresh
```

Storing an **index into mesh-local space** is the whole trick: the world point is
recomputed from the coral's transform each frame, so the pin rides the tracked coral
with no registration logic of its own, and freezes correctly when the pose is held.

**Why latch from the raycast hit** rather than searching the map against the view ray:
the raycast has already established a point that is visible, unoccluded and
front-facing, and a nearest-neighbour lookup inherits all three for free. A ray-vs-map
search would have to rediscover them and would happily pin a cup on the far side of
the dome.

**Why the latch is invisible:** it can move the centre by at most half the corallite
pitch — ~2.1 mm on this bake — at the instant the iris is under 4 mm and barely drawn.

**Re-picking** is allowed only below `repickMaxReveal` (0.25) if the pinned cup drifts
past `repickViewportMargin` of the frame. Above that the iris is large, its centre
barely matters, and a changed anchor would be far more visible than an off-centre one.

**`irisWorldRadiusStart` was corrected 0.003 → 0.0018** as part of this. The old value
was justified by "a Goniastrea corallite is roughly 8 mm across", but the baked map for
*this* scan has a 4.2 mm median pitch — so a 6 mm opening straddled two or three cups at
the exact moment it claimed to be inside one. Survivable while the centre was a free
cursor; a contradiction once it pins.

**`sourceMeshName` is a worthless guard** — the committed bake says `default`, which
matches anything. `ValidatePin()` compares baked `sourceBounds` against the live mesh
extents instead and errors above 2% mismatch. A wrong map is not a crash; it is polyps
pinned to cups that are not physically there, which `CLAUDE.md` §3 calls the single
worst failure at loupe range.

---

## 4. The video

**A filesystem path is not a URL, and `VideoPlayer.url` parses it as one.** This
project lives under `2026 University`. The space is not escaped in a raw path, so
AVFoundation was handed a malformed URL: the clips never prepared, no RenderTexture
was ever built, `Ready` stayed false and the magnifier held black. The nudge watchdog
could not help — it only restarts players that *prepared and then stopped*.

**It only broke in the Editor.** On the iPad `streamingAssetsPath` is inside the app
bundle and has no spaces, so a device build would have worked while the Editor did not
— the opposite of the direction this gets looked for. Now uses `Uri.AbsoluteUri`.
Keep it even if the project moves somewhere without spaces.

**The playhead invariant is unchanged and absolute:** playheads only ever advance, and
a clip is seeked only while its weight is 0. Here, nothing seeks at all. `intensity`
drives opacity, never a playhead — scrubbing would run the polyps backwards whenever
the reef cooled before the latch, which is instantly legible as an error in a way no
dissolve is.

**"Ready" was never the right question.** A player that prepared and then parked on
frame 0 is Ready, shows a plausible still, and is indistinguishable from working
footage. That has cost this project time twice (this URL; and several `VideoPlayer`s
sharing a GameObject before it). `IMagnifierSource.Status` now reports it, sampled
twice a second — per-frame would read as a stall constantly with 30 fps footage on a
60 Hz display.

---

## 5. Config surface

All in `coral-ar.json`. `persistentDataPath` wins over `StreamingAssets`, so the editor
override does not affect the shipped copy.

| Key | Default | Meaning |
|---|---|---|
| `magnifier_blur_mode` | `"radial"` | `radial` \| `bokeh` \| `gaussian`. Only radial keeps the loupe sharp. |
| `magnifier_blur` | `1.0` | How soft everything outside the loupe goes. |
| `magnifier_blur_onset` | `0.35` | Reveal at which blur reaches full. Meaningful only because the ramp is perceptually linear. |

`MagnifierDefocus` is added **at runtime**, so Inspector edits during Play do not
survive a domain reload — config is the only place a tweak persists. Radial shaping
(`radialFalloff` 0.28, `radialMaxRadius` 0.045, `radialSharpMargin` 0.02) is still
component-level; promote to config if it turns out to need tuning on the floor.

Distances and curves live in `SceneBuilder.cs` **and** the scene. Editing the scene by
hand is fine; re-running the builder reproduces the committed values.

---

## 6. Diagnostics

The HUD is the acceptance test. Three-finger tap toggles it; `H` in the editor.

```
magnifier=alive 1.00 / fluoro 0.00 / dead 0.00   src=video [3/3 moving]
magnify: Blending 2.3s   m=0.47   d=0.062 (raw 0.061)   pose ok
cover 0.42 partial   loupe 30mm   x1.0   blur 0.61   vuforia trk
```

- **`m` and `d` must move together or not at all.** If `m` changes while `d` sits
  still, something downstream has grown a mind of its own — that is a bug, not a
  tuning problem. This single check is the acceptance test for §1.
- **`src=video [3/3 moving]`** is working footage. `STALLED` is three still images.
  `NOT PREPARED` is the URL/file problem in §4.
- **`vuforia trk/EXT` beside `cover`** is how the tracking limit in §2 gets measured.
- **`HELD n.ns`** means the pose is not believable and `d` is frozen — which is why
  the picture is frozen too, correctly.

---

## 7. ⚠️ Manual setup — the blur does nothing without this

Renderer features live in the renderer asset, not the scene, and cannot be added from
code reliably.

1. Select `Assets/Settings/Mobile_Renderer.asset` — **the only one in use**.
   GraphicsSettings points at `Mobile_RPAsset`, no quality level overrides it, and that
   asset's renderer list points here. `PC_Renderer.asset` is unused.
2. **Add Renderer Feature → Magnifier Blur Feature**
3. Assign `Assets/CoralPolyps/Runtime/MagnifierRadialBlur.shader` to its Shader field.
   The picker lists it by **file** name (`MagnifierRadialBlur`), not the declared
   shader name (`CoralPolyps/MagnifierRadialBlur`).
4. Save the project (Cmd+S). A scene save will not persist this.

Step 3 is not optional for a device build: a shader reachable only through
`Shader.Find` is stripped on iOS — the same trap that once silently removed the
fullscreen takeover from a build while the Editor worked perfectly. If
`[MagnifierBlurFeature] shader found by name, not by reference` appears at startup,
the field is still empty.

---

## 8. Left undone — pick up here

**Unverified assumptions**

- **Vuforia's ~5 cm give-up point is an estimate**, inherited from a code comment, not
  a measurement of this print under exhibition lighting. The entire near end of the arc
  hangs on it. Measure it once from the HUD (`EXT` appearing before `cover` reads
  `FULL` means the real limit is further out and `fullscreenFullDistance` must rise)
  and write the number down.
- **The radial blur has compiled but not run.** RenderGraph behaviour at runtime is
  untested — it could black-screen or silently no-op. Unticking the feature on the
  renderer asset reverts it instantly.
- **No thermal soak** has been run with the radial pass, and none with bokeh either.
  All-day thermals is risk #5 in `CLAUDE.md`. The depth texture also adds a prepass for
  the DoF modes.

- **The pin has not been judged on device.** Watch `pin #n` on the HUD: a number that
  *changes* while leaning in is the pin thrashing, which would look exactly like the
  sliding it exists to stop.

**Known-incomplete**

- **`confidence` in the scatter map is unusable as a quality gate.** All 457 entries
  fall between 0.06 and 0.181 (median 0.102) — nothing above 0.2, so no absolute
  threshold can separate good detections from bad. Either `CoralliteBaker`'s
  normalisation is wrong or every detection is weak. The pin currently ignores the
  field entirely and takes the geometrically nearest cup. Worth a look at the baker
  before relying on confidence for anything.

- **The pacing was tuned against still images.** The video was not playing for the
  whole period the arc length and curve were being judged. Moving footage carries
  motion that a frozen frame did not — re-judge the arc before changing more numbers.
- Radial falloff values (§5) are first guesses, unjudged on device.
- `MagnifierDefocus` is runtime-added, so it is invisible in the scene hierarchy unless
  `SceneBuilder` is re-run. Fine, but surprising when looking for it.
- `fullscreen: {fileID: 0}` is unassigned in the scene; `Awake` finds it by search.
  Works, but the wiring is implicit rather than visible in the scene diff.

**Not started**

- Occlusion depth mesh (invisible coral copy hiding polyps behind real geometry) — from
  `CLAUDE.md` §6, still outstanding and unrelated to this rework.

---

## 9. Commit index

| | |
|---|---|
| `c32038c` | Pin the reveal to distance: confidence acts on the input, never the output |
| `1c4e0c7` | Give the emergence room to happen, and defocus everything behind it |
| `ba04935` | Silence the FindFirstObjectByType deprecation warnings |
| `a619a5f` | Make the takeover accelerate instead of easing off |
| `110dd5d` | The defocus was a silent no-op: Gaussian DoF needs a depth texture |
| `e24458c` | Establish the blur early instead of earning it late |
| `afb2fc5` | Lengthen the emergence: 12 cm of takeover travel, up from 7.5 |
| `c68297c` | Fix the video never playing, and report whether footage is moving |
| `3b6372a` | Make the blur arrive gradually instead of stepping in |
| `0e9d0e8` | Radial blur: keep the loupe sharp, soften outward from its rim |
| `2604bc9` | Fix build: bokehApertureStart referenced after being removed |
| `0423945` | Document the magnifier rework: decisions, constraints, and what is left |
| _(this)_  | Pin the emergence to a corallite instead of the crosshair |

Each message carries the reasoning for its own change; this document is the synthesis.
