Shader "CoralPolyps/MagnifierFullscreen"
{
    // The takeover layer: at closest range the footage leaves the coral and fills the
    // screen. Drawn as a single full-screen UI quad by FullscreenMagnifier.
    //
    // Three jobs in one pass:
    //   * WHAT   <- CoralMagnifier's _TexA / _TexB / _Blend, the same pair the loupe
    //               shows, so the two layers can never disagree mid-handover.
    //   * WHERE  <- an iris centred on _Center (viewport coords), opening as _Radius
    //               grows. _Center tracks a corallite, so the video appears to come
    //               UP OUT OF a specific crater rather than fading in over everything.
    //   * FIT    <- cover-fit UVs from _ScreenAspect / _FootageAspect: fill the screen,
    //               crop the overflow, never distort and never letterbox.
    //
    // WHY AN IRIS RATHER THAN A CROSSFADE. A crossfade says "here is a video now".
    // An iris opening from a cup says "this is what is inside that cup" — the same
    // claim the whole piece makes, that the micro-scale is present in the object and
    // not an illustration beside it. It also hides the moment Vuforia drops tracking:
    // by the time the iris covers the screen there is no registration left to lose.
    Properties
    {
        _TexA ("Clip A (heaviest)", 2D) = "black" {}
        _TexB ("Clip B (second)", 2D) = "black" {}
        _Blend ("A -> B blend", Range(0, 1)) = 0

        _Center ("Iris centre (viewport xy)", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Iris radius (screen heights)", Float) = 0
        _Feather ("Iris edge softness", Range(0.0001, 0.5)) = 0.08

        // HOW BIG THE POLYPS ARE, as opposed to how big the hole is. Same units as
        // _Radius: the projected half-width of the clip, in screen heights.
        //
        // These used to be one number — the clip was fitted to the iris, so the
        // footage's scale was whatever the mask happened to be. That made the polyps
        // start at nothing: at emergence the whole frame was squeezed into a 3.6 mm
        // opening, drawing a polyp at roughly a third of the cup it was coming out of,
        // while the loupe underneath was drawing the same polyp several times larger.
        // Splitting them lets the footage begin at 1:1 with the skeleton and magnify
        // from there, which is what a lens does. Larger than _Radius means you see the
        // middle of the clip through a smaller hole.
        _ContentRadius ("Footage half-width (screen heights)", Float) = 0

        // 0 = footage at crater scale (a physical size on the coral, _ContentRadius),
        // 1 = cover-fitted to the screen (100%). Driven by FullscreenMagnifier from
        // coverage; the migration between the two is the emergence movement itself.
        _Settle ("Crater fit -> screen fit", Range(0, 1)) = 0

        // Magnification of the SCREEN-FIT mapping, 1 = the clip at 100%. Screen fit is a
        // destination, and a destination is where growth stops — which is the bug this
        // fixes: the iris clamps at the corner and _Settle reaches 1, so the footage sits
        // frozen at native scale while the coral keeps swelling past it. Cropping in past
        // 100% lets the polyps keep coming.
        _EndZoom ("Screen-fit zoom (1 = 100%)", Float) = 1

        // Both are overwritten every frame — screen from Screen.width/height, footage
        // from the clip itself — so these defaults only ever show in a material preview.
        // Kept honest anyway: a landscape app playing landscape clips (see
        // docs/FOOTAGE_TRIAL.md), not the portrait 0.462 / 0.5625 they used to be.
        _ScreenAspect ("Screen w/h", Float) = 1.778
        _FootageAspect ("Footage w/h", Float) = 1.778
        _Opacity ("Master opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        LOD 100

        Pass
        {
            Name "MagnifierFullscreen"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
            TEXTURE2D(_TexB); SAMPLER(sampler_TexB);

            CBUFFER_START(UnityPerMaterial)
                float  _Blend;
                float4 _Center;
                float  _Radius;
                float  _ContentRadius;
                float  _Feather;
                float  _Settle;
                float  _EndZoom;
                float  _ScreenAspect;
                float  _FootageAspect;
                float  _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- The iris. Measured in screen HEIGHTS with the x axis corrected by
                // the screen aspect, so it is a circle on any device rather than an
                // ellipse on tall ones.
                float2 d = (IN.uv - _Center.xy) * float2(_ScreenAspect, 1.0);
                float r = length(d);
                float iris = 1.0 - smoothstep(max(_Radius - _Feather, 0.0), _Radius, r);

                float alpha = iris * _Opacity * IN.color.a;
                if (alpha <= 0.001) discard;   // outside the iris: the AR view shows through

                // --- TWO MAPPINGS, AND THE JOURNEY BETWEEN THEM IS THE MOVEMENT.
                //
                // Mapping A — the clip at a PHYSICAL SIZE ON THE CORAL. Its width spans
                // _ContentRadius, which is a world length at the crater projected each
                // frame, so at emergence the clip's polyp is drawn at the same size as
                // the skeleton's own cups: 1x, no magnification yet, the lens merely
                // resting on the surface. This is what makes the opening read as a lens
                // finding a cup rather than as a video starting to play.
                //
                // NOT the iris radius, which is what this used to be. Fitting the clip
                // to the mask meant the footage's scale was decided by the hole, so the
                // polyps began at roughly a third of a cup and had to swell to catch up.
                //
                // Mapping B — cover-fitted to the SCREEN. The clip at 100%, filling the
                // frame edge to edge, overflow cropped, no bars, no distortion. This is
                // the destination: a full screen of polyps at native scale.
                //
                // _Settle carries the footage from A to B as the iris opens. That
                // migration is not a blend trick — it is what makes the polyps EMERGE
                // WITH MOVEMENT: every texel travels radially outward from the crater as
                // the mapping relaxes, so the content visibly grows out of the cup
                // toward the viewer instead of being a disc that merely gets bigger.
                // Played backwards on the way out, the footage funnels back INTO the
                // crater, which is the exit reading as the same lens withdrawing.
                float2 uvI;
                uvI.x = d.x / max(2.0 * _ContentRadius, 1e-4);
                uvI.y = d.y * _FootageAspect / max(2.0 * _ContentRadius, 1e-4);
                uvI += 0.5;

                float2 uvS = IN.uv;
                if (_FootageAspect > _ScreenAspect)
                {
                    // Footage relatively wider: match heights, crop the sides.
                    float s = _ScreenAspect / max(_FootageAspect, 1e-4);
                    uvS.x = (uvS.x - 0.5) * s + 0.5;
                }
                else
                {
                    // Footage relatively taller: match widths, crop top and bottom.
                    float s = _FootageAspect / max(_ScreenAspect, 1e-4);
                    uvS.y = (uvS.y - 0.5) * s + 0.5;
                }

                // PAST 100%. Screen fit is where the old mapping stopped, and stopping is
                // wrong: at the end of the approach the iris is clamped at the corner and
                // _Settle has reached 1, so the footage froze at native scale while the
                // coral kept magnifying past it. The polyps visibly fell behind the thing
                // they are supposed to be emerging from.
                //
                // Cropping in about the centre keeps them coming. It costs resolution —
                // the clips are square and not large — so this is the one knob here that
                // is bounded by the footage rather than by taste.
                uvS = (uvS - 0.5) / max(_EndZoom, 1e-4) + 0.5;

                float2 fuv = lerp(uvI, uvS, saturate(_Settle));

                float3 a = SAMPLE_TEXTURE2D(_TexA, sampler_TexA, fuv).rgb;
                float3 b = SAMPLE_TEXTURE2D(_TexB, sampler_TexB, fuv).rgb;
                return half4(lerp(a, b, saturate(_Blend)), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
