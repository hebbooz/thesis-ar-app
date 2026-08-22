# MAGNIFIER.md — the reveal, the arc, and the blur

_Written 2026-08-04, extended 2026-08-05 with §3b (corallite pinning), 2026-08-08 with
§3c (rotational registration), and 2026-08-16 with §2b (the emergence scale) and §2c
(the silhouette leash)._
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

## 2b. How big the opening is, and how big the polyps are

These are two questions, and they used to be one number.

`MagnifierFullscreen.shader` fitted the clip's **width to the iris diameter**, so the
footage's scale was decided by the mask. At emergence the whole 720×1280 frame was
squeezed into a 3.6 mm opening and the central polyp drew about **a third of the cup it
was coming out of** — while the loupe layer underneath was drawing that same polyp
several times larger at the same instant. The handover at 0.17 m shrank the footage by
roughly an order of magnitude and the polyps then had to swell back, which is what read
as *emerging from nothing*.

`_ContentRadius` splits them. The iris still masks at `_Radius`; the clip is now sized
by a **world length at the crater**, projected each frame like everything else here:

```
                     emergence (0.17 m)        full (0.05 m)
  opening  _Radius        5.8 mm  = 1 cup   ──►  70 mm, screen-filling
  footage  _ContentRadius 9.6 mm  = 1 cup   ──►  70 mm   (they converge at g = 1)
                          drawn at LIFE SIZE
```

Both ride the same `irisGrowth` curve, so **the far end of the arc is untouched** — they
meet at `g = 1` and hand over to the screen-fit mapping exactly as before. Only the start
differs: the magnifier now begins at 1×, resting on the surface, and earns every bit of
magnification after that. Larger than the opening is normal and intended — you are
looking at the middle of the clip through a cup-sized hole.

**Where 9.6 mm comes from:** a cup is 5.78 mm world (§3b), and the clip's central polyp
fills roughly **0.6** of the frame width, so 5.78 / 0.6 puts that polyp at exactly one
cup. The 0.6 is eyeballed from the footage and is the number to change if the emergence
scale looks wrong — **re-encoding the clips at a different framing changes it and nothing
in code will notice.**

> Branch `trial/anya-footage` is the worked example: a tighter macro set fills 0.85 of the
> frame instead of 0.6, taking this field to 0.0068. See `FOOTAGE_TRIAL.md`.

**`_ContentRadius` is deliberately not clamped** by the silhouette leash or the corner
cap. Those bound the *mask*, and a lens that is partly occluded still magnifies by the
same amount. While they shared one variable the silhouette clamp silently changed the
magnification — an interaction no amount of tuning the curves could have explained.

The **loupe** beat still draws these polyps ~5.6× larger than a cup, so a residual step
survives at the handover, masked by the iris fading in from zero opacity at 6 mm wide.
Closing it would mean `_FootageScale` 0.07 → ~0.0125 *and* `maxLoupeRadius` 0.03 → ~0.006
(below that the footage tiles visibly inside the window), which changes the loupe beat
rather than its scale. Left alone until the step is judged on device.

---

## 2c. The opening ends on the coral, not on a number

**The symptom.** The footage cuts off inside the tissue: the magnified honeycomb runs on
past the video's feathered rim, so the health material is visibly outside the frame the
video is playing in. It reads as two layers that have come apart.

> ### ⚠️ The first diagnosis was wrong, and the way it was wrong is the useful part.
>
> The obvious suspect was the silhouette leash, and it *did* contain a real bug (below).
> Fixing it changed nothing, because **the leash was never the binding constraint** — the
> growth arc was, and one number would have said so before any code was written:
>
> | | |
> |---|---|
> | coral's covering radius, life size | **105 mm** |
> | `irisWorldRadiusEnd` | **70 mm** |
>
> The opening could not reach the coral's edge *even at 1×*, and the magnification arc
> then took the coral to 2.5× while the opening stayed put. `irisWorldRadiusEnd` was
> sized to outgrow the **screen** at the near end of the arc — a different requirement
> from covering the **coral**, and the smaller of the two.
>
> **Check which cap is binding before fixing a cap.** `min(a, b, c)` gives no hint which
> term won, and all three were plausible. The `scale:` HUD line (§6) exists so this is
> one glance rather than one rebuild.

