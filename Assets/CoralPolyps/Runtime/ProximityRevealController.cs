using UnityEngine;
using UnityEngine.Rendering;

namespace CoralPolyps
{
    /// <summary>
    /// Stage 4 — proximity drives the REVEAL and the MAGNIFICATION, and nothing else
    /// (CONTROL_INTEGRATION.md §4). The healthy→bleached arc belongs to the
    /// orchestration server; _Stress comes off the wire via CoralAppearance.
    ///
    /// THE ONE INVARIANT
    /// -----------------
    /// Every arc in this file is a PURE FUNCTION of one number: the effective distance
    /// `d`. The same d always gives the same reveal, in both directions of travel.
    /// Nothing here animates, ramps, dwells, latches or rate-limits the output. Phone
    /// still => image still. Phone moves => image moves with it, immediately, and by
    /// the same amount coming out as going in.
    ///
    /// WHY THAT HAS TO BE STATED
    /// -------------------------
    /// It was broken once, and the fix is counter-intuitive enough to be worth the
    /// paragraph. A black-box evaluation found flicker — ~45 unintended state changes
    /// in ten seconds at close range — and the response was to make the REVEAL sticky:
    /// an entry dwell, exit hysteresis, a minimum state age, a refractory period, a
    /// re-lock gate, and rate-limited retreat. It suppressed the flicker, and every bit
    /// of it was applied to the wrong variable. The thing that actually flickered was
    /// the coral MESH, drawn at a bad solve for a frame or two. Freezing the reveal
    /// instead bought that at the cost of the magnifying-glass fiction itself:
    ///
    ///   * Hold the iPad perfectly still at close range and the polyps appear out of
    ///     nothing a second later. MESO pinned the reveal to zero regardless of
    ///     distance until minStateS elapsed, then released it to its distance-derived
    ///     value in a single frame.
    ///   * Pull away and nothing happens for seconds, then it cuts. The re-lock gate
    ///     required frame-to-frame steadiness of 8 mm — a ceiling of roughly 0.5 m/s,
    ///     sustained for 0.75 s — which no retreating hand can satisfy. So the reveal
    ///     held at full cover for the whole withdrawal (in MICRO it actively re-inflated
    ///     toward full) and snapped only once the hand stopped.
    ///
    /// Both were the same mistake: a positional quantity governed by a state machine.
    ///
    /// So the rule is now: CONFIDENCE ACTS ON THE INPUT, NEVER ON THE OUTPUT.
    ///
    ///     raw measure ─► plausibility gate ─► hold / release ─► One Euro ─► d ─► arcs
    ///                                         ▲
    ///                          all distrust is expressed HERE
    ///
    /// When the pose cannot be trusted, `d` is held — and every arc holds with it, for
    /// free, resuming with no pop because the input never jumped. When the target is
    /// lost for good, `d` is driven back OUTWARD, so the reveal closes through the
    /// identical arc it opened through instead of through a special-case fade. There is
    /// one code path, and it runs in both directions.
    ///
    /// The mesh still gets its own protection — it is pinned to the last pose taken
    /// while confidence was high — which is where the anti-flicker work belonged all
    /// along. A stale pose is invisible (the coral simply sits where it was); a wrong
    /// pose that MOVES reads instantly as a fault.
    ///
    /// WHAT PROXIMITY OWNS
    /// -------------------
    ///   - MAGNIFICATION — the coral scales up about the surface being inspected.
    ///     Off by default (maxMagnification 1); scaling breaks registration.
    ///   - THE LOUPE — a soft world-space sphere marking where the magnifier's polyp
    ///     footage is revealed on the coral's own surface.
    ///   - THE TAKEOVER — past the loupe, the footage leaves the coral and fills the
    ///     screen (FullscreenMagnifier).
    ///
    /// It controls how much of the micro-scale you see, never what condition it is in.
    /// Distances are WORLD metres from the camera to the coral's tracked bounds.
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

