/// <summary>
/// The last beat of the magnifier: at closest range the footage comes up out of one
/// of the coral's craters and takes the whole screen.
///
/// WHY THIS EXISTS AS A SEPARATE LAYER
/// -----------------------------------
/// MagnifierLoupe.shader paints a duplicate of the coral mesh, projecting the footage
/// in object space. That is what keeps the polyps registered to the same cups from
/// every angle (USER_STORIES V4), and it is worth keeping — but it also means the
/// loupe can never grow past the coral's silhouette. The surface IS its canvas. No
/// amount of maxLoupeRadius reaches the corners of a phone screen.
///
/// So the reveal is two beats, not one:
///
///     0.30 -> 0.12 m   loupe opens on the coral        (object space, registered)
///     0.14 -> 0.05 m   iris opens from a crater, fills (this file, unregistered)
///
/// THE IRIS IS THE POINT
/// ---------------------
/// The takeover does not crossfade. It opens as a circle centred on the corallite the
/// viewer is already aiming at — the same point the loupe uses (ProximityRevealController
/// .LoupeCenter, a raycast through the screen centre onto the coral). So the video
/// appears to emerge FROM a specific cup, which is the claim the whole piece makes:
/// the micro-scale is inside the object, not an illustration beside it.
///
/// The centre keeps tracking that world point while the iris opens, so the video is
/// anchored to the coral right up until it fills the frame. Only once it has covered
/// the screen does registration stop mattering — and that is also, conveniently, about
/// where Vuforia gives up on a ~10 cm Model Target. The tracking loss happens behind
/// a full screen of polyps and is never seen.
///
/// This is NOT the screen-space sampling that CONTROL_INTEGRATION.md §11 rejected.
/// That was sampling footage in screen space *while it sat on the coral*, so content
/// slid across the cups. Here the coral has left the frame entirely.
///
/// WHY uGUI AND NOT A FULLSCREEN BLIT
/// ----------------------------------
/// A ScriptableRendererFeature would be the "proper" URP way and costs a renderer
/// asset edit, a feature and a pass — all of it invisible in the scene diff. One
/// RawImage with a custom material does the same job in one draw call and is
/// inspectable. The UI is built in code rather than in the scene for the same reason
/// SceneBuilder exists: wiring that lives only in a scene file is the wiring this
/// project already lost once.
/// </summary>
using UnityEngine;
using UnityEngine.UI;

namespace CoralPolyps
{
    [RequireComponent(typeof(CoralMagnifier))]
    public class FullscreenMagnifier : MonoBehaviour
    {
        [Tooltip("Drives the takeover via FullscreenReveal, and supplies the crater the " +
                 "iris opens from. Found in the scene if left empty.")]
        public ProximityRevealController proximity;

        [Tooltip("Assign MagnifierFullscreen.shader so it is guaranteed to ship in the build " +
                 "— a shader only reachable through Shader.Find gets stripped on iOS.")]
        public Shader fullscreenShader;

        [Tooltip("Softness of the iris edge as a FRACTION of its radius, so the rim reads the " +
                 "same whether the opening is one corallite or the whole screen. A fixed " +
                 "width is invisible on a small iris and a huge smear on a full one. " +
                 "Generous reads as optics; a hard edge reads as a cut-out.")]
        [Range(0.01f, 0.9f)] public float irisEdgeSoftness = 0.35f;

        // THE IRIS IS SIZED IN THE CORAL'S METRES, NOT IN SCREEN FRACTIONS.
        //
        // A screen-space ramp (radius = coverage x screen) was the first attempt and it
        // is wrong in a way that is obvious once seen: at any midpoint it is a large
        // blob spread over many corallites, which reads as a video pasted on top of the
        // coral. A real lens shows a circle of fixed PHYSICAL size on the object, and
        // that circle grows on screen only because you are getting closer.
        //
        // So the radius is a world length anchored at the corallite, projected to the
        // screen each frame. Lean in and it swells the way a real magnified spot does,
        // because it is the same geometry doing it. Full coverage arrives on its own
        // when the projected circle outgrows the screen — no separate ramp needed.
        [Header("Iris size (metres on the coral)")]
        [Tooltip("Radius when the takeover begins. A Goniastrea corallite is roughly 8 mm " +
                 "across, so ~0.003 keeps the opening inside a single cup, between the walls.")]
        public float irisWorldRadiusStart = 0.003f;

        [Tooltip("Radius at full approach. Needs to be large enough that its projection " +
                 "outruns the screen — around the coral's own radius works.")]
        public float irisWorldRadiusEnd = 0.075f;

