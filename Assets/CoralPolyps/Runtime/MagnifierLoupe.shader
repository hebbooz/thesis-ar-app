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

        [Header(Framing)]
        // Screen-space framing: the footage sits still and the loupe is a window that
        // uncovers it, which reads as looking THROUGH a lens rather than as a decal
        // smeared over the honeycomb's UVs. Tune on device.
        _FootageScale ("Footage scale (smaller = more magnified)", Range(0.05, 2)) = 0.5

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
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
            TEXTURE2D(_TexB); SAMPLER(sampler_TexB);

            CBUFFER_START(UnityPerMaterial)
                float  _Blend;
                float  _Brightness;
                float  _Opacity;
                float  _FootageScale;
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
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- Screen-space framing, aspect-corrected so the footage is not
                //     stretched by the iPad's orientation ---
                float2 suv = IN.positionHCS.xy / _ScreenParams.xy;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 uv = suv - 0.5;
                if (aspect > 1.0) uv.x *= aspect; else uv.y /= max(aspect, 1e-4);
                uv = uv / max(_FootageScale, 1e-4) + 0.5;

                // --- What is showing: the two heaviest clips, crossfaded ---
                // The project is Linear + HDR, so this lerp is a linear-space blend.
                // Weights are opacities only; no playhead is ever scrubbed.
                float3 a = SAMPLE_TEXTURE2D(_TexA, sampler_TexA, uv).rgb;
                float3 b = SAMPLE_TEXTURE2D(_TexB, sampler_TexB, uv).rgb;
                float3 color = lerp(a, b, saturate(_Blend)) * _Brightness;

                // --- Where it is showing: the proximity-driven window ---
                // With _LoupeRadius at 0 (viewer far away, or tracking lost) the
                // smoothstep saturates and reveal is 0 everywhere — the layer
                // disappears completely and the coral is just the coral.
            #if defined(_LOUPE_ON)
                float dLoupe = distance(IN.positionWS, _LoupeCenter.xyz);
                float reveal = 1.0 - smoothstep(_LoupeRadius - _LoupeSoftness, _LoupeRadius, dLoupe);
            #else
                float reveal = 1.0;
            #endif

                return half4(color, saturate(reveal * _Opacity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
