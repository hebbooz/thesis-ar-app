/// <summary>
/// Surface tissue layer — turns the server's (state, intensity) into the coral
/// material's one _Stress dial (CONTROL_INTEGRATION.md §3.1).
///
/// This lands cleanly because both sides are already a single continuous dial:
///
///     _Stress  0 ──────────── _FluorPoint ──────────── 1
///             natural       peak fluorescence       bleached (bare skeleton)
///
/// The rule is the same one every output in the room follows — STATE SELECTS THE
/// REGIME, INTENSITY SETS THE POSITION WITHIN IT — so the AR coral, the projected
/// reef, the sound and the lamp all move as one organism.
///
/// No networking knowledge lives here. It reads a CoralOscListener if one is
/// present and is otherwise driven by hand through <see cref="useManualState"/>,
/// so the mapping is testable with the network absent and vice versa.
/// </summary>
using UnityEngine;

namespace CoralPolyps
{
    public class CoralAppearance : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The broadcast. Found in the scene if left empty; absent is fine — the coral " +
                 "then rests at state 0, which is the correct look for a room with no server.")]
        public CoralOscListener listener;

        [Tooltip("The coral mesh renderer whose material carries FluorescentTissue. Required.")]
        public Renderer coralRenderer;

        [Header("Testing (no server needed)")]
        [Tooltip("Ignore the broadcast and use the two values below. The whole §3.1 arc, " +
                 "including both backward transitions, can be walked from here in the editor.")]
        public bool useManualState = false;
        [Range(0, 3)] public int manualState = 0;
        [Range(0f, 1f)] public float manualIntensity = 0f;

        /// <summary>Applied stress, after the slew. Read by the HUD — showing this next
        /// to the received state is what separates a network fault from a mapping fault.</summary>
        public float Stress { get; private set; }
        public float EmissionScale { get; private set; } = 1f;

        static readonly int StressID = Shader.PropertyToID("_Stress");
        static readonly int FluorPointID = Shader.PropertyToID("_FluorPoint");
        static readonly int EmissionScaleID = Shader.PropertyToID("_EmissionScale");

        CoralConfig _cfg;
        Material _mat;
        bool _converged;      // has the first broadcast landed? drives snap-vs-slew

        void Awake()
        {
            _cfg = CoralConfig.Shared;
            if (listener == null) listener = FindAnyObjectByType<CoralOscListener>();
        }

        void Start()
        {
            if (coralRenderer == null)
            {
                Debug.LogError($"[{nameof(CoralAppearance)}] no coralRenderer assigned — the coral will not respond to the installation.", this);
                enabled = false;
                return;
            }

            // Per-renderer instance: auto-freed with the renderer, and never dirties
            // the saved look-dev material asset.
            _mat = coralRenderer.material;

            // Keep the shader's peak-fluorescence point in sync with the mapping's, so
            // "capped at fluorPoint" means the same thing on both sides of the wire.
            _mat.SetFloat(FluorPointID, _cfg.fluor_point);

            // CLAUDE.md §9 rule 3: boot to Natural and converge silently.
            Stress = 0f;
            EmissionScale = 1f;
            Push();
        }

        void LateUpdate()
        {
            if (_mat == null) return;

            int state = useManualState ? manualState : (listener != null ? listener.State : 0);
            float intensity = useManualState ? manualIntensity : (listener != null ? listener.Intensity : 0f);

            float targetStress = TargetStress(state, intensity);
            float targetEmission = (_cfg.suppress_emission_during_recovery && state == 3) ? 0f : 1f;

            if (ShouldSnap())
            {
                // Cold start into an already-bleached room: the rupture happened before
                // this app existed, and replaying it as a 2 s drain to white would be a
                // lie. Snap on the very first broadcast only. (The projection player
                // makes the identical exception.)
                _converged = true;
                Stress = targetStress;
                EmissionScale = targetEmission;
            }
            else
            {
                // One rate doing three jobs: it never limits ordinary warming (~0.006/s
                // against a slew of ~0.33/s), it shapes the latch jump into a ~2 s drain
                // to white, and it softens the two backward transitions — 3→2 (a re-warm
                // cancelling recovery) and 2→1 (the idle reset with hot water). Both are
                // real and reachable; the arc does not only run forwards.
                float step = Time.deltaTime / Mathf.Max(0.01f, _cfg.crossfade_s);
                Stress = Mathf.MoveTowards(Stress, targetStress, step);
                EmissionScale = Mathf.MoveTowards(EmissionScale, targetEmission, step);
            }

            Push();
        }

        bool ShouldSnap() => !_converged && (useManualState || (listener != null && listener.EverReceived));

        void Push()
        {
            // Idempotent by design — the same values arrive 5x/second and applying
            // them repeatedly must be harmless.
            _mat.SetFloat(StressID, Stress);
            _mat.SetFloat(EmissionScaleID, EmissionScale);
        }

        /// <summary>
        /// The §3.1 mapping. Every line earns its place:
        ///
        /// - States 0/1 cover the whole reversible arc, CAPPED AT fluor_point. That cap
        ///   is the thematic invariant made structural: the app cannot bleach itself,
        ///   only the server's latch takes the coral past peak fluorescence.
        /// - State 2 is latched on the server, which pins intensity at 1.0 there — so
        ///   intensity carries no information and the constant is honest.
        /// - State 3 passes intensity straight through. The server ramps it 1.0 → 0.0
        ///   across recovery_ramp_s, so the heal retimes from the server's config file
        ///   with no rebuild and no reauthoring here.
        /// </summary>
        float TargetStress(int state, float intensity) => state switch
        {
            0 or 1 => intensity * _cfg.fluor_point,
            2 => 1f,
            3 => intensity,
            _ => 0f,
        };
    }
}
