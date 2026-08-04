Shader "CoralPolyps/MagnifierRadialBlur"
{
    // The lens blur: SHARP INSIDE THE LOUPE, softening outward from its rim.
    //
    // WHY THIS EXISTS RATHER THAN DEPTH OF FIELD
    // -----------------------------------------
    // The loupe's polyp footage is painted on a duplicate of the coral mesh, sitting at
    // the same depth as the coral itself. Depth of Field separates by distance from the
    // camera, so it cannot possibly tell those two apart — to it they are one surface.
    // Any depth-based blur either softens the footage along with the skeleton or leaves
    // both sharp.
    //
    // What a magnifying glass actually does is radial: the disc you are looking through
    // is sharp, and everything outside it falls away. That is a SCREEN-SPACE mask, not a
    // depth one, so it belongs in a fullscreen pass with the loupe's projected circle
    // handed to it.
    //
    // The mask is fed by MagnifierDefocus via global shader properties rather than a
    // material reference, so the renderer feature never has to find a scene object and
    // works identically whether the component exists yet or not (with the strength at 0
    // it is a pass-through).
    //
    // NOTE ON WHAT IS AND IS NOT BLURRED. The fullscreen takeover draws into a
    // ScreenSpaceOverlay canvas, which uGUI composites AFTER the whole render pipeline —
    // so the emerging footage stays pixel-sharp for free and is untouched by this pass.
    // Only the camera feed and the coral behind it are affected, which is exactly the
    // separation the effect wants.

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "MagnifierRadialBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            // Blit.hlsl supplies Vert, Varyings, _BlitTexture and sampler_LinearClamp —
            // the URP-sanctioned fullscreen blit scaffolding. Do not hand-roll a triangle;
            // this handles the render-scale and XR texture-array cases for us.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // xy = sharp-region centre in UV, z = inner radius, w = outer radius.
            // Radii are in SCREEN HEIGHTS, with x corrected by the aspect below, so the
            // region is a circle on any device rather than an ellipse on tall ones.
            float4 _MagBlur;

            // x = blur radius at full strength (screen heights), y = master strength 0..1.
            float2 _MagBlurParams;

            float _MagBlurAspect;

            // Two rings of six. Twelve taps is the cheapest count that still reads as an
            // optical defocus rather than as a ghosted double-image on a hard edge — and
            // this runs on an iPad that is already decoding three videos and tracking a
            // model target, so the count is a budget decision, not an aesthetic one.
            #define TAPS 12

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float strength = _MagBlurParams.y;
                if (strength <= 0.002) return c;

                // Distance from the loupe centre, in screen heights.
                float2 d = (uv - _MagBlur.xy) * float2(_MagBlurAspect, 1.0);
                float r = length(d);

                // Ramp from the rim outward. Squared so the first millimetres past the
                // edge stay nearly sharp: a lens does not have a hard focus boundary, and
                // a linear ramp puts a visible ring exactly where the eye is looking.
                float t = saturate((r - _MagBlur.z) / max(_MagBlur.w - _MagBlur.z, 1e-4));
                float amount = t * t * strength;
                if (amount <= 0.002) return c;

                float radius = amount * _MagBlurParams.x;
                float invAspect = 1.0 / max(_MagBlurAspect, 1e-4);

                half3 sum = c.rgb;
                float total = 1.0;

                [unroll]
                for (int i = 0; i < TAPS; i++)
                {
                    // Half-step offset so no tap lands exactly on the axes, where a
                    // regular grid of samples reads as a cross-shaped smear.
                    float a = (i + 0.5) * (6.28318530718 / TAPS);
                    float ring = (i < TAPS / 2) ? 0.55 : 1.0;

                    float2 o = float2(cos(a), sin(a)) * radius * ring;
                    o.x *= invAspect;   // back from screen heights into UV

                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o, 0).rgb;
                    total += 1.0;
                }

                return half4(sum / total, c.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
