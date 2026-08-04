/// <summary>
/// Configuration for the AR client — the Unity-side equivalent of the server's
/// config.yaml. Every host, port, id and timing lives here so that moving the
/// installation or retiming a fade is a file edit and never a rebuild
/// (CONTROL_INTEGRATION.md §5, "config, not code").
///
/// iOS has no "beside the .app" directory to drop a file into, so the projection
/// player's search-upward pattern does not transfer. Search order is:
///   1. Application.persistentDataPath/coral-ar.json — editable over USB via the
///      Files app, and the destination Save() writes to, so the server address can
///      be corrected on exhibition morning without an Xcode round trip.
///   2. StreamingAssets/coral-ar.json — the default shipped inside the build.
///   3. The field initialisers below.
///
/// Fail soft at every step: a missing or malformed config logs loudly and falls
/// back to defaults rather than refusing to start. An exhibition device must
/// always boot (CLAUDE.md §9 rule 3).
/// </summary>
using System;
using System.IO;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// The three looping magnifier clips (CONTROL_INTEGRATION.md §3.2). Three, not
    /// four: recovery heals dead → alive directly, never back through fluorescent,
    /// because fluorescence is a stress response — the way out is not the way in.
    /// </summary>
    [Serializable]
    public class MagnifierClips
    {
        public string alive = "polyps-alive.mp4";
        public string fluorescent = "polyps-fluorescent.mp4";
        public string dead = "polyps-dead.mp4";
    }

    [Serializable]
    public class CoralConfig
    {
        // --- Network (CONTROL_INTEGRATION.md §2) ---
        public string server_host = "192.168.8.10";
        public int server_port = 9000;      // the server's OSC listener (broadcast.listen_port)
        public int listen_port = 9001;      // our socket (broadcast.client_port)

        /// <summary>
        /// Empty means "generate a stable one for this device on first run". The
        /// server's registry is keyed by id, so two devices sharing an id clobber
        /// each other — never hard-code this into a build that gets installed three
        /// times. See <see cref="EnsureClientId"/>.
        /// </summary>
        public string client_id = "";

        public float hello_interval_s = 5.0f;   // well inside the server's 15 s prune window

        // --- Appearance (CONTROL_INTEGRATION.md §3) ---
        /// <summary>
        /// One slew rate shared by the tissue and the magnifier, so the two layers —
        /// seen together, one inside the other — never disagree about how bleached
        /// the coral is (§10's open item).
        /// </summary>
        public float crossfade_s = 3.0f;

        /// <summary>
        /// Where peak fluorescence sits on the shader's 0..1 _Stress dial. States 0/1
        /// are capped at this, which is what makes it structurally impossible for the
        /// app to bleach itself — only the server's latch goes past it.
        /// </summary>
        public float fluor_point = 0.5f;

        /// <summary>
        /// Hold emission at 0 through state 3 so the coral heals bleached → healthy
        /// directly instead of flashing fluorescent on its way out (§3.1).
        /// </summary>
        public bool suppress_emission_during_recovery = true;

        // --- Magnifier (CONTROL_INTEGRATION.md §3.3) ---
        /// <summary>
        /// "placeholder", "grid" or "video". The placeholder is a permanent regression
        /// harness, not scaffolding — it keeps the magnifier testable on a laptop with
        /// no footage and no device. "grid" is the same source showing one static test
        /// pattern instead, which is how the object-space projection is checked: the
        /// grid must stay stuck to the coral as the device moves.
        /// </summary>
        public string magnifier_source = "placeholder";
        public MagnifierClips magnifier_clips = new MagnifierClips();

        /// <summary>
        /// How far out of focus everything except the footage goes, 0..1. A lens has a
        /// shallow depth of field the whole time it is held close, so this wants to be
        /// high — the surroundings should stop competing for the eye as soon as there is
        /// anything to look into.
        /// </summary>
        public float magnifier_blur = 1.0f;

        /// <summary>
        /// The reveal at which the blur reaches full, 0..1 — i.e. how much of the approach
        /// the softening is spread across.
        ///
        /// It sets a balance rather than a preference. Too small and the screen snaps from
        /// sharp to soft in under a centimetre of hand travel, which reads as a cut. Too
        /// large and the blur is still arriving when the footage has already taken over, so
        /// it never does its job of quieting the surroundings while the opening is small.
        /// 0.35 spreads it over roughly the first 7 cm of the takeover.
        ///
        /// This is only meaningful because the bokeh ramp is perceptually linear (see
        /// MagnifierDefocus). Against a raw focal-length sweep the number would be a lie,
        /// since all the visible change would sit in the last fraction of it.
        /// </summary>
        public float magnifier_blur_onset = 0.35f;

        /// <summary>
        /// "bokeh" or "gaussian". Gaussian is cheap but URP caps its radius at 1.5, which
        /// is a low and hard ceiling — bokeh is the only way to a genuinely heavy defocus.
        /// It costs more per frame, but only while the iris is partly open, and not at all
        /// once the screen is covered. Drop to gaussian if the endurance soak complains.
        /// </summary>
        public string magnifier_blur_mode = "bokeh";

        public bool hud_enabled = true;

        /// <summary>Where this config was actually loaded from. Shown in the HUD:
        /// "which file am I running?" is the first question on exhibition morning.</summary>
        [NonSerialized] public string LoadedFrom = "built-in defaults";

        const string FileName = "coral-ar.json";
        const string ClientIdKey = "coral.client_id";

        static string PersistentPath => Path.Combine(Application.persistentDataPath, FileName);
        static string StreamingPath => Path.Combine(Application.streamingAssetsPath, FileName);

        static CoralConfig _shared;

        /// <summary>
        /// The one config instance for the run. The listener, the appearance and the
        /// magnifier all read this rather than each loading their own, so a host
        /// corrected in the HUD is the same host every component sees — and so any
        /// one of them still works with the other two absent (they must stay
        /// independently testable, CONTROL_INTEGRATION.md §4).
        /// </summary>
        public static CoralConfig Shared => _shared ??= Load();

        public static CoralConfig Load()
        {
            var cfg = LoadFrom(PersistentPath) ?? LoadFrom(StreamingPath) ?? new CoralConfig();
            Debug.Log($"[config] using {cfg.LoadedFrom}");
            cfg.EnsureClientId();
            return cfg;
        }

        static CoralConfig LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var cfg = JsonUtility.FromJson<CoralConfig>(File.ReadAllText(path));
                if (cfg == null) throw new Exception("parsed to null");
                cfg.LoadedFrom = path;
                return cfg;
            }
            catch (Exception e)
            {
                // Malformed JSON must not stop the installation from booting; fall
                // through to the next source rather than throwing.
                Debug.LogError($"[config] failed to read {path} ({e.Message}) — falling back");
                return null;
            }
        }

        /// <summary>
        /// Persist to <see cref="PersistentPath"/>. Used when the server address is
        /// corrected from the HUD, so the fix survives a relaunch.
        /// </summary>
        public bool Save()
        {
            try
            {
                File.WriteAllText(PersistentPath, JsonUtility.ToJson(this, true));
                LoadedFrom = PersistentPath;
                Debug.Log($"[config] saved {PersistentPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[config] could not save {PersistentPath}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Give this device a stable, unique id, persisted so it survives relaunches
        /// (the server keys its registry by id; a fresh id every launch would leave
        /// stale entries to be pruned and make the server log unreadable).
        /// </summary>
        public void EnsureClientId()
        {
            if (!string.IsNullOrWhiteSpace(client_id)) return;

            client_id = PlayerPrefs.GetString(ClientIdKey, "");
            if (!string.IsNullOrWhiteSpace(client_id)) return;

            // Device name for a human-readable log line, GUID tail so that three
            // identically-named iPads still get distinct registry entries.
            string name = string.IsNullOrWhiteSpace(SystemInfo.deviceName) ? "ipad" : SystemInfo.deviceName;
            client_id = $"{Sanitise(name)}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            PlayerPrefs.SetString(ClientIdKey, client_id);
            PlayerPrefs.Save();
            Debug.Log($"[config] generated client_id={client_id}");
        }

        static string Sanitise(string s)
        {
            var chars = s.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '-';
            return new string(chars).Trim('-');
        }
    }
}
