using UnityEngine;
using UnityEngine.Rendering;

namespace CoralPolyps
{
    /// <summary>
    /// Stage 4 — proximity drives the coral (REVISED 2026-07-14 after the first device test).
    ///
    /// The old loupe "reveal fade" is retired. New behaviour, when the coral is tracked:
    ///   - It is shown at FULL fluorescence by default (far away = healthy glowing coral).
    ///     No fade-in; the shader's <c>_LOUPE_ON</c> keyword stays OFF so the whole coral
    ///     shows.
    ///   - As the camera nears, the fluorescence BLEACHES GRADUALLY — the colour arc is
    ///     spread across the whole approach distance (drives the material's <c>_Stress</c>),
    ///     so it shifts slowly instead of snapping through the colours.
    ///   - As the camera nears, the coral MAGNIFIES — it scales up about its own centre so
    ///     it reads as zooming into the polyps (the "magnifying glass" of the piece).
    ///
    /// Two independent proximity arcs (colour + magnification), each far→near, each with
    /// its own distance range + curve. Distances are WORLD metres; measured from the camera
    /// to the coral's tracked bounds centre (stable — unaffected by the magnification).
    ///
    /// Registration note: at 1× (far) the coral sits 1:1 on the print; as it magnifies it
    /// intentionally grows past the print. So tighten registration against the FAR/base state.
    /// </summary>
    public class ProximityRevealController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The coral mesh renderer (its material is driven; its bounds give the tracked centre). Required.")]
        public Renderer coralRenderer;

        [Tooltip("The AR camera. Defaults to Camera.main if empty.")]
        public Camera cam;

        [Tooltip("Transform scaled/moved for magnification. Defaults to the coral renderer's own transform " +
                 "so the aligned parent (ModelTarget child) is left untouched.")]
        public Transform coralRoot;

        [Header("Appearance cycle: natural -> fluorescent -> bleached -> reset")]
        [Tooltip("Where PEAK fluorescence sits on the 0..1 arc. Pushed to the material's _FluorPoint " +
                 "so shader and controller always agree.")]
        [Range(0.1f, 0.9f)] public float fluorPoint = 0.5f;

        [Tooltip("At or beyond this distance the coral rests fully NATURAL (arc 0).")]
        public float naturalDistance = 0.60f;

        [Tooltip("At or within this distance the coral hits PEAK FLUORESCENCE (max closeness). " +
                 "Reaching it arms the bleach.")]
        public float peakDistance = 0.10f;

        [Tooltip("AFTER peaking, pulling back out to this distance completes the BLEACH (arc 1).")]
        public float bleachFullDistance = 0.45f;

        [Tooltip("Once bleached, beyond this distance it blends back to natural for the next viewer.")]
        public float resetDistance = 0.55f;

        [Tooltip("Seconds for the bleached -> natural reset blend (emission is suppressed during it " +
                 "so it doesn't flash fluorescent on the way back).")]
        public float resetBlendTime = 1.5f;

        [Tooltip("Shapes the approach: natural -> peak fluorescence.")]
        public AnimationCurve approachCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Shapes the retreat: fluorescent -> bleached.")]
        public AnimationCurve bleachCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

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

        [Header("Distance measurement")]
        [Tooltip("Measure to the coral's SURFACE rather than its hidden bounds centre, so the " +
                 "distances above mean the real gap between device and coral. With this off, a " +
                 "13 cm coral puts ~6.5 cm of itself between surface and centre, making every " +
                 "threshold feel far closer than the number suggests.")]
        public bool measureFromSurface = true;

        [Header("Smoothing")]
        [Tooltip("SmoothDamp time (s) on the measured distance. Higher = calmer but laggier.")]
        [Range(0f, 1f)] public float distanceSmoothTime = 0.12f;

        [Header("Material")]
        [Tooltip("On Start, put a runtime material instance into AR mode: fully OPAQUE with depth " +
                 "write (100% opacity + correct cup/wall sorting) and the loupe keyword OFF (whole " +
                 "coral shows). The bleach drains to the white skeleton, which matches the print.")]
        public bool configureMaterialForAR = true;

        [Header("Testing (editor)")]
        [Tooltip("Bypass camera measurement and use manualDistance instead. Driven by ProximityTestRig " +
                 "so you can tune the feel in the editor without a device.")]
        public bool useManualDistance = false;
        public float manualDistance = 1.0f;

