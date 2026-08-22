# USER_STORIES.md — Coral Polyp AR

User stories across the people this project serves: the **Visitor** at the
installation, the **Curator/Operator** running it, the **Developer** building
it, and the **Researcher** answering to it in the thesis.

Format: *As a `<role>`, I want `<goal>`, so that `<benefit>`.* Each story has
**acceptance criteria** (AC) that are meant to be checkable. IDs are for
traceability; risk tags (⚠️#) and phase tags (P#) map to CLAUDE.md §7 and
`docs/BUILD_PLAN.md`.

---

## Visitor — the person holding the iPad

The core experience. This is the piece.

### V1 — Proximity is the zoom
**As a** visitor, **I want** leaning the iPad closer to the coral to take me
deeper into its hidden world, **so that** exploring feels like using a
magnifying glass, not operating a UI.
- AC: No on-screen buttons, sliders, or menus drive the reveal — only distance does.
- AC: Moving closer reveals *more*; moving away reveals *less*, reversibly.
- AC: The mapping is smooth — no pop or flicker as I move (⚠️#4, P6).

### V2 — Discovering the polyps
**As a** visitor, **I want** tiny glowing polyps to appear on the coral as I get
close, **so that** I discover the living animals that build it.
- AC: At normal viewing distance the coral looks like bare printed skeleton.
- AC: As I lean in, polyps fade/emerge in the region I'm looking at.
- AC: Each polyp sits **in a real cup** on the physical coral — none float over
  flat skeleton (⚠️#1, P2).
- AC: Polyps glow from within (fluorescence), not like a lit plastic surface.

### V3 — The bleaching narrative
**As a** visitor, **I want** the polyps to sicken and bleach white **as the reef
warms**, **so that** I feel the healthy-to-dying story of a warming reef.
- AC: The coral's condition follows the **installation state**, never my distance
  from it — two visitors standing at different distances see the same condition
  at the same moment (`docs/CONTROL_INTEGRATION.md` §3).
- AC: The arc reads healthy → colourful surge → drained to white, driven by
  `/coral/cue` + `/coral/intensity` (Shader `_Stress`).
- AC: The switch to bleached lands on the same beat as the soundscape, the lamp and
  the projected reef — all four read the bar-quantised `/coral/cue` from one
  datagram (`docs/CONTROL_INTEGRATION.md` §2).
- AC: Backing away does **not** heal it. Bleaching latches on the server and heals
  only on the server's schedule — the cool button is not an undo.
- AC: The magnifier's polyps match: alive when the reef is alive, fluorescent when
  it fluoresces, still when it is bleached (§3.2).

### V4 — It tracks the real object
**As a** visitor, **I want** the polyps to stay locked to the physical coral as I
move around it, **so that** the illusion holds and the phone feels like a lens
onto the real thing.
- AC: Polyps stay registered to the same cups from different angles.
- AC: Brief tracking loss hides the polyps cleanly rather than showing them
  floating in space (`closeOnTrackingLoss`).
- AC: Polyps behind real coral geometry are hidden, not drawn on top (P5).

### V5 — No instructions needed
**As a** first-time visitor, **I want** to understand what to do within seconds,
**so that** I don't need a staff member or a sign to start.
- AC: A visitor with no briefing leans in and gets a reaction within ~5 seconds.
- AC: Nothing breaks if I move erratically, cover the camera, or hold it too close.

---

## Curator / Operator — running it in the space

### C1 — All-day reliability
**As an** operator, **I want** the installation to run a full exhibition day
without overheating or crashing, **so that** it's up whenever a visitor arrives.
- AC: Sustained use doesn't thermally throttle the iPad into stutter (⚠️#5, P7).
- AC: Active polyp count stays bounded (the Pool's `poolSize`) regardless of how
  much a visitor sweeps around.

### C2 — Easy recovery
**As an** operator, **I want** to reset or relaunch the piece quickly, **so that**
a glitch doesn't take the exhibit down for long.
- AC: Documented start-up and recovery steps.
- AC: On tracking loss, it self-recovers when the coral is in view again — no restart.

### C3 — Works in the room's lighting
**As an** operator, **I want** tracking and the glow to hold under the actual
exhibition lighting, **so that** it looks right where it's installed, not just in
the lab.
- AC: Validated under the venue's lighting; bloom/emission still reads as
  fluorescence, not wash-out (P7).

---

## Developer — building and tuning

### D1 — Placement from the coral itself
**As a** developer, **I want** polyp positions detected from the scan's cups
rather than hand-placed, **so that** placement is grounded in the real geometry
and scales to thousands of cups.
- AC: The Baker outputs one vetted spot per cup with position, outward normal,
  scale, and confidence (P2).
- AC: Gizmos let me verify centring **before** trusting anything downstream (⚠️#1).

### D2 — Map and anchor are the same coral
**As a** developer, **I want** the placement map and the printed/tracked mesh to
be the identical coral, **so that** no polyp ever lands where there's no physical cup.
- AC: `PolypScatterMap.sourceMeshName` matches the tracked/printed mesh.
- AC: The Diploastrea scan is used for colour reference only, never as anchor or
  polyp source (CLAUDE.md §3).

### D3 — One dial for the whole sickness state
**As a** developer, **I want** a single `_Stress` value to drive the entire
healthy→bleached look, **so that** placement and appearance never fall out of sync.
- AC: Setting `stress` on the Pool visibly moves every active polyp along the arc.
- AC: No second, separate control has to be kept in step by hand.

### D4 — Tunable proximity beats
**As a** developer, **I want** to tune where the reveal and magnification trigger
without editing code, **so that** I can dial the experience on-device against the
print.
- AC: Reveal/magnification distances, curves, and smoothing are inspector fields (P6).
- AC: Appearance timings (`crossfade_s`, `fluor_point`) are **config-file** fields,
  not inspector fields — they must be changeable on an exhibition device without a
  rebuild (`docs/CONTROL_INTEGRATION.md` §5).
- AC: Misconfigured thresholds warn me (`OnValidate`) instead of failing silently.
- AC: Editor gizmos show where each beat triggers relative to the camera.

### D5 — Thermal safety by design
**As a** developer, **I want** a fixed pool of reused polyp instances, **so that**
close-range exploration can't spawn unbounded objects and cook the device.
- AC: Never more than `poolSize` active polyps, however wide the loupe sweeps (⚠️#5).

### D6 — Editor tools stay out of device builds
**As a** developer, **I want** the Baker excluded from player builds, **so that**
editor-only code never ships to the iPad.
- AC: Baker is `#if UNITY_EDITOR` / in an `Editor` assembly and the device build
  compiles without it (P0).

---

## Researcher — the thesis / honest representation

### R1 — Honest stylisation
**As a** researcher, **I want** the piece framed as a defensible stylised
impression, **so that** it isn't mistaken for a photograph of science (⚠️#2).
- AC: The write-up states plainly it's an impression, not a captured image.
- AC: Exaggerations (e.g. `Global Scale > 1` for legibility) are named as such.

### R2 — Colours grounded in real fluorescence
**As a** researcher, **I want** polyp colours sourced from real fluorescence
imagery of the species, **so that** the glow tracks real wavelengths rather than
the daylight scan's reflective pigment (⚠️#3, P3).
- AC: `_HealthyColor`/`_StressColor` trace to cited fluorescence sources, not the
  Diploastrea daylight scan.

### R3 — Traceable sources and licences
**As a** researcher, **I want** every asset's source and licence recorded, **so
that** the work is reproducible and properly credited.
- AC: Smithsonian Goniastrea (CC0), Diploastrea colour ref (CC-BY, "QQman"), and
  fluorescence imagery are all cited (P8).

### R4 — Defensible bleaching behaviour
**As a** researcher, **I want** the bleaching arc to reflect the real
photoprotective-pigment surge before bleaching, **so that** the mid-arc colour
burst is grounded, not invented.
- AC: The 0.4–0.7 colourful-bleach stage is documented against the real response
  it mirrors.

---

## Out of scope (for now)

Recording these so they're a deliberate "not yet," not an oversight:
- Multi-visitor / multiple iPads on one coral simultaneously.
- Audio / narration layer.
- Real photographic imagery layered over the stylised polyps (noted as a possible
  later layer in ⚠️#2).
- Analytics on visitor behaviour.