        [Tooltip("The takeover layer, asked whether the screen is ACTUALLY covered before the " +
                 "coral is ever hidden. Found in the scene if left empty; without it the " +
                 "controller falls back to the distance-derived reveal, which over-reports " +
                 "coverage whenever the coral does not fill the frame.")]
        public FullscreenMagnifier fullscreen;

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
        // Keep it CONTIGUOUS with the loupe arc (fullscreenStartDistance at or very near
        // loupeFullDistance). A gap between the two is dead travel where the viewer moves
        // and nothing changes, which teaches them that moving does nothing and makes the
        // eventual onset read as an event rather than as something they are driving.
        //
        // A useful side effect: Vuforia loses a ~10 cm Model Target somewhere around
        // 5 cm, and the takeover is opaque by then — so the tracking failure happens
        // behind a full screen of footage and is never seen.
        [Header("Fullscreen takeover arc (metres from coral surface)")]
        [Tooltip("At or beyond this distance the footage is entirely on the coral. Put it at " +
                 "loupeFullDistance so the loupe finishes opening exactly as this begins.")]
        public float fullscreenStartDistance = 0.07f;

        [Tooltip("At or within this distance the footage fills the screen. Must be < start. " +
                 "Give this arc room — the span between the two IS the emergence, and a span " +
                 "of a couple of centimetres is a wrist twitch, so it can only ever read as a " +
                 "cut no matter how well the mapping is pinned.")]
        public float fullscreenFullDistance = 0.03f;

        [Tooltip("Shapes proximity (0 at start, 1 at full) -> screen coverage.")]
        public AnimationCurve fullscreenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Pose sanity (Vuforia reports 'found' with garbage transforms up close)")]
        [Tooltip("Distance beyond which a reported pose is not believed, in metres.")]
        public float implausibleFarM = 3.0f;

        [Tooltip("Largest believable change in measured distance between two frames, in metres. " +
                 "A hand cannot move this fast; a lost pose can. This is a TELEPORT test, not a " +
                 "steadiness test — it must stay loose enough that a brisk deliberate movement " +
                 "passes, or it becomes the re-lock gate all over again.")]
        public float implausibleJumpM = 0.15f;

        [Header("Holding the input (never the output)")]
        [Tooltip("How long to hold `d` at its last measured value once the target is lost, " +
                 "before releasing. Vuforia drops a ~10 cm Model Target around 5 cm, which is " +
                 "exactly where the takeover should be total — so leaning in past tracking range " +
                 "must simply freeze the picture, not collapse it. Long enough that a visitor " +
                 "exploring up close is never interrupted.")]
        public float takeoverHoldMaxS = 8f;

        [Tooltip("Once the hold expires, seconds for `d` to travel back out to loupeStartDistance. " +
                 "The reveal then closes through the ordinary arc — the way in, played backwards — " +
                 "rather than through a separate fade. Nobody is left staring at frozen polyps " +
                 "because they walked away.")]
        public float takeoverReleaseS = 1.0f;

        [Tooltip("On re-acquiring the target, seconds to reconcile the held `d` with the live " +
                 "measurement. This is the ONLY motion in this file that is not the hand, and it " +
                 "exists because for a moment we genuinely did not know where the hand was. Keep " +
                 "it short — it should read as a snap with a soft edge, never as an animation.")]
        public float reacquireReconcileS = 0.25f;

        [Header("Distance measurement")]
        [Tooltip("Measure to the coral's SURFACE rather than its hidden bounds centre, so the " +
                 "distances above mean the real gap between device and coral. With this off, a " +
                 "13 cm coral puts ~6.5 cm of itself between surface and centre, making every " +
                 "threshold feel far closer than the number suggests.")]
        public bool measureFromSurface = true;

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

        [Header("Diagnostics")]
        [Tooltip("Log every change of the reported state label with d and m. The label DRIVES " +
                 "NOTHING — it is a description of where the reveal already is — but the log is " +
                 "still the flicker evidence and thesis data.")]
        public bool logStateChanges = true;

        [Tooltip("Seconds the label must disagree with the reported state before the change is " +
                 "recorded. Debounces the log at the arc boundaries; affects nothing on screen.")]
        public float stateLogDwellS = 0.2f;

