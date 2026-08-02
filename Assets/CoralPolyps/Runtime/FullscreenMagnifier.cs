/// <summary>
/// The last beat of the magnifier: at closest range the footage leaves the coral and
/// takes the whole screen.
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
///     0.30 -> 0.08 m   loupe opens on the coral      (object space, registered)
///     0.07 -> 0.03 m   footage lifts off, fills screen (this file, unregistered)
///
/// The second beat deliberately abandons registration, because by then registration
/// has nothing left to do: the viewer is close enough that the coral fills the frame,
/// and Vuforia loses a ~10 cm Model Target at around 5 cm anyway. The takeover is
/// opaque before that happens, so the tracking failure occurs behind a full screen of
/// polyps and is never seen. What would otherwise be the interaction's worst moment
/// becomes invisible.
///
/// This is NOT the screen-space sampling that CONTROL_INTEGRATION.md §11 rejected.
/// That was sampling footage in screen space *while it sat on the coral*, which made
/// the content slide across the cups as the device moved. Here the coral is no longer
/// on screen — there is nothing left to slide against.
///
/// WHY uGUI AND NOT A FULLSCREEN BLIT
/// ----------------------------------
/// A ScriptableRendererFeature would be the "proper" URP way and costs a renderer
/// asset edit, a feature, and a pass — all of it invisible in the scene diff. Two
/// stacked RawImages on a Screen Space Overlay canvas composite identically (B over A
/// at alpha = Blend is exactly lerp(A, B, Blend) when A is opaque), cost one draw call
/// each, and are inspectable. The UI is built in code rather than in the scene for the
/// same reason SceneBuilder exists: wiring that lives only in a scene file is the
/// wiring this project already lost once.
/// </summary>
using UnityEngine;
using UnityEngine.UI;

namespace CoralPolyps
{
    [RequireComponent(typeof(CoralMagnifier))]
    public class FullscreenMagnifier : MonoBehaviour
    {
        [Tooltip("Drives the takeover via its FullscreenReveal. Found in the scene if left empty.")]
        public ProximityRevealController proximity;

        [Tooltip("Sorting order for the overlay canvas. Above the AR view, below the HUD " +
                 "(CoralHud is IMGUI and always draws last), so diagnostics stay readable " +
                 "even at full cover.")]
        public int sortingOrder = 100;

        /// <summary>Current screen coverage, 0..1. Shown in the HUD.</summary>
        public float Coverage { get; private set; }

        CoralMagnifier _magnifier;
        CanvasGroup _group;
        RawImage _a, _b;

        void Awake()
        {
            _magnifier = GetComponent<CoralMagnifier>();
            // FindAnyObjectByType, not FindFirstObjectByType: the latter is deprecated
            // for relying on instance-ID ordering, and there is only ever one of these.
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (proximity == null)
                Debug.LogWarning($"[{nameof(FullscreenMagnifier)}] no ProximityRevealController — " +
                                 "the takeover will never open.", this);
            Build();
        }

        void Build()
        {
            // No GraphicRaycaster: nothing here is interactive, and adding one would
            // imply an EventSystem this scene does not have.
            var canvasGO = new GameObject("MagnifierFullscreen",
                typeof(Canvas), typeof(CanvasGroup));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            _group = canvasGO.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            // Never intercept touches: the HUD's three-finger tap has to keep working
            // even when the screen is fully covered, or a mis-tuned arc is unrecoverable
            // on a device with no console.
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _a = NewLayer(canvasGO.transform, "ClipA");
            _b = NewLayer(canvasGO.transform, "ClipB");
        }

        static RawImage NewLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);

            // The clips are square (720x720) and the screen is not. EnvelopeParent fills
            // the screen and crops the overflow rather than letterboxing or distorting —
            // the same "cover" choice the projection player makes, for the same reason:
            // a black bar reads as a bug, a crop reads as framing.
            var fitter = go.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;

            return img;
        }

        void LateUpdate()
        {
            // After CoralMagnifier's own LateUpdate has resolved the pair for this frame.
            // Script execution order is not relied upon: one frame of lag in the textures
            // would be invisible, but reading a stale Blend during the handover would let
            // the two layers disagree, which is the one thing §3.2 forbids.
            Coverage = proximity != null ? Mathf.Clamp01(proximity.FullscreenReveal) : 0f;

            if (!_magnifier.SourceReady)
            {
                // Footage not decoding yet. Covering the screen with black here would be
                // worse than not covering it: hold the AR view instead.
                _group.alpha = 0f;
                return;
            }

            _group.alpha = Coverage;
            if (Coverage <= 0.0001f) return;   // fully transparent: skip the texture churn

            SetLayer(_a, _magnifier.ClipA, 1f);
            SetLayer(_b, _magnifier.ClipB, Mathf.Clamp01(_magnifier.Blend));

            // Aspect can change under rotation, and the clips are only known once the
            // players have prepared, so it is resolved here rather than at Build().
            ApplyAspect(_a, _magnifier.ClipA);
            ApplyAspect(_b, _magnifier.ClipB);
        }

        static void SetLayer(RawImage img, Texture tex, float alpha)
        {
            img.texture = tex;
            // A null clip must not paint white — RawImage with no texture draws its
            // colour flat, which at full cover would be a white screen where the polyps
            // should be. Alpha 0 is the only safe answer.
            img.color = new Color(1f, 1f, 1f, tex != null ? alpha : 0f);
        }

        static void ApplyAspect(RawImage img, Texture tex)
        {
            if (tex == null || tex.height <= 0) return;
            var fitter = img.GetComponent<AspectRatioFitter>();
            if (fitter != null) fitter.aspectRatio = (float)tex.width / tex.height;
        }
    }
}
