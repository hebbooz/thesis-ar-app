/// <summary>
/// Procedural stand-in for the polyp footage (CONTROL_INTEGRATION.md §3.3).
///
/// Each state gets an unmistakable visual signature so a glance at the screen says
/// which one is showing, and — the part that matters — alive and fluorescent MOVE
/// while dead is FROZEN. State 2's beat is the discovery that the life is gone, and
/// a still that is merely a different colour cannot test that. Dead is deliberately
/// pale rather than black: absence of life, not absence of image.
///
/// Permanent, not scaffolding. This is what lets the whole magnifier path — weights,
/// slew, both backward transitions, the loupe mask — be verified on a laptop with no
/// footage and no device.
/// </summary>
using UnityEngine;

namespace CoralPolyps
{
    public class PlaceholderMagnifierSource : MonoBehaviour, IMagnifierSource
    {
        const int Size = 96;        // small on purpose: regenerated per frame on the CPU
        const int Blobs = 14;

        Texture2D _alive, _fluorescent, _dead;
        Color32[] _buffer;

        public Texture Alive => _alive;
        public Texture Fluorescent => _fluorescent;
        public Texture Dead => _dead;
        public bool Ready => _alive != null;
        public string SourceName => "placeholder";

        void Awake()
        {
            _buffer = new Color32[Size * Size];
            _alive = New();
            _fluorescent = New();
            _dead = New();

            // Dead is drawn once and never again — the stillness is the point.
            Draw(_dead, new Color(0.10f, 0.11f, 0.12f), new Color(0.86f, 0.87f, 0.84f), 0f);
        }

        void Update()
        {
            float t = Time.time;
            // Deep teal water with bright green polyps: the "reef is alive" read.
            Draw(_alive, new Color(0.02f, 0.10f, 0.11f), new Color(0.20f, 0.95f, 0.45f), t);
            // Near-black under excitation light, pigment glowing gold — the same
            // organism, fluorescing, not a different one.
            Draw(_fluorescent, new Color(0.01f, 0.02f, 0.03f), new Color(1f, 0.78f, 0.12f), t);
        }

        static Texture2D New()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            return tex;
        }

        /// <summary>
        /// Soft drifting blobs — polyps waving, abstracted. <paramref name="time"/> of 0
        /// freezes them, which is exactly what the dead clip needs.
        /// </summary>
        void Draw(Texture2D tex, Color background, Color polyp, float time)
        {
            for (int i = 0; i < _buffer.Length; i++) _buffer[i] = background;

            for (int b = 0; b < Blobs; b++)
            {
                // Fixed per-blob phase so each drifts on its own path rather than the
                // whole field sliding as one sheet.
                float phase = b * 2.399963f;                     // golden angle, spreads them
                float cx = 0.5f + 0.36f * Mathf.Sin(phase + time * 0.7f);
                float cy = 0.5f + 0.36f * Mathf.Cos(phase * 1.7f + time * 0.5f);
                float r = Size * (0.055f + 0.02f * Mathf.Sin(phase * 3f + time * 2.1f));

                int px = Mathf.RoundToInt(cx * Size), py = Mathf.RoundToInt(cy * Size);
                int ri = Mathf.CeilToInt(r);

                for (int y = py - ri; y <= py + ri; y++)
                {
                    if (y < 0 || y >= Size) continue;
                    for (int x = px - ri; x <= px + ri; x++)
                    {
                        if (x < 0 || x >= Size) continue;
                        float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        float a = Mathf.Clamp01(1f - d / r);
                        if (a <= 0f) continue;
                        a *= a;                                   // soft falloff
                        int idx = y * Size + x;
                        Color32 cur = _buffer[idx];
                        _buffer[idx] = Color32.Lerp(cur, polyp, a);
                    }
                }
            }

            tex.SetPixels32(_buffer);
            tex.Apply(false);
        }

        void OnDestroy()
        {
            if (_alive != null) Destroy(_alive);
            if (_fluorescent != null) Destroy(_fluorescent);
            if (_dead != null) Destroy(_dead);
        }
    }
}
