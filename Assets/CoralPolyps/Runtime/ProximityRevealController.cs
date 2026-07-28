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

        /// <summary>Current magnification factor (1 = life-size).</summary>
        public float Magnification { get; private set; } = 1f;

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
        }

        private void Start()
        {
            if (coralRenderer == null) return;
            _root = coralRoot != null ? coralRoot : coralRenderer.transform;
            _baseLocalPos = _root.localPosition;
            _baseLocalScale = _root.localScale;
            _haveBase = true;

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
                CloseLoupe();
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
            LoupeRadius = 0f;
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
