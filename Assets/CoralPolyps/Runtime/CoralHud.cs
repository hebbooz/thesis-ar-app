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

        [Tooltip("The proximity controller, for the magnifier diagnostics block. " +
                 "Found in the scene if left empty.")]
        public ProximityRevealController proximity;

        [Tooltip("The defocus layer, so the blur can be read off the same line as the reveal " +
                 "that drives it. Optional; found in the scene if left empty.")]
        public MagnifierDefocus defocus;

        [Tooltip("Visible on launch. Toggle any time with a three-finger tap (or H in the editor). " +
                 "Set hud_enabled:false in coral-ar.json to disable it entirely for the exhibition.")]
        public bool visible = true;

        [Tooltip("Show the editable server-host row. Off by default — it is a wide input " +
                 "box that swamps the overlay, and the address is already on the id line. " +
                 "Turn it on for the exhibition, when correcting the IP on the day matters " +
                 "more than a clean view.")]
        public bool showHostEditor = false;

        string _hostEdit;
        bool _hostEditPrimed;
        GUIStyle _box, _label, _field, _button;

        void Awake()
        {
            if (listener == null) listener = FindAnyObjectByType<CoralOscListener>();
            if (appearance == null) appearance = FindAnyObjectByType<CoralAppearance>();
            if (magnifier == null) magnifier = FindAnyObjectByType<CoralMagnifier>();
            if (proximity == null) proximity = FindAnyObjectByType<ProximityRevealController>();
            if (defocus == null) defocus = FindAnyObjectByType<MagnifierDefocus>();
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

                // Diagnostics show `state` — the unquantised truth — but the coral is
                // rendered from `cue`. They differ only while quantisation holds a
                // change back for the bar line, so surfacing the cue exactly when it
                // lags is what distinguishes "waiting for the downbeat" from "the
                // broadcast stopped arriving".
                string cue = listener.Cue == listener.State
                    ? ""
                    : $"   cue={listener.Cue} {CoralOscListener.StateName(listener.Cue)}";
                GUILayout.Label($"state={listener.State} {CoralOscListener.StateName(listener.State)}{cue}   " +
                                $"intensity={listener.Intensity:F2}   T={listener.Temp:F1}C", _label);

                string applied = AppliedLine();
                if (applied != null) GUILayout.Label(applied, _label);

                // --- Magnification diagnostics (Phase 1 of the black-box fix) ---
                // The acceptance criteria are read straight off these lines during a screen
                // recording: a 60 s close hold must show zero state changes here.
                //
                // The third line is registration. The first two describe the pose as a single
                // distance, which is the half of it that was never the problem — a yaw error
                // moves the coral not at all in `d` and quite visibly on the print.
                if (proximity != null)
                {
                    var prevMag = GUI.color;
                    GUI.color = MagnifyColor();
                    GUILayout.Label(MagnifyLine(), _label);
                    GUILayout.Label(MagnifyTimersLine(), _label);
                    GUILayout.Label(MagnifyScaleLine(), _label);
                    GUILayout.Label(RegistrationLine(), _label);
                    GUILayout.Label(UprightLine(), _label);
                    GUI.color = prevMag;
                }

                var prev = GUI.color;
                GUI.color = StatusColor();
                GUILayout.Label($"last broadcast {Age(listener.SecondsSinceMessage)}   " +
                                $"hello sent {Age(listener.SecondsSinceHello)}   rx={listener.RxCount}", _label);
                GUI.color = prev;

                GUILayout.Label($"config: {cfg.LoadedFrom}", _label);

                // --- Runtime server address, for the morning the Mac's IP has moved ---
                // Off by default: it is a wide input row that dominates the overlay while
                // judging the magnifier, and the address is already shown on the id line
                // above. Tick showHostEditor when you actually need to change it.
                if (!showHostEditor) { GUILayout.EndVertical(); GUILayout.EndArea(); return; }

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
                ? $"stress={appearance.Stress:F2} fluor={appearance.FluorPresence:F2}"
                : "stress=-";
            string mag = magnifier != null
                ? $"magnifier=alive {magnifier.WAlive:F2} / fluoro {magnifier.WFluorescent:F2} / " +
                  $"dead {magnifier.WDead:F2}   src={magnifier.SourceName} [{magnifier.SourceStatus}]"
                : $"magnifier=-   src={CoralConfig.Shared.magnifier_source}";

            return $"{stress}   {mag}";
        }

        /// <summary>
        /// The magnifier's dashboard: label + age, the reveal, the effective and raw
        /// distances, and whether the input is being held.
        ///
        /// `d` is the number to watch. Since the rewrite the reveal is a pure function of
        /// it, so `m` and `d` must move together or not at all — if `m` changes while `d`
        /// sits still, something downstream has grown a mind of its own and that is a bug,
        /// not a tuning problem. HELD means the pose is not believable and `d` is frozen,
        /// which is why the picture is frozen too.
        /// </summary>
        string MagnifyLine()
        {
            string d = float.IsInfinity(proximity.SmoothedDistanceM)
                ? "lost" : $"{proximity.SmoothedDistanceM:F3}";
            string raw = float.IsInfinity(proximity.RawDistanceM)
                ? "-" : $"{proximity.RawDistanceM:F3}";
            return $"magnify: {proximity.State} {proximity.StateAgeS:F1}s   " +
                   $"m={proximity.FullscreenReveal:F2}   d={d} (raw {raw})   " +
                   $"pose {(proximity.LastPosePlausible ? "ok" : "BAD")}" +
                   (proximity.TakeoverHeld ? $"   HELD {proximity.HeldForS:F1}s" : "");
        }

        string MagnifyTimersLine() =>
            $"cover {(proximity.fullscreen != null ? proximity.fullscreen.ScreenCoverage01 : -1f):F2} " +
            $"{(proximity.fullscreen != null && proximity.fullscreen.ScreenFullyCovered ? "FULL" : "partial")}   " +
            $"loupe {proximity.LoupeRadius * 1000f:F0}mm   x{proximity.Magnification:F1}   " +
            $"blur {(defocus != null ? $"{defocus.Weight:F2}" : "-")}   " +
            // Which cup the polyps are emerging from. "free" means the loupe is still
            // aim-following; a number that CHANGES while leaning in is the pin thrashing,
            // which would look exactly like the sliding it exists to stop.
            $"pin {(proximity.PinnedIndex >= 0 ? $"#{proximity.PinnedIndex}" : "free")}   " +
            $"vuforia {(proximity.VuforiaTracked ? "trk" : "EXT")}";

        /// <summary>
        /// The two scales the emergence is judged on, neither of which is observable from a
        /// plinth without printing it.
        ///
        /// `clip` is how many millimetres of coral the footage's width currently spans — the
        /// footage's magnification in the one unit that can be held against the object it is
        /// coming out of. At the start of the takeover it should read about 9.6 mm, which is
        /// one corallite (5.8 mm in world metres) divided by the polyp's ~0.6 share of the
        /// frame: the polyps arrive at LIFE SIZE and magnify from there. If it starts much
        /// below that they are emerging from nothing again.
        ///
        /// `limit` names which of the three terms is holding the opening down, and it is the
        /// most useful word on this line. The iris radius is a Min of the growth arc, the
        /// silhouette leash and the screen corner, and the screen looks identical whichever
        /// one wins — so "the footage is cutting off inside the tissue" has three different
        /// causes that cannot be told apart by eye. `arc` is the normal answer; `leash` while
        /// the tissue visibly extends past the video means the bound is measured too small;
        /// `cap` means the takeover is complete. `coral` is the magnified radius the arc now
        /// ends on.
        /// </summary>
        string MagnifyScaleLine()
        {
            var fs = proximity.fullscreen;
            if (fs == null) return "scale: (no takeover layer)";

            string leash = float.IsInfinity(fs.LeashRadius) ? "off" : $"{fs.LeashRadius:F2}";
            return $"scale: clip {fs.FootageWorldWidthM * 1000f:F1}mm   " +
                   $"coral {proximity.CoralWorldRadiusM * 1000f:F0}mm   " +
                   $"leash {leash}   limit {fs.IrisLimit}";
        }

        /// <summary>
        /// Registration, in degrees — the half of the pose the distance readouts cannot see.
        ///
        /// WITH THE GATE OFF (the default, and where it should stay until this has been read):
        /// `spin` is the solve's own frame-to-frame jitter in deg/s and `drift` is one frame of
        /// it. **This is the number that sets implausibleSpinDegPerS.** Walk a slow circuit,
        /// watch the peak `spin` during honest tracking, and put the threshold comfortably
        /// above it — the gate is a teleport test, not a steadiness test, so erring loose costs
        /// nothing and erring tight is catastrophic. The first attempt guessed 60 without
        /// looking, which turned out to be at or below normal jitter, and the coral latched to
        /// a stale pose and was flung around by the magnification anchor. See MAGNIFIER.md §3c.
        ///
        /// WITH THE GATE ON: `drift` becomes how far the live solve has twisted from the
        /// orientation actually being drawn, because the reference stops advancing while a
        /// solve is refused.
        ///   * drift near 0 with rej climbing slowly  — flips are transient and being caught.
        ///     This is the working state; the mesh does not visibly twist.
        ///   * drift parked at 20-40deg and NOT falling — the tracker has settled on a wrong
        ///     yaw and the gate has (correctly) given up refusing it. No filter recovers this;
        ///     it means the symmetry has to be broken on the model target or the print.
        ///   * rej climbing continuously from every angle — the threshold is below honest
        ///     tracking. Turn the gate back OFF and re-read `spin`.
        /// </summary>
        string RegistrationLine() =>
            $"rot: drift {proximity.SolveDriftDeg:F1}deg   spin {proximity.SolveSpinDegPerS:F0}deg/s   " +
            $"{(proximity.LastSpinPlausible ? "ok" : "REJECTED")}   rej {proximity.SpinRejections}";

        /// <summary>
        /// Which way is up, in degrees — the OTHER half of the rotation, and the only half with
        /// an outside witness. `up` is the angle between the print's up axis as solved and world
        /// up. The print is bolted upright, so this is ground truth rather than a comparison
        /// against another solve, which is why it reads so much more plainly than `rot:`.
        ///
        ///   * up near 0, rej not moving — honest tracking. The working state.
        ///   * up near 180 with rej climbing — the tracker is flipping the coral and the gate is
        ///     catching it. Expected on this target: the scan's footprint is a circle and its top
        ///     and bottom caps differ by 4%, so upside down and right way up score alike.
        ///   * up near 180 CONSTANTLY while the coral looks correct on screen — printUpInTargetSpace
        ///     is wrong, not the tracker. Negate it. The gate releases itself after flipHoldMaxS
        ///     and logs a warning when this happens, so it cannot wreck registration meanwhile.
        ///   * up near 90 — printUpInTargetSpace names the wrong axis entirely.
        ///
        /// See MAGNIFIER.md §3d.
        /// </summary>
        string UprightLine() =>
            $"up: {proximity.SolveTiltDeg:F0}deg   " +
            $"{(proximity.LastTiltPlausible ? "ok" : "FLIPPED")}   rej {proximity.FlipRejections}";

        /// <summary>
        /// White at meso, green through the blend, blue at full micro — and amber the moment
        /// the pose is distrusted or the input is held, so a recording shows exactly when the
        /// picture stopped being live.
        /// </summary>
        Color MagnifyColor()
        {
            if (proximity.TakeoverHeld || !proximity.LastPosePlausible ||
                !proximity.LastSpinPlausible || !proximity.LastTiltPlausible)
                return new Color(1f, 0.8f, 0.3f);
            switch (proximity.State)
            {
                case ProximityRevealController.MagState.Micro:
                    return new Color(0.6f, 0.85f, 1f);
                case ProximityRevealController.MagState.Blending:
                    return new Color(0.8f, 1f, 0.8f);
                default:
                    return Color.white;
            }
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