        [Header("Testing (editor)")]
        [Tooltip("Bypass camera measurement and use manualDistance instead. Driven by ProximityTestRig " +
                 "so you can tune the feel in the editor without a device.")]
        public bool useManualDistance = false;
        public float manualDistance = 1.0f;

        /// <summary>
        /// Where the reveal sits, as a description rather than a controller. MICRO means the
        /// footage genuinely covers every pixel; BLENDING means some of it does. Nothing reads
        /// these to decide anything — that is the point of the rewrite — but they make a screen
        /// recording legible and they are the thesis's transition data.
        /// </summary>
        public enum MagState { Meso, Blending, Micro }

        /// <summary>Effective camera-to-coral distance in metres: the single input every arc
        /// reads. Held rather than invalidated when the pose cannot be trusted, so it is always
        /// a real number once the target has been seen at all.</summary>
        public float Distance => _d;

        /// <summary>Alias of <see cref="Distance"/> kept for the HUD's naming.</summary>
        public float SmoothedDistanceM => _d;

        /// <summary>The measurement itself, before any holding or filtering — HUD truth.</summary>
        public float RawDistanceM { get; private set; } = float.PositiveInfinity;

        /// <summary>Current loupe centre in WORLD space (the point the viewer is peering at).</summary>
        public Vector3 LoupeCenter { get; private set; }

        /// <summary>Current loupe radius in world metres; 0 means shut.</summary>
        public float LoupeRadius { get; private set; }

        /// <summary>
        /// 0 = footage lives entirely on the coral, 1 = footage fills the screen. A pure
        /// function of <see cref="Distance"/>. Read by FullscreenMagnifier.
        /// </summary>
        public float FullscreenReveal { get; private set; }

        /// <summary>
        /// True while `d` is being held because the pose cannot be measured or believed. The
        /// fullscreen layer freezes its iris centre and radius when this is set: the coral's
        /// transform is stale (or hidden), so re-projecting from it would swing the iris around
        /// the screen for as long as the dropout lasts.
        /// </summary>
        public bool TakeoverHeld { get; private set; }

        /// <summary>Seconds `d` has been held with no believable measurement. 0 when live.</summary>
        public float HeldForS => _heldFor;

        /// <summary>Current magnification factor (1 = life-size).</summary>
        public float Magnification { get; private set; } = 1f;

        public MagState State { get; private set; } = MagState.Meso;
        public float StateAgeS { get; private set; }

        /// <summary>Did this frame's measurement survive the sanity check?</summary>
        public bool LastPosePlausible { get; private set; }

        /// <summary>True when Vuforia reports genuinely TRACKED (not EXTENDED_TRACKED, which is
        /// dead reckoning — the mesh can sit centimetres off the print under it). Defaults true
        /// when no observer is found, so editor Play mode and manual rigs behave.</summary>
        public bool VuforiaTracked { get; private set; } = true;

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
        private Material[] _loupeMats;
        private Vuforia.ObserverBehaviour _observer;
        private bool _coralHidden;

        // The effective distance and everything that governs it. This is the whole of the
        // controller's mutable state now; there is no second machine holding a second opinion.
        private float _d = Mathf.Infinity;
        private float _heldFor;                       // seconds with no believable measurement
        private float _lastGoodRaw = -1f;             // for the teleport test
        private float _reconcileLeft;                 // seconds of re-acquire reconciliation left
        private float _reconcileFrom;                 // value of _d when the target came back
        private readonly OneEuro _euro = new OneEuro();

        // Last pose the coral was seen at while confidence was high. Re-applied while confidence
        // is low, so the mesh holds still instead of chasing bad solves. THIS is the anti-flicker
        // measure — it acts on the mesh, which is the thing that flickered.
        private Vector3 _confidentPos;
        private Quaternion _confidentRot;
        private bool _haveConfidentPose;

