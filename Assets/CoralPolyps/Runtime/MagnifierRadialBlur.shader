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
    //
    // TWO PASSES, AND WHY (2026-08-19)
    // --------------------------------
    // This began as one full-resolution pass taking 13 samples per pixel. On a phone at
    // 0.8 render scale that is ~25 MILLION texture fetches per frame, every one of them
    // scattered up to ~90 px away from its neighbours — the worst case for a mobile GPU's
    // texture cache. It ran alongside Vuforia's tracking and three video decodes, and it
    // switched on precisely when the viewer leaned in, which is when the app was reported
    // to slow down.
    //
    // So the expensive part now runs at HALF RESOLUTION (a quarter of the pixels) and a
    // cheap full-resolution pass composites it back. ~5.3 fetches per full-res pixel
    // instead of 13, with four times the cache locality on the scattered ones.
    //
    //   Pass 0  BLUR       source (full)  -> half-res target, 13 taps
    //   Pass 1  COMPOSITE  source (full) + half-res blur -> full-res target, 2 taps
    //
    // The RADIUS RAMP STAYS IN PASS 0. It would be simpler to blur at a fixed maximum in
    // pass 0 and let pass 1 crossfade sharp -> blurred, but a crossfade between a sharp
    // image and a heavily blurred copy of it reads as a ghosted double-image at every
    // intermediate value — the exact artefact the 12-tap count was chosen to avoid. A
    // growing radius reads as optics; a crossfade reads as a mistake. Pass 1's blend
    // therefore does nothing but choose between two versions of the SAME ramp, and only
    // near the sharp region where they differ.

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        HLSLINCLUDE
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

        // The half-resolution result of pass 0, read back by pass 1.
        TEXTURE2D_X(_MagBlurred);

        // How much of the ramp is spent handing over from the sharp full-resolution image
        // to the blurred half-resolution one. Deliberately steep: the handover has to
        // finish while the blur is still small enough to be judged against the loupe's
        // rim, but not so early that the half-res image is taken while it is still sharp
        // enough for its lost resolution to show as a seam. At the far end of this ramp
        // the radius is ~11 px against a bilinear upsample that costs about one — the
        // blur dominates its own resolution loss, so there is nothing to see.
        #define HANDOVER 8.0

        // Two rings of six. Twelve taps is the cheapest count that still reads as an
        // optical defocus rather than as a ghosted double-image on a hard edge — and
        // this runs on a phone that is already decoding three videos and tracking a
        // model target, so the count is a budget decision, not an aesthetic one.
        #define TAPS 12

        /// How defocused this pixel is, 0..1. The one piece of maths both passes must
        /// agree on exactly: pass 0 turns it into a radius, pass 1 into a choice of
        /// source, and any disagreement puts a visible ring on the result.
        float BlurAmount(float2 uv)
        {
            // Distance from the loupe centre, in screen heights.
            float2 d = (uv - _MagBlur.xy) * float2(_MagBlurAspect, 1.0);
            float r = length(d);

            // Ramp from the rim outward. Squared so the first millimetres past the
            // edge stay nearly sharp: a lens does not have a hard focus boundary, and
            // a linear ramp puts a visible ring exactly where the eye is looking.
            float t = saturate((r - _MagBlur.z) / max(_MagBlur.w - _MagBlur.z, 1e-4));
            return t * t * _MagBlurParams.y;
        }
        ENDHLSL

        Pass
        {
            Name "MagnifierRadialBlurHalf"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            #pragma target 3.0

            // Runs at half resolution. Every quantity it works in — UV, screen heights —
            // is resolution independent, so the blur it produces is the same size on the
            // coral as the full-resolution version was; only the number of pixels doing
            // the work changes.
            half4 FragBlur(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                if (_MagBlurParams.y <= 0.002) return c;

                float amount = BlurAmount(uv);
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

        Pass
        {
            Name "MagnifierRadialBlurComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 3.0

            // Full resolution, two taps. _BlitTexture is the untouched camera colour, so
            // the loupe interior comes through at native sharpness — the half-resolution
            // image is never allowed anywhere the viewer is actually looking.
            half4 FragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                if (_MagBlurParams.y <= 0.002) return c;

                float amount = BlurAmount(uv);
                if (amount <= 0.002) return c;

                half4 b = SAMPLE_TEXTURE2D_X_LOD(_MagBlurred, sampler_LinearClamp, uv, 0);
                return half4(lerp(c.rgb, b.rgb, saturate(amount * HANDOVER)), c.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