### The fix: the arc's endpoint

`worldEnd` is now `ProximityRevealController.CoralWorldRadiusM` — the coral's covering
radius times the magnification — with `irisWorldRadiusEnd` demoted to a floor for when
that cannot be measured. The opening finishes exactly covering the coral as drawn, and
grows with it.

**`CoralWorldRadiusM` is a bounding SPHERE, not `Renderer.bounds`,** and that is §1's
problem rather than a tidiness one. `Renderer.bounds` is axis-aligned in *world* space, so
its extents change with viewing angle — on this alignment (304/247/260°, nowhere near
axis-aligned) the box half-extents run 82–103 mm depending which axis you take. Sizing an
arc from that would swell the opening by a quarter as the visitor merely **rotated the
phone**, with `d` sitting still. The mesh's own bounds are constant, so this is a constant
× `k`, and `k` is a pure function of `d`.

**The content had to follow it — but by a `Max`, not by a shared endpoint.** Mapping A
draws the clip across `_ContentRadius`, so a hole *wider* than the clip runs the UVs
outside [0,1] and the rim fills with whatever the RenderTexture's wrap mode does. The first
attempt satisfied that by giving the content the same endpoint as the opening, which was
wrong and visibly so — see §2d. `contentRadius = Max(its own arc, radius)` costs the same
and only binds when it must.

### What this does and does not close

The opening still starts at one cup, so **a ring of bare tissue early in the approach is
inherent** — that ring *is* the emergence. What changed is how fast it closes, and when
the screen fills:

| at d ≈ 85 mm | opening (opaque core) | tissue | ring |
|---|---|---|---|
| before | 0.11 | 0.43 | 0.32 |
| now | 0.19 | 0.43 | 0.23 |
| now, with `irisGrowth` straightened to linear | 0.43 | 0.43 | 0.00 |

Full coverage also moves **from d = 50 mm to 70 mm**, which is margin against §2's
constraint 2 rather than a cost.