        // --- Shader property IDs ---
        private static readonly int StressID   = Shader.PropertyToID("_Stress");
        private static readonly int FluorPointID    = Shader.PropertyToID("_FluorPoint");
        private static readonly int EmissionScaleID = Shader.PropertyToID("_EmissionScale");
        private static readonly int SrcBlendID = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendID = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteID   = Shader.PropertyToID("_ZWrite");
        private const string LoupeKeyword = "_LOUPE_ON";

        // --- Runtime state ---
        private Material _mat;
        private Transform _root;
        private Vector3 _baseLocalPos;
        private Vector3 _baseLocalScale;
        private bool _haveBase;
        private float _smoothedDistance = Mathf.Infinity;
        private float _distVel;

        // Appearance-cycle state. The coral must REMEMBER that it peaked, because bleaching
        // is only allowed after fluorescing (and then only while pulling away).
        private bool _hasPeaked;    // reached max closeness -> bleach is armed
        private bool _resetting;    // blending bleached -> natural for the next viewer
        private float _stress;      // current arc value 0..1

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        private void Start()
        {
            if (coralRenderer == null) return;
            _root = coralRoot != null ? coralRoot : coralRenderer.transform;
            _baseLocalPos = _root.localPosition;
            _baseLocalScale = _root.localScale;
            _haveBase = true;

            _mat = coralRenderer.material; // per-renderer instance (auto-freed; never dirties the asset)
            if (configureMaterialForAR && _mat != null)
            {
                _mat.DisableKeyword(LoupeKeyword);                 // no loupe -> the whole coral shows
                _mat.SetFloat(SrcBlendID, (float)BlendMode.One);   // OPAQUE: no see-through, and...
                _mat.SetFloat(DstBlendID, (float)BlendMode.Zero);
                _mat.SetFloat(ZWriteID, 1f);                       // ...depth write -> cups/walls sort correctly
                _mat.renderQueue = (int)RenderQueue.Geometry;
            }

            // Keep the shader's peak-fluorescence point in sync with ours.
            if (_mat != null) _mat.SetFloat(FluorPointID, fluorPoint);
            ResetCycle();
        }

        private void LateUpdate()
        {
            if (coralRenderer == null || cam == null || _mat == null || !_haveBase) return;

            // Tracking-loss gate: Vuforia's DefaultObserverEventHandler hides the coral by
            // disabling the renderer (or the GameObject) on target-lost. Reset + skip so the
            // magnified/bleached state doesn't hang around. (Skipped in manual test mode.)
            // With magnification off the controller must NEVER touch the coral's transform —
            // that keeps registration purely Vuforia's, which is what we want by default.
            bool magnifyEnabled = maxMagnification > 1.0001f;

            if (!useManualDistance &&
                (!coralRenderer.enabled || !coralRenderer.gameObject.activeInHierarchy))
            {
                if (magnifyEnabled) ResetToBase();
                // Viewer has walked off / target lost: snap the cycle back to natural so the
                // next person starts fresh. Invisible right now, so no need to blend it.
                ResetCycle();
                _smoothedDistance = Mathf.Infinity;
                return;
            }

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

            if (float.IsInfinity(_smoothedDistance)) _smoothedDistance = rawDist;
            _smoothedDistance = useManualDistance
                ? rawDist
                : Mathf.SmoothDamp(_smoothedDistance, rawDist, ref _distVel, distanceSmoothTime);
            float d = _smoothedDistance;

            // --- Appearance cycle: natural -> fluorescent -> bleached -> reset ---
            UpdateAppearanceCycle(d);

            // --- Magnification: scale up as the camera nears ---
            float mt = InvLerpClamped(magnifyStartDistance, magnifyFullDistance, d);
            float k = Mathf.Lerp(1f, Mathf.Max(1f, maxMagnification), Mathf.Clamp01(magnifyCurve.Evaluate(mt)));
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
        }

        private void ResetToBase()
        {
            _root.localPosition = _baseLocalPos;
            _root.localScale = _baseLocalScale;
        }

