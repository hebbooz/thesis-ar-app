/// <summary>
/// The real footage half of the §3.3 seam: three looping clips decoding into three
/// RenderTextures.
///
/// THE PLAYHEAD INVARIANT — copy this, it is hard-won:
///
///     Playheads only ever advance. A clip is seeked only while its weight is 0.
///
/// Here that is absolute: nothing seeks at all. `intensity` drives OPACITY, NEVER a
/// playhead. Scrubbing a clip to intensity × duration would run the polyps backwards
/// whenever the reef cools before the latch — instantly legible as an error in a way
/// no dissolve ever is. All three clips loop forever, untouched; only their weights
/// move, and those live in CoralMagnifier.
///
/// iPad video budget: all three are prepared at launch and left decoding. A Prepare()
/// mid-transition stalls for hundreds of milliseconds and the blend visibly hitches;
/// three simultaneous decodes is the price of never stalling. If thermals or frame
/// rate suffer during the endurance soak, drop to two players and swap the idle one
/// while its weight is 0 — which the invariant above already makes safe.
/// </summary>
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace CoralPolyps
{
    public class VideoMagnifierSource : MonoBehaviour, IMagnifierSource
    {
        RenderTexture _rtAlive, _rtFluorescent, _rtDead;
        readonly System.Collections.Generic.List<VideoPlayer> _players =
            new System.Collections.Generic.List<VideoPlayer>();

        // Watchdog budget. Generous enough to cover a slow prepare on a cold app start,
        // bounded so a genuinely broken clip cannot turn into an endless Play() storm.
        const float NudgeIntervalS = 0.5f;
        int _nudgesLeft = 6;
        float _sinceNudge;

        public Texture Alive => _rtAlive;
        public Texture Fluorescent => _rtFluorescent;
        public Texture Dead => _rtDead;
        public bool Ready => _rtAlive != null && _rtFluorescent != null && _rtDead != null;
        public string SourceName => "video";

        /// <summary>
        /// How many clips prepared, and how many of them actually advanced their playhead
        /// since the last probe. "3/3 moving" is working footage; "3/3 STALLED" is three
        /// correct-looking still images, which is what a frozen VideoPlayer looks like and
        /// is otherwise impossible to tell apart on a device with no console.
        /// </summary>
        public string Status =>
            $"{_prepared}/{_players.Count} " +
            (_players.Count == 0 ? "none"
             : _prepared == 0 ? "NOT PREPARED"
             : _advancing > 0 ? $"{_advancing} moving" : "STALLED");

        int _prepared;
        int _advancing;
        long[] _lastFrame = new long[0];
        float _sinceFrameProbe;

        void Awake()
        {
            var clips = CoralConfig.Shared.magnifier_clips;
            Create(clips.alive, rt => _rtAlive = rt);
            Create(clips.fluorescent, rt => _rtFluorescent = rt);
            Create(clips.dead, rt => _rtDead = rt);
        }

        /// <summary>
        /// A filesystem path is not a URL, and VideoPlayer.url parses it as one.
        ///
        /// THIS PROJECT LIVES UNDER "2026 University". That space is not escaped in a raw
        /// path, so AVFoundation gets a malformed URL, and the failure is close to silent:
        /// the player never prepares, prepareCompleted never fires, no RenderTexture is
        /// ever created, Ready stays false and the magnifier holds black forever. The
        /// nudge watchdog below cannot help either — it only restarts players that
        /// PREPARED and then stopped.
        ///
        /// Worse, it only breaks in the EDITOR. On the iPad, streamingAssetsPath is inside
        /// the app bundle and has no spaces, so the device build works and the editor does
        /// not — which is the exact opposite of the direction bugs are usually looked for,
        /// and is why this survived being "the video isn't playing" for a while.
        ///
        /// Uri.AbsoluteUri percent-encodes properly and yields a file:// URL that is valid
        /// on both. Keep this even after the project moves somewhere without spaces: the
        /// next person to check out into a path with one should not have to find this
        /// twice.
        /// </summary>
        static string LocalUrl(string path)
        {
            try { return new System.Uri(path).AbsoluteUri; }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[magnifier] could not build a file URL for {path} " +
                                 $"({e.Message}) — falling back to the raw path.");
                return path;
            }
        }

        void Create(string fileName, System.Action<RenderTexture> assign)
        {
            // ONE CHILD GAMEOBJECT PER CLIP — not three VideoPlayers on this one.
            //
            // Unity is unreliable about several VideoPlayer components sharing a
            // GameObject: they prepare, they render a first frame, and then they
            // stomp on each other and none of them advance. The symptom is exactly
            // the one that is hardest to attribute — the magnifier shows a correct
            // but FROZEN frame per state, so the footage looks like a set of stills
            // and every other explanation (decode budget, RenderTexture, loop seams)
            // gets investigated first.
            //
            // The projection player learned this and gives each layer its own child.
            // A child costs nothing. Do not fold these back onto one object.
            var host = new GameObject($"Clip_{Path.GetFileNameWithoutExtension(fileName)}");
            host.transform.SetParent(transform, false);

            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                Debug.LogError($"[magnifier] {fileName} NOT FOUND at {path} — the magnifier " +
                               "will hold black. Check magnifier_clips in coral-ar.json.");

            var vp = host.AddComponent<VideoPlayer>();
            vp.source = VideoSource.Url;
            vp.url = LocalUrl(path);
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.isLooping = true;                  // seamless, forever, never seeked
            vp.playOnAwake = false;
            vp.waitForFirstFrame = true;
            vp.audioOutputMode = VideoAudioOutputMode.None;   // the soundscape is Ableton's job

            vp.errorReceived += (_, msg) =>
                Debug.LogError($"[magnifier] {fileName}: {msg}");

            vp.prepareCompleted += p =>
            {
                // Size the target from the clip itself rather than stating it in config.
                // A RenderTexture whose aspect disagrees with the video's squashes the
                // frame before anything downstream ever sees it — the projection player
                // had to declare the size for want of this hook; we have it, so use it.
                var rt = new RenderTexture((int)p.width, (int)p.height, 0, RenderTextureFormat.ARGB32)
                {
                    name = $"magnifier-{fileName}",
                    // Repeat to match the placeholder: the shader projects this across
                    // the coral in object space, so outside one repeat it must tile
                    // rather than streak the edge pixels. Tune _FootageScale so one
                    // repeat covers the loupe and the seam never comes into view.
                    wrapMode = TextureWrapMode.Repeat,
                };
                rt.Create();
                p.targetTexture = rt;
                assign(rt);
                p.Play();
                _prepared++;
                Debug.Log($"[magnifier] {fileName} prepared {p.width}x{p.height}, looping");
            };

            vp.Prepare();
            _players.Add(vp);
        }

        /// <summary>
        /// Keep every prepared clip running. Play() issued from prepareCompleted is
        /// occasionally swallowed on iOS — the callback arrives before the player will
        /// accept the command, and it stays parked on frame 0 forever with no error.
        /// A frozen clip is indistinguishable from a still image, so this costs one
        /// bool check per clip per frame and removes a whole class of silent failure.
        ///
        /// This does NOT violate the playhead invariant: it only ever starts playback
        /// from wherever the head already is. Nothing seeks, nothing rewinds.
        /// </summary>
        void Update()
        {
            ProbeFrameAdvance();

            if (_nudgesLeft <= 0) return;

            _sinceNudge += Time.unscaledDeltaTime;
            if (_sinceNudge < NudgeIntervalS) return;
            _sinceNudge = 0f;

            bool nudged = false;
            for (int i = 0; i < _players.Count; i++)
            {
                var vp = _players[i];
                if (vp == null || !vp.isPrepared || vp.isPlaying) continue;
                vp.Play();
                nudged = true;
            }

            // Spend an attempt only when something actually needed one, and stop after a
            // few. Hammering Play() every frame at three AVFoundation players that are
            // refusing to start is not a retry — it is a denial-of-service against the
            // system video stack, and it will take the app down rather than fix a clip.
            if (nudged && --_nudgesLeft == 0)
                Debug.LogWarning("[magnifier] gave up nudging stalled clips — check the " +
                                 "prepared logs above; a clip that never plays is a decode " +
                                 "problem, not a timing one.");
        }

        /// <summary>
        /// Is the playhead actually moving? Sampled rather than watched every frame,
        /// because at 30 fps footage on a 60 fps display roughly half the frames show no
        /// change and a per-frame test would read as a stall constantly. Half a second is
        /// long enough that even a slow clip must have advanced.
        /// </summary>
        void ProbeFrameAdvance()
        {
            _sinceFrameProbe += Time.unscaledDeltaTime;
            if (_sinceFrameProbe < 0.5f) return;
            _sinceFrameProbe = 0f;

            if (_lastFrame.Length != _players.Count)
            {
                _lastFrame = new long[_players.Count];
                for (int i = 0; i < _lastFrame.Length; i++) _lastFrame[i] = -1;
            }

            int moving = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                var vp = _players[i];
                if (vp == null || !vp.isPrepared) continue;
                if (vp.frame != _lastFrame[i]) moving++;
                _lastFrame[i] = vp.frame;
            }
            _advancing = moving;
        }

        void OnDestroy()
        {
            Release(ref _rtAlive);
            Release(ref _rtFluorescent);
            Release(ref _rtDead);
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }
    }
}