        [Tooltip("Coverage at which the footage starts migrating from crater-fitted to " +
                 "full-screen 100% (finishing at ~0.95). Kept LATE so the content stays " +
                 "locked to crater scale for most of the approach — migrating early is " +
                 "what made the footage read as too large from the very start.")]
        [Range(0f, 0.9f)] public float settleStartCoverage = 0.75f;

        [Tooltip("How much of the coral's on-screen silhouette the iris may occupy while the " +
                 "leash is on. Below 1 keeps the rim visibly inside the coral's edge.")]
        [Range(0.5f, 1f)] public float coralEdgeMargin = 0.9f;

        [Tooltip("Coverage above which the silhouette leash is released so maximum " +
                 "magnification can fill the whole screen. Below this the iris is strictly " +
                 "bounded by the coral. Raise it to keep the iris on the coral for longer; " +
                 "lower it if full-screen is being reached too reluctantly.")]
        [Range(0.5f, 1f)] public float clampReleaseCoverage = 0.8f;

        [Tooltip("Sorting order for the overlay canvas. Above the AR view; CoralHud is IMGUI " +
                 "and always draws last, so diagnostics stay readable at full cover.")]
        public int sortingOrder = 100;

        /// <summary>Current screen coverage, 0..1. Shown in the HUD.</summary>
        public float Coverage { get; private set; }

        /// <summary>
        /// What the iris is ACTUALLY covering this frame: the opaque core's radius as a
        /// fraction of the distance to the farthest screen corner. 1 means every pixel is
        /// footage.
        ///
        /// This is not the same thing as <see cref="Coverage"/>, and conflating the two
        /// was a real bug. Coverage is derived from DISTANCE; the rendered radius is
        /// additionally clamped to the coral's on-screen silhouette. Whenever the coral
        /// does not fill the frame those two disagree — and the controller, hiding the
        /// coral on Coverage alone, was hiding the tissue while the footage still had
        /// transparent feathered edges. The viewer then saw straight through to the bare
        /// print and the table, with no coral material anywhere. Anything deciding
        /// "is the screen covered?" must ask THIS.
        /// </summary>
        public float ScreenCoverage01 { get; private set; }

        /// <summary>True when the opaque core reaches the farthest corner — genuinely
        /// every pixel. The only safe condition under which to hide the coral.</summary>
        public bool ScreenFullyCovered => ScreenCoverage01 >= 0.999f;

        static readonly int TexAID   = Shader.PropertyToID("_TexA");
        static readonly int TexBID   = Shader.PropertyToID("_TexB");
        static readonly int BlendID  = Shader.PropertyToID("_Blend");
        static readonly int CenterID = Shader.PropertyToID("_Center");
        static readonly int RadiusID = Shader.PropertyToID("_Radius");
        static readonly int FeatherID = Shader.PropertyToID("_Feather");
        static readonly int SettleID = Shader.PropertyToID("_Settle");
        static readonly int ScreenAspectID = Shader.PropertyToID("_ScreenAspect");
        static readonly int FootageAspectID = Shader.PropertyToID("_FootageAspect");
        static readonly int OpacityID = Shader.PropertyToID("_Opacity");

        CoralMagnifier _magnifier;
        RawImage _image;
        Material _mat;
        Vector2 _center = new Vector2(0.5f, 0.5f);
        float _heldRadius;

        void Awake()
        {
            _magnifier = GetComponent<CoralMagnifier>();
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (proximity == null)
                Debug.LogWarning($"[{nameof(FullscreenMagnifier)}] no ProximityRevealController — " +
                                 "the takeover will never open.", this);
            Build();
        }

