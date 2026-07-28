/// <summary>
/// Magnifier layer — turns the server's (state, intensity) into which polyp footage
/// shows inside the loupe, and at what weight (CONTROL_INTEGRATION.md §3.2).
///
/// The magnifier shows the polyps ALIVE when the reef is alive, FLUORESCENT when it
/// is fluorescent, and DEAD when it is dead. It follows the installation state; it
/// does not have a life of its own. Proximity opens the window — that is
/// ProximityRevealController's job — but what is inside the window comes off the
/// wire, exactly like everything else in the room. A visitor leaning closer sees
/// more; a visitor pressing the warm button changes what there is to see.
///
/// State 2 is the key emotional beat: leaning in and finding the life gone.
///
/// NOTE ON THE COMPOSITE: do not copy the projection player's compositor.
/// ProjectionPlayer.cs blends in OnRenderImage, which silently never fires under
/// URP — it is Built-In RP precisely for that reason, and this project is URP. The
/// blend happens in MagnifierLoupe.shader instead, which is the better fit anyway:
/// the magnifier is a masked reveal on a tracked surface, not a fullscreen effect.
/// The weight logic below is what was worth copying.
/// </summary>
using UnityEngine;

namespace CoralPolyps
{
    public class CoralMagnifier : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The broadcast. Found in the scene if left empty; absent is fine — the magnifier " +
                 "then rests on 'alive', which is the correct look for a room with no server.")]
        public CoralOscListener listener;

        [Tooltip("The renderer carrying MagnifierLoupe.shader — a duplicate of the coral mesh sitting " +
                 "just over the tissue. Required. Also assign it to ProximityRevealController's " +
                 "loupeTargets so proximity drives its reveal window.")]
        public Renderer magnifierRenderer;

        [Header("Testing (no server needed)")]
        [Tooltip("Ignore the broadcast and use the two values below. All four states and both " +
                 "backward transitions can be walked from here against the placeholder source.")]
        public bool useManualState = false;
        [Range(0, 3)] public int manualState = 0;
        [Range(0f, 1f)] public float manualIntensity = 0f;

        // Applied weights, after the slew. Read by the HUD.
        public float WAlive { get; private set; } = 1f;
        public float WFluorescent { get; private set; }
        public float WDead { get; private set; }
        public string SourceName => _source != null ? _source.SourceName : "none";

        static readonly int TexAID = Shader.PropertyToID("_TexA");
        static readonly int TexBID = Shader.PropertyToID("_TexB");
        static readonly int BlendID = Shader.PropertyToID("_Blend");

        CoralConfig _cfg;
        IMagnifierSource _source;
        Material _mat;
        bool _converged;

        void Awake()
        {
            _cfg = CoralConfig.Shared;
            if (listener == null) listener = FindFirstObjectByType<CoralOscListener>();

            // The seam: which implementation is a config edit, never a code change.
            // If flipping magnifier_source ever requires touching code, the seam was
            // built wrong. ("grid" is the placeholder in registration-test mode, which
            // it detects for itself — see PlaceholderMagnifierSource.)
            if (_cfg.magnifier_source == "video")
                _source = gameObject.AddComponent<VideoMagnifierSource>();
            else
                _source = gameObject.AddComponent<PlaceholderMagnifierSource>();

            Debug.Log($"[magnifier] source={_source.SourceName}");
        }

        void Start()
        {
            if (magnifierRenderer == null)
            {
                Debug.LogError($"[{nameof(CoralMagnifier)}] no magnifierRenderer assigned — the loupe will show nothing.", this);
                enabled = false;
                return;
            }
            _mat = magnifierRenderer.material;
        }

        void LateUpdate()
        {
            if (_mat == null) return;

            int state = useManualState ? manualState : (listener != null ? listener.State : 0);
            float intensity = useManualState ? manualIntensity : (listener != null ? listener.Intensity : 0f);

            // State selects, intensity interpolates — the same rule as everywhere else.
            // intensity is pinned at 1.0 in state 2 and ramps 1.0 → 0.0 across state 3,
            // so states 2 and 3 are the same blend: no special case for the latch, and
            // recovery walks dead → alive without ever passing through fluorescent.
            (float tAlive, float tFluoro, float tDead) = state switch
            {
                0 or 1 => (1f - intensity, intensity, 0f),
                _      => (1f - intensity, 0f, intensity),
            };

            if (ShouldSnap())
            {
                // Same exception as the tissue layer: a rupture that happened before this
                // app launched is not replayed as an animation.
                _converged = true;
                WAlive = tAlive; WFluorescent = tFluoro; WDead = tDead;
            }
            else
            {
                // The SAME rate the tissue slews at (one shared crossfade_s), so the two
                // layers — seen together, one inside the other — never disagree about how
                // bleached the coral is. If the tissue drained to white while the loupe
                // still showed green polyps, the illusion would break.
                float step = Time.deltaTime / Mathf.Max(0.01f, _cfg.crossfade_s);
                WAlive = Mathf.MoveTowards(WAlive, tAlive, step);
                WFluorescent = Mathf.MoveTowards(WFluorescent, tFluoro, step);
                WDead = Mathf.MoveTowards(WDead, tDead, step);
            }

            Push();
        }

        bool ShouldSnap() => !_converged && (useManualState || (listener != null && listener.EverReceived));

        /// <summary>
        /// Push the two heaviest clips and the blend between them. At rest exactly two
        /// weights are non-zero by construction (alive+fluorescent, or alive+dead); the
        /// third is only briefly non-zero while crossing between those pairs, and it is
        /// always the lightest, so dropping it costs nothing visible.
        ///
        /// Weights are opacities and nothing else — no playhead is ever touched. See
        /// VideoMagnifierSource for why that invariant is absolute.
        /// </summary>
        void Push()
        {
            if (_source == null || !_source.Ready)
            {
                _mat.SetTexture(TexAID, null);
                _mat.SetTexture(TexBID, null);
                _mat.SetFloat(BlendID, 0f);
                return;
            }

            Texture a = _source.Alive, b = _source.Fluorescent;
            float wa = WAlive, wb = WFluorescent;

            // Keep the two heaviest of the three.
            if (WDead > Mathf.Min(wa, wb))
            {
                if (wa <= wb) { a = _source.Dead; wa = WDead; }
                else { b = _source.Dead; wb = WDead; }
            }

            float sum = wa + wb;
            _mat.SetTexture(TexAID, a);
            _mat.SetTexture(TexBID, b);
            _mat.SetFloat(BlendID, sum > 1e-4f ? wb / sum : 0f);
        }
    }
}