**`irisGrowth` is now the knob that decides when the ring closes** — a plain
`AnimationCurve`, draggable in the Inspector, no code. It was left accelerating on
purpose (the cup-emergence beat is the piece's central claim); straighten it toward linear
if the ring reads as a fault rather than as an opening.

### The leash, which was a real bug and a red herring at the same time

`FullscreenMagnifier` compared two projected radii with a `Min` as though they were the
same quantity, but projected them from different points:

| | projected at |
|---|---|
| iris radius | `LoupeCenter` — the pinned cup, on the coral's **near face** |
| silhouette leash | `coralRenderer.bounds.center` — the coral's **middle** |

The coral's own half-extents are ~70 mm, which would be bad enough. The magnification
block makes it far worse: scaling about the pinned cup pins the **anchor**, so
`_root.position = P + (anchor - P) * (1 - k)` sends the bounds centre *away* from the
camera by `(k-1) ×` the anchor-to-centre distance. At 5 cm and k = 2.5 the near face is
0.05 m from the camera while the centre is ~0.22 m — the leash was computed as if the
coral were a flat disc **four and a half times further away** than the surface being
looked at.

`CoralSilhouetteRadius` now runs the coral's radius through `ProjectedRadius` — the same
function, from the same point, at the same depth as the iris it is compared against. That
also fixes the quieter half of the error: the old measurement was a radius about the
*coral's* centre while the iris is centred on the *pinned corallite*, which can sit near
the rim.

**Then it stopped mattering.** With the arc ending on the coral, the leash and the arc
derive from the same `CoralWorldRadiusM`, so the arc can only reach the leash at `g = 1`,
by which point the release has opened it. The leash is a **backstop** now, against the
endpoint being unmeasurable — not the thing that decides how big the opening is.
`coralEdgeMargin` and `clampReleaseCoverage` are unchanged, and the release is still what
guarantees the takeover can reach every pixel.

Two things worth keeping from the episode:

- **The publish, not the read.** `CoralWorldRadiusM` is taken while the transform is at
  base scale and multiplied by `k`, rather than re-read from `coralRenderer.bounds`. That
  file resets the transform at the top of `LateUpdate` and re-scales it at the bottom, so
  anything reading the renderer's bounds gets whichever of those it happens to catch — and
  **neither script declares an execution order**, so today's answer is arbitrary.
- **`Mathf.Lerp` from `+infinity` is NaN for every `t`, including 0** (`a + (b-a)*t` is
  `inf + (-inf)*t`). `CoralSilhouetteRadius` returns `+inf` for "could not measure", which
  the release ramp then lerped — and a NaN radius makes the iris vanish rather than open.
  Guarded explicitly.

---

## 2d. The migration is measured against the opening, not against coverage

**The symptom.** The footage zooms far past native scale as the opening expands, then
visibly *retracts* to native once it is fully open. A close-up that pulls back — the one
motion a lens never makes.

**Three requirements meet at `_Settle`, and two of them are hard:**

| | |
|---|---|
| the opening must be **big** | it has to reach the tissue's edge (§2c) |
| the clip must **fill the opening** | or mapping A samples off the end of the video |
| the clip must **end at native** | that is where mapping B lands |

An opening larger than the clip-at-native **forces** the clip past native, and `_Settle`
then has to bring it back down. That is the retraction, and it is arithmetic rather than
tuning: §2c raised the opening's endpoint from 70 mm to the coral's radius (up to 262 mm),
the content was tied to the same endpoint, and the footage went to ~3× native mid-arc with
a 5× migration waiting at the end of it.

**`_Settle` used to run on two coverage thresholds** (0.75 → 0.95). That only ever worked
because the opening grew slowly enough to still be smaller than the clip when the migration
began. Nothing in it *knew* how big the opening had got, so it could not defend the one
invariant that matters here.

**Measured against `nativeHalf` — the clip's half-width when cover-fitted, `max(footage,
screen aspect) / 2` — the failure stops being possible.** The migration completes precisely
as the opening reaches the clip's native extent, so the clip is never required to exceed
native and never has to come back:

```
opening:   0.01 ──────────────► 0.28 ────────────► 0.71 (corner)
footage:   0.09x ─────────────► 1.00x ───────────► 1.00x
                        settle 0 → 1 across here
