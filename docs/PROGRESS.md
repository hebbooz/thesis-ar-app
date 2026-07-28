# PROGRESS.md — Status & roadmap

_Last updated: 2026-07-28_

A living record of what's built, the key decisions made along the way, and what
remains. For the original vision see `CLAUDE.md`; for the phase-by-phase plan see
`BUILD_PLAN.md` (note: Phases 3+ were **re-shaped by a design pivot** — see below).

---

## Snapshot

> **⚠️ SCOPE CHANGE 2026-07-27 — read `docs/CONTROL_INTEGRATION.md`.**
> This app is now a **passive subscriber** to the installation's orchestration
> server. The server owns the healthy→bleached arc; **proximity drives only the
> reveal and the magnification**. The proximity→bleach logic described below (and
> in `BUILD_PLAN.md` Phase 6, `USER_STORIES.md` V3) is **superseded** and is
> removed during integration. Remaining work is therefore **not** only refinement —
> see `CONTROL_INTEGRATION.md` §11 for the order of work.
>
> **✅ INTEGRATION CODE LANDED 2026-07-28 — `CONTROL_INTEGRATION.md` §11 steps 1–5.**
> `ProximityRevealController`'s local narrative (approach → peak → arm bleach →
> retreat bleaches → reset) **has been deleted**; it now owns only magnification and
> the loupe. Everything below that describes proximity driving colour is history,
> not behaviour. See _"Installation integration"_ near the bottom for what exists.
>
> **⚠️ AND: the scene wiring described in _"Current concrete state"_ below is NOT in
> this repository.** See the corrected bullet in that section — this is the single
> thing most likely to mislead the next session.

**The full loop works on device (2026-07-14):** the iPad tracks the physical
3D print via a Vuforia Model Target, and leaning in reveals the fluorescent coral,
then bleaches it — proximity IS the zoom. *(The bleach half of that sentence is what
changed on 2026-07-27; the tracking, the reveal and the magnification all stand.)*
The remaining work **on the rendering side** is **refinement**: tighter *spatial
registration* of the glow onto the print (Phase 5) and *tuning the zoom/reveal
beats* against the real object (Phase 7), plus the plinth mount, an
endurance/thermal soak, and the thesis write-up.

---

## The design pivot (important context)

The original plan placed a **discrete polyp mesh in every corallite** (Baker →
Pool → per-cup instances). Partway through Phase 3 we recognised this is **not
accurate for this coral**: _Goniastrea_ (Astraea favistella) is **cerioid** — a
continuous shared-wall honeycomb — so the true fluorescent view is a **glowing
honeycomb of tissue over the skeleton**, not anemone-like tentacle polyps.

**Decision:** render the fluorescence as **living tissue on the coral surface**
(a shader on the coral mesh), not as discrete polyps. Bonus insight: the physical
3D print *is* the bleached white skeleton, so the virtual layer only needs to
render the **fluorescence**, which drains away to reveal the print as it bleaches.

- **Shelved (not deleted):** `PolypPool.cs`, the polyp prefab, `FluorescentPolyp.shader`.
  Kept in case a hybrid (glowing honeycomb + raised oral discs) is ever wanted.
- **Still useful:** the Baker's `PolypScatterMap` (columella positions can drive
  where oral discs glow brightest, later); `ProximityRevealController`'s reveal/
  bleach logic (to be retargeted at the coral material).

---

## Current concrete state — what ALREADY EXISTS (don't re-create this)

_Read this before proposing any setup steps. Most setup is done; re-instructing it
is the #1 way to confuse the operator. Named assets below already exist and are wired._

