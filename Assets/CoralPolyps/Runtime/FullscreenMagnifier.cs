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
        // ONE CUP ACROSS, IN THE UNITS THIS FIELD IS ACTUALLY READ IN. The baked map's
        // median pitch is 4.22 mm in the mesh's OBJECT space, but ProjectedRadius offsets
        // a WORLD point by `cam.transform.up * worldRadius` — and SceneBuilder.AlignScale
        // is 1.37, so a cup is 5.78 mm out here. The previous 0.0018 was that object-space
        // figure used as if it were world, which made the opening 0.62 of a cup while
        // claiming to be one.
        [Tooltip("Radius when the takeover begins, in WORLD metres. A corallite on this " +
                 "scan is 5.78 mm across in world space, so 0.0029 is an opening of exactly " +
                 "one cup — between the walls, which is the claim the pinning makes.")]
        public float irisWorldRadiusStart = 0.0029f;

        // A FLOOR NOW, NOT THE DESTINATION. The opening ends on the coral as drawn
        // (ProximityRevealController.CoralWorldRadiusM); this only catches the case where that
        // cannot be measured — no mesh, no controller — and keeps the old guarantee that the
        // iris can still outrun the screen at the near end of the arc.
        [Tooltip("Smallest allowed end-of-arc radius, in world metres. The opening normally " +
                 "ends on the coral's own covering radius, which is larger; this is the floor " +
                 "used if that cannot be measured. Large enough that its projection outruns " +
                 "the screen at fullscreenFullDistance.")]
        public float irisWorldRadiusEnd = 0.075f;

        // HOW BIG THE OPENING IS AND HOW BIG THE POLYPS ARE ARE TWO QUESTIONS.
        //
        // They used to be one number. The shader fitted the clip's width to the iris
        // diameter, so the footage's scale was decided by the mask: at emergence the
        // whole 720x1280 frame was squeezed into a 3.6 mm opening and the clip's central
        // polyp drew about a THIRD of the cup it was supposedly coming out of. The loupe
        // layer underneath was drawing that same polyp several times larger at the same
        // instant, so the handover shrank the footage by an order of magnitude and the
        // polyps then had to swell back — which is the "emerging from nothing" this
        // separates out.
        //
        // WHERE THE DEFAULT COMES FROM, so it can be re-judged rather than re-derived:
        // the baked map's median corallite pitch is 4.22 mm in the mesh's OBJECT units,
        // and SceneBuilder.AlignScale is 1.37, so a cup is 5.78 mm in the WORLD metres
        // this field is measured in. The clip's central polyp fills roughly 0.6 of the
        // frame width, so 5.78 / 0.6 puts that polyp at exactly one cup. The 0.6 is
        // eyeballed from the footage and is the number to change if the emergence scale
        // looks wrong on device — re-encoding the clips at a different framing changes
        // it and nothing here will notice.
        [Tooltip("Metres of coral spanned by the clip's WIDTH at emergence. At 0.0096 the " +
                 "footage's central polyp is drawn the same size as a real corallite cup, " +
                 "so the magnifier starts at 1x and earns every bit of magnification after " +
                 "that. Larger than the opening is normal and intended: you see the middle " +
                 "of the clip through a cup-sized hole.")]
        public float footageWorldWidthStart = 0.0096f;

        // WHERE THE GROWTH SITS ALONG THE ARC.
        //
        // Linear in coverage, the projected iris reaches cornerCap — the point at which it
        // already covers the screen and is clamped — at roughly coverage 0.7, and then sits
        // flat for the last third of the approach. So the polyps finish growing well before
        // the viewer finishes leaning in, and the coral (still scaling toward
        // maxMagnification) visibly overtakes them.
        //
        // The fix is not a bigger final size; screen-filling is exactly the right
        // destination, and cropping past it costs the surrounding colony. It is to hold the
        // opening SMALLER for longer, so the same growth is spent later and it is still
        // accelerating when it arrives. Slower early reads as faster late, which is the
        // asymmetry a lens actually has.
        [Tooltip("Shapes coverage (0..1) -> where the iris sits between its start and end " +
                 "radius. Accelerating by default: held near one corallite through the middle " +
                 "of the approach, then springing as it fills. Straighten it toward linear if " +
                 "the opening now feels like it lingers too long.")]
        public AnimationCurve irisGrowth = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.2f, 0f));

        [Tooltip("Where the footage starts migrating from crater scale to full-screen 100%, " +
                 "as a fraction of the OPENING's native extent — 0.5 means the migration " +
                 "begins when the opening is half the size the clip will be at 100%, and " +
                 "completes exactly as it reaches it. Measured against the opening rather " +
                 "than against coverage so the clip is never forced past native scale and " +
                 "then pulled back; see the note in LateUpdate.")]
        [Range(0.1f, 0.95f)] public float settleStartFraction = 0.5f;

        // SCREEN-FILLING IS THE RIGHT DESTINATION. Going past it crops into the clip, and
        // the thing that gets cropped away is the surrounding colony — the context that
        // makes a single polyp read as one animal among thousands rather than as an
        // abstract texture. Left at 1; the mechanism stays because a future higher-
        // resolution master might make a small push past 100% affordable, but it is a
        // considered trade against losing the colony, not a free knob.
        [Header("End zoom (past 100%)")]
        [Tooltip("Footage magnification once cover-fitted. 1 = fill the screen and stop, " +
                 "which is what you want. Above 1 crops in and the surrounding colony is the " +
                 "first thing lost.")]
        [Range(1f, 3f)] public float endZoomMax = 1f;

        [Tooltip("Coverage at which the zoom past 100% begins. Irrelevant while endZoomMax is 1.")]
        [Range(0f, 1f)] public float endZoomStartCoverage = 0.72f;

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

        /// <summary>
        /// Metres of coral the clip's WIDTH currently spans — the footage's magnification
        /// expressed in the one unit that can be judged against the object it is coming
        /// out of. At emergence it should read the corallite pitch (5.8 mm) divided by the
        /// polyp's share of the frame, i.e. ~9.6 mm. Shown in the HUD because neither this
        /// nor the leash below is observable from a plinth, and both are the numbers being
        /// tuned.
        /// </summary>
        public float FootageWorldWidthM { get; private set; }

        /// <summary>The silhouette leash applied this frame, in screen heights, after
        /// coralEdgeMargin and before the release ramp. +inf means it could not be
        /// measured and the corner cap is in sole charge.</summary>
        public float LeashRadius { get; private set; } = float.PositiveInfinity;

        /// <summary>
        /// Which term is holding the opening down this frame: <c>arc</c> (the distance-driven
        /// growth — the normal answer), <c>leash</c> (the coral's silhouette), or <c>cap</c>
        /// (the screen corner, i.e. the takeover is complete).
        ///
        /// Exists because the iris radius is a three-way <c>Min</c> and the screen looks the
        /// same whichever term wins, so a mis-sized cap is indistinguishable from a mis-shaped
        /// curve by eye. Reading <c>leash</c> when the footage is cutting off inside the tissue
        /// means the bound is wrong; reading <c>arc</c> means the growth is.
        /// </summary>
        public string IrisLimit { get; private set; } = "-";

        static readonly int TexAID   = Shader.PropertyToID("_TexA");
        static readonly int TexBID   = Shader.PropertyToID("_TexB");
        static readonly int BlendID  = Shader.PropertyToID("_Blend");
        static readonly int CenterID = Shader.PropertyToID("_Center");
        static readonly int RadiusID = Shader.PropertyToID("_Radius");
        static readonly int ContentRadiusID = Shader.PropertyToID("_ContentRadius");
        static readonly int FeatherID = Shader.PropertyToID("_Feather");
        static readonly int SettleID = Shader.PropertyToID("_Settle");
        static readonly int EndZoomID = Shader.PropertyToID("_EndZoom");
        static readonly int ScreenAspectID = Shader.PropertyToID("_ScreenAspect");
        static readonly int FootageAspectID = Shader.PropertyToID("_FootageAspect");
        static readonly int OpacityID = Shader.PropertyToID("_Opacity");

        CoralMagnifier _magnifier;
        RawImage _image;
        Material _mat;
        Vector2 _center = new Vector2(0.5f, 0.5f);
        float _heldRadius;
        float _heldContentRadius;

        void Awake()
        {
            _magnifier = GetComponent<CoralMagnifier>();
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (proximity == null)
                Debug.LogWarning($"[{nameof(FullscreenMagnifier)}] no ProximityRevealController — " +
                                 "the takeover will never open.", this);

            // The defocus is part of the takeover, not an option beside it: an iris opening
            // over a perfectly sharp coral reads as a video pasted on top, which is the whole
            // failure it exists to fix. SceneBuilder adds it explicitly so it is visible in
            // the hierarchy; this catches a scene built before it existed, so the feature
            // does not depend on somebody remembering a drag.
            if (GetComponent<MagnifierDefocus>() == null)
                gameObject.AddComponent<MagnifierDefocus>();

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

            // The clip's half-width once cover-fitted to the screen, in the mask's units.
            // This is the DESTINATION scale — where _Settle lands and where the footage
            // stops magnifying — and it is the number the migration below is measured
            // against rather than a coverage threshold. See the note on settleStartFraction.
            float nativeHalf = Mathf.Max(FootageAspect(), screenAspect) * 0.5f;

            // The feather stays PROPORTIONAL the whole way. A soft rim is the optics of
            // the thing; tapering it away mid-flight produced a hard expanding circle,
            // which was tried on device and read as a cut-out, not a lens. Crispness at
            // full cover comes from the radius cap below instead — capped at
            // corner / (1 - featherFrac), the opaque core lands exactly on the far
            // corner, so the entire soft band sits OFF-screen when the takeover is
            // total. Soft whenever the rim is visible; pixel-crisp once it is not.
            float featherFrac = Mathf.Min(irisEdgeSoftness, 0.9f);

            float radius, contentRadius;
            if (held)
            {
                // Pose untrusted: the controller has frozen Coverage, and the radius and
                // centre freeze with it. Nothing is recomputed from the stale transform.
                // Everything resumes from exactly here when trust returns.
                radius = _heldRadius;
                contentRadius = _heldContentRadius;
            }
            else
            {
                TrackCrater();

                // The physical size of the magnified spot on the coral. Small at the
                // start — one corallite, between the walls — opening out as the viewer
                // commits. Purely a function of distance: phone still => spot still.
                //
                // Shaped rather than linear, so the growth is spent late and the opening is
                // still accelerating as it fills the screen instead of having arrived a
                // third of the way back. See the note on irisGrowth.
                float g = Mathf.Clamp01(irisGrowth.Evaluate(Coverage));

                // THE ARC ENDS ON THE CORAL, NOT ON A NUMBER.
                //
                // It used to end at a fixed irisWorldRadiusEnd, and that number was chosen to
                // outgrow the SCREEN at 5 cm — never to cover the CORAL at 7-17 cm. They are
                // not the same requirement and the coral is the larger one: this mesh's
                // covering radius is ~105 mm at life size, so a 70 mm opening could not reach
                // the coral's edge even before magnification, and the magnification arc then
                // took the coral to 2.5x while the opening stayed put. The visible result is a
                // ring of bare tissue around the footage that never closes — the dead space.
                //
                // Ending on ProximityRevealController.CoralWorldRadiusM makes the opening
                // finish exactly covering the coral AS DRAWN, and grow with it. That value is
                // a constant times the magnification, so this is still a pure function of `d`.
                float worldEnd = Mathf.Max(
                    proximity != null ? proximity.CoralWorldRadiusM : 0f,
                    irisWorldRadiusEnd);

                float worldRadius = Mathf.Lerp(irisWorldRadiusStart, worldEnd, g);
                radius = ProjectedRadius(worldRadius);

                // Cap where the OPAQUE CORE (radius - feather) reaches the far corner:
                // radius = corner / (1 - featherFrac). The earlier corner*(1+softness)
                // cap got this wrong — the core stopped at ~0.88x corner and the screen
                // edges sat permanently inside the soft band (the "blurry ring").
                float cornerCap = corner / (1f - featherFrac);

                // THE IRIS STAYS ON THE CORAL — and this is now the BACKSTOP, not the rule.
                //
                // "A lens can only magnify what it is held over" used to be enforced here, by
                // capping the opening at the coral's on-screen silhouette. It is now enforced
                // by the arc's endpoint above, which is a better place for it: a cap can only
                // ever make the opening smaller than the coral, which is precisely the dead
                // ring this change exists to close. Driving it to the coral does the job the
                // cap was standing in for.
                //
                // What survives is a guard against the endpoint being wrong — worldEnd falls
                // back to irisWorldRadiusEnd when the coral cannot be measured — plus the
                // release, which is still what guarantees the takeover can reach every pixel.
                // Below clampReleaseCoverage the opening is bounded by the coral; above it the
                // bound opens out to the corner cap.
                //
                // In normal service this no longer binds: worldEnd IS the coral's radius, so
                // the arc can only reach the cap at g = 1, by which point the release has
                // opened it. "The coral" means the coral AS DRAWN — magnified — not the print
                // it is registered to. See CoralSilhouetteRadius.
                float silhouette = CoralSilhouetteRadius() * coralEdgeMargin;
                LeashRadius = silhouette;
                float release = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(clampReleaseCoverage, 1f, Coverage));

                // "Could not be measured" has to mean NO LEASH, not a NaN. Lerp is
                // a + (b-a)*t, so from +inf that is inf + (-inf)*t — NaN for every t,
                // including 0 — and a NaN radius makes the iris vanish rather than open.
                // The corner cap below is the answer either way.
                float leash = float.IsPositiveInfinity(silhouette)
                    ? cornerCap
                    : Mathf.Lerp(silhouette, cornerCap, release);

                // WHICH OF THE THREE WON. min(a, b, c) throws that away, and not knowing it
                // cost a whole build cycle: the leash was fixed while the ARC was the term
                // actually holding the opening down, and the screen looks identical either
                // way. One word in the HUD is the difference between reading it and guessing.
                float bound = Mathf.Min(Mathf.Max(leash, 0f), cornerCap);
                IrisLimit = radius <= bound ? "arc"
                          : cornerCap <= Mathf.Max(leash, 0f) ? "cap" : "leash";
                radius = Mathf.Min(radius, bound);

                // THE FOOTAGE'S OWN SCALE — resolved AFTER the opening, because it has to
                // answer to it.
                //
                // Its own arc is the emergence: one polyp at life size, magnifying as the
                // viewer leans in. The Max is the fill constraint — mapping A draws the clip
                // across _ContentRadius, so an opening WIDER than the clip runs the UVs
                // outside [0,1] and the rim fills with whatever the RenderTexture's wrap mode
                // does. The clip must be at least as wide as the hole it is seen through.
                //
                // Tying the content's ENDPOINT to the opening's was the wrong way to satisfy
                // that. The opening now ends on the coral (up to 262 mm), so the footage got
                // dragged to ~3x native scale mid-arc and _Settle's migration to native then
                // pulled it back out — the footage zoomed in and visibly retracted. A Max
                // costs the same and only binds when it must, so the clip is never drawn
                // larger than the hole actually requires.
                //
                // NOT clamped by the leash or the corner cap: those bound the MASK, and a lens
                // that is partly occluded still magnifies by the same amount.
                float contentWorldRadius =
                    Mathf.Lerp(footageWorldWidthStart * 0.5f, irisWorldRadiusEnd, g);
                contentRadius = Mathf.Max(ProjectedRadius(contentWorldRadius), radius);

                float perMetre = ProjectedRadius(1f);
                FootageWorldWidthM = perMetre > 1e-6f ? 2f * contentRadius / perMetre : 0f;

                _heldRadius = radius;
                _heldContentRadius = contentRadius;
            }

            // THE MIGRATION IS MEASURED AGAINST THE OPENING, NOT AGAINST COVERAGE.
            //
            // _Settle carries the footage from crater-fitted (mapping A, a physical size on
            // the coral) to screen-fitted (mapping B, the clip at native 100%). It used to run
            // on a pair of coverage thresholds, and that only worked while the opening grew
            // slowly enough to still be smaller than the clip when the migration began.
            //
            // Three requirements meet here and two of them are hard:
            //   * the opening must be big     — it has to reach the tissue's edge
            //   * the clip must fill the opening — or mapping A samples off the end of the video
            //   * the clip must END at native — that is where mapping B lands
            // An opening larger than the clip-at-native forces the clip PAST native, and
            // _Settle then has to bring it back down. That overshoot-and-retract is exactly
            // what a coverage threshold cannot prevent, because it does not know how big the
            // opening got.
            //
            // Measuring against nativeHalf removes the failure rather than tuning around it:
            // the migration completes precisely as the opening reaches the clip's native
            // extent, so the clip is never required to exceed native and never has to come
            // back. It is also self-tuning — retune irisGrowth or the arc distances and this
            // still lands in the right place, where two hand-picked coverage numbers would
            // silently drift.
            //
            // The old reason for keeping it late holds automatically: early in the approach
            // the opening is a few millimetres, far below the start fraction, so the footage
            // stays at crater scale and does not read as too large from the very start.
            float settle = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(nativeHalf * settleStartFraction, nativeHalf, radius));

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
            _mat.SetFloat(ContentRadiusID, Mathf.Max(contentRadius, 1e-5f));
            _mat.SetFloat(FeatherID, Mathf.Max(radius * featherFrac, 1e-4f));
            _mat.SetFloat(SettleID, settle);

            // Accelerating rather than linear, so the last centimetres of the approach are
            // where the polyps genuinely spring rather than merely continue. Squared is the
            // same shape the takeover curve uses, for the same reason.
            float ez = Mathf.InverseLerp(endZoomStartCoverage, 1f, Coverage);
            _mat.SetFloat(EndZoomID, Mathf.Lerp(1f, Mathf.Max(1f, endZoomMax), ez * ez));
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
        /// The coral's projected radius on screen, in the mask's units (screen heights).
        /// This is the leash that keeps the iris on the coral.
        ///
        /// MEASURED AT THE CRATER, WHICH IS THE WHOLE FIX. It used to project the coral's
        /// bounds from the BOUNDS CENTRE while the iris was projected from LoupeCenter —
        /// two different depths, then compared with a Min as though they were the same
        /// quantity. The coral's own half-extents are ~70 mm, and the magnification block
        /// makes the gap far worse rather than merely imprecise: scaling about the pinned
        /// cup pins the ANCHOR, so the bounds centre retreats by (k-1) x the anchor-to-
        /// centre distance. At 5 cm and k = 2.5 the near face is 0.05 m from the camera
        /// while the centre is ~0.22 m, so the leash was computed as if the coral were a
        /// flat disc four and a half times further away than the surface being looked at.
        ///
        /// The visible result was the footage cutting off well inside the magnified
        /// tissue: the video stopped at roughly where the PRINT ends while the health
        /// material carried on past it.
        ///
        /// Projecting through ProjectedRadius fixes the second, quieter half of the same
        /// error too — the old measurement was a radius about the CORAL's centre, but the
        /// iris is centred on the pinned corallite, which can sit near the rim. Both are
        /// now measured from the same point.
        ///
        /// The world radius comes from ProximityRevealController rather than being
        /// re-derived from coralRenderer.bounds here. That component owns the coral's
        /// transform and knows the magnification, and reading its output removes an
        /// unstated dependency on which of the two LateUpdates happens to run first —
        /// neither declares an execution order, so today's answer is arbitrary.
        ///
        /// Returns +inf when it cannot be measured, which simply leaves the other caps
        /// in charge rather than shutting the iris.
        /// </summary>
        float CoralSilhouetteRadius()
        {
            float worldRadius = proximity != null ? proximity.CoralWorldRadiusM : 0f;
            if (worldRadius <= 0f) return float.PositiveInfinity;

            float projected = ProjectedRadius(worldRadius);
            return projected > 0f ? projected : float.PositiveInfinity;   // behind the camera
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