```

Measured over the whole arc the effective scale now runs 0.09× → 1.00× and stops. It
previously peaked at **5.15×** at d ≈ 62 mm before falling back.

Two things come free:

- **It is self-tuning.** Retune `irisGrowth` or the arc distances and the migration still
  lands in the right place; two hand-picked coverage numbers would drift silently, and the
  symptom of the drift is the overshoot coming back.
- **The old reason for keeping it late still holds, without being stated.** Early in the
  approach the opening is a few millimetres — far below `settleStartFraction` — so the
  footage stays at crater scale and cannot "read as too large from the very start", which
  is what killed the previous attempt at migrating early.

**The cost, and it is real:** the footage reaches native at d ≈ 78 mm and stops magnifying,
so the last ~28 mm of approach is the opening widening rather than the polyps growing.
`endZoomMax` is the knob (it crops past 100%), deliberately left at 1 — §4's note on it is
a considered trade against losing the surrounding colony, not a free parameter.

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

**The twelve taps run at half resolution** and a two-tap full-resolution pass
composites them back, because at full resolution they were the single most expensive
thing in the frame (see §8). The radius ramp stays in the half-res pass: blurring at a
fixed maximum and crossfading sharp → blurred in the composite would be cheaper still,
but a crossfade between an image and a heavily blurred copy of it reads as a ghosted
double-image at every intermediate value — the exact artefact twelve taps were chosen
to avoid. The composite's own blend only picks between two versions of the same ramp,
and only near the sharp region, so the loupe interior is always native resolution.

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

**`irisWorldRadiusStart` — wrong twice, in opposite directions, and now 0.0029.**
The original 0.003 was justified by "a Goniastrea corallite is roughly 8 mm across",
which is the species in general rather than this scan. It was corrected to 0.0018
using the baked map's median pitch of 4.2 mm — correct for the map, but **that figure
is in the mesh's OBJECT space, and the field is consumed in WORLD metres**
(`ProjectedRadius` offsets a world point by `cam.transform.up * r`). `AlignScale` is
1.37, so a cup is **5.78 mm** out there and 0.0018 made the opening 0.62 of a cup
while the comment claimed it was one.

> **Anything in metres that is compared against the scatter map has an `AlignScale`
> in it.** The map is baked in object space; every arc in `ProximityRevealController`
> and `FullscreenMagnifier` is world. The two differ by 1.37 and neither side says so.

**`sourceMeshName` is a worthless guard** — the committed bake says `default`, which
matches anything. `ValidatePin()` compares baked `sourceBounds` against the live mesh
extents instead and errors above 2% mismatch. A wrong map is not a crash; it is polyps
pinned to cups that are not physically there, which `CLAUDE.md` §3 calls the single
worst failure at loupe range.

---

## 3c. Registration — the half of the pose the guards could not see

**The symptom.** The coral sits in the right *place* on the print and is visibly twisted off
it — sometimes by tens of degrees, intermittently, from ordinary viewing angles.

**Why it is a rotation and not a position.** A Model Target solves pose from silhouette and
edge geometry, and those constrain the two halves of the pose very unequally. The silhouette
centroid and apparent size pin translation hard. Yaw about the dome axis they barely pin at
all: this target is 133 × 139 × 110 mm with a near-square footprint and 457 corallites at
4.2 mm pitch that all look like each other, so rotating the model about its vertical axis
changes the silhouette almost not at all. Many yaw hypotheses score within noise of one
another and the winner changes frame to frame.

**Why nothing caught it.** Every guard in `ProximityRevealController` measured a scalar
distance — `implausibleFarM`, `implausibleJumpM`, the One Euro filter, the hold, the
reconcile. A yaw flip changes camera-to-coral distance by *essentially zero*. So it passed
`IsPlausiblePose`, was marked `believable`, and was then captured into `_confidentRot` as a
pose worth defending. §1's guard — the one that exists to stop the mesh chasing bad solves —
was memorising the error and holding it steady.

The invariant in §1 is still right. It was just enforced on one of the two quantities.

### ⚠️ The gate shipped enabled at 60 deg/s and made registration WORSE. It is now off by default.

Symptoms on device: the coral snapped to positions further wrong than before, and was
sometimes *much smaller* than the print.

**The error.** The reasoning was "the coral is bolted down, so its world rotation is constant,
so 60 deg/s is loose by an order of magnitude." The **object** is steady; the **solve** is not.
Ordinary Vuforia jitter on a low-feature target runs a degree or three frame to frame, and at
60 fps one degree per frame *is* 60 deg/s. The gate was set at the level of honest tracking and
refused most of it.

**Why that is so much worse than merely useless — two compounding failures:**

1. Rejecting continuously latches the mesh near its **first** solve and it never updates again.
   That is not "a stale pose is invisible"; it is a coral pinned to a world pose from thirty
   seconds ago while the visitor walks around it.
2. A frozen mesh **does not stop the magnification block**. It still runs, scaling by `k` and
   displacing the pivot by `(anchor - P) * (1 - k)`. At `maxMagnification` 2.5 that is a 1.5×
   shove along a vector derived from a **live raycast against stale geometry**, so the scale-up
   and the displacement stop cancelling. The coral lands anywhere — including far behind the
   print, where a 2.5× mesh still reads as far too small. *This one is a pre-existing bug that
   the gate merely exposed:* it fires on any frozen pose, including every `EXTENDED_TRACKED`
   dropout today, and is a live suspect for part of the original complaint. Not yet fixed.

**The order was wrong.** The gate is a fix whose target was never measured. `enableSpinGate` is
now `false`; the `rot:` readout runs regardless and costs nothing. Watch `spin` on device, see
what honest tracking actually does, set `implausibleSpinDegPerS` comfortably above it — expect
several hundred, not 60 — and only then turn the gate on.

**The gate's design, for when it is switched on.** The coral is bolted into the bath and
Vuforia's world centre mode is `DEVICE`, so a correct solve should report a world rotation that
is nearly *constant* at the scale of seconds, whatever it does frame to frame.

Two details carry the design:

- **It measures against the last ACCEPTED solve, divided by the time since.** The tolerance is
  a budget that *grows* while a solve is being refused, so a transient flip is rejected and a
  solve that genuinely stays put is adopted about half a second later. Distrust can never
  become permanent. That ceiling is the entire lesson of §1 — the re-lock gate died because it
  could refuse the tracker indefinitely, and a rotational version of that mistake was available
  here for free.
- **The reference is the OBSERVER's rotation, not the coral's.** §3's freeze writes the
  coral's transform, so reading rotation back from it would compare a solve against this
  file's own output and the test would silently measure nothing.

**A bad rotation holds the MESH; it does not hold `d`.** `spinPlausible` is deliberately not
folded into `believable`. `believable` governs `d`, and `d` is what the reveal tracks — so
folding a yaw fault in would freeze the emergence on every tracker twitch and charge a 0.25 s
reconcile for each recovery. A yaw flip tells us nothing about how far away the hand is; that
measurement is still good. Two independent faults, two independent gates: **a bad distance
holds the reveal, a bad rotation holds the mesh.** The hand keeps driving the picture either way.

**This matters most at loupe range,** because §3b pins the emergence to a baked corallite
index. Yaw off ⇒ the polyps emerge from a cup that is not physically there, which `CLAUDE.md`
§3 calls the single worst failure the piece can make.

**What the gate cannot do.** It selects among the poses Vuforia offers. It cannot manufacture
yaw information the geometry does not contain. If from some viewpoint the wrong yaw
*consistently* wins the solve, this buys a stable wrong answer instead of an unstable one —
see §8 for how to tell, and what to do about it.

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

`settleStartCoverage` **is gone** — replaced by `settleStartFraction` (§2d), which is a
fraction of the opening's native extent rather than a coverage threshold. A scene saved
before that carries the old key; Unity drops it silently and the new field takes its
default, so the only symptom of missing this is the migration starting at the wrong time.

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
cover 0.42 partial   loupe 30mm   x1.0   blur 0.61   pin #212   vuforia trk
scale: clip 35.3mm   coral 110mm   leash 1.41   limit arc
rot: drift 1.4deg   spin 3deg/s   ok   rej 6
```

