# BUILD_PLAN.md — From scripts to a working installation

The code pipeline (Baker → ScatterMap → Pool → Shader → ProximityReveal) is
complete. What remains is turning those scripts into a runnable AR app plus the
physical piece. This plan is **dependency-ordered** — each phase unblocks the
next. Every phase ends with a **Done when** gate; don't move on until it's met.

Risk numbers (⚠️#) refer to CLAUDE.md §7.

---

## Phase 0 — Unity project scaffolding

Get a project that compiles with the AR stack before touching content.

- [ ] Create / confirm a Unity project on **URP**.
- [ ] Install packages: **AR Foundation**, **ARKit XR Plugin** (iOS), **Vuforia**.
- [ ] Organise the scripts to match the docs and keep the editor tool out of
      device builds:
      - `Runtime/` → `PolypPool.cs`, `PolypScatterMap.cs`,
        `FluorescentPolyp.shader`, `ProximityRevealController.cs`
      - `Editor/` → `CoralliteBaker.cs`
      - Add an asmdef per folder (Editor asmdef references the Runtime one and is
        Editor-platform only). Currently the files sit flat in the repo root.
- [ ] iOS build settings: bundle ID, camera usage description, min iOS version,
      ARKit capability.

**Done when:** the project compiles with no errors and builds an empty scene to
an iPad.

---

## Phase 1 — Bring in the coral mesh

- [ ] Import the **Smithsonian _Goniastrea_ scan** (CC0). This is the anchor,
      the print, AND the bake source — all the same coral (⚠️ the map==anchor rule).
- [ ] Set **real-world scale** (life-size, as it will print). Everything
      downstream — loupe radius, distance thresholds — depends on this being right.
- [ ] Keep a **high-density** copy for baking (the Baker needs fine geometry to
      detect cups). You can decimate a *separate* copy later for device rendering,
      but bake from the dense one.

**Done when:** the coral is in the scene at life-size, dense enough that the cups
are visibly resolved in the mesh.

---

## Phase 2 — Bake placement ⚠️#1 (make-or-break)

This is the highest-risk step. Everything visible downstream depends on it.

- [ ] `Window > CoralPolyps > Corallite Baker`, drag in the dense mesh, **Detect & Bake**.
- [ ] Tune in this order (per FILE_DOCS): **Concavity Threshold** → **Min Spacing**
      → mask sliders (Underside Reject, Base Cutoff).
- [ ] **Verify via gizmos** (select the Pool object): one sphere per cup,
      centred in the cup, normal rays pointing outward, nothing on the base or
      underside. This visual check is the gate — trust nothing downstream until
      it passes.
- [ ] If cups are too shallow to detect, the fix is a **better/denser mesh**, not
      more slider-fiddling.

**Done when:** exactly one correctly-centred, outward-facing spot per real
corallite, saved as a `PolypScatterMap` asset.

---

## Phase 3 — Polyp prefab, colour, and bloom

- [ ] Model or source **polyp geometry** (a small tentacle crown / dome). Keep it
      low-poly — hundreds may be active at once.
- [ ] Create a **material** from `FluorescentPolyp.shader`; put it on the prefab.
- [ ] Replace `_HealthyColor` (and check `_StressColor`) using **real fluorescence
      imagery of Goniastrea** or a close relative — NOT the daylight Diploastrea
      scan (⚠️#3). Note the wavelengths/source for the thesis.
- [ ] Add a **Bloom** override to a post-process Volume — HDR emission won't read
      as fluorescence without it.
- [ ] Scrub `_Stress` 0 → 1 on a single polyp and confirm the arc reads:
      healthy cyan-green → colourful surge → drains to white.

**Done when:** one polyp on screen glows convincingly and bleaches believably as
you drag `_Stress`.

---

## Phase 4 — Tracking + runtime placement

- [ ] Build a **Vuforia Model Target** from the print mesh.
- [ ] Add an **image marker at the base** as fallback tracking (per CLAUDE.md §2).
- [ ] Add `PolypPool` as a **child of the Model Target**; assign the baked `map`
      and the polyp `prefab`; set `poolSize` (start ~300, ⚠️#5 thermals).
- [ ] Sanity-test placement in-editor: temporarily call `SetLoupe()` with a hand-
      picked centre/radius (or a tiny test script) and confirm pooled polyps drop
      into the right cups, facing out, at sensible size.

**Done when:** opening a loupe on the tracked coral fills the looked-at cups with
correctly-placed polyps.

---

## Phase 5 — Occlusion depth mesh

- [ ] Duplicate the coral into an **invisible depth-only mesh** (write depth,
      `ColorMask 0`, render before the polyps) so real branches hide polyps behind
      them.
- [ ] Give it a **collider** and assign it as `coralCollider` on the
      ProximityRevealController — this both occludes AND lets the loupe centre on
      exactly where the viewer looks.

**Done when:** polyps behind coral geometry are correctly hidden, and the loupe
centres under the camera's aim rather than the anchor origin.

---

## Phase 6 — Wire proximity and tune the beats

- [ ] Add `ProximityRevealController`; assign `pool`, leave `cam` empty
      (uses `Camera.main`), assign `coralCollider` from Phase 5.
- [ ] Tune the four distance thresholds **on device** against the physical print
      (⚠️#4 — tracking is at its limit this close):
      `bleachFull < revealFull < bleachStart < revealStart` (the `OnValidate`
      guard warns if you cross them).
- [ ] Tune `distanceSmoothTime` and the reveal/bleach curves so leaning in feels
      like a smooth magnifying-glass zoom, not a pop.

**Done when:** leaning the iPad in reveals healthy polyps, then bleaches them as
you get closer — smoothly, end-to-end, on the real object.

---

## Phase 7 — Physical build & endurance

- [ ] **3D print** the coral life-size; mount on the plinth.
- [ ] Test **tracking stability at closest (loupe) range** on the print (⚠️#4).
- [ ] **All-day thermal / framerate** soak test; adjust `poolSize` down if the
      iPad throttles (⚠️#5).
- [ ] Test lighting conditions of the actual exhibition space.

**Done when:** the installation runs stably for a full exhibition session without
overheating or losing track.

---

## Phase 8 — Research framing (thesis)

- [ ] Document the **stylised-not-scientific** framing honestly (⚠️#2) — it's a
      defensible impression, not a photograph of science.
- [ ] Cite sources: Smithsonian Goniastrea (CC0), Diploastrea colour ref
      (CC-BY, "QQman"), and the fluorescence imagery used for `_HealthyColor`.
- [ ] Record which design choices are grounded in the real animal vs. exaggerated
      for legibility (e.g. `Global Scale > 1`).

**Done when:** the design decisions are traceable to their sources in the write-up.

---

## Optional code polish (not blocking)

- [ ] Move scripts into `Editor/` + `Runtime/` with asmdefs (folded into Phase 0).
- [ ] Cache each pooled polyp's `Renderer` instead of
      `GetComponentInChildren<Renderer>()` every frame in `PolypPool.LateUpdate`.
- [ ] If you want polyps to bleach at *slightly different rates* (the shader
      supports it per-instance), have the Pool offset each polyp's `_Stress` by a
      small amount derived from `randomSeed` rather than pushing one global value.

---

## Critical-path summary

Phase 2 (the bake) is the gate everything hangs on — do it early and verify it
hard. Phases 3–6 can partly overlap once the bake is trusted. Phase 7 needs the
physical print, so start that print running as soon as the mesh scale (Phase 1)
is locked.
