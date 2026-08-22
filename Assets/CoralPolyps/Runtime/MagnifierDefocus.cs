/// <summary>
/// Everything except the footage falls out of focus as the polyps emerge.
///
/// WHY IT READS AS A LENS
/// ----------------------
/// A magnifying glass has one focal plane. Hold it close enough to resolve a
/// corallite and the room behind it is gone — not dimmed, not darkened, defocused.
/// Until now the takeover was an iris opening over a perfectly sharp coral, which
/// says "a video is being drawn on top of this". Blurring what is behind it says
/// the opposite: the sharp thing is what you are looking THROUGH the glass at, and
/// everything else is simply not where your eye is.
///
/// It also does real work for the illusion at the moment it is most fragile. The
/// last few centimetres are where Vuforia's pose gets soft and where the coral is
/// most likely to be sitting a millimetre off the print — and a defocused mesh is
/// a mesh whose registration cannot be judged. The blur rises exactly as the
/// tracking degrades, which is not a coincidence worth hiding: both are functions
/// of the same distance.
///
/// HOW IT IS DONE
/// --------------
/// A dedicated global Volume carrying nothing but a Gaussian Depth of Field, whose
/// WEIGHT is driven from the reveal. Weight rather than gaussianMaxRadius because
/// URP clamps that parameter to [0.5, 1.5] — it has no "off", so ramping it would
/// pop into 0.5 worth of blur the instant the override went active. Weight blends
/// the whole override in from zero, which is the ramp we actually want.
///
/// Its own volume, not the existing Bloom one: bloom must stay at full strength the
/// entire time (it is what makes the fluorescent tissue read as glow rather than as
/// bright paint), and a shared volume would drag it along with the defocus.
///
/// THE FOOTAGE IS SHARP FOR FREE. FullscreenMagnifier draws into a ScreenSpaceOverlay
/// canvas, which uGUI composites AFTER post-processing. So the camera feed and the
/// coral defocus while the polyps stay pixel-crisp, with no compositing work and no
/// second camera. Do not "fix" that canvas to ScreenSpaceCamera — it would start
/// being blurred along with everything else.
///
/// PINNED, LIKE EVERYTHING ELSE. The weight is a pure function of FullscreenReveal,
/// which is itself a pure function of distance (see ProximityRevealController's
/// invariant). Nothing here animates, eases toward a target, or holds state. Phone
/// still => blur still.
/// </summary>
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoralPolyps
{
    public class MagnifierDefocus : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Supplies the reveal that drives the blur. Found in the scene if left empty.")]
        public ProximityRevealController proximity;

        [Tooltip("Asked whether the screen is genuinely covered, so the pass can be skipped " +
                 "when nothing behind the footage is visible. Found in the scene if left empty.")]
        public FullscreenMagnifier fullscreen;

        [Tooltip("The volume carrying the Depth of Field override. One is built at runtime if " +
                 "this is empty — assign one only if you want to tune the DoF by hand in the " +
                 "inspector. Must NOT be the bloom volume.")]
        public Volume volume;

        // THE BLUR IS ESTABLISHED EARLY, NOT EARNED LATE.
        //
        // The first version ramped the blur alongside the reveal, so the screen only went
        // soft once the footage was most of the way to covering it. That is backwards. A
        // magnifying glass has a shallow depth of field the entire time it is held close —
        // the surroundings are already gone when the magnified spot is still tiny. Ramping
        // them together also made the takeover read as a change of shot, because everything
        // changed at once; with the blur established first the footage arrives into a frame
        // that is already behaving like a lens, and the transition stops being an event.
        //
        // So: full blur by the time the opening is roughly one corallite wide, held from
        // there. Both numbers live in coral-ar.json — they are exactly the kind of thing
        // that gets judged on a plinth and needs changing without a rebuild.
        [Header("Response (defaults come from coral-ar.json)")]
        [Tooltip("Blur at and beyond the onset, 0..1. Overwritten from config on Awake.")]
        [Range(0f, 1f)] public float maxWeight = 1.0f;

        [Tooltip("The reveal at which the blur reaches maxWeight; it holds there for the rest " +
                 "of the approach. Small on purpose — see the note above. Overwritten from " +
                 "config on Awake.")]
        [Range(0.01f, 1f)] public float blurOnsetReveal = 0.12f;

        [Tooltip("Shapes the compressed onset ramp (0 at reveal 0, 1 at blurOnsetReveal). " +
                 "Smooth by default so the softening arrives without a visible edge. This " +
                 "shapes only the first sliver of the approach; past the onset it is held.")]
        public AnimationCurve response = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Drop the blur to zero once the footage covers every pixel. Free: by " +
                 "definition nothing behind it is visible, and this is the same test that " +
                 "decides it is safe to hide the coral entirely. Saves a full-screen pass at " +
                 "exactly the range the device is hottest and closest to thermal throttling.")]
        public bool skipWhenFullyCovered = true;

        // WHY THERE ARE TWO MODES.
        //
        // Gaussian is the mobile-sane choice and was the original pick. Its ceiling is low
        // and hard: URP clamps gaussianMaxRadius to 1.5 and there is no way past it, so
        // "make the blur much greater" is simply not answerable in that mode — it was
        // already at the cap.
        //
        // Bokeh is a physical-camera simulation and will go as far as you want: focus at
        // 0.1 m with a 300 mm lens wide open puts everything in this scene many stops out
        // of focus. It costs meaningfully more, which is a real concern for an all-day
        // exhibition on an iPad already running three video decodes and a tracker — but
        // the pass only runs while the iris is partly open, and switches off entirely once
        // the screen is covered, so it is a spend of seconds per visitor rather than a
        // permanent tax. Fall back to gaussian in config if the soak says otherwise.
        [Header("Mode")]
        [Tooltip("\"radial\" keeps the loupe patch sharp and softens outward from its rim — " +
                 "the magnifying-glass look, and the only mode that can do it. \"bokeh\" and " +
                 "\"gaussian\" are whole-screen Depth of Field, which cannot separate the " +
                 "footage from the coral because they sit at the same depth. Set from " +
                 "coral-ar.json.")]
        public string mode = "radial";

        [Header("Radial (mode = radial)")]
        [Tooltip("How far past the loupe's rim the blur takes to reach full, in screen " +
                 "heights. Wide reads as optics; narrow puts a visible ring right where the " +
                 "viewer is looking.")]
        public float radialFalloff = 0.28f;

        [Tooltip("Blur radius at full strength, in screen heights. This is the actual " +
                 "softness — raise it for a heavier lens.")]
        public float radialMaxRadius = 0.045f;

        [Tooltip("Extra margin around the loupe's projected circle kept fully sharp, in " +
                 "screen heights. A little slack stops the rim of the footage catching the " +
                 "very start of the ramp, which reads as the patch having a soft edge.")]
        public float radialSharpMargin = 0.02f;

        // BOKEH IS RAMPED BY ITS OPTICS, NOT BY VOLUME WEIGHT.
        //
        // Weight-blending a bokeh volume produces a hard step, and the reason is not
        // obvious: weight interpolates a volume's parameters against the STACK DEFAULTS,
        // and URP's default Depth of Field focuses at 10 metres. The mode enum does not
        // interpolate at all — enums cannot — so the instant the volume carries any weight,
        // the pass switches to Bokeh and a subject 20 cm from the lens is already many
        // stops out of focus. The ramp was real; it just ran between "sharp" and "very
        // blurry" with nothing in between, which is exactly the hard transition reported.
        //
        // So weight is pinned at 1 whenever the pass runs, and the optics themselves are
        // driven: a short lens stopped well down (sharp) opening into a long lens wide open
        // (extreme). Circle of confusion goes as focalLength^2 / aperture, so a linear
        // drive gives a naturally gentle start and a strong finish — which is the shape
        // being asked for, arrived at by physics rather than by curve-fitting.
        [Tooltip("Bokeh: metres at which the scene is IN focus. Far away on purpose — " +
                 "everything real is much nearer, so the whole scene sits on one side of the " +
                 "focal plane and defocuses together with no in-focus band sliding through it.")]
        public float bokehFocusDistanceM = 10f;

        [Tooltip("Bokeh: focal length in mm at ZERO blur. Short = deep depth of field. Wants " +
                 "to be short enough that the first frame of the ramp is genuinely sharp, or " +
                 "enabling the pass is itself a visible step.")]
        public float bokehFocalLengthStart = 5f;

        [Tooltip("Bokeh: focal length in mm at FULL blur, 1-300. Longer = shallower depth of " +
                 "field. At 300 the defocus is extreme, which is the point.")]
        public float bokehFocalLength = 300f;

        [Tooltip("Bokeh: f-stop, held constant across the ramp. LOWER is more blur. The focal " +
                 "length alone carries the ramp — see the note below on why moving both at " +
                 "once makes the response impossible to reason about.")]
        public float bokehAperture = 1.4f;

        [Tooltip("Gaussian: blur radius. URP clamps this to 1.5 — this is the ceiling that " +
                 "made bokeh necessary.")]
        public float gaussianMaxRadius = 1.5f;

        [Tooltip("Gaussian: everything beyond this distance from the camera (metres) is fully " +
                 "defocused. Tiny on purpose — the subject of this blur is the whole scene, so " +
                 "there is no in-focus plane to preserve. The sharp thing on screen is the " +
                 "footage, and that is not part of the scene at all.")]
        public float focusEndM = 0.01f;

        /// <summary>Applied volume weight this frame. Shown in the HUD.</summary>
        public float Weight { get; private set; }

        Volume _owned;
        VolumeProfile _ownedProfile;
        DepthOfField _dof;
        bool _bokeh;
        bool _radial;
        Vector2 _lastCenter = new Vector2(0.5f, 0.5f);
        float _lastInner;

        /// <summary>
        /// The radial path. Sharp inside the loupe's projected circle, softening outward —
        /// which is the whole point of choosing it over Depth of Field, since the footage and
        /// the coral it is painted on sit at identical depth and no depth-based effect can
        /// ever tell them apart.
        ///
        /// The strength ramp is the same pure function of distance the other modes use, so
        /// the blur is pinned to the hand exactly like the reveal is.
        /// </summary>
        void UpdateRadial()
        {
            float reveal = proximity != null ? Mathf.Clamp01(proximity.FullscreenReveal) : 0f;
            bool covered = skipWhenFullyCovered && fullscreen != null && fullscreen.ScreenFullyCovered;

            float t = Mathf.Clamp01(reveal / Mathf.Max(blurOnsetReveal, 0.01f));
            Weight = covered ? 0f : Mathf.Clamp01(response.Evaluate(t)) * maxWeight;

            // Hold the last circle when the loupe cannot be projected (behind the camera, or
            // the target is lost and the transform is stale). Recomputing from a bad pose
            // would swing the sharp region around the screen, which is far more visible than
            // it being a frame or two out of date.
            if (ProjectLoupe(out Vector2 c, out float r))
            {
                _lastCenter = c;
                _lastInner = r + Mathf.Max(radialSharpMargin, 0f);
            }

            PushRadial(Weight, _lastCenter, _lastInner);
        }

        void Awake()
        {
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (fullscreen == null) fullscreen = FindAnyObjectByType<FullscreenMagnifier>();

            // Config wins over the inspector defaults. This component is added at runtime
            // (SceneBuilder wires it, and FullscreenMagnifier adds it if a scene predates
            // it), so inspector edits during Play do not persist — coral-ar.json is the
            // only place a blur tweak survives a domain reload.
            var cfg = CoralConfig.Shared;
            maxWeight = Mathf.Clamp01(cfg.magnifier_blur);
            blurOnsetReveal = Mathf.Clamp(cfg.magnifier_blur_onset, 0.01f, 1f);
            if (!string.IsNullOrWhiteSpace(cfg.magnifier_blur_mode)) mode = cfg.magnifier_blur_mode;

            _radial = mode.Equals("radial", System.StringComparison.OrdinalIgnoreCase);

            if (_radial)
            {
                // No volume, no Depth of Field, no depth texture. The whole effect is a
                // fullscreen pass fed by the globals pushed in LateUpdate.
                PushRadial(0f, new Vector2(0.5f, 0.5f), 0f);
                Debug.Log("[defocus] radial blur — needs MagnifierBlurFeature on the URP " +
                          "renderer asset; nothing will blur without it.");
                return;
            }

            if (volume == null) volume = Build();
            if (volume != null && volume.profile != null)
                volume.profile.TryGet(out _dof);

            if (_dof == null)
                Debug.LogWarning($"[{nameof(MagnifierDefocus)}] no DepthOfField on the assigned " +
                                 "volume — the blur will do nothing.", this);

            RequireCameraDepth();

            if (volume != null) volume.weight = 0f;
        }

        /// <summary>
        /// Gaussian Depth of Field derives its circle of confusion from _CameraDepthTexture.
        /// Without one the CoC resolves to nothing and the whole pass is a silent no-op —
        /// no error, no warning, just a volume at full weight doing visibly nothing, which
        /// is a genuinely horrible thing to debug from a gallery floor.
        ///
        /// The camera was set to UsePipelineSettings and Mobile_RPAsset has the depth texture
        /// off (reasonably — it is a mobile profile and nothing else here needed it). Claiming
        /// the requirement on the CAMERA rather than flipping it in the pipeline asset keeps
        /// the cost attached to the feature that wants it, survives a change of quality level,
        /// and cannot be undone by someone tidying the render pipeline settings.
        ///
        /// Also checks post-processing is actually on, because that is the other way for this
        /// to fail completely while looking correctly configured.
        /// </summary>
        void RequireCameraDepth()
        {
            var cam = proximity != null ? proximity.cam : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning($"[{nameof(MagnifierDefocus)}] no camera found — cannot confirm " +
                                 "the depth texture, and the blur may silently do nothing.", this);
                return;
            }

            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;

            if (!data.requiresDepthTexture)
            {
                data.requiresDepthTexture = true;
                Debug.Log($"[defocus] enabled the depth texture on '{cam.name}' — Gaussian DoF " +
                          "cannot compute a circle of confusion without it.");
            }

            if (!data.renderPostProcessing)
                Debug.LogWarning($"[{nameof(MagnifierDefocus)}] post-processing is OFF on " +
                                 $"'{cam.name}' — the blur (and the bloom the fluorescent " +
                                 "tissue depends on) will not render.", this);
        }

        /// <summary>
        /// Build the volume in code rather than expecting it in the scene, for the same
        /// reason FullscreenMagnifier builds its own canvas: wiring that lives only in a
        /// scene file is the wiring this project already lost once.
        ///
        /// A runtime <see cref="Volume.profile"/> (not sharedProfile) so nothing here can
        /// dirty a committed asset — this component writes to the override every frame.
        /// </summary>
        Volume Build()
        {
            var go = new GameObject("MagnifierDefocus Volume");
            go.transform.SetParent(transform, false);

            _owned = go.AddComponent<Volume>();
            _owned.isGlobal = true;
            // Above the bloom volume so the ordering is explicit, though they carry
            // different overrides and cannot actually contend.
            _owned.priority = 1f;

            _ownedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _owned.profile = _ownedProfile;

            var dof = _ownedProfile.Add<DepthOfField>(true);

            if (mode.Equals("gaussian", System.StringComparison.OrdinalIgnoreCase))
            {
                dof.mode.Override(DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(0f);
                dof.gaussianEnd.Override(Mathf.Max(focusEndM, 1e-3f));
                dof.gaussianMaxRadius.Override(gaussianMaxRadius);
                dof.highQualitySampling.Override(false);   // never inspected closely
            }
            else
            {
                _bokeh = true;
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.focusDistance.Override(Mathf.Max(bokehFocusDistanceM, 0.1f));
                // Aperture is fixed for the whole ramp; only the focal length moves, so that
                // the blur is the product of one term rather than two (see LateUpdate).
                dof.aperture.Override(Mathf.Clamp(bokehAperture, 1f, 32f));
                // Start at the SHARP end. LateUpdate drives the focal length every frame;
                // setting it here only decides what the very first frame looks like, and it
                // must be sharp or enabling the pass is itself a visible step.
                dof.focalLength.Override(Mathf.Clamp(bokehFocalLengthStart, 1f, 300f));
            }

            Debug.Log($"[defocus] {dof.mode.value} blur, max weight {maxWeight:F2}, " +
                      $"full by reveal {blurOnsetReveal:F2}");
            return _owned;
        }

        static readonly int MagBlurID = Shader.PropertyToID("_MagBlur");
        static readonly int MagBlurParamsID = Shader.PropertyToID("_MagBlurParams");
        static readonly int MagBlurAspectID = Shader.PropertyToID("_MagBlurAspect");

        /// <summary>
        /// Publish the sharp circle and the blur strength for MagnifierBlurFeature. Globals
        /// rather than a material reference: the renderer feature then never has to find a
        /// scene object, and a scene with no defocus component simply leaves strength at 0,
        /// where the shader early-outs and returns the frame untouched.
        /// </summary>
        void PushRadial(float strength, Vector2 center, float innerRadius)
        {
            // Landscape fallback, matching FullscreenMagnifier — the app no longer
            // rotates to portrait.
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.778f;
            Shader.SetGlobalVector(MagBlurID,
                new Vector4(center.x, center.y, innerRadius, innerRadius + Mathf.Max(radialFalloff, 0.01f)));
            Shader.SetGlobalVector(MagBlurParamsID, new Vector4(Mathf.Max(radialMaxRadius, 0f), strength, 0f, 0f));
            Shader.SetGlobalFloat(MagBlurAspectID, aspect);
        }

        /// <summary>
        /// The loupe's circle, projected to the screen in the mask's units (screen heights).
        /// Measured with the camera's own up vector rather than derived from FOV, so it stays
        /// correct through whatever projection Vuforia hands the camera — the same approach
        /// FullscreenMagnifier uses for the iris, and for the same reason.
        /// </summary>
        bool ProjectLoupe(out Vector2 center, out float radius)
        {
            center = new Vector2(0.5f, 0.5f);
            radius = 0f;

            var cam = proximity != null ? proximity.cam : null;
            if (cam == null) return false;

            Vector3 vp = cam.WorldToViewportPoint(proximity.LoupeCenter);
            if (vp.z <= 0f) return false;                 // behind the camera; keep the last values
            center = new Vector2(vp.x, vp.y);

            Vector3 edge = cam.WorldToViewportPoint(
                proximity.LoupeCenter + cam.transform.up * Mathf.Max(proximity.LoupeRadius, 0f));
            radius = Mathf.Abs(edge.y - vp.y);
            return true;
        }

        void LateUpdate()
        {
            if (_radial) { UpdateRadial(); return; }
            if (volume == null) return;

            // Both this and FullscreenMagnifier run in LateUpdate, and they sit on the same
            // GameObject, so component add-order decides which goes first — this one second,
            // as both SceneBuilder and the runtime fallback add it after. Nothing depends on
            // that: a frame-late ScreenFullyCovered only ever delays switching the pass OFF.
            float reveal = proximity != null ? Mathf.Clamp01(proximity.FullscreenReveal) : 0f;
            bool covered = skipWhenFullyCovered && fullscreen != null && fullscreen.ScreenFullyCovered;

            // Compress the ramp into the first sliver of the reveal, then hold. Still a pure
            // function of distance — the onset only decides how quickly the softening arrives,
            // never that it arrives on its own.
            float t = Mathf.Clamp01(reveal / Mathf.Max(blurOnsetReveal, 0.01f));
            Weight = covered ? 0f : Mathf.Clamp01(response.Evaluate(t)) * maxWeight;

            if (_bokeh && _dof != null)
            {
                // Drive the optics, hold the weight. See the note on the bokeh fields for
                // why weight-blending this mode produces a step rather than a ramp.
                //
                // LERP THE SQUARE, NOT THE LENGTH. Circle of confusion goes as focalLength
                // squared, so a linear sweep of focal length is not a linear sweep of blur —
                // it is imperceptible for the first two thirds and then arrives all at once,
                // which is the same hard transition by a subtler route. Interpolating f^2 and
                // taking the root makes the RESULT linear in the drive, so `response` and
                // blurOnsetReveal shape what is actually seen rather than what is set.
                //
                // Aperture is held constant for the same reason: moving both multiplies two
                // ramps together and the response stops being reasonable about.
                float f0 = Mathf.Clamp(bokehFocalLengthStart, 1f, 300f);
                float f1 = Mathf.Clamp(bokehFocalLength, 1f, 300f);
                _dof.focalLength.value = Mathf.Sqrt(Mathf.Lerp(f0 * f0, f1 * f1, Weight));
                _dof.aperture.value = Mathf.Clamp(bokehAperture, 1f, 32f);
                volume.weight = 1f;
            }
            else
            {
                // Gaussian blends acceptably on weight: its mode IS the stack default, so
                // nothing snaps, and gaussianEnd interpolates from 30 m down, which keeps a
                // 20 cm subject genuinely in focus through the early part of the ramp.
                volume.weight = Weight;
            }

            // Volumes are not free even at weight 0 — the override still resolves and URP
            // still schedules the pass. Switch the whole thing off when there is nothing
            // to blur, which is most of the time the piece is running. Safe to toggle: at
            // this threshold the driven optics are still at their sharp end, so the frame
            // either side of the switch is identical.
            bool wanted = Weight > 0.01f;
            if (volume.enabled != wanted) volume.enabled = wanted;
        }

        void OnDestroy()
        {
            if (_ownedProfile != null) Destroy(_ownedProfile);
            if (_owned != null) Destroy(_owned.gameObject);
        }
    }
}