- **`m` and `d` must move together or not at all.** If `m` changes while `d` sits
  still, something downstream has grown a mind of its own — that is a bug, not a
  tuning problem. This single check is the acceptance test for §1.
- **`scale:` is the §2b/§2c line**, and neither number is observable from a plinth
  without printing it.
  - **`clip`** is how many millimetres of coral the footage's width spans — the
    footage's magnification, in the one unit that can be held against the object it is
    emerging from. **At the start of the takeover it must read ~9.6 mm.** Much below
    that and the polyps are emerging from nothing again.
  - **`limit` is the most useful word on the line**, and the one whose absence cost a
    build cycle. The iris radius is a `Min` of three terms and the screen looks identical
    whichever wins, so "the footage is cutting off inside the tissue" has three causes
    that cannot be told apart by eye:

    | Reading | Meaning |
    |---|---|
    | `arc` | The growth curve is holding it. The normal answer, and the one to expect through the approach. Too small a ring for too long ⇒ straighten `irisGrowth`, not a bound. |
    | `leash` | The coral's silhouette is holding it. Should be rare now — if it reads here while the tissue visibly extends past the video's rim, the bound is measured too small (§2c). |
    | `cap` | The screen corner. The takeover is complete; `cover` should say `FULL`. |

  - **`coral`** is the magnified covering radius the arc now ends on, and **`leash`** the
    backstop derived from it; `off` means it could not be measured and the corner cap is
    in sole charge.
