Shader "CoralPolyps/MagnifierLoupe"
{
    // The magnifier layer: polyp footage revealed inside a soft world-space window
    // that opens as the viewer leans in (CONTROL_INTEGRATION.md §3.2).
    //
    // Two jobs, from two different owners, and keeping them apart is the whole point
    // of the architecture:
    //   * WHERE it shows  <- proximity. ProximityRevealController drives _LoupeCenter
    //     and _LoupeRadius. Lean in, the window opens.
    //   * WHAT it shows   <- the wire. CoralMagnifier drives _TexA / _TexB / _Blend
    //     from the server's (state, intensity). Alive, fluorescing, or dead.
    //
    // The composite is done HERE, not in a fullscreen image effect. The projection
    // player blends in OnRenderImage, which silently never fires under URP; and a
    // masked reveal on a tracked surface is what this actually is, so the surface
    // shader is the right place for it.
    //
    // THE FOOTAGE IS PROJECTED IN OBJECT SPACE, NOT SCREEN SPACE. That is the whole
    // metaphor: with a real magnifying glass the content is attached to the OBJECT
    // and the glass moves over it. Sampling in screen space would map a given cup to
    // different footage texels as the device moved, so the polyps would slide across
    // the coral — and USER_STORIES.md V4 requires the opposite ("polyps stay
    // registered to the same cups from different angles").
    //
    // Object space rather than mesh UVs, because the scan's UVs were authored for the
    // skeleton texture and would smear the footage across the honeycomb. Object space
    // is rigidly attached to the mesh, so it survives tracking updates for free, and
    // it ignores the magnification scale — so the footage magnifies WITH the coral,
    // which is what a loupe should do.
    //
    // Triplanar rather than a single plane because the coral is a dome: over a ~6 cm
    // loupe on a ~10 cm boulder the surface turns far enough that one plane visibly
    // stretches on the flanks. _ProjectionSharpness collapses this toward a planar
    // projection (high values pick the dominant axis), so the planar case is still
    // available without a second shader.
    //
    // Intended setup: a duplicate of the coral mesh sitting just over the tissue
    // layer, so the footage is occluded and registered exactly like the coral is.

    Properties
    {
        [Header(Footage (driven by CoralMagnifier))]
        // Default black so an unprepared or missing clip reads as "nothing revealed"
        // rather than flashing a white quad over the coral.
        _TexA ("Clip A (heaviest)", 2D) = "black" {}
        _TexB ("Clip B (second)", 2D) = "black" {}
        _Blend ("A -> B blend", Range(0, 1)) = 0

        _Brightness ("Footage brightness", Range(0, 4)) = 1.0
        _Opacity ("Footage opacity inside the loupe", Range(0, 1)) = 1.0

        [Header(Projection (object space))]
        // Tile size in the coral's own metres: how much of the coral one repeat of the
        // footage covers. SMALLER = MORE MAGNIFIED. The coral is ~10 cm across, so a
        // value near the loupe's diameter puts roughly one repeat inside the window
        // and keeps any tiling seam out of sight. This is the tuning knob.
        _FootageScale ("Footage tile size (coral metres)", Range(0.005, 0.5)) = 0.04

        // 1 = smooth triplanar blend; high = effectively planar on the dominant axis.
        _ProjectionSharpness ("Planar-ness (high = single plane)", Range(1, 16)) = 4

        [Header(Loupe Reveal (driven by ProximityRevealController))]
        [Toggle(_LOUPE_ON)] _LoupeOn ("Loupe reveal enabled", Float) = 1
        _LoupeCenter ("Loupe centre (world, runtime-driven)", Vector) = (0,0,0,0)
        _LoupeRadius ("Loupe radius (world m)", Float) = 0.0
        _LoupeSoftness ("Loupe edge softness (world m)", Range(0.001, 0.1)) = 0.012
    }

    SubShader
    {
        // Draws over the tissue, which is opaque geometry. No depth write: this is a
        // single thin layer with nothing to sort against itself.
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "MagnifierLoupe"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _LOUPE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 positionOS  : TEXCOORD1;
                float3 normalOS    : TEXCOORD2;
            };

            TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
            TEXTURE2D(_TexB); SAMPLER(sampler_TexB);

            CBUFFER_START(UnityPerMaterial)
                float  _Blend;
                float  _Brightness;
                float  _Opacity;
                float  _FootageScale;
                float  _ProjectionSharpness;
                float4 _LoupeCenter;
                float  _LoupeRadius;
                float  _LoupeSoftness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.positionOS  = IN.positionOS.xyz;   // rigidly attached to the coral
                OUT.normalOS    = IN.normalOS;
                return OUT;
            }

            // Triplanar sample of one clip, weighted by the object-space normal.
            float3 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 p, float3 w)
            {
                float3 x = SAMPLE_TEXTURE2D(tex, samp, p.zy).rgb;
                float3 y = SAMPLE_TEXTURE2D(tex, samp, p.xz).rgb;
                float3 z = SAMPLE_TEXTURE2D(tex, samp, p.xy).rgb;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- Where it is showing: the proximity-driven window ---
                // Computed FIRST and clipped, so the six texture fetches below only
                // happen inside the loupe. With _LoupeRadius at 0 (viewer far away, or
                // tracking lost) the smoothstep saturates, reveal is 0 everywhere, and
                // the whole layer costs a clip per fragment.
            #if defined(_LOUPE_ON)
                float dLoupe = distance(IN.positionWS, _LoupeCenter.xyz);
                float reveal = 1.0 - smoothstep(_LoupeRadius - _LoupeSoftness, _LoupeRadius, dLoupe);
            #else
                float reveal = 1.0;
            #endif
                clip(reveal - 0.002);

                // --- Object-space triplanar projection ---
                float3 p = IN.positionOS / max(_FootageScale, 1e-4);
                float3 w = abs(normalize(IN.normalOS));
                w = pow(w, _ProjectionSharpness);
                w /= max(w.x + w.y + w.z, 1e-4);

                // --- What is showing: the two heaviest clips, crossfaded ---
                // The project is Linear + HDR, so this lerp is a linear-space blend.
                // Weights are opacities only; no playhead is ever scrubbed.
                float3 a = SampleTriplanar(TEXTURE2D_ARGS(_TexA, sampler_TexA), p, w);
                float3 b = SampleTriplanar(TEXTURE2D_ARGS(_TexB, sampler_TexB), p, w);
                float3 color = lerp(a, b, saturate(_Blend)) * _Brightness;

                return half4(color, saturate(reveal * _Opacity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
