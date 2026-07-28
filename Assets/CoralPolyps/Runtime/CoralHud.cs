/// <summary>
/// On-device diagnostic overlay (CONTROL_INTEGRATION.md §7).
///
/// Every failure mode in this system is silent by design — a lost datagram, an
/// unregistered client, a denied Local Network permission, a wrong subnet and a
/// stopped server all look identical from the render side: a healthy coral that
/// never changes. The "seconds since last broadcast" line distinguishes all of
/// them at a glance, and it is the only practical triage tool once the iPad is on
/// a plinth in a gallery. Build it early, not last.
///
/// Showing the APPLIED values (stress, magnifier weights) next to the RECEIVED
/// ones is what separates a network fault from a mapping fault without a rebuild.
///
/// The server host is editable here on purpose: on exhibition morning the Mac's
/// address is the one thing most likely to differ, and you will not want to
/// rebuild an iOS app to fix it (§5).
///
/// Shape copied from tools/fake_client.py in the control repo, so the two can be
/// compared line for line.
/// </summary>
using UnityEngine;

namespace CoralPolyps
{
    public class CoralHud : MonoBehaviour
    {
        [Tooltip("The listener to report on. Found in the scene if left empty.")]
        public CoralOscListener listener;

        [Tooltip("The tissue layer, for the APPLIED half of the readout. Optional.")]
        public CoralAppearance appearance;

        [Tooltip("The magnifier layer, for the APPLIED half of the readout. Optional.")]
        public CoralMagnifier magnifier;

        [Tooltip("Visible on launch. Toggle any time with a three-finger tap (or H in the editor). " +
                 "Set hud_enabled:false in coral-ar.json to disable it entirely for the exhibition.")]
        public bool visible = true;

        string _hostEdit;
        bool _hostEditPrimed;
        GUIStyle _box, _label, _field, _button;

        void Awake()
        {
            if (listener == null) listener = FindFirstObjectByType<CoralOscListener>();
            if (appearance == null) appearance = FindFirstObjectByType<CoralAppearance>();
            if (magnifier == null) magnifier = FindFirstObjectByType<CoralMagnifier>();
            if (!CoralConfig.Shared.hud_enabled) enabled = false;
        }

        void Update()
        {
            // Three fingers is deliberately awkward: a visitor will not find it by
            // accident, and it needs no on-screen affordance cluttering the piece.
            bool tap = Input.touchCount == 3 && Input.GetTouch(2).phase == TouchPhase.Began;
#if UNITY_EDITOR
            tap |= Input.GetKeyDown(KeyCode.H);
#endif
            if (tap) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;

            EnsureStyles();
            var cfg = CoralConfig.Shared;

            float pad = 10f * Scale;
            GUILayout.BeginArea(new Rect(pad, pad, Screen.width - pad * 2f, Screen.height - pad * 2f));
            // Sized to its content: the overlay is a status line, not a panel that
            // swallows the whole screen.
            GUILayout.BeginVertical(_box, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            if (listener == null)
            {
                GUILayout.Label("no CoralOscListener in the scene", _label);
            }
            else
            {
                GUILayout.Label($"id={listener.ClientId}   server={cfg.server_host}:{cfg.server_port}   " +
                                $"listening={cfg.listen_port} {(listener.IsListening ? "ok" : "FAILED")}", _label);

                GUILayout.Label($"state={listener.State} {CoralOscListener.StateName(listener.State)}   " +
                                $"intensity={listener.Intensity:F2}   T={listener.Temp:F1}C", _label);

                string applied = AppliedLine();
                if (applied != null) GUILayout.Label(applied, _label);

                var prev = GUI.color;
                GUI.color = StatusColor();
                GUILayout.Label($"last broadcast {Age(listener.SecondsSinceMessage)}   " +
                                $"hello sent {Age(listener.SecondsSinceHello)}   rx={listener.RxCount}", _label);
                GUI.color = prev;

                GUILayout.Label($"config: {cfg.LoadedFrom}", _label);

                // --- Runtime server address, for the morning the Mac's IP has moved ---
                if (!_hostEditPrimed) { _hostEdit = cfg.server_host; _hostEditPrimed = true; }
                GUILayout.BeginHorizontal();
                GUILayout.Label("server host", _label, GUILayout.Width(140f * Scale));
                _hostEdit = GUILayout.TextField(_hostEdit, _field, GUILayout.Width(260f * Scale));
                if (GUILayout.Button("apply + save", _button, GUILayout.Width(200f * Scale)))
                {
                    cfg.server_host = _hostEdit.Trim();
                    cfg.Save();
                    listener.Reconnect();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The applied half of the readout — what the renderer actually did with the
        /// received values. Null while no render layer is present, which is the
        /// correct state for §11 step 1 ("nothing rendering").
        /// </summary>
        string AppliedLine()
        {
            if (appearance == null && magnifier == null) return null;

            string stress = appearance != null
                ? $"stress={appearance.Stress:F2} em={appearance.EmissionScale:F2}"
                : "stress=-";
            string mag = magnifier != null
                ? $"magnifier=alive {magnifier.WAlive:F2} / fluoro {magnifier.WFluorescent:F2} / " +
                  $"dead {magnifier.WDead:F2}   src={magnifier.SourceName}"
                : $"magnifier=-   src={CoralConfig.Shared.magnifier_source}";

            return $"{stress}   {mag}";
        }

        /// <summary>
        /// Green = live, amber = connected but the stream has gone quiet (server
        /// stopped or restarting — not an error, we hold the last value), red = never
        /// heard anything, which is the permission / subnet / hello-port family.
        /// </summary>
        Color StatusColor()
        {
            if (!listener.EverReceived) return new Color(1f, 0.45f, 0.4f);
            return listener.Stalled ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 1f, 0.6f);
        }

        static string Age(float seconds) =>
            float.IsPositiveInfinity(seconds) ? "never" : $"{seconds:F1}s ago";

        static float Scale => Mathf.Max(1f, Screen.height / 800f);

        void EnsureStyles()
        {
            if (_box != null) return;
            int size = Mathf.RoundToInt(15f * Scale);

            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            bg.Apply();

            _box = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bg },
                padding = new RectOffset(size, size, size, size),
                alignment = TextAnchor.UpperLeft,
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                font = Font.CreateDynamicFontFromOSFont("Courier", size),
                normal = { textColor = Color.white },
                wordWrap = false,
            };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = size };
            _button = new GUIStyle(GUI.skin.button) { fontSize = size };
        }
    }
}