**Engine / project:** Unity **6000.5.3f1**, URP. iOS target configured (camera
usage description, min iOS 16, iPad-only, bundle `com.thesis.coralpolyps`).
`Mobile_RPAsset` is the active URP pipeline, **assigned across Graphics + all
Quality levels**, **HDR ON**. (Don't remove that assignment — see gotchas.)

**Working scene: ⚠️ NOT IN THE REPOSITORY (verified 2026-07-28).** This entry used to
read "`Assets/Scenes/SampleScene.unity` — the coral GameObject is in it with the
CoralTissue material applied". That is **not true of the committed tree**:

- `Assets/Scenes/SampleScene.unity` is the **stock scene** — Main Camera, Directional
  Light, Global Volume, nothing else. It has one commit ("first commit", 24 July) and
  has never contained the coral, the ModelTarget, or `ProximityRevealController`
  (searched by script GUID across all history). Independently re-verified: only one
  `.unity` has ever been tracked, nothing was deleted from history, and there is no
  `Temp/__Backupscenes` to recover from.
- `ProjectSettings/EditorBuildSettings.asset` still lists
  `Assets/SamplesResources/Scenes/0-Main.unity`, and **that file no longer exists** —
  `Assets/SamplesResources/` has been deleted. So the build scene list points at
  nothing.

**Root cause — confirmed 2026-07-28.** Of the two possibilities above, it was the
second. `Library/LastSceneManagerSetup.txt` shows the last scene Unity had open was
`Assets/SamplesResources/Scenes/0-Main.unity` — the **Vuforia sample scene**. The
coral, the ModelTarget and the controller were wired up *inside a sample scene*, so
deleting `Assets/SamplesResources/` deleted the working scene with it. The device
builds of 12–14 July were real; they were built from that scene.

> **The lesson, because this document caused it.** An earlier revision of this very
> section described `Assets/SamplesResources/` as "Vuforia-sample clutter, safe to
> ignore/delete" — while the working scene was sitting in it. Deleting it was
> *following the documentation*. Two rules follow: **never build in a vendor's sample
> folder** (create the scene under `Assets/Scenes/` from the start), and **never call
> a directory disposable without checking what is open in it.** The rebuild is now a
> script (below) precisely so that losing a scene costs minutes instead of days.

**Consequence:** the scene has to be rebuilt, and there were no tuned inspector values
to lose when `CONTROL_INTEGRATION.md` §11 step 4 deleted the serialized fields (§10's
warning about that turned out to be moot here). Every *asset* it needs does still
exist — mesh, `CoralTissue.mat` with its tuned look-dev values, textures, the
`coral-rendering` Model Target database, the bake.

**The rebuild is a script, not a checklist:** `Window > CoralPolyps > Rebuild AR Scene`
(`Assets/CoralPolyps/Editor/SceneBuilder.cs`) constructs the whole scene — coral +
material + collider, `ModelTarget` parenting, the magnifier layer, the four control
components with their references wired, a directional light and a Bloom volume with
post-processing enabled on the AR camera — saves it to `Assets/Scenes/CoralAR.unity`,
and repoints `EditorBuildSettings` (which still listed the deleted `0-Main.unity`).
**The root cause of the loss was wiring that existed only in the Editor**, so it must
stay that way: if the scene changes structurally, change the builder and re-run it
rather than hand-editing the scene. Two steps remain genuinely interactive and the
script reports them: picking the Model Target's database/target, and
`Add Target Representation` + `Align Coral To Model Target`.

> **⚠️ IF YOU RETRAIN THE MODEL TARGET DATABASE, RE-RUN THE BUILDER.** The builder
> unpacks the coral prefab completely so the wiring serializes into the scene file —
> that is what makes it diffable, and the point of the whole exercise. The cost is
> that the saved scene is a **snapshot with no prefab link**: a retrained
> `coral-rendering` database does **not** propagate into it, and the coral's alignment
> will still be the one fitted to the *old* target representation. That misfit is
> invisible in the editor and only shows up as a drifting overlay on device. Recovery
> is cheap — re-run `Rebuild AR Scene` and redo the two interactive steps — but only
> if you know to, which is precisely the kind of knowledge that vanished with the July
> scene.

> **⚠️ NEVER COMMIT A `.cs` OR `.shader` WITHOUT ITS `.meta`, IN THE SAME COMMIT.**
> The ten new `.cs`/`.shader` files were written outside the Editor and have no
> `.meta` yet; Unity generates them on first import. A source file committed without
> its `.meta` gets a **different GUID on every machine that imports it**, so any scene
> or material referencing it breaks on the next clone or CI run — silently, as a
> missing-script placeholder. Check `git status` for `Assets/CoralPolyps/**/*.meta`
> after Unity has imported, and commit them alongside their source.
>
> Ordering relative to `Rebuild AR Scene` is **irrelevant**: GUIDs are assigned at
> **import**, not at commit, so by the time the builder runs the `.meta` files already
> exist on disk and the scene will reference the same GUIDs either way.

**Coral mesh:** `Assets/CoralPolyps/Coral/astraea_favistella.obj` — 100k tris,
~10 cm real scale, **Read/Write ON**, **Tangents = Calculate**.

**Active material:** `Assets/CoralPolyps/Coral/CoralTissue.mat`, shader
`CoralPolyps/FluorescentTissue`. Textures already assigned + import-configured:
| Slot | Texture (`Coral/textures/`) | Import |
|---|---|---|
| `_AOMap` (occlusion) | `coral_occlusion.jpg` | sRGB **OFF** |
| `_NormalMap` (septa) | `coral_normals.jpg` | Type **Default**, sRGB **OFF** (shader unpacks manually) |
| `_MainTex` (skeleton) | `coral_diffuse.jpg` | sRGB ON |

Current tuned look-dev values (live in the material — read them there, don't reset):
WallColor green `(0.11,1,0.31)`, FloorColor violet `(0.59,0.37,1)`, StressColor warm
`(1,0.55,0.2)`, AOContrast 1.79, ColorSplit 2.38, FloorGlow 0.80, WallGlow 0,
Depth 0.65, ReliefStrength 0.45, EmissionStrength 2.24, SurgeBoost 1.35.
Blend state is **opaque look-dev mode** (`_SrcBlend 1 / _DstBlend 0 / _ZWrite 1`) —
the Phase-4 loupe/transparency is intentionally OFF on this material; the controller
enables it on a runtime instance.

**Post-processing:** a **Global Volume with Bloom** (Threshold ~1, Intensity ~0.6)
exists; **Main Camera has Post Processing ON**. (This is why the glow reads.)

**Bake:** `Assets/CoralPolyps/PolypScatterMap.asset` exists (secondary now — the
tissue approach doesn't need it, but it holds columella positions if ever wanted).

**Phase 4 code (written, NOT yet wired in-scene):** `FluorescentTissue.shader` has
the loupe reveal + AR transparency (`_LoupeOn` keyword, `_LoupeCenter/_LoupeRadius/
_LoupeSoftness`, material-driven blend). `ProximityRevealController` is retargeted to
drive the coral material. Remaining: add the controller to the scene, assign the
coral renderer + camera, and verify in Play mode.

**Phase 5 started:** a Vuforia **Model Target database `coral-rendering`** already
exists at `Assets/Resources/VuforiaModels/coral-rendering/`.

**Shelved — exists but NOT on the active path (don't use, don't delete):**
`Assets/CoralPolyps/Polyp/` (`polyp.obj` + `PolypFluorescent.mat`),
`FluorescentPolyp.shader`, `PolypPool.cs`. These were the discrete-polyp approach.

---

## Completed

### Phase 0 — Unity project scaffolding ✅
- Unity **6000.5.3f1**, URP, AR Foundation + ARKit + Vuforia (11.4.4).
- Scripts split into `Runtime/` + `Editor/` with asmdefs.
- iOS player settings, code-signing, and an **empty scene deployed and running on
  the iPad** (live camera) — the Phase 0 gate.
- Overcame two serious issues: a ShaderGraph bug on 6.4 (fixed by moving to 6.5),
  and a **72-billion shader-variant build crash** caused by no URP pipeline asset
  being assigned (fixed by assigning `Mobile_RPAsset` + trimming features).
- Added a **headless build harness** (`Assets/CoralPolyps/Editor/BuildScript.cs`).

### Phase 1 — Coral mesh ✅
- Smithsonian **_Goniastrea_ (Astraea favistella)** scan, CC0. Converted
  **glTF → OBJ** via Blender (mesh-only, no decimation) — **100k tris**, real-world
  **~10 cm** (verified from the glTF's metre-unit bounds).
- **Read/Write Enabled** on for baking. Raw download preserved in `SourceAssets/`.
- Scan textures (diffuse, occlusion, normal) imported to `Coral/textures/`.

### Phase 2 — Corallite bake ✅
- `CoralliteBaker` detects one outward-facing spot per corallite. Working settings:
  **Concavity 0.06, Min Spacing 0.025**.
- Upgraded the Baker to compute each cup's normal from a **smoothed outward
  average** (the raw single-vertex normal on a noisy scan pointed every which way).
- Output: `PolypScatterMap` asset (100k density validated as sufficient).

### Phase 3 — Fluorescent tissue look ✅ (editor look-dev)
- New shader **`Runtime/FluorescentTissue.shader`** on the coral:
  - **Two-tone fluorescence** — green ridges / cyan floors, split by the occlusion map.
  - **Depth** — occlusion-driven wall emphasis + shadowed recesses + directional relief.
  - **Fine septal detail** — from the scan's normal map (tangent-space).
  - **Stress arc** (0→1): healthy honeycomb → **neon colourful-bleach surge** →
    fluorescence drains to bare skeleton. (Biologically grounded — chromoprotein
    "colourful bleaching" before tissue loss.)
- HDR + a **Bloom** post-process volume set up so the emission actually glows.

---

## To do

### Phase 3 remainder (small)
- [ ] Replace the two fluorescence colours (`_WallColor`, `_FloorColor`) from **real
      _Goniastrea_ fluorescence imagery**; record source/wavelengths (⚠️ honesty rule).
- [ ] Final scrub of the full `_Stress` arc with the neon surge + septal detail.

### Phase 4 — The reveal mechanism (proximity IS zoom) ⭐ CODE DONE
- [x] Added a **loupe reveal + AR transparency** to `FluorescentTissue`: fluorescence
      appears only inside a soft **world-space** sphere (`_LoupeCenter`/`_LoupeRadius`/
      `_LoupeSoftness`), and the tissue alpha-blends over the print — outside the loupe
      AND fully bleached both go transparent so the real coral shows through. A
      `_LOUPE_ON` toggle + **material-driven blend state** keep the Phase-3 look-dev
      material working unchanged (opaque, fully revealed) while the AR path is enabled
      on a runtime instance.
- [x] **Retargeted `ProximityRevealController`** to drive the coral material's loupe
      centre/radius + `_Stress` (via a runtime `.material` instance it auto-configures
      into AR mode) instead of the shelved Pool.
- [x] **Wired in the scene + verified in editor (2026-07-12):** controller on
      `astraea_favistella`, `coralRenderer` = the `default` mesh, `cam` = ARCamera,
      `configureMaterialForAR` on. Verified the full reveal→bleach arc in Play mode via
      `ProximityTestRig.cs` (a throwaway slider that moves the coral toward the camera to
      stand in for proximity): opacity fades in → grows → glow → colourful bleach →
      drains to transparent. ✅
- [x] Added a **Mesh Collider** on the coral (`default`) and assigned it as
      `coralCollider`, so the loupe centres where the viewer looks. _(Still to do: a
      depth-only occlusion mesh so real coral bumps hide glow behind them — Phase 5.)_
- [x] **Verified on device (2026-07-14):** built to iPad; the Vuforia Model Target locks
      the coral to the physical print, and leaning in reveals → bleaches the glow. Test
      rig removed for the device build. The full Phase 4 + Phase 5 loop works end-to-end. ✅

**Implementation notes for the next session:**
- **Shader:** add `_LoupeCenter` (world pos), `_LoupeRadius`, `_LoupeSoftness`.
  Pass world position to the fragment; compute
  `reveal = 1 - smoothstep(_LoupeRadius - _LoupeSoftness, _LoupeRadius, distance(fragWS, _LoupeCenter))`.
  Multiply the emission (and the output alpha) by `reveal`. Switch the SubShader to
  **transparent/additive** blending and output `alpha = reveal * emissionPresence`
  so *outside the loupe* and *fully bleached* both go see-through → the real printed
  coral shows through. (`emissionPresence` already drains via the `_Stress` arc.)
- **Controller:** `ProximityRevealController` already does the raycast → smoothed
  distance → two arcs (reveal + bleach) logic. Only the *outputs* change: instead of
  `pool.SetLoupe()` / `pool.stress`, set the coral renderer's
  `_LoupeCenter`/`_LoupeRadius`/`_Stress` via a `MaterialPropertyBlock` (or
  `Shader.SetGlobalVector/Float`). Keep the threshold ordering
  `bleachFull < revealFull < bleachStart < revealStart`.
- **Coordinates:** loupe centre is a world point (the raycast hit); do the distance
  test in world space in the shader (simplest), or convert to the coral's local
  space — either works as long as it's consistent.

**Phase-4 code review (2026-07 — verify these when wiring up; not blocking):**
1. **Tracking-loss gate may not fire.** `ProximityRevealController` closes the loupe
   on `!_anchor.gameObject.activeInHierarchy`, but Vuforia's `DefaultObserverEventHandler`
   often disables the *Renderer / child content* on target-lost, not the GameObject —
   so this check can miss and the glow hangs in space. **Test on device;** if it
   doesn't close, also check `coralRenderer.enabled`/`isVisible` or hook Vuforia's
   `OnTargetLost`.
2. **Transparency sorting with `ZWrite` off.** AR mode alpha-blends with no depth
   write; on the honeycomb you can see into cups (near rim + far wall both front-face
   and overlap), so cups may sort wrong. Subtle at healthy alpha ~0.9 — **watch on
   device.** Fixes if needed: depth pre-pass, alpha-to-coverage, or switch AR to
   **additive** blend (order-independent, but then the real print always shows through).
3. **Registration precision dependency.** Healthy tissue is a *dark* base + glow that
   *covers* the print (alpha ~0.9), so the virtual coral must register tightly onto the
   physical print or dark tissue spills past its silhouette. Raises the bar on Model
   Target quality (Phase 5).

_The rest reviewed clean: world-space loupe stays correct as tracking moves the coral;
the `.material` runtime-instance approach cleanly isolates AR mode from the saved
look-dev material; the raycast → nearest-point → origin fallback chain is robust._

### Phase 5 — Tracking ✅ WORKING ON DEVICE (needs refinement)
- [x] Built a **Vuforia Model Target** (`coral-rendering` database) from the coral mesh.
- [x] Parented `astraea_favistella` under the `ModelTarget`; aligned it via the
      `AlignCoralWindow` editor tool (matches world render bounds to Vuforia's target
      representation). Tracks and overlays the glow on the physical print on device.
- [ ] **Refine spatial mapping / registration (flagged on device 2026-07-14):** the
      overlay is close but not tight. Nudge the coral's local transform under
      `ModelTarget` against the real overlay; improve guide views / detection if needed.
      (Tight registration matters — dark tissue covers the print; code-review note #3.)
- [ ] Add a **depth-only occlusion mesh** (reuse the target-representation mesh) so real
      coral bumps hide glow behind them. Image-marker fallback at the base (optional).

### Phase 6 — Physical build
- [x] **3D-print the coral at life-size (10 cm)** — DONE (printed 2026-07).
- [ ] Mount on the plinth.

### Phase 7 — Tune on device
> **Superseded in part, 2026-07-27** (no longer the active phase — integration is;
> see the banner at the top). The **magnification** work below stands. The
> **colour/`_Stress` arc** does not: bleaching is the server's now. Kept as the
> record of the 2026-07-14 device feedback and why magnification exists.

**Reveal mechanic redesigned (2026-07-14) after device feedback** — `ProximityRevealController`
+ `ProximityTestRig` rewritten:
- **No reveal fade** — the coral shows at full fluorescence whenever tracked (loupe retired;
  shader `_LOUPE_ON` kept OFF). Far = healthy glowing coral by default.
- **Gradual colour** — the bleach/`_Stress` arc is spread over a WIDE distance range
  (`colorStartDistance`→`colorFullDistance`), with `maxStress` capping the end state
  (1 = drains to the bare print; <1 keeps magnified fluorescence visible).
- **Magnification** — the coral scales up about its own centre as the camera nears
  (`magnifyStart/FullDistance`, `maxMagnification`) — the magnifying-glass zoom.
- `ProximityTestRig` now feeds the controller a MANUAL distance (no moving coral/camera),
  so the feel is tunable in-editor (Play + scrub, watch the Scene view) before building.
- [ ] Tune the colour + magnify ranges/curves in-editor with the test rig, then validate +
      fine-tune on device (each device change needs a fresh Unity→Xcode build — quit Chrome).
      _(FILE_DOCS.md still describes the old loupe reveal — update it once this mechanic is
      validated on device.)_

### Installation integration (`CONTROL_INTEGRATION.md` §11) — CODE DONE, UNVERIFIED

**Steps 1–5 written 2026-07-28. None of it has been run yet** — not in Play mode, not
against the server, not on device. Treat every box below as "compiles-and-reviewed",
not "works".

- [x] **extOSC 1.21.0** via an OpenUPM scoped registry in `Packages/manifest.json`
      (+ `extOSC` in `CoralPolyps.Runtime.asmdef`). Unity resolves it on next focus.
- [x] **`CoralConfig.cs`** — `persistentDataPath/coral-ar.json` → `StreamingAssets/
      coral-ar.json` → built-in defaults, fail-soft at each step; stable per-device
      `client_id` in PlayerPrefs. Default shipped at `Assets/StreamingAssets/coral-ar.json`.
- [x] **`CoralOscListener.cs`** — receiver + transmitter on **one shared socket**
      (`LocalPortMode.FromReceiver`), 5 s hello heartbeat, reconnect + immediate hello
      on `OnApplicationPause(false)`. Holds the last value on silence.
- [x] **`CoralHud.cs`** — §7's status line, colour-coded on broadcast age, three-finger
      tap to toggle, **editable server host** that saves and reconnects live.
- [x] **`BuildScript.AddIosPlistKeys`** — `PostProcessBuild` adds
      `NSLocalNetworkUsageDescription` (without it UDP silently never arrives) plus
      `UIFileSharingEnabled` / `LSSupportsOpeningDocumentsInPlace`.
- [x] **`CoralAppearance.cs`** — §3.1's `(state, intensity)` → `_Stress`, one slew,
      snap-don't-slew on first convergence, emission held at 0 through recovery.
- [x] **`ProximityRevealController.cs` stripped** — the local narrative is **deleted**.
      Keeps distance measurement + magnification; **revives the loupe** (`_LOUPE_ON`,
      `_LoupeCenter`, `_LoupeRadius`) and pushes it to `loupeTargets`. On target-lost it
      resets the transform and shuts the loupe, and **never touches appearance**.
- [x] **`CoralMagnifier.cs`** + `IMagnifierSource` / `PlaceholderMagnifierSource` /
      `VideoMagnifierSource` + **`MagnifierLoupe.shader`** — §3.2's weights, the §3.3
      seam, and a URP-safe in-shader composite (**not** the projection player's
      `OnRenderImage`, which never fires under URP).
- [x] **Footage projects in OBJECT space, triplanar** — not screen space (a first pass
      used screen space; it was wrong). Screen-space sampling maps a given cup to
      different footage texels as the device moves, so the content slides across the
      coral — which breaks `USER_STORIES.md` V4 ("polyps stay registered to the same
      cups from different angles") and inverts the metaphor: with a real magnifying
      glass the content belongs to the object, not the glass. Object space rather than
      mesh UVs because the scan's UVs were authored for the skeleton texture and would
      smear; triplanar because a ~6 cm loupe on a ~10 cm dome turns far enough that one
      plane visibly stretches on the flanks (`_ProjectionSharpness` collapses it back
      toward planar if that reads better). `_FootageScale` is now a tile size in coral
      metres — smaller = more magnified — and remains the tuning knob.
- [x] **Grid registration test** — `"magnifier_source": "grid"` shows one static grid
      with a red and a blue reference cell per repeat, identical across all three
      states. Move the device: the grid must **stick to the coral**, not slide with the
      screen. That is the check that the projection above is actually locked.
- [x] **`SceneBuilder.cs`** — `Window > CoralPolyps > Rebuild AR Scene`.

**Next, in order:**
- [ ] **Commit the generated `.meta` files** alongside their sources the moment Unity
      has imported the new scripts (see the warning above).
- [ ] **Run `Window > CoralPolyps > Rebuild AR Scene`**, then the two interactive Model
      Target steps it reports. Review the resulting `CoralAR.unity` diff — it should be
      readable, which is the point of building it from a script.
- [ ] **§11 step 1 verification** — HUD tracks `tools/fake_client.py` value-for-value
      against `src/server.py` + `fake_rig.py`, hello registration appears in the server
      log, and the entry is pruned when the app backgrounds.
- [ ] **§11 step 2 on device** — Local Network prompt appears and is accepted; sleep/wake
      resyncs. If it responds to `drive_projection.py` but not to the real server, the
      hello is leaving the wrong port.
- [ ] **§11 steps 3+5 look-dev** — hold each state still with
      `drive_projection.py --state 0|1|2|3`; provoke both backward transitions (3→2, 2→1).
      Confirm the tissue and the loupe agree at every moment (§10's open item).
- [ ] **Verify the projection with `"magnifier_source": "grid"`** before tuning
      anything else — if the grid slides, nothing downstream is worth judging.
- [ ] Tune `loupeStart/FullDistance`, `maxLoupeRadius`, `_FootageScale` and
      `_ProjectionSharpness` on device.
- [ ] **§11 step 6** — full arc from the keyboard rig, then the three-device Phase 7
      acceptance test.
- [ ] **§11 step 7** — footage lands: encode to §3.2's contract, flip `magnifier_source`
      to `"video"`, retune nothing.

### Phase 8 — Endurance & exhibition
- [ ] All-day thermal / framerate soak; test the actual room's lighting.

### Phase 9 — Research framing (thesis)
- [ ] Document the stylised-not-scientific framing; cite the Smithsonian scan (CC0),
      the fluorescence imagery used for colours, and which choices are grounded vs.
      exaggerated for legibility.

---

## Key decisions & gotchas (so they aren't re-litigated)

- **URP pipeline asset must stay assigned** (Graphics + all Quality levels) or the
  iOS build hits a shader-variant explosion. Re-check after any Unity upgrade.
- **Unity 6.5 (Tech Stream), not LTS** — chosen to dodge a 6.4 ShaderGraph bug.
- **Coral is 100k tris** (Sketchfab's ceiling; no denser geometry available there).
  Validated as sufficient for the bake. Fallback for more detail: 3d.si.edu original.
- **Baker normal computation was upgraded** to averaged/outward — don't revert.
- The **physical print is the bleached skeleton** — the virtual layer only renders
  fluorescence, which drains to reveal it. This shapes the whole reveal/shader design.
