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

        public Texture Alive => _rtAlive;
        public Texture Fluorescent => _rtFluorescent;
        public Texture Dead => _rtDead;
        public bool Ready => _rtAlive != null && _rtFluorescent != null && _rtDead != null;
        public string SourceName => "video";

        void Awake()
        {
            var clips = CoralConfig.Shared.magnifier_clips;
            Create(clips.alive, rt => _rtAlive = rt);
            Create(clips.fluorescent, rt => _rtFluorescent = rt);
            Create(clips.dead, rt => _rtDead = rt);
        }

        void Create(string fileName, System.Action<RenderTexture> assign)
        {
            var vp = gameObject.AddComponent<VideoPlayer>();
            vp.source = VideoSource.Url;
            vp.url = Path.Combine(Application.streamingAssetsPath, fileName);
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
                Debug.Log($"[magnifier] {fileName} prepared {p.width}x{p.height}, looping");
            };

            vp.Prepare();
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