- **`src=video [3/3 moving]`** is working footage. `STALLED` is three still images.
  `NOT PREPARED` is the URL/file problem in §4.
- **`vuforia trk/EXT` beside `cover`** is how the tracking limit in §2 gets measured.
- **`HELD n.ns`** means the pose is not believable and `d` is frozen — which is why
  the picture is frozen too, correctly.
- **`rot:` is the registration line** (§3c) and it is the only place a yaw error is
  measurable — the two lines above it describe the pose as a single distance, which is the
  half of it that was never the problem. `drift` is how far the live solve has twisted from
  the orientation actually being drawn, in the unit you can judge against the print.

  Walk a slow circuit of the coral and read which regime you are in:

  | Reading | Meaning |
  |---|---|
  | `drift` near 0, `rej` climbing slowly | Flips are transient and being caught. Working state — the mesh does not visibly twist. |
  | `drift` parked at 20-40deg and not falling | The tracker has settled on a wrong yaw and the gate has correctly stopped refusing it. **No filter recovers this** — the symmetry has to be broken on the model target or the print. |
  | `rej` climbing continuously from every angle | `implausibleSpinDegPerS` is too tight and is refusing honest tracking. Raise it. |

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

  **2026-08-19 — it is still not running, and that is now a recorded fact rather than
  an assumption.** `Mobile_Renderer.asset` carries `m_Active: 0` on the feature, and
  URP skips inactive features outright (`ScriptableRenderer.cs:1099`). Radial mode
  builds no volume and claims no depth texture either, so **the device currently has no
  blur of any kind** — worth knowing before attributing a frame-rate symptom to it, as
  was done once already.

  Before ticking it on, note what it was going to cost: 13 full-resolution taps over a
  ~2 M-pixel target is **~25 M texture fetches per frame**, each scattered up to ~90 px
  from its neighbours, arriving exactly when the viewer leans in — next to Vuforia and
  three video decodes. It is now **half-resolution blur + full-resolution composite**
  (~5.3 fetches per pixel, and four times the cache locality on the scattered ones), and
  the feature enqueues nothing at all while the strength is zero, which also gives URP
  its backbuffer fast path back for the whole of the approach. **Tick it on and judge it
  on device** — that is the step that is still outstanding.
- **No thermal soak** has been run with the radial pass, and none with bokeh either.
  All-day thermals is risk #5 in `CLAUDE.md`. The depth texture also adds a prepass for
  the DoF modes.

- **The pin has not been judged on device.** Watch `pin #n` on the HUD: a number that
  *changes* while leaning in is the pin thrashing, which would look exactly like the
  sliding it exists to stop.

- **The emergence scale rests on one eyeballed number.** `footageWorldWidthStart`
  (§2b) assumes the clip's central polyp fills ~0.6 of the frame width. Watch `clip`
  on the HUD at the start of the takeover and compare the drawn polyp against a real
  cup; adjust the field, not the assumption, since a re-encode changes the framing
  and nothing in code will notice.

- **The loupe → takeover scale step survives** (§2b, ~5.6×). It should be hidden
  behind a 6 mm iris fading in from zero opacity. If it reads, the fix is the loupe
  beat, not the takeover.