        // Label debounce. Purely cosmetic: it delays what the log and the HUD SAY, never what
        // the screen does.
        private MagState _pendingLabel = MagState.Meso;
        private float _pendingLabelFor;

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

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (fullscreen == null) fullscreen = FindAnyObjectByType<FullscreenMagnifier>();
        }

        /// <summary>
        /// Is every pixel genuinely footage right now? Asks the layer that actually drew it.
        /// The distance-derived reveal is NOT an answer to this question — it ignores the
        /// silhouette clamp, so it reads 1.0 while the rim is still transparent.
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

            // Subscribe to the tracker's own verdict. The coral hangs under the Model Target,
            // so its ObserverBehaviour is in the parents.
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

            SetCoralHidden(false);
            LoupeRadius = 0f;
            FullscreenReveal = 0f;
            PushLoupe();
        }

        private void LateUpdate()
        {
            if (coralRenderer == null || cam == null || !_haveBase) return;

            float dt = Time.deltaTime;

            // With magnification off the controller must NEVER touch the coral's transform —
            // that keeps registration purely Vuforia's, which is what we want by default.
            bool magnifyEnabled = maxMagnification > 1.0001f;

            // Start every frame from the aligned/tracked base pose so distance and
            // magnification are computed cleanly (base = 1:1 with the print).
            if (magnifyEnabled) ResetToBase();

            // --------------------------------------------------------------- 1. MEASURE
            // Vuforia's DefaultObserverEventHandler hides the coral by disabling the renderer
            // on target-lost. We hide it ourselves with forceRenderingOff precisely so that
            // .enabled stays Vuforia's signal and never our own.
            bool targetVisible = useManualDistance ||
                (coralRenderer.enabled && coralRenderer.gameObject.activeInHierarchy);

            Vector3 center = coralRenderer.bounds.center;   // tracks the print at base scale
            float rawDist = MeasureRaw(center);
            RawDistanceM = targetVisible ? rawDist : float.PositiveInfinity;

            // IS THIS POSE BELIEVABLE? Vuforia does not only lose a target — at very close
            // range it keeps reporting it as FOUND while handing over a nonsense transform,
            // and the coral then renders shattered across the near plane. The renderer is
            // still enabled through that, so the visibility test above cannot catch it.
            //
            // Note this is a TELEPORT test, not a steadiness test. It rejects motion no hand
            // could produce (15 cm in a frame) and passes everything else. The previous
            // version also ran a steadiness test — 8 mm per frame, sustained — and that is
            // what made retreating impossible, because a retreating hand is not steady.
            bool believable = useManualDistance ||
                (targetVisible && IsPlausiblePose(center, rawDist));
            LastPosePlausible = believable;

            // ------------------------------------------- 2. THE EFFECTIVE DISTANCE `d`
            // ALL distrust is expressed here and nowhere else. Downstream is pure arithmetic.
            if (believable)
            {
                _lastGoodRaw = rawDist;

                float live = useManualDistance
                    ? rawDist
                    : (float.IsInfinity(_d)
                        ? Prime(rawDist)
                        : _euro.Filter(rawDist, dt, euroMinCutoff, euroBeta, euroDerivCutoff));

                if (_heldFor > 0f)
                {
                    // Coming back from a hold. Reconcile rather than jump: for the length of
                    // the gap we did not know where the hand was, and snapping the reveal to a
                    // new truth is the pop this rewrite exists to remove. Bounded and short.
                    _reconcileLeft = reacquireReconcileS;
                    _reconcileFrom = _d;
                    _heldFor = 0f;
                }

                if (_reconcileLeft > 0f)
                {
                    _reconcileLeft = Mathf.Max(0f, _reconcileLeft - dt);
                    float t = reacquireReconcileS > 1e-4f
                        ? 1f - (_reconcileLeft / reacquireReconcileS) : 1f;
                    _d = Mathf.Lerp(_reconcileFrom, live, Mathf.SmoothStep(0f, 1f, t));
                }
                else
                {
                    _d = live;
                }

                TakeoverHeld = false;
            }
            else if (!float.IsInfinity(_d))
            {
                // No believable measurement. HOLD THE INPUT — every arc holds with it, and
                // resumes from exactly here, because nothing downstream has any state of its
                // own to be out of step.
                _heldFor += dt;
                _reconcileLeft = 0f;

                if (_heldFor > takeoverHoldMaxS)
                {
                    // Held long enough with no re-acquire: the visitor walked away rather than
                    // leaned in. Drive `d` back OUTWARD so the reveal closes through the same
                    // arc it opened through — the way in, played backwards — instead of
                    // through a bespoke fade with its own timing and its own bugs.
                    float span = Mathf.Max(loupeStartDistance - fullscreenFullDistance, 0.01f);
                    _d = Mathf.MoveTowards(_d, loupeStartDistance,
                                           span / Mathf.Max(takeoverReleaseS, 0.01f) * dt);
                }

                // Freeze the iris geometry too: the coral's transform is stale, so letting
                // FullscreenMagnifier re-project from it would swing the opening around the
                // screen for the length of the dropout — the flicker, by another route.
                TakeoverHeld = true;

                // The teleport test compares against the last good frame. Re-acquiring
                // somewhere genuinely far from where we left off is normal after a gap, so
                // forget it or every returning frame would be rejected and the hold would
                // never end.
                _lastGoodRaw = -1f;
            }
            else
            {
                // Cold start: nothing has ever been measured. Everything shut, nothing held.
                TakeoverHeld = false;
                _heldFor = 0f;
            }

            // ------------------------------------------------- 3. THE MESH'S OWN GUARD
            // Rather than chase every solve, the mesh is pinned to the last pose taken while
            // confidence was high. A stale pose is invisible — the coral simply sits where it
            // was — whereas a wrong pose that MOVES reads instantly as a fault. Confidence
            // returning resumes live tracking with no snap, because the frozen pose is by
            // definition the last good one.
            if (!magnifyEnabled && _haveBase && targetVisible)
            {
                if (VuforiaTracked && believable)
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

            // ------------------------------------------------------------- 4. THE ARCS
            // From here down there is no state, no history and no time: three pure functions
            // of `d`. Read them as the definition of the magnifier's behaviour, because that
            // is now literally what they are.
            float d = _d;

            if (float.IsInfinity(d))
            {
                LoupeRadius = 0f;
                FullscreenReveal = 0f;
                Magnification = 1f;
                LoupeCenter = center;
                PushLoupe();
                SetCoralHidden(false);
                UpdateLabel(dt, d);
                return;
            }

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
            // While held, keep the last centre: the raycast would be against a stale or
            // disabled collider and would wander.
            if (!TakeoverHeld) LoupeCenter = FindLoupeCenter(center);
            PushLoupe();

            // --- Takeover: past the loupe, the footage leaves the coral entirely ---
            float ft = InvLerpClamped(fullscreenStartDistance, fullscreenFullDistance, d);
            FullscreenReveal = Mathf.Clamp01(fullscreenCurve.Evaluate(ft));

            // ------------------------------------------------------- 5. HIDE THE CORAL
            // Only when the footage genuinely covers every pixel — asked of the layer that
            // drew it, not derived from distance. The distance-derived reveal ignores the
            // silhouette clamp, so it reads 1.0 while the rim is still transparent; hiding on
            // that showed the bare white print and the table through the feathered edge.
            //
            // forceRenderingOff rather than .enabled, because the visibility test above reads
            // .enabled and would mistake our own hiding for Vuforia's.
            SetCoralHidden(ScreenIsCovered);

            UpdateLabel(dt, d);
        }

        /// <summary>
        /// Distance from the camera to the coral, in metres. Optionally to the SURFACE rather
        /// than the hidden bounds centre, so the thresholds mean the real gap between device
        /// and coral: a 13 cm coral otherwise puts ~6.5 cm of itself between the two.
        /// </summary>
        private float MeasureRaw(Vector3 center)
        {
            if (useManualDistance) return manualDistance;

            float raw = Vector3.Distance(cam.transform.position, center);
            if (!measureFromSurface) return raw;

            Vector3 toCam = cam.transform.position - center;
            if (toCam.sqrMagnitude > 1e-8f)
            {
                toCam.Normalize();
                Vector3 e = coralRenderer.bounds.extents;
                raw -= Mathf.Abs(toCam.x) * e.x + Mathf.Abs(toCam.y) * e.y + Mathf.Abs(toCam.z) * e.z;
            }
            return Mathf.Max(raw, 0f);
        }

        private float Prime(float raw)
        {
            _euro.Reset(raw);
            return raw;
        }

        /// <summary>
        /// Report where the reveal has ended up. This DESCRIBES the screen; it does not
        /// decide anything. The dwell debounces the log at arc boundaries and has no effect
        /// on what is drawn — which is the difference between this and what it replaced.
        /// </summary>
        private void UpdateLabel(float dt, float dNow)
        {
            StateAgeS += dt;

            MagState observed = ScreenIsCovered ? MagState.Micro
                              : FullscreenReveal > 0.001f ? MagState.Blending
                              : MagState.Meso;

            if (observed == State) { _pendingLabelFor = 0f; return; }

            if (observed != _pendingLabel) { _pendingLabel = observed; _pendingLabelFor = 0f; }
            _pendingLabelFor += dt;
            if (_pendingLabelFor < stateLogDwellS) return;

            if (logStateChanges)
                Debug.Log($"[magnify] {State} -> {observed}   " +
                          $"d={(float.IsInfinity(dNow) ? -1f : dNow):F3}m   " +
                          $"m={FullscreenReveal:F2}   after {StateAgeS:F1}s in {State}");

            State = observed;
            StateAgeS = 0f;
            _pendingLabelFor = 0f;
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
        /// Reject transforms that cannot be real: behind the camera, absurdly near or far, or
        /// jumped further since last frame than a hand could plausibly move. This is what
        /// distinguishes "the viewer leaned in" from "Vuforia lost the pose but has not
        /// admitted it yet". Deliberately loose — see implausibleJumpM.
        /// </summary>
        private bool IsPlausiblePose(Vector3 center, float rawDist)
        {
            Vector3 toCoral = center - cam.transform.position;
            if (Vector3.Dot(toCoral, cam.transform.forward) <= 0f) return false;   // behind us
            if (rawDist < 0f || rawDist > implausibleFarM) return false;

            if (_lastGoodRaw > 0f && Mathf.Abs(rawDist - _lastGoodRaw) > implausibleJumpM) return false;
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
        /// Where the viewer is peering: a ray through the screen centre onto the coral, so the
        /// window follows the aim rather than sitting on a fixed spot. Falls back to the
        /// nearest point on the bounds (no collider, or aimed off the coral) and finally to the
        /// bounds centre, so it degrades rather than jumping to the origin.
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

            if (fullscreenFullDistance >= fullscreenStartDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] fullscreenFullDistance " +
                    $"({fullscreenFullDistance}) should be NEARER (smaller) than " +
                    $"fullscreenStartDistance ({fullscreenStartDistance}).", this);

            // Dead travel between the two beats is the thing that makes the emergence read as
            // an event rather than as something the viewer is driving.
            if (fullscreenStartDistance < loupeFullDistance - 0.005f)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] there is " +
                    $"{(loupeFullDistance - fullscreenStartDistance) * 100f:F1} cm of dead travel " +
                    $"between the loupe finishing ({loupeFullDistance} m) and the takeover " +
                    $"starting ({fullscreenStartDistance} m) — the viewer moves and nothing " +
                    "changes. Put fullscreenStartDistance at loupeFullDistance.", this);
        }

        private void OnDrawGizmosSelected()
        {
            Camera c = cam != null ? cam : Camera.main;
            if (c == null) return;
            Vector3 o = c.transform.position, f = c.transform.forward;
            DrawRing(o, f, loupeStartDistance,      new Color(0.7f, 0.7f, 0.7f, 0.9f)); // loupe shut
            DrawRing(o, f, loupeFullDistance,       new Color(0.2f, 1f, 0.9f, 0.9f));   // loupe fully open
            DrawRing(o, f, fullscreenStartDistance, new Color(1f, 0.5f, 0.9f, 0.9f));   // takeover begins
            DrawRing(o, f, fullscreenFullDistance,  new Color(1f, 0.3f, 0.3f, 0.9f));   // takeover total

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
