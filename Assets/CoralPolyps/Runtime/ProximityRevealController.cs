using UnityEngine;
using UnityEngine.Rendering;

namespace CoralPolyps
{
    /// <summary>
    /// Stage 4 — proximity drives the REVEAL and the MAGNIFICATION, and nothing else
    /// (CONTROL_INTEGRATION.md §4).
    ///
    /// This class used to implement the whole narrative locally: approach → peak
    /// fluorescence → arm bleach → retreat bleaches → reset. Under the installation
    /// architecture that is a direct violation — the orchestration server owns the
    /// healthy→bleached arc, and a visitor changes the coral by pressing a button at
    /// the water bath, not by moving the iPad. That state machine is gone; _Stress
    /// now comes off the wire via CoralAppearance.
    ///
    /// What proximity still owns:
    ///   - MAGNIFICATION — the coral scales up about the surface you are inspecting
    ///     as the camera nears. The magnifying-glass zoom of the piece.
    ///   - THE LOUPE — a soft world-space sphere that opens as you lean in, marking
    ///     the region where the magnifier's polyp footage is revealed. It controls
    ///     how much of the micro-scale you see, never what condition it is in.
    ///
    /// Both arcs run far→near, each with its own distance range and curve. Distances
    /// are WORLD metres, measured from the camera to the coral's tracked bounds
    /// (stable — unaffected by the magnification).
    ///
    /// Registration note: at 1× (far) the coral sits 1:1 on the print; as it magnifies
    /// it intentionally grows past it. Tighten registration against the FAR/base state.
    /// </summary>
    public class ProximityRevealController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The coral mesh renderer. Its bounds give the tracked centre used for every " +
                 "distance measurement. Required.")]
        public Renderer coralRenderer;

        [Tooltip("The AR camera. Defaults to Camera.main if empty.")]
        public Camera cam;

        [Tooltip("Transform scaled/moved for magnification. Defaults to the coral renderer's own " +
                 "transform so the aligned parent (ModelTarget child) is left untouched.")]
        public Transform coralRoot;

        [Tooltip("Optional collider on the coral. With one assigned the loupe centres where the " +
                 "viewer is actually looking (a ray through the screen centre); without one it " +
                 "falls back to the nearest point on the coral's bounds.")]
        public Collider coralCollider;

        // WARNING: magnification INHERENTLY breaks registration. Scaling the coral moves its
        // surface off the print, and the anchor is camera-relative, so the coral shifts as the
        // viewer moves. Keep maxMagnification at 1 unless you deliberately want that trade, and
        // keep the range very close so 1:1 registration holds at normal viewing distances.
        [Header("Magnification arc (metres from coral surface)")]
        [Tooltip("At or beyond this distance the coral is life-size (1x, registered to the print).")]
        public float magnifyStartDistance = 0.06f;

        [Tooltip("At or within this distance the coral reaches maxMagnification. Must be < start.")]
        public float magnifyFullDistance = 0.02f;

        [Tooltip("Scale multiplier at closest range. 1 = OFF (keeps registration exact). " +
                 "Anything above 1 deliberately slides the coral off the print as you move.")]
        public float maxMagnification = 1.0f;

        [Tooltip("Shapes proximity (0 at start, 1 at full) -> magnification.")]
        public AnimationCurve magnifyCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public enum MagnifyAnchor { CoralCenter, FrontSurface }

        [Tooltip("Where the zoom scales FROM. FrontSurface keeps the cups you're inspecting " +
                 "as the stable focal point (less 'cups racing ahead of the edges'); CoralCenter " +
                 "grows evenly about the middle but the near face bulges toward the camera.")]
        public MagnifyAnchor magnifyAnchor = MagnifyAnchor.FrontSurface;

        [Header("Loupe reveal arc (metres from coral surface)")]
        [Tooltip("Renderers whose material carries the loupe properties — the magnifier layer. " +
                 "_LOUPE_ON is enabled on a runtime instance of each and its centre/radius driven " +
                 "here. Leave empty to compute the loupe without pushing it anywhere (the values " +
                 "are still exposed as LoupeCenter / LoupeRadius).")]
        public Renderer[] loupeTargets = new Renderer[0];

        [Tooltip("At or beyond this distance the loupe is shut (radius 0) — no polyp footage, " +
                 "just the coral. Leaning in is what opens the window.")]
        public float loupeStartDistance = 0.30f;

        [Tooltip("At or within this distance the loupe reaches maxLoupeRadius. Must be < start.")]
        public float loupeFullDistance = 0.08f;

        [Tooltip("Loupe radius at closest range, in world metres. The coral is ~10 cm across, so " +
                 "values near 0.03 read as a window onto part of it rather than the whole surface.")]
        public float maxLoupeRadius = 0.03f;

        [Tooltip("Shapes proximity (0 at start, 1 at full) -> loupe radius.")]
        public AnimationCurve loupeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // The third and last arc. The loupe can only ever paint the coral's own surface —
        // it is a projection onto a duplicate of the coral mesh — so a reveal that grows
        // past the silhouette is impossible there by construction. This arc hands the
        // footage to FullscreenMagnifier, which is not bound to any geometry.
        //
        // It deliberately starts INSIDE loupeFullDistance: the loupe finishes opening
        // first, then the takeover lifts it off the object. Two beats, not a dissolve
        // between two things fighting for the same moment.
        //
        // A useful side effect: Vuforia loses a ~10 cm Model Target somewhere around
        // 5 cm, and the takeover is opaque by then — so the tracking failure happens
        // behind a full screen of footage and is never seen.
        [Header("Fullscreen takeover arc (metres from coral surface)")]
        [Tooltip("At or beyond this distance the footage is entirely on the coral. Should sit " +
                 "at or inside loupeFullDistance so the loupe finishes opening first.")]
        public float fullscreenStartDistance = 0.07f;

        [Tooltip("At or within this distance the footage fills the screen. Must be < start.")]
        public float fullscreenFullDistance = 0.03f;

        [Tooltip("Shapes proximity (0 at start, 1 at full) -> screen coverage.")]
        public AnimationCurve fullscreenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Takeover latch (survives tracking loss)")]
        [Tooltip("Once FullscreenReveal is at least this, losing the target HOLDS the takeover " +
                 "instead of collapsing it. Vuforia drops a 10 cm target around 5 cm, which is " +
                 "exactly where the iris should be total — without this the polyps strobe at " +
                 "the closest range. Below it, tracking loss closes normally.")]
        [Range(0f, 1f)] public float takeoverLatchThreshold = 0.6f;

        [Tooltip("How long to hold the frozen takeover with no re-acquire before releasing. " +
                 "Covers leaning in past tracking range; long enough that a visitor exploring " +
                 "up close is never interrupted, short enough that walking away recovers.")]
        public float takeoverHoldMaxS = 8f;

        [Tooltip("Seconds to fade the held takeover out once the hold expires.")]
        public float takeoverReleaseS = 1.0f;

        [Header("Pose sanity (Vuforia reports 'found' with garbage transforms up close)")]
        [Tooltip("Distance beyond which a reported pose is not believed, in metres.")]
        public float implausibleFarM = 3.0f;

        [Tooltip("Largest believable change in measured distance between two frames, in metres. " +
                 "A hand cannot move this fast; a lost pose can.")]
        public float implausibleJumpM = 0.15f;

        [Tooltip("How long the pose must stay believable before the takeover trusts it and " +
                 "starts easing out. Vuforia reports a plausible pose before the mesh is " +
                 "actually locked back onto the print, so releasing on the first good frame " +
                 "uncovers a coral that is still floating.")]
        public float poseRecoverS = 0.4f;

        // ----------------------------------------------------- state machine (Phase 1)
        //
        // From the black-box evaluation: the observed behaviour was ~45 unintended state
        // changes in ten seconds at close range. The continuous reveal below is unchanged;
        // what this adds is an explicit MESO -> BLENDING -> MICRO -> RETREAT machine whose
        // only outputs are GATES — a transition must now be EARNED with sustained evidence,
        // while mid-blend motion stays 1:1 with the hand. Acceptance: a 60 s close hold
        // produces ZERO unintended transitions (read them off the HUD or the log).
        public enum MagState { Meso, Blending, Micro, Retreat }

        [Header("Magnification state machine (gates only — visuals unchanged)")]
        [Tooltip("d must stay under fullscreenStartDistance for this long before BLENDING may " +
                 "begin. One close frame — real or a pose spike — no longer starts anything.")]
        public float enterDwellS = 0.25f;

        [Tooltip("Hysteresis: leaving MICRO requires d beyond THIS distance, not merely beyond " +
                 "fullscreenStartDistance. Must exceed it — the gap is what kills oscillation " +
                 "at the boundary.")]
        public float microExitDistance = 0.12f;

        [Tooltip("d must stay beyond microExitDistance CONTINUOUSLY for this long to leave " +
                 "MICRO. Any dip resets the timer to zero — a noisy frame cannot un-commit " +
                 "the view, and neither can tracking loss (a lost target accrues nothing).")]
        public float exitDwellS = 1.0f;

        [Tooltip("Minimum seconds in any state before the next transition is allowed.")]
        public float minStateS = 1.0f;

        [Tooltip("After a completed exit (RETREAT reaching MESO), re-entry into MICRO is " +
                 "blocked this long. BLENDING stays available throughout, so the reveal still " +
                 "tracks the hand 1:1 — only the full-cover commit is debounced.")]
        public float refractoryS = 1.5f;

        [Tooltip("Log every state change with d and m — this is the flicker evidence for the " +
                 "acceptance test, and thesis data.")]
        public bool logStateChanges = true;

        [Header("Re-lock confidence — the exit door out of MICRO")]
        // Leaving the footage is the single worst moment to be wrong: uncovering onto a
        // meso mesh that is floating off the print breaks the entire magnifying-glass
        // fiction in one frame. So the distance vote alone no longer opens the door —
        // the footage HOLDS at full cover until the pose is genuinely re-locked:
        //   1. Vuforia itself reports TRACKED (not EXTENDED_TRACKED, which is dead
        //      reckoning — the mesh can sit centimetres off the print under it), and
        //   2. the measured distance has been steady, frame over frame, for a sustained
        //      window. A hand retreating moves ~1-2 mm/frame; a re-solving pose jumps
        //      far more. Steadiness is what separates "locked on" from "still hunting".
        [Tooltip("The measured pose must be continuously plausible AND steady for this long " +
                 "before the meso layer is trusted to be sitting on the print again.")]
        public float relockStableS = 0.75f;

        [Tooltip("Frame-to-frame change in measured distance above this (metres) counts as " +
                 "the pose still hunting, and resets the steadiness clock. 0.008 tolerates " +
                 "any human retreat speed while rejecting re-solve jumps.")]
        public float relockJitterM = 0.008f;

        [Tooltip("Fail-soft: if the exit is voted but a lock never arrives within this long, " +
                 "retreat anyway — a visitor must never be trapped inside the footage by a " +
                 "target that refuses to re-acquire.")]
        public float relockMaxWaitS = 3f;

        [Tooltip("The takeover layer, asked whether the screen is ACTUALLY covered before " +
                 "the coral is ever hidden. Found in the scene if left empty; without it " +
                 "the controller falls back to the distance-derived reveal, which " +
                 "over-reports coverage whenever the coral does not fill the frame.")]
        public FullscreenMagnifier fullscreen;

        [Header("Distance measurement")]
        [Tooltip("Measure to the coral's SURFACE rather than its hidden bounds centre, so the " +
                 "distances above mean the real gap between device and coral. With this off, a " +
                 "13 cm coral puts ~6.5 cm of itself between surface and centre, making every " +
                 "threshold feel far closer than the number suggests.")]
        public bool measureFromSurface = true;

        [Header("Smoothing")]
        [Tooltip("LEGACY — no longer used. Distance is filtered by the One Euro parameters " +
                 "below, which replaced SmoothDamp: one constant cannot be both steady at " +
                 "rest and responsive in motion. Kept only so the serialised value in the " +
                 "scene does not resolve as a missing field.")]
        [Range(0f, 1f)] public float distanceSmoothTime = 0.12f;

        [Header("Distance filter (One Euro — adaptive)")]
        [Tooltip("Cutoff in Hz at zero speed. LOWER = steadier when the phone is still, at " +
                 "the cost of a little lag starting a move. 1.0 is a good middle.")]
        public float euroMinCutoff = 1.0f;

        [Tooltip("Speed coefficient. HIGHER = follows fast movement more tightly (less lag). " +
                 "This is the knob for 'it feels delayed' — raise it.")]
        public float euroBeta = 1.5f;

        [Tooltip("Cutoff for the speed estimate itself. Rarely needs changing.")]
        public float euroDerivCutoff = 1.0f;

        [Header("Material")]
        [Tooltip("On Start, put the coral's runtime material instance into AR mode: fully OPAQUE " +
                 "with depth write (100% opacity + correct cup/wall sorting) and the loupe keyword " +
                 "OFF, so the tissue covers the whole coral. The tissue's condition is the server's " +
                 "business; the loupe belongs to the magnifier layer, not to this material.")]
        public bool configureMaterialForAR = true;

        [Header("Testing (editor)")]
        [Tooltip("Bypass camera measurement and use manualDistance instead. Driven by ProximityTestRig " +
                 "so you can tune the feel in the editor without a device.")]
        public bool useManualDistance = false;
        public float manualDistance = 1.0f;

        /// <summary>Smoothed camera-to-coral distance in metres, or Infinity while untracked.</summary>
        public float Distance => _smoothedDistance;

        /// <summary>Current loupe centre in WORLD space (the point the viewer is peering at).</summary>
        public Vector3 LoupeCenter { get; private set; }

        /// <summary>Current loupe radius in world metres; 0 means shut.</summary>
        public float LoupeRadius { get; private set; }

        /// <summary>
        /// 0 = footage lives entirely on the coral, 1 = footage fills the screen.
        /// Read by FullscreenMagnifier. Zeroed on tracking loss along with the loupe,
        /// so losing the target never strands the viewer inside an opaque takeover.
        /// </summary>
        public float FullscreenReveal { get; private set; }

        /// <summary>
        /// True while the takeover is being held open across a tracking dropout. The
        /// fullscreen layer freezes its iris centre and radius when this is set: the
        /// coral's transform is stale (or hidden) under tracking loss, so re-projecting
        /// from it would swing the iris around the screen for exactly as long as the
        /// dropout lasts — which is the flicker, arriving by a different route.
        /// </summary>
        public bool TakeoverHeld { get; private set; }

        float _untrackedHold;
        float _poseGoodFor;
        float _lastGoodRaw = -1f;
        float _prevRawForLock = float.PositiveInfinity;
        Vuforia.ObserverBehaviour _observer;
        readonly OneEuro _euro = new OneEuro();

        // Last pose the coral was seen at while confidence was high. Re-applied while
        // confidence is low, so the mesh holds still instead of chasing bad solves.
        Vector3 _confidentPos;
        Quaternion _confidentRot;
        bool _haveConfidentPose;

        /// <summary>
        /// One Euro filter (Casiez et al. 2012). A first-order low-pass whose cutoff rises
        /// with the signal's own speed: steady at rest, responsive in motion. Small enough
        /// to keep here rather than pull in a dependency.
        /// </summary>
        sealed class OneEuro
        {
            float _x, _dx;
            bool _primed;

            public void Reset(float x) { _x = x; _dx = 0f; _primed = true; }

            static float Alpha(float cutoff, float dt)
            {
                float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-4f));
                return 1f / (1f + tau / Mathf.Max(dt, 1e-5f));
            }

            public float Filter(float x, float dt, float minCutoff, float beta, float dCutoff)
            {
                if (!_primed || dt <= 0f) { Reset(x); return x; }

                float dxRaw = (x - _x) / Mathf.Max(dt, 1e-5f);
                _dx = Mathf.Lerp(_dx, dxRaw, Alpha(dCutoff, dt));

                // The adaptive part: faster movement -> higher cutoff -> less smoothing.
                float cutoff = minCutoff + beta * Mathf.Abs(_dx);
                _x = Mathf.Lerp(_x, x, Alpha(cutoff, dt));
                return _x;
            }
        }
        bool _takeoverLatched;
        bool _coralHidden;
        bool _loupeHeld { get => TakeoverHeld; set => TakeoverHeld = value; }

        /// <summary>Current magnification factor (1 = life-size).</summary>
        public float Magnification { get; private set; } = 1f;

        // Diagnostics for CoralHud. The black-box evaluation was made from screen
        // recordings, so the fix must be verifiable from the screen: every number the
        // acceptance test needs is exposed here rather than buried in private state.
        public MagState State { get; private set; } = MagState.Meso;
        public float StateAgeS { get; private set; }
        public float EnterDwellProgressS { get; private set; }
        public float ExitDwellProgressS { get; private set; }
        public float MicroRefractoryS { get; private set; }
        public float RawDistanceM { get; private set; } = float.PositiveInfinity;
        public float SmoothedDistanceM => _smoothedDistance;
        public float PoseTrustS => _poseGoodFor;
        public bool LastPosePlausible { get; private set; }

        /// <summary>True when Vuforia reports genuinely TRACKED (not extended/dead-reckoned).
        /// Defaults true when no observer is found, so the stability clock alone governs.</summary>
        public bool VuforiaTracked { get; private set; } = true;

        /// <summary>Seconds the measured pose has been continuously plausible and steady.</summary>
        public float LockStableS { get; private set; }

        /// <summary>Both conditions met: Vuforia TRACKED and steady for relockStableS.</summary>
        public bool PoseLocked { get; private set; }

        /// <summary>Seconds spent holding in MICRO after the exit vote, waiting for a lock.</summary>
        public float RelockWaitS { get; private set; }

        // --- Shader property IDs ---
        private static readonly int LoupeCenterID = Shader.PropertyToID("_LoupeCenter");
        private static readonly int LoupeRadiusID = Shader.PropertyToID("_LoupeRadius");
        private static readonly int SrcBlendID = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendID = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteID   = Shader.PropertyToID("_ZWrite");
        private const string LoupeKeyword = "_LOUPE_ON";

        // --- Runtime state ---
        private Transform _root;
        private Vector3 _baseLocalPos;
        private Vector3 _baseLocalScale;
        private bool _haveBase;
        private float _smoothedDistance = Mathf.Infinity;
        private float _distVel;
        private Material[] _loupeMats;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (fullscreen == null) fullscreen = FindAnyObjectByType<FullscreenMagnifier>();
        }

        /// <summary>
        /// Is every pixel genuinely footage right now? Asks the layer that actually drew
        /// it. The distance-derived reveal is NOT an answer to this question — it ignores
        /// the silhouette clamp, so it reads 1.0 while the rim is still transparent.
        /// </summary>
        private bool ScreenIsCovered =>
            fullscreen != null ? fullscreen.ScreenFullyCovered : FullscreenReveal > 0.995f;

        private void Start()
        {
            if (coralRenderer == null) return;
            _root = coralRoot != null ? coralRoot : coralRenderer.transform;
            _baseLocalPos = _root.localPosition;
            _baseLocalScale = _root.localScale;
            _haveBase = true;

            // Subscribe to the tracker's own verdict. The coral hangs under the Model
            // Target, so its ObserverBehaviour is in the parents. EXTENDED_TRACKED is
            // deliberately NOT counted as tracked: it is dead reckoning, and the mesh can
            // sit centimetres off the print under it — the exact state the MICRO exit
            // must not trust. No observer found (editor Play mode, manual rigs) leaves
            // VuforiaTracked true and the steadiness clock solely in charge.
            _observer = coralRenderer.GetComponentInParent<Vuforia.ObserverBehaviour>();
            if (_observer != null) _observer.OnTargetStatusChanged += OnVuforiaStatus;

            if (configureMaterialForAR)
            {
                // Per-renderer instance (auto-freed; never dirties the asset). The same
                // instance CoralAppearance drives — Renderer.material caches it.
                var mat = coralRenderer.material;
                if (mat != null)
                {
                    mat.DisableKeyword(LoupeKeyword);                 // tissue covers the whole coral
                    mat.SetFloat(SrcBlendID, (float)BlendMode.One);   // OPAQUE: no see-through, and...
                    mat.SetFloat(DstBlendID, (float)BlendMode.Zero);
                    mat.SetFloat(ZWriteID, 1f);                       // ...depth write -> cups/walls sort correctly
                    mat.renderQueue = (int)RenderQueue.Geometry;
                }
            }

            // The magnifier's materials are the ones that DO want the loupe.
            if (loupeTargets == null) loupeTargets = new Renderer[0];
            _loupeMats = new Material[loupeTargets.Length];
            for (int i = 0; i < loupeTargets.Length; i++)
            {
                if (loupeTargets[i] == null) continue;
                _loupeMats[i] = loupeTargets[i].material;
                _loupeMats[i].EnableKeyword(LoupeKeyword);
            }

            CloseLoupe();
        }

        private void LateUpdate()
        {
            if (coralRenderer == null || cam == null || !_haveBase) return;

            // Tracking-loss gate: Vuforia's DefaultObserverEventHandler hides the coral by
            // disabling the renderer (or the GameObject) on target-lost. Reset the TRANSFORM
            // and shut the loupe so nothing hangs in space — but never touch the appearance.
            // _Stress is the server's, and the coral must still be showing the correct state
            // when tracking re-acquires a second later.
            // With magnification off the controller must NEVER touch the coral's transform —
            // that keeps registration purely Vuforia's, which is what we want by default.
            bool magnifyEnabled = maxMagnification > 1.0001f;

            if (!useManualDistance &&
                (!coralRenderer.enabled || !coralRenderer.gameObject.activeInHierarchy))
            {
                if (magnifyEnabled) ResetToBase();

                // Tracking is gone, so no credit accrues toward "the pose has been good
                // for a while". Without this the timer keeps whatever it held when the
                // target vanished, and the first frame back counts as fully recovered —
                // which is precisely the premature release this whole path exists to stop.
                _poseGoodFor = 0f;

                // Forget the last distance too. Re-acquiring somewhere genuinely far from
                // where we left off is normal; measuring that as an impossible jump would
                // reject every frame and wedge the takeover open.
                _lastGoodRaw = -1f;

                // And the lock is gone with the target — steadiness cannot be measured
                // blind, and pretending otherwise would let MICRO exit into nothing.
                LockStableS = 0f;
                PoseLocked = false;
                _prevRawForLock = float.PositiveInfinity;

                // THE TAKEOVER LATCHES THROUGH TRACKING LOSS.
                //
                // Vuforia gives up on a ~10 cm Model Target somewhere around 5 cm — which
                // is precisely where the takeover is meant to be total. Collapsing it on
                // target-lost produced the exact failure this guards against: lean all the
                // way in and the polyps strobe in and out as tracking flickers, at the one
                // moment the piece asks the viewer to commit.
                //
                // Once the iris is substantially open, registration has nothing left to
                // do — the coral is off-screen behind a full frame of footage, so there is
                // nothing for the viewer to notice being unregistered. Hold it.
                //
                // Below the threshold it still closes immediately: losing the target at
                // arm's length must not strand a half-open iris in mid-air.
                if (FullscreenReveal >= takeoverLatchThreshold)
                {
                    _untrackedHold += Time.deltaTime;
                    if (_untrackedHold <= takeoverHoldMaxS)
                    {
                        // Freeze — do not recompute from a stale coral transform.
                        _loupeHeld = true;
                        LoupeRadius = 0f;
                        PushLoupe();
                        _smoothedDistance = Mathf.Infinity;
                        return;
                    }
                    // Held long enough with no re-acquire: the visitor has walked away
                    // rather than leaned in. Release rather than leaving a phone stuck
                    // showing fullscreen polyps forever.
                    FullscreenReveal = Mathf.MoveTowards(
                        FullscreenReveal, 0f, Time.deltaTime / Mathf.Max(takeoverReleaseS, 0.01f));
                    _loupeHeld = FullscreenReveal > 0.0001f;
                    // The fade completing IS an exit — record it, and pay the refractory,
                    // or the state label would still say MICRO over an empty screen.
                    if (!_loupeHeld && State != MagState.Meso)
                        ChangeState(MagState.Meso, float.PositiveInfinity, refractoryS);
                    LoupeRadius = 0f;
                    PushLoupe();
                    _smoothedDistance = Mathf.Infinity;
                    return;
                }

                _untrackedHold = 0f;
                _loupeHeld = false;
                // No dwell accrues while blind: a lost target is not evidence of anything
                // (the black-box evaluation's central point — at close range it usually
                // means the viewer moved CLOSER).
                EnterDwellProgressS = 0f;
                ExitDwellProgressS = 0f;
                // Below the latch the blend lapses with the target. Coming down off a
                // MICRO visit still pays the refractory; an aborted BLENDING does not.
                if (State != MagState.Meso)
                    ChangeState(MagState.Meso, float.PositiveInfinity,
                                State == MagState.Blending ? 0f : refractoryS);
                CloseLoupe();
                _smoothedDistance = Mathf.Infinity;
                return;
            }

            // Re-acquired. Distance takes over again from here, so pulling away closes the
            // iris through the ordinary arc — the transition out is the transition in,
            // played backwards, exactly as before.
            _untrackedHold = 0f;
            _loupeHeld = false;

            // Start every frame from the aligned/tracked base pose so distance and
            // magnification are computed cleanly (base = 1:1 with the print).
            if (magnifyEnabled) ResetToBase();

            Vector3 center = coralRenderer.bounds.center;          // tracks the print at base scale

            float rawDist;
            if (useManualDistance)
            {
                rawDist = manualDistance;
            }
            else
            {
                rawDist = Vector3.Distance(cam.transform.position, center);
                if (measureFromSurface)
                {
                    // Subtract the coral's own extent along the view axis, so the thresholds mean
                    // "gap to the coral SURFACE" instead of "distance to its hidden centre".
                    Vector3 toCam = cam.transform.position - center;
                    if (toCam.sqrMagnitude > 1e-8f)
                    {
                        toCam.Normalize();
                        Vector3 e = coralRenderer.bounds.extents;
                        rawDist -= Mathf.Abs(toCam.x) * e.x + Mathf.Abs(toCam.y) * e.y + Mathf.Abs(toCam.z) * e.z;
                    }
                    rawDist = Mathf.Max(rawDist, 0f);
                }
            }

            // IS THIS POSE BELIEVABLE?
            //
            // Vuforia does not only lose a target — at very close range it keeps
            // reporting it as FOUND while handing over a nonsense transform. The coral
            // then renders shattered across the near plane, which is what the viewer
            // actually sees flickering: not the microscale failing, but the skeleton
            // being drawn somewhere impossible for a frame or two.
            //
            // The tracking-loss gate above cannot catch that, because the renderer is
            // still enabled. So the pose is sanity-checked directly: the coral must be
            // in front of the camera, within a plausible range, and must not have
            // teleported since the previous frame. A rejected frame holds the last good
            // distance rather than feeding garbage into the arcs.
            bool plausible = useManualDistance || IsPlausiblePose(center, rawDist);
            RawDistanceM = rawDist;          // the measurement itself, pre-substitution — HUD truth
            LastPosePlausible = plausible;
            if (plausible) _lastGoodRaw = rawDist;
            else rawDist = _lastGoodRaw;

            // Re-lock steadiness, measured on the RAW value: a hand retreating changes it
            // by a millimetre or two per frame; a pose still hunting jumps it. Implausible
            // frames reset the clock by construction, since the measurement itself leaps.
            float lockJump = Mathf.Abs(RawDistanceM - _prevRawForLock);
            _prevRawForLock = RawDistanceM;
            LockStableS = (plausible && lockJump <= relockJitterM)
                ? LockStableS + Time.deltaTime : 0f;
            PoseLocked = VuforiaTracked && LockStableS >= relockStableS;

            // THE CORAL HOLDS ITS LAST CONFIDENT POSE.
            //
            // The reported flicker is not the video failing — it is the MESH, drawn at a
            // slightly-but-visibly wrong solve for a frame or two, alternating with the
            // footage. Vuforia keeps saying TRACKED through those frames, so hiding on
            // target-lost never caught them.
            //
            // Rather than chase every solve, the mesh is pinned to the last pose taken
            // while confidence was high. A stale pose is invisible — the coral simply sits
            // where it was — whereas a wrong pose that MOVES reads instantly as a fault.
            // Confidence returning resumes live tracking with no snap, because the frozen
            // pose is by definition the last good one.
            if (!magnifyEnabled && _haveBase)
            {
                if (VuforiaTracked && plausible)
                {
                    _confidentPos = _root.position;
                    _confidentRot = _root.rotation;
                    _haveConfidentPose = true;
                }
                else if (_haveConfidentPose)
                {
                    _root.SetPositionAndRotation(_confidentPos, _confidentRot);
                }
            }

            if (float.IsInfinity(_smoothedDistance)) { _smoothedDistance = rawDist; _euro.Reset(rawDist); }

            // ONE EURO, NOT SMOOTHDAMP.
            //
            // A single smoothing constant is asked to do two irreconcilable jobs: kill
            // jitter while the hand is still, and not lag while it moves. SmoothDamp can
            // only trade one for the other, and tuned for stillness (0.79 s in the scene)
            // it takes ~2 s to converge — which is precisely the "responds delayed to the
            // phone's movements" being reported, and it is not fixable by picking a
            // better constant.
            //
            // The One Euro filter adapts: its cutoff rises with the measured speed, so it
            // is heavily damped at rest and nearly transparent during a deliberate move.
            // That is what makes the magnifier feel attached to the hand.
            _smoothedDistance = useManualDistance
                ? rawDist
                : _euro.Filter(rawDist, Time.deltaTime, euroMinCutoff, euroBeta, euroDerivCutoff);
            float d = _smoothedDistance;

            // --- Magnification: scale up as the camera nears ---
            float mt = InvLerpClamped(magnifyStartDistance, magnifyFullDistance, d);
            float k = Mathf.Lerp(1f, Mathf.Max(1f, maxMagnification), Mathf.Clamp01(magnifyCurve.Evaluate(mt)));
            Magnification = k;
            if (k > 1.0001f)
            {
                // Choose the point the scale radiates FROM. FrontSurface anchors on the near
                // face (the inspected cups) so they hold their focal position; CoralCenter
                // grows about the middle (near face bulges toward the camera in perspective).
                Vector3 anchor = center;
                if (magnifyAnchor == MagnifyAnchor.FrontSurface)
                {
                    Vector3 toCam = cam.transform.position - center;
                    if (toCam.sqrMagnitude > 1e-8f)
                    {
                        toCam.Normalize();
                        Vector3 e = coralRenderer.bounds.extents; // half-extents at base scale
                        float extAlong = Mathf.Abs(toCam.x) * e.x + Mathf.Abs(toCam.y) * e.y + Mathf.Abs(toCam.z) * e.z;
                        anchor = center + toCam * extAlong;        // front surface toward the camera
                    }
                }

                Vector3 P = _root.position;                        // base pivot (world), post-reset
                _root.localScale = _baseLocalScale * k;            // uniform scale about the pivot...
                _root.position = P + (anchor - P) * (1f - k);      // ...shift so the anchor stays put
            }

            // --- Loupe: the window onto the micro-scale opens as the viewer leans in ---
            float lt = InvLerpClamped(loupeStartDistance, loupeFullDistance, d);
            LoupeRadius = maxLoupeRadius * Mathf.Clamp01(loupeCurve.Evaluate(lt));
            LoupeCenter = FindLoupeCenter(center);
            PushLoupe();

            // --- Takeover: past the loupe, the footage leaves the coral entirely ---
            float ft = InvLerpClamped(fullscreenStartDistance, fullscreenFullDistance, d);
            float wanted = Mathf.Clamp01(fullscreenCurve.Evaluate(ft));

            // ONE GOOD FRAME IS NOT A RECOVERY. Vuforia re-acquires gradually: the pose
            // becomes arithmetically plausible before the mesh is actually locked back
            // onto the print, so trust requires poseRecoverS of continuous plausibility.
            if (plausible) _poseGoodFor += Time.deltaTime;
            else _poseGoodFor = 0f;
            bool recovered = _poseGoodFor >= poseRecoverS;

            StateAgeS += Time.deltaTime;
            MicroRefractoryS = Mathf.Max(0f, MicroRefractoryS - Time.deltaTime);
            bool mature = StateAgeS >= minStateS;

            switch (State)
            {
                case MagState.Meso:
                    // Fullscreen is structurally OFF here — nothing that never runs can
                    // flicker. Entry is earned: d under the start distance, continuously.
                    FullscreenReveal = 0f;
                    TakeoverHeld = false;
                    _takeoverLatched = false;

                    EnterDwellProgressS = d < fullscreenStartDistance
                        ? EnterDwellProgressS + Time.deltaTime : 0f;
                    if (EnterDwellProgressS >= enterDwellS && mature)
                        ChangeState(MagState.Blending, d);
                    break;

                case MagState.Micro:
                    // Committed — but committed is not the same as frozen.
                    //
                    // The lock decides which. With the pose LOCKED the meso material is
                    // sitting on the print, so it is safe to be seen: the reveal tracks
                    // the hand 1:1 and the polyp shrinks as the viewer withdraws, exactly
                    // as a lens does. `wanted` comes off the smoothed distance, so
                    // following it directly is smooth without being laggy.
                    //
                    // WITHOUT a lock it holds at full cover, because shrinking would
                    // uncover a coral that is not yet in place — the failure the hold
                    // exists to prevent. So the two requirements are not in tension:
                    // shrink whenever the material is ready, hold whenever it is not.
                    //
                    // Either way MICRO is not left until the exit dwell and the lock both
                    // agree; the state machine governs COMMITMENT, the reveal governs
                    // what is on screen, and only the latter follows the hand.
                    FullscreenReveal = PoseLocked
                        ? wanted
                        : Mathf.MoveTowards(FullscreenReveal, 1f,
                                            Time.deltaTime / Mathf.Max(takeoverReleaseS, 0.01f));
                    TakeoverHeld = !recovered;   // stale pose: keep the iris centre frozen

                    // Exit needs d beyond the HYSTERESIS distance, continuously. Any dip
                    // resets the vote — and d only moves on plausible poses, so garbage
                    // frames and tracking loss can never cast an exit vote at all.
                    ExitDwellProgressS = d > microExitDistance
                        ? ExitDwellProgressS + Time.deltaTime : 0f;

                    if (ExitDwellProgressS >= exitDwellS && mature)
                    {
                        // The distance vote opens nothing by itself. The footage HOLDS at
                        // full cover until the pose is re-LOCKED — Vuforia reporting
                        // genuinely TRACKED and the measurement steady — because the frame
                        // after this door is the meso mesh snapping back onto the print,
                        // and uncovering onto a floating mesh is the worst frame this app
                        // can show. The visitor just sees the footage linger a beat longer;
                        // the lock usually arrives within a second of the coral re-entering
                        // view. Fail-soft after relockMaxWaitS so nobody is ever trapped.
                        RelockWaitS += Time.deltaTime;
                        if (PoseLocked)
                            ChangeState(MagState.Retreat, d);
                        else if (RelockWaitS > relockMaxWaitS)
                        {
                            if (logStateChanges)
                                Debug.Log($"[magnify] relock TIMEOUT after {RelockWaitS:F1}s " +
                                          $"(vuforia={(VuforiaTracked ? "trk" : "EXT")} " +
                                          $"stable={LockStableS:F2}s) — retreating without lock");
                            ChangeState(MagState.Retreat, d);
                        }
                    }
                    else RelockWaitS = 0f;
                    break;

                default:   // Blending and Retreat: the reveal follows the hand, both ways
                    if (wanted >= takeoverLatchThreshold) _takeoverLatched = true;

                    if (_takeoverLatched && !recovered)
                    {
                        // Committed and the pose is not trustworthy: FREEZE. The reveal is
                        // a pure function of hand position — when the hand stops, it stops,
                        // and it resumes from exactly here once the pose earns trust back.
                        TakeoverHeld = true;
                    }
                    else
                    {
                        // THE FOOTAGE SUBSUMES THE MATERIAL UNLESS CONFIDENCE IS HIGH.
                        //
                        // Growing is always safe — leaning in only ever adds footage, and
                        // it follows the hand immediately. SHRINKING is the dangerous
                        // direction, because every pixel it gives back is a pixel of coral
                        // mesh revealed, and revealing a mesh at a bad solve is the
                        // flicker. So the reveal may only retreat while PoseLocked; with
                        // confidence low it simply holds where it is, and the footage
                        // keeps the screen until the mapping is trustworthy again.
                        //
                        // Holding is not the same as expanding: nothing moves on its own,
                        // so a stationary phone still produces a stationary image.
                        FullscreenReveal = wanted >= FullscreenReveal
                            ? wanted
                            : (PoseLocked
                                ? Mathf.MoveTowards(FullscreenReveal, wanted,
                                                    Time.deltaTime / Mathf.Max(takeoverReleaseS, 0.01f))
                                : FullscreenReveal);

                        TakeoverHeld = false;
                        if (wanted < takeoverLatchThreshold * 0.5f && FullscreenReveal <= 0.001f)
                            _takeoverLatched = false;
                    }

                    // MICRO is only entered once the screen is REALLY covered — which,
                    // because the iris is clamped to the coral's silhouette, can only
                    // happen when the coral itself fills the frame. Full commitment is
                    // earned by getting close enough, never granted by the arc alone.
                    if (FullscreenReveal >= 0.995f && ScreenIsCovered && mature && MicroRefractoryS <= 0f)
                        ChangeState(MagState.Micro, d);
                    else if (FullscreenReveal <= 0.001f && d > fullscreenStartDistance && mature)
                        // Completing a RETREAT is "an exit" and pays the refractory. A
                        // blend that never reached MICRO just lapses, free — that is what
                        // keeps slow in-and-out movement 1:1 with the hand.
                        ChangeState(MagState.Meso, d,
                                    State == MagState.Retreat ? refractoryS : 0f);
                    break;
            }

            // THE CORAL IS HIDDEN ONLY WHEN THE FOOTAGE GENUINELY COVERS EVERY PIXEL.
            //
            // The old test was `FullscreenReveal > 0.995 || (latched && !recovered)`, and
            // both halves were wrong in the same way: they hid the tissue while the iris
            // was still clamped to the coral's silhouette, so the feathered rim showed
            // the bare white print and the table with no coral material anywhere. The
            // meso layer must be present the entire time any part of the screen is not
            // footage — it is the thing the magnification transitions back INTO, so it
            // cannot be absent at the moment of transition.
            //
            // Losing the mis-registration guard costs little: a garbage pose at partial
            // coverage now shows a briefly-wrong coral instead of no coral, and a
            // briefly-wrong coral is the better failure. At full coverage — where the
            // shattered near-plane mesh actually appeared — this still hides it.
            //
            // forceRenderingOff rather than .enabled, because the tracking-loss gate
            // above reads .enabled and would mistake our own hiding for Vuforia's.
            SetCoralHidden(ScreenIsCovered);
        }

        /// <summary>
        /// The only door between states. Every passage is logged with the evidence (d, m,
        /// how long the old state lasted) — the acceptance test is literally counting
        /// these lines during a 60-second close hold, and they are thesis data besides.
        /// </summary>
        private void ChangeState(MagState next, float dNow, float refractory = 0f)
        {
            if (logStateChanges)
                Debug.Log($"[magnify] {State} -> {next}   " +
                          $"d={(float.IsInfinity(dNow) ? -1f : dNow):F3}m   " +
                          $"m={FullscreenReveal:F2}   after {StateAgeS:F1}s in {State}");
            State = next;
            StateAgeS = 0f;
            EnterDwellProgressS = 0f;
            ExitDwellProgressS = 0f;
            RelockWaitS = 0f;
            if (refractory > 0f) MicroRefractoryS = refractory;
        }

        private void OnVuforiaStatus(Vuforia.ObserverBehaviour behaviour, Vuforia.TargetStatus status)
        {
            VuforiaTracked = status.Status == Vuforia.Status.TRACKED;
        }

        private void OnDestroy()
        {
            if (_observer != null) _observer.OnTargetStatusChanged -= OnVuforiaStatus;
        }

        /// <summary>
        /// Reject transforms that cannot be real: behind the camera, absurdly near or
        /// far, or jumped further since last frame than a hand could plausibly move.
        /// This is what distinguishes "the viewer leaned in" from "Vuforia lost the pose
        /// but has not admitted it yet".
        /// </summary>
        private bool IsPlausiblePose(Vector3 center, float rawDist)
        {
            Vector3 toCoral = center - cam.transform.position;
            if (Vector3.Dot(toCoral, cam.transform.forward) <= 0f) return false;   // behind us
            if (rawDist < 0f || rawDist > implausibleFarM) return false;

            if (_lastGoodRaw > 0f)
            {
                float jump = Mathf.Abs(rawDist - _lastGoodRaw);
                if (jump > implausibleJumpM) return false;
            }
            return true;
        }

        private void SetCoralHidden(bool hidden)
        {
            if (_coralHidden == hidden) return;
            _coralHidden = hidden;

            if (coralRenderer != null) coralRenderer.forceRenderingOff = hidden;
            if (loupeTargets != null)
                for (int i = 0; i < loupeTargets.Length; i++)
                    if (loupeTargets[i] != null) loupeTargets[i].forceRenderingOff = hidden;
        }

        /// <summary>
        /// Where the viewer is peering: a ray through the screen centre onto the coral,
        /// so the window follows the aim rather than sitting on a fixed spot. Falls back
        /// to the nearest point on the bounds (no collider, or aimed off the coral) and
        /// finally to the bounds centre, so it degrades rather than jumping to the origin.
        /// </summary>
        private Vector3 FindLoupeCenter(Vector3 boundsCenter)
        {
            if (!useManualDistance && coralCollider != null)
            {
                Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (coralCollider.Raycast(ray, out RaycastHit hit, 10f)) return hit.point;
            }
            if (!useManualDistance) return coralRenderer.bounds.ClosestPoint(cam.transform.position);
            return boundsCenter;
        }

        private void CloseLoupe()
        {
            SetCoralHidden(false);   // never leave the coral hidden with nothing covering it
            LoupeRadius = 0f;
            // Drop the takeover too. If tracking dies while the screen is fully covered,
            // holding the cover would leave the viewer staring at footage with no way to
            // understand why — and no way to re-acquire, since they cannot see the coral
            // to point at it.
            FullscreenReveal = 0f;
            PushLoupe();
        }

        private void PushLoupe()
        {
            if (_loupeMats == null) return;
            for (int i = 0; i < _loupeMats.Length; i++)
            {
                if (_loupeMats[i] == null) continue;
                _loupeMats[i].SetVector(LoupeCenterID, LoupeCenter);
                _loupeMats[i].SetFloat(LoupeRadiusID, LoupeRadius);
            }
        }

        private void ResetToBase()
        {
            _root.localPosition = _baseLocalPos;
            _root.localScale = _baseLocalScale;
        }

        /// <summary>Like Mathf.InverseLerp but tolerant of a > b (reversed range) and clamped.</summary>
        private static float InvLerpClamped(float a, float b, float v)
        {
            if (Mathf.Approximately(a, b)) return v <= b ? 1f : 0f;
            return Mathf.Clamp01((v - a) / (b - a));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (magnifyFullDistance >= magnifyStartDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] magnifyFullDistance " +
                    $"({magnifyFullDistance}) should be NEARER (smaller) than magnifyStartDistance " +
                    $"({magnifyStartDistance}).", this);

            if (loupeFullDistance >= loupeStartDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] loupeFullDistance " +
                    $"({loupeFullDistance}) should be NEARER (smaller) than loupeStartDistance " +
                    $"({loupeStartDistance}) — the loupe opens as you approach.", this);
        }

        private void OnDrawGizmosSelected()
        {
            Camera c = cam != null ? cam : Camera.main;
            if (c == null) return;
            Vector3 o = c.transform.position, f = c.transform.forward;
            DrawRing(o, f, loupeStartDistance,   new Color(0.7f, 0.7f, 0.7f, 0.9f)); // loupe shut
            DrawRing(o, f, loupeFullDistance,    new Color(0.2f, 1f, 0.9f, 0.9f));   // loupe fully open
            DrawRing(o, f, magnifyStartDistance, new Color(0.2f, 0.8f, 1f, 0.9f));   // magnify begins
            DrawRing(o, f, magnifyFullDistance,  new Color(1f, 0.9f, 0.2f, 0.9f));   // max magnification

            if (Application.isPlaying && LoupeRadius > 0f)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.9f, 0.5f);
                Gizmos.DrawWireSphere(LoupeCenter, LoupeRadius);
            }
        }

        private static void DrawRing(Vector3 origin, Vector3 fwd, float dist, Color col)
        {
            Gizmos.color = col;
            Gizmos.DrawWireSphere(origin + fwd * dist, dist * 0.12f);
        }
#endif
    }
}