        /// <summary>
        /// Drives the natural -> fluorescent -> bleached -> natural cycle.
        ///
        /// The approach is fully reversible UNTIL the coral peaks. Reaching
        /// <see cref="peakDistance"/> arms the bleach; from then on pulling away drives
        /// fluorescent -> bleached (never back to natural). Once bleached and past
        /// <see cref="resetDistance"/>, it blends back to natural for the next viewer —
        /// with emission suppressed, so passing back through the fluorescent band on the
        /// way down doesn't make it flash.
        /// </summary>
        private void UpdateAppearanceCycle(float d)
        {
            float emissionScale = 1f;

            if (_resetting)
            {
                float step = Time.deltaTime / Mathf.Max(resetBlendTime, 0.01f);
                _stress = Mathf.MoveTowards(_stress, 0f, step);
                emissionScale = 0f;                       // no re-glow on the way back
                if (_stress <= 0.001f)
                {
                    _stress = 0f;
                    _resetting = false;
                    _hasPeaked = false;                   // re-arm for the next viewer
                }
            }
            else if (!_hasPeaked)
            {
                // APPROACH — natural -> peak fluorescence (reversible until it peaks).
                float t = InvLerpClamped(naturalDistance, peakDistance, d);
                _stress = Mathf.Clamp01(approachCurve.Evaluate(t)) * fluorPoint;
                if (d <= peakDistance) _hasPeaked = true; // arm the bleach
            }
            else
            {
                // BLEACH — armed by the peak; pulling away drives fluorescent -> bleached.
                // Guard the ordering. If bleachFullDistance ever ends up <= peakDistance the
                // mapping inverts and the coral reads FULLY BLEACHED at max closeness — the
                // opposite of the intent. (Easy to hit: this field predates the redesign, so a
                // stale serialized value can carry over with the old, inverted meaning.)
                float bleachEnd = Mathf.Max(bleachFullDistance, peakDistance + 0.01f);
                float t = InvLerpClamped(peakDistance, bleachEnd, d);
                _stress = fluorPoint + Mathf.Clamp01(bleachCurve.Evaluate(t)) * (1f - fluorPoint);
                // Once they're far enough away, reset — whether or not the bleach finished.
                // (Requiring a fully-complete bleach could strand it bleached forever if the
                // viewer walked off mid-arc.)
                if (d >= resetDistance) _resetting = true;
            }

            _mat.SetFloat(StressID, _stress);
            _mat.SetFloat(EmissionScaleID, emissionScale);
        }

        /// <summary>Snap the cycle straight back to natural (used on start and tracking loss).</summary>
        private void ResetCycle()
        {
            _stress = 0f;
            _hasPeaked = false;
            _resetting = false;
            if (_mat != null)
            {
                _mat.SetFloat(StressID, 0f);
                _mat.SetFloat(EmissionScaleID, 1f);
            }
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
            if (peakDistance >= naturalDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] peakDistance ({peakDistance}) " +
                    $"must be NEARER (smaller) than naturalDistance ({naturalDistance}) — you approach " +
                    $"from natural to peak fluorescence.", this);

            if (bleachFullDistance <= peakDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] bleachFullDistance " +
                    $"({bleachFullDistance}) must be FARTHER (larger) than peakDistance ({peakDistance}) — " +
                    $"the bleach happens as you pull AWAY from the peak.", this);

            if (resetDistance < bleachFullDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] resetDistance ({resetDistance}) " +
                    $"should be at or beyond bleachFullDistance ({bleachFullDistance}) so it finishes " +
                    $"bleaching before it resets to natural.", this);

            if (magnifyFullDistance >= magnifyStartDistance)
                Debug.LogWarning($"[{nameof(ProximityRevealController)}] magnifyFullDistance " +
                    $"({magnifyFullDistance}) should be NEARER (smaller) than magnifyStartDistance " +
                    $"({magnifyStartDistance}).", this);
        }

        private void OnDrawGizmosSelected()
        {
            Camera c = cam != null ? cam : Camera.main;
            if (c == null) return;
            Vector3 o = c.transform.position, f = c.transform.forward;
            DrawRing(o, f, naturalDistance,      new Color(0.9f, 0.6f, 0.4f, 0.9f)); // natural (resting)
            DrawRing(o, f, peakDistance,         new Color(0.2f, 1f, 0.9f, 0.9f));   // PEAK fluorescence
            DrawRing(o, f, bleachFullDistance,   new Color(1f, 0.25f, 0.2f, 0.9f));  // fully bleached
            DrawRing(o, f, resetDistance,        new Color(0.7f, 0.7f, 0.7f, 0.9f)); // resets to natural
            DrawRing(o, f, magnifyStartDistance, new Color(0.2f, 0.8f, 1f, 0.9f));   // magnify begins
            DrawRing(o, f, magnifyFullDistance,  new Color(1f, 0.9f, 0.2f, 0.9f));   // max magnification
        }

        private static void DrawRing(Vector3 origin, Vector3 fwd, float dist, Color col)
        {
            Gizmos.color = col;
            Gizmos.DrawWireSphere(origin + fwd * dist, dist * 0.12f);
        }
#endif
    }
}
