using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// EDITOR TEST RIG (throwaway): scrub a single 0..1 slider to feed the controller a
    /// MANUAL distance, so you can watch the loupe + magnification respond in Play mode
    /// without a device or live tracking. Nothing is physically moved — it just drives
    /// <see cref="ProximityRevealController.manualDistance"/>, and the controller does the
    /// reveal + scale from there exactly as it will on device.
    ///
    /// This rig no longer touches the coral's CONDITION — the healthy→bleached arc belongs
    /// to the orchestration server. To scrub that, use CoralAppearance/CoralMagnifier's
    /// useManualState, or drive the device from tools/drive_projection.py.
    ///
    /// With <see cref="autoRange"/> on, proximity 0 sits just beyond the farther of the
    /// loupe/magnify start distances (window shut, 1x) and proximity 1 just inside the
    /// nearer of the full distances (window open, magnified) — so the slider always sweeps
    /// the whole range no matter how it is tuned.
    ///
    /// It also force-enables the coral renderer so you can see it even though there's no
    /// tracked target in the editor (Vuforia's event handler would otherwise hide it).
    ///
    /// Runs in Play mode only. WATCH THE SCENE VIEW (orbit to the coral) — the Game/AR
    /// camera may not be framing it in the editor. Disable/delete before device builds.
    /// </summary>
    public class ProximityTestRig : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The controller under test. Auto-found in the scene if left empty.")]
        public ProximityRevealController controller;

        [Header("Scrub  (drag this in Play mode)")]
        [Tooltip("0 = far (loupe shut, life-size)  ->  1 = closest (loupe open, magnified).")]
        [Range(0f, 1f)] public float proximity = 0f;

        [Header("Distance range")]
        [Tooltip("Derive the far/near sweep from the controller's loupe + magnify ranges " +
                 "so the slider always covers the full range. Turn off to set metres by hand.")]
        public bool autoRange = true;

        [Tooltip("Distance (m) fed at proximity 0, when auto-range is off.")]
        public float farDistance = 0.7f;

        [Tooltip("Distance (m) fed at proximity 1, when auto-range is off.")]
        public float nearDistance = 0.05f;

        [Header("Readout (live)")]
        [Tooltip("The distance this rig is currently feeding the controller (metres).")]
        [SerializeField] private float currentDistance;

        private void OnEnable()
        {
            if (controller == null)
                controller = FindAnyObjectByType<ProximityRevealController>();
        }

        private void Update()
        {
            // Safety: this rig is EDITOR-ONLY. Left enabled in a device build it would feed a
            // fake distance and force-show the coral, overriding Vuforia's hide-when-untracked —
            // which looks exactly like "the coral is parked in one spot and never tracks".
            if (!Application.isEditor) return;

            if (controller == null) return;

            // Bracket the arcs that are actually in play: the loupe opening at the far end,
            // the takeover completing at the near end. The magnify arc is deliberately left
            // out — maxMagnification is 1 (registration beats zoom), so including its 2 cm
            // full-distance only stretched the slider into 4 cm of travel where everything
            // is long since saturated, compressing the part worth scrubbing.
            float far = autoRange
                ? Mathf.Max(controller.loupeStartDistance, controller.magnifyStartDistance) * 1.1f
                : farDistance;
            float near = autoRange
                ? Mathf.Max(controller.fullscreenFullDistance * 0.7f, 0.01f)
                : nearDistance;

            currentDistance = Mathf.Lerp(far, near, proximity);
            controller.useManualDistance = true;
            controller.manualDistance = currentDistance;

            // Make sure the coral is visible in the editor even without a tracked target.
            if (controller.coralRenderer != null && !controller.coralRenderer.enabled)
                controller.coralRenderer.enabled = true;
        }

        private void OnDisable()
        {
            // Hand control back to real (camera-measured) distance when the rig is off.
            if (controller != null) controller.useManualDistance = false;
        }
    }
}