- **The ring closes late, on purpose, and has not been judged on device.** With the arc
  ending on the coral (§2c) the ring at d ≈ 85 mm is 0.23 against the tissue's 0.43 —
  better than the 0.32 before, not gone. `irisGrowth` decides the rest: straightened to
  linear it closes there completely. The accelerating curve was kept because the
  cup-emergence beat is the piece's central claim, but that is a judgement made off
  device against a number, which is the position §2's own pacing note warns about.

- **Full coverage now arrives at d ≈ 70 mm rather than 50 mm.** That is *more* margin
  against §2's constraint 2, not less, but it is a change to when the tracking dropout
  gets hidden and it should be watched: confirm `cover` reads `FULL` before `vuforia`
  flips to `EXT`.

- **The last ~28 mm no longer magnifies the footage** (§2d): it reaches native at
  d ≈ 78 mm and holds while the opening widens. Judge whether that reads as arriving or
  as stalling. `endZoomMax` above 1 is the answer if it stalls, and it is paid for in
  resolution and in the surrounding colony — read §4 before spending it.

- **`settleStartFraction` (0.5) has not been judged on device.** It decides how much of
  the approach is spent at crater scale versus native. Lower keeps the physical-size
  reading for longer; higher gets to full-screen footage sooner.

- **`implausibleSpinDegPerS` (60) is a first guess and the spin gate has not run on device.**
  It was reasoned from "the coral does not move, so its world rotation should be constant",
  not measured. Read the `rot:` line (§6) on a slow circuit and set it from what honest
  tracking actually does.

**Registration — the model target itself (§3c)**

None of these are code, and all of them are upstream of the gate, which only ever chooses
among the poses Vuforia hands it. Worth doing in this order once §3c has been judged:

- **`motionHint` is `adaptive` and should almost certainly be `static`.** `coral-rendering.xml`
  says `motionHint="adaptive"`, i.e. "expect this object to be picked up and moved" — so
  Vuforia re-solves pose every frame, which is the door the yaw flips walk through. The coral
  is fixed in a water bath. Vuforia's guidance is that STATIC suits immobile objects and lets
  the tracker lean on the device pose instead of re-solving; the cost, that moving the object
  breaks tracking until re-detection, does not apply here. One field in the Model Target
  Generator, and the highest-leverage non-code change available.
- **`trackingMode="car"`** on a 13 cm coral. Unverified — a vehicle tracking mode on this
  object deserves one look in the MTG before anything else is tuned.
- **The database is untrained.** All 7 entry points in the XML say `trained="no"`, so this is
  a standard Model Target leaning on guide-view alignment for recognition. A persistent yaw
  error usually descends from a bad *initial* solve; an Advanced (trained) database gives
  view-independent recognition.
- **`mTrackingOptimization` is DEFAULT and `mHasRealisticTextures` is 0** — a white untextured
  print is a low-feature object by definition, so `LOW_FEATURE_OBJECTS` is worth a test.
  Likewise `mEnhanceRuntimeDetection` (currently 0). Both reversible one-field changes.
- **Break the symmetry physically.** The only fix that addresses the cause rather than the
  symptom: one rigid asymmetric landmark in the Model Target CAD — an asymmetric base collar
  under the print, a notch, one distinctly-shaped lobe. A dome with a single yaw landmark
  collapses the ambiguity outright. Reach for this only if the `rot:` line says the wrong yaw
  is winning *consistently* rather than intermittently.

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
| | Pin the emergence to a corallite instead of the crosshair |
| | Measure the solve's rotation; the gate that acts on it stays off until it is |
| | Start the polyps at life size, and leash the iris to the coral you can see |
| | Open onto the coral instead of onto a number, and measure which cap is binding |
| _(this)_  | Migrate to native when the opening reaches it, so the footage stops retracting |

Each message carries the reasoning for its own change; this document is the synthesis.
