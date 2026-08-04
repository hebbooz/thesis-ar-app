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

        [Header("Response")]
        [Tooltip("Blur at full takeover, 0..1. Modest is more convincing than heavy: the point " +
                 "is that the surroundings stop competing for the eye, not that they vanish.")]
        [Range(0f, 1f)] public float maxWeight = 0.85f;

        [Tooltip("Shapes reveal (0 = footage entirely on the coral, 1 = footage fills the " +
                 "screen) -> blur. Weighted late by default so the coral stays legibly sharp " +
                 "through the loupe stage and only softens once the iris is genuinely opening. " +
                 "To start the blur EARLIER, widen the takeover arc on the controller rather " +
                 "than flattening this curve — that keeps blur and reveal telling one story.")]
        public AnimationCurve response = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.35f, 0.05f), new Keyframe(1f, 1f));

        [Tooltip("Drop the blur to zero once the footage covers every pixel. Free: by " +
                 "definition nothing behind it is visible, and this is the same test that " +
                 "decides it is safe to hide the coral entirely. Saves a full-screen pass at " +
                 "exactly the range the device is hottest and closest to thermal throttling.")]
        public bool skipWhenFullyCovered = true;

        [Header("Depth of Field (runtime-built volume only)")]
        [Tooltip("Gaussian rather than Bokeh. Bokeh is a physical-camera simulation with a " +
                 "cost to match; on an iPad running three video decodes, a tracker and an " +
                 "all-day soak it is not a defensible spend for an effect that is behind an " +
                 "opaque iris seconds later.")]
        public float gaussianMaxRadius = 1.5f;

        [Tooltip("Everything beyond this distance from the camera (metres) is fully defocused. " +
                 "Tiny on purpose: the subject of this blur is the whole scene, so there is no " +
                 "in-focus plane to preserve — the sharp thing on screen is the footage, and " +
                 "that is not part of the scene at all.")]
        public float focusEndM = 0.01f;

        /// <summary>Applied volume weight this frame. Shown in the HUD.</summary>
        public float Weight { get; private set; }

        Volume _owned;
        VolumeProfile _ownedProfile;
        DepthOfField _dof;

        void Awake()
        {
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (fullscreen == null) fullscreen = FindAnyObjectByType<FullscreenMagnifier>();

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
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0f);
            dof.gaussianEnd.Override(Mathf.Max(focusEndM, 1e-3f));
            dof.gaussianMaxRadius.Override(gaussianMaxRadius);
            dof.highQualitySampling.Override(false);   // mobile; the blur is never inspected closely

            return _owned;
        }

        void LateUpdate()
        {
            if (volume == null) return;

            // Both this and FullscreenMagnifier run in LateUpdate, and they sit on the same
            // GameObject, so component add-order decides which goes first — this one second,
            // as both SceneBuilder and the runtime fallback add it after. Nothing depends on
            // that: a frame-late ScreenFullyCovered only ever delays switching the pass OFF.
            float reveal = proximity != null ? Mathf.Clamp01(proximity.FullscreenReveal) : 0f;
            bool covered = skipWhenFullyCovered && fullscreen != null && fullscreen.ScreenFullyCovered;

            Weight = covered ? 0f : Mathf.Clamp01(response.Evaluate(reveal)) * maxWeight;
            volume.weight = Weight;

            // Volumes are not free even at weight 0 — the override still resolves and URP
            // still schedules the pass. Switch the whole thing off when there is nothing
            // to blur, which is most of the time the piece is running.
            bool wanted = Weight > 0.001f;
            if (volume.enabled != wanted) volume.enabled = wanted;
        }

        void OnDestroy()
        {
            if (_ownedProfile != null) Destroy(_ownedProfile);
            if (_owned != null) Destroy(_owned.gameObject);
        }
    }
}