        void Build()
        {
            var shader = fullscreenShader != null ? fullscreenShader : Shader.Find("CoralPolyps/MagnifierFullscreen");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(FullscreenMagnifier)}] MagnifierFullscreen.shader not found — " +
                               "assign it on this component.", this);
                enabled = false;
                return;
            }
            _mat = new Material(shader);

            // No GraphicRaycaster: nothing here is interactive, and adding one would
            // imply an EventSystem this scene does not have.
            var canvasGO = new GameObject("MagnifierFullscreen", typeof(Canvas), typeof(CanvasGroup));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var group = canvasGO.GetComponent<CanvasGroup>();
            // Never intercept touches: the HUD's three-finger tap must keep working even
            // at full cover, or a mis-tuned arc is unrecoverable on a device with no console.
            group.blocksRaycasts = false;
            group.interactable = false;

            var go = new GameObject("Footage", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(canvasGO.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            _image = go.GetComponent<RawImage>();
            _image.raycastTarget = false;
            _image.material = _mat;
            // The shader samples _TexA/_TexB itself; this only has to be non-null so uGUI
            // issues the draw call at all.
            _image.texture = Texture2D.whiteTexture;
            _image.enabled = false;
        }

        void LateUpdate()
        {
            // After CoralMagnifier's LateUpdate has resolved the pair for this frame.
            // Reading a stale Blend during the handover would let the two layers disagree,
            // which is the one thing §3.2 forbids.
            Coverage = proximity != null ? Mathf.Clamp01(proximity.FullscreenReveal) : 0f;

            if (_mat == null) return;

            if (!_magnifier.SourceReady || Coverage <= 0.0001f)
            {
                // Not decoding yet, or fully closed. Covering the screen with black would
                // be worse than not covering it — hold the AR view.
                ScreenCoverage01 = 0f;
                _image.enabled = false;
                return;
            }
            _image.enabled = true;

            float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0.5f;
            bool held = proximity != null && proximity.TakeoverHeld;
            float corner = CornerDistance(screenAspect);

            // How far the footage has migrated from crater-fitted to screen-fitted
            // (_Settle in the shader). Held late deliberately: the content stays locked
            // to crater scale for most of the approach and only relaxes to 100%
            // full-screen at the very end, once the rim is about to leave the frame.
            float settle = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(settleStartCoverage, 0.95f, Coverage));

            // The feather stays PROPORTIONAL the whole way. A soft rim is the optics of
            // the thing; tapering it away mid-flight produced a hard expanding circle,
            // which was tried on device and read as a cut-out, not a lens. Crispness at
            // full cover comes from the radius cap below instead — capped at
            // corner / (1 - featherFrac), the opaque core lands exactly on the far
            // corner, so the entire soft band sits OFF-screen when the takeover is
            // total. Soft whenever the rim is visible; pixel-crisp once it is not.
            float featherFrac = Mathf.Min(irisEdgeSoftness, 0.9f);

            float radius;
            if (held)
            {
                // Pose untrusted: the controller has frozen Coverage, and the radius and
                // centre freeze with it. Nothing is recomputed from the stale transform.
                // Everything resumes from exactly here when trust returns.
                radius = _heldRadius;
            }
            else
            {
                TrackCrater();

                // The physical size of the magnified spot on the coral. Small at the
                // start — one corallite, between the walls — opening out as the viewer
                // commits. Purely a function of distance: phone still => spot still.
                float worldRadius = Mathf.Lerp(irisWorldRadiusStart, irisWorldRadiusEnd, Coverage);
                radius = ProjectedRadius(worldRadius);

                // Cap where the OPAQUE CORE (radius - feather) reaches the far corner:
                // radius = corner / (1 - featherFrac). The earlier corner*(1+softness)
                // cap got this wrong — the core stopped at ~0.88x corner and the screen
                // edges sat permanently inside the soft band (the "blurry ring").
                float cornerCap = corner / (1f - featherFrac);

                // THE IRIS STAYS ON THE CORAL — until the very end.
                //
                // A lens can only magnify what it is held over, so for the bulk of the
                // approach the opening is clamped to the coral's own on-screen
                // silhouette. But that leash, left on, makes FULL SCREEN unreachable: the
                // silhouette is measured conservatively (the smaller of the coral's
                // on-screen half-height and half-width, times coralEdgeMargin), so it can
                // sit below the corner distance even at maximum magnification, and the
                // takeover could never complete.
                //
                // So the leash is released across the final stretch of the arc. Below
                // clampReleaseCoverage the iris is strictly bounded by the coral; above
                // it, the bound opens out to the corner cap and maximum magnification
                // always fills every pixel. By then the coral fills the frame anyway, so
                // the two bounds are nearly the same number and the release is invisible.
                float silhouette = CoralSilhouetteRadius(screenAspect) * coralEdgeMargin;
                float release = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(clampReleaseCoverage, 1f, Coverage));
                float leash = Mathf.Lerp(silhouette, cornerCap, release);

                radius = Mathf.Min(radius, Mathf.Max(leash, 0f));
                radius = Mathf.Min(radius, cornerCap);
                _heldRadius = radius;
            }

            // Fade the first instant so the opening does not pop into existence at full
            // strength; after that the geometry alone carries it.
            float opacity = Mathf.Clamp01(Coverage / 0.12f);

            // What is genuinely covered: the OPAQUE core, not the feathered rim. This is
            // the number the coral-hiding decision is allowed to trust.
            ScreenCoverage01 = corner > 1e-4f
                ? Mathf.Clamp01(radius * (1f - featherFrac) / corner) * opacity
                : 0f;

            _mat.SetTexture(TexAID, _magnifier.ClipA);
            _mat.SetTexture(TexBID, _magnifier.ClipB);
            _mat.SetFloat(BlendID, _magnifier.Blend);
            _mat.SetVector(CenterID, new Vector4(_center.x, _center.y, 0f, 0f));
            _mat.SetFloat(RadiusID, radius);
            _mat.SetFloat(FeatherID, Mathf.Max(radius * featherFrac, 1e-4f));
            _mat.SetFloat(SettleID, settle);
            _mat.SetFloat(ScreenAspectID, screenAspect);
            _mat.SetFloat(FootageAspectID, FootageAspect());
            _mat.SetFloat(OpacityID, opacity);
        }

        /// <summary>
        /// Follow the corallite the loupe is centred on, projected to viewport space, so
        /// the iris stays pinned to that cup while it opens. Falls back to the last known
        /// point when the target is behind the camera — WorldToViewportPoint mirrors the
        /// result there, which would fling the iris to the opposite corner for a frame.
        /// </summary>
        void TrackCrater()
        {
            if (proximity == null || proximity.cam == null) return;
            Vector3 vp = proximity.cam.WorldToViewportPoint(proximity.LoupeCenter);
            if (vp.z > 0f) _center = new Vector2(vp.x, vp.y);
        }

        /// <summary>
        /// Project a world-space circle at the corallite onto the screen, in the units
        /// the shader's mask uses (screen HEIGHTS — the shader corrects x by the aspect,
        /// so a viewport-y delta is already the right unit).
        ///
        /// Measured with the camera's own up vector rather than computed from FOV, so it
        /// stays correct through the magnification arc, any FOV the AR session picks, and
        /// whatever projection Vuforia hands the camera. Returns 0 if the point is behind
        /// the camera.
        /// </summary>
        float ProjectedRadius(float worldRadius)
        {
            var cam = proximity != null ? proximity.cam : null;
            if (cam == null) return 0f;

            Vector3 c = proximity.LoupeCenter;
            Vector3 vpC = cam.WorldToViewportPoint(c);
            if (vpC.z <= 0f) return 0f;

            Vector3 vpEdge = cam.WorldToViewportPoint(c + cam.transform.up * worldRadius);
            return Mathf.Abs(vpEdge.y - vpC.y);
        }

        /// <summary>
        /// True once the iris has swallowed the farthest corner, whatever the centre. An
        /// iris anchored off to one side has further to travel than one at the middle, so
        /// this is what stops a wedge of camera feed surviving in a corner exactly when
        /// the takeover is meant to be total.
        /// </summary>
        float CornerDistance(float screenAspect)
        {
            float dx = Mathf.Max(_center.x, 1f - _center.x) * screenAspect;
            float dy = Mathf.Max(_center.y, 1f - _center.y);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// The coral's own projected radius on screen, in the mask's units (screen
        /// heights). This is the leash that keeps the iris on the coral: measured from
        /// the renderer's bounds along the camera's up and right axes, taking the
        /// smaller of the two so the clamp stays conservatively INSIDE the silhouette
        /// (the bounds themselves overestimate an irregular dome). Returns +inf when it
        /// cannot be measured, which simply leaves the other caps in charge.
        /// </summary>
        float CoralSilhouetteRadius(float screenAspect)
        {
            var cam = proximity != null ? proximity.cam : null;
            var rend = proximity != null ? proximity.coralRenderer : null;
            if (cam == null || rend == null) return float.PositiveInfinity;

            Bounds b = rend.bounds;
            Vector3 c = b.center;
            Vector3 vpC = cam.WorldToViewportPoint(c);
            if (vpC.z <= 0f) return float.PositiveInfinity;

            // Bounds extents are world-axis-aligned; project them onto the camera's own
            // axes the same way the proximity controller measures its surface offset.
            Vector3 e = b.extents;
            Vector3 up = cam.transform.up, right = cam.transform.right;
            float extUp = Mathf.Abs(up.x) * e.x + Mathf.Abs(up.y) * e.y + Mathf.Abs(up.z) * e.z;
            float extRight = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.y) * e.y + Mathf.Abs(right.z) * e.z;

            Vector3 vpUp = cam.WorldToViewportPoint(c + up * extUp);
            Vector3 vpRight = cam.WorldToViewportPoint(c + right * extRight);
            float rUp = Mathf.Abs(vpUp.y - vpC.y);
            float rRight = Mathf.Abs(vpRight.x - vpC.x) * screenAspect;
            return Mathf.Min(rUp, rRight);
        }

        float FootageAspect()
        {
            var t = _magnifier.ClipA != null ? _magnifier.ClipA : _magnifier.ClipB;
            return (t != null && t.height > 0) ? (float)t.width / t.height : 0.5625f;
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
