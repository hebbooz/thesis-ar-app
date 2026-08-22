Shader "CoralPolyps/FluorescentTissue"
{
    // Fluorescent LIVING TISSUE over the cerioid coral skeleton (Goniastrea / faviid).
    // Shared-wall honeycomb: sunken floors + septal ridges to raised rims. Real
    // fluorescence concentrates on the RAISED ridges (often green) with the recessed
    // floors a different hue (often cyan/blue) and darker. The scan's OCCLUSION map
    // drives the ridge/floor colour split + depth; the NORMAL map adds the fine,
    // razor-sharp septal detail that catches the directional excitation light.
    //
    // One dial, _Stress (0..1) — THREE stages: NATURAL -> FLUORESCENT -> BLEACHED
    //   0 .. _FluorPoint : NATURAL     - ordinary daylight-ish living coral (dusky mauve-brown
    //                                    walls, pale teal mouths). Little or no glow.
    //   ~ _FluorPoint    : FLUORESCENT - peak glow: a vivid/neon version of that SAME palette.
    //   _FluorPoint .. 1 : BLEACHED    - glow drains, colour desaturates -> white skeleton.
    //
    // So the natural and fluorescent palettes are deliberately paired: the fluorescent colours
    // should read as the neon version of the natural ones, not an unrelated hue.

    Properties
    {
        // Natural palette read from daylight reference imagery of a living faviid:
        // golden-olive septa/walls, pale grey-lilac oral discs at the corallite centres.
        // Natural palette from living-faviid daylight reference: a warm GOLDEN YELLOW coral —
        // pale buttery cream on the raised ridges, richer golden amber down in the valleys/cups.
        [Header(Natural Appearance stage 1)]
        _NaturalWallColor  ("Natural ridge / wall (pale golden cream)", Color) = (0.92, 0.83, 0.58, 1)
        _NaturalFloorColor ("Natural floor / cup (golden amber)", Color) = (0.79, 0.63, 0.30, 1)
        // Trim for the natural stage only. Keep near 1: the natural colours are already bright,
        // and values much above ~1.2 push every channel past 1.0 on the lit ridges, which CLIPS
        // the whole coral to flat white. Raise slightly only if the natural stage reads muddy.
        _NaturalBrightness ("Natural brightness trim (keep near 1)", Range(0.25, 2)) = 1.0

        // Fluorescent = the SAME hue family as the natural palette, pushed to vivid and made
        // self-luminous — not a hue shift. This is closer to how real coral fluorescence reads:
        // the pigment colour stays recognisable, it just glows. Contrast between the natural and
        // fluorescent stages therefore comes mostly from _EmissionStrength + bloom, not hue.
        // NOTE: keep green well below red here. A near-white yellow (R~1, G~1) has no headroom —
        // any emission boost clips both channels and the "glow" turns flat white, which reads as
        // BLEACHED. Holding green lower keeps it chromatic gold however hard it is driven.
        [Header(Fluorescence Two Tone stage 2)]
        [HDR] _WallColor  ("Fluorescent ridge / wall (vivid gold)", Color) = (1.0, 0.82, 0.10, 1)
        [HDR] _FloorColor ("Fluorescent floor / cup (vivid amber-orange)", Color) = (1.0, 0.62, 0.07, 1)
        [HDR] _StressColor("(legacy, unused)", Color) = (1.0, 0.15, 0.7, 1)
        _EmissionStrength ("Fluorescent Emission Strength", Range(0, 8)) = 3.0
        _FluorPoint ("Stress at PEAK fluorescence", Range(0.1, 0.9)) = 0.5
        // How much of the FLUORESCENT REGIME is expressed at all — the glow AND the
        // lights-out dark base it is seen against, which are one look and must be
        // suppressed together (see the arc code below). Driven to 0 through recovery
        // so the coral heals bleached -> healthy directly.
        _FluorPresence ("Fluorescent expression (runtime; 0 during recovery)", Range(0, 1)) = 1.0

        [Header(Honeycomb Pattern)]
        _AOMap      ("Occlusion / cavity map", 2D) = "white" {}
        _AOContrast ("Cavity contrast", Range(0.5, 5)) = 2.0
        _ColorSplit ("Ridge-Floor colour split", Range(0.5, 4)) = 1.5
        _FloorGlow  ("Floor / disc baseline glow", Range(0, 1)) = 0.22
        _WallGlow   ("Wall / ridge glow boost", Range(0, 3)) = 1.7
        _Depth      ("Depth shading (shadow recesses)", Range(0, 1)) = 0.75
        _ReliefStrength ("Directional relief", Range(0, 1)) = 0.4

        [Header(Septal Detail)]
        [NoScaleOffset] _NormalMap ("Septa normal map", 2D) = "bump" {}
        _NormalStrength ("Septa detail strength", Range(0, 3)) = 1.2

        [Header(Skeleton (bleached))]
        _MainTex        ("Skeleton (diffuse)", 2D) = "white" {}
        _SkeletonColor  ("Bleached skeleton tint", Color) = (0.92, 0.9, 0.86, 1)
        // The surface colour at PEAK fluorescence: near-black, as under blue/UV excitation.
        // This darkness is what makes the emission read as glow rather than as a white light.
        _DarkTissue     ("Fluorescent-stage base (lights-out dark)", Color) = (0.03, 0.05, 0.05, 1)

        [Header(Stress 0 to 1)]
        _Stress     ("Stress", Range(0, 1)) = 0.0

        [Header(Surge and Pulse)]
        _SurgeBoost ("Colourful-Bleach Surge", Range(1, 4)) = 1.5
        _PulseSpeed ("Pulse Speed", Range(0, 12)) = 4.0
        _PulseDepth ("Pulse Depth", Range(0, 1)) = 0.35

        [Header(Rim)]
        _FresnelPower ("Rim Glow Power", Range(0.5, 8)) = 3.0

        // --- Phase 4: proximity loupe reveal + AR transparency ---
        // When _LoupeOn is enabled the fluorescence is only revealed inside a soft
        // sphere in WORLD space (centred where the viewer peers), and the tissue
        // alpha-blends over the real (printed) skeleton: outside the loupe AND fully
        // bleached both go transparent so the physical print shows through.
        // ProximityRevealController drives _LoupeCenter / _LoupeRadius / _Stress.
        [Header(Loupe Reveal AR)]
        [Toggle(_LOUPE_ON)] _LoupeOn ("Loupe reveal enabled (AR)", Float) = 0
        _LoupeCenter   ("Loupe centre (world, runtime-driven)", Vector) = (0,0,0,0)
        _LoupeRadius   ("Loupe radius (world m)", Float) = 0.0
        _LoupeSoftness ("Loupe edge softness (world m)", Range(0.001, 0.1)) = 0.02
        _TissueOpacity ("Tissue coverage over print", Range(0, 1)) = 1.0

        // Blend state is material-driven so ONE shader serves both look-dev (opaque:
        // One / Zero / ZWrite On) and AR (SrcAlpha / OneMinusSrcAlpha / ZWrite Off).
        // The existing look-dev material already carries One/Zero/On, so it is
        // unchanged; the controller flips a runtime instance into transparent mode.
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _ZWrite   ("__zw",  Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            // Material-driven so look-dev stays opaque (One/Zero/On) while the AR
            // controller flips a runtime instance to transparent (SrcAlpha/OMSA/Off).
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Keep BOTH variants in the build so the controller can toggle the loupe
            // on a runtime material instance (shader_feature could strip the unused
            // one; this shader has no other keywords, so 2x is negligible).
            #pragma multi_compile_local _ _LOUPE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 tangentWS   : TEXCOORD1;
                float3 bitangentWS : TEXCOORD2;
                float3 viewDirWS   : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float3 positionWS  : TEXCOORD5;
            };

            TEXTURE2D(_AOMap);     SAMPLER(sampler_AOMap);
            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _NaturalWallColor;
                float4 _NaturalFloorColor;
                float  _NaturalBrightness;
                float4 _WallColor;
                float4 _FloorColor;
                float4 _StressColor;
                float  _EmissionStrength;
                float  _FluorPoint;
                float  _FluorPresence;
                float4 _AOMap_ST;
                float  _AOContrast;
                float  _ColorSplit;
                float  _FloorGlow;
                float  _WallGlow;
                float  _Depth;
                float  _ReliefStrength;
                float  _NormalStrength;
                float4 _MainTex_ST;
                float4 _SkeletonColor;
                float4 _DarkTissue;
                float  _Stress;
                float  _SurgeBoost;
                float  _PulseSpeed;
                float  _PulseDepth;
                float  _FresnelPower;
                float4 _LoupeCenter;
                float  _LoupeRadius;
                float  _LoupeSoftness;
                float  _TissueOpacity;
            CBUFFER_END

            float3 Desaturate(float3 c, float amount)
            {
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                return lerp(c, float3(l, l, l) + (1.0 - l) * amount, amount);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionHCS = pos.positionCS;
                OUT.normalWS    = nrm.normalWS;
                OUT.tangentWS   = nrm.tangentWS;
                OUT.bitangentWS = nrm.bitangentWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(pos.positionWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.positionWS  = pos.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float stress = _Stress;
                float3 V = normalize(IN.viewDirWS);

                // --- Fine septal detail: perturb the surface normal from the scan normal map ---
                float3 ts = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv).rgb * 2.0 - 1.0;
                ts.xy *= _NormalStrength;
                ts = normalize(ts);
                float3 N = normalize(ts.x * IN.tangentWS + ts.y * IN.bitangentWS + ts.z * IN.normalWS);

                // --- Cavity from occlusion (raised rims read light, floors dark) ---
                float occ = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, TRANSFORM_TEX(IN.uv, _AOMap)).r;
                occ = saturate((occ - 0.5) * _AOContrast + 0.5);

                // --- Two-tone split by cavity: floors/mouths vs ridges/walls ---
                // The natural and fluorescent palettes are split the same way, so the glow
                // reads as the neon version of the natural colour underneath it.
                float wallness = saturate((occ - 0.5) * _ColorSplit + 0.5);
                float3 naturalCol = lerp(_NaturalFloorColor.rgb, _NaturalWallColor.rgb, wallness);
                float3 fluorCol   = lerp(_FloorColor.rgb, _WallColor.rgb, wallness);

                // --- Brightness pattern + depth ---
                float pattern    = _FloorGlow + _WallGlow * occ;
                float depthShade = lerp(1.0, occ, _Depth);

                // --- Directional relief on the DETAILED normal so the septa catch light ---
                float3 L = normalize(float3(0.35, 1.0, 0.35));
                float  ndl = saturate(dot(N, L));
                float  relief = lerp(1.0, 0.25 + 0.75 * ndl, _ReliefStrength);

                float fluorMask = pattern * depthShade * relief;

                // --- Three-stage arc: NATURAL -> FLUORESCENT -> BLEACHED ---
                // 0.._FluorPoint climbs from natural into peak fluorescence;
                // _FluorPoint..1 drains that glow away to the bare white skeleton.
                //
                // _FluorPresence collapses the middle stage. It scales toFluor, which
                // suppresses the glow AND the dark base together — the base falling to
                // _DarkTissue is not a separate effect, it is the unlit backdrop the glow
                // is seen against, and darkening without glowing is just black.
                //
                // With the middle stage gone the arc is a straight natural <-> skeleton
                // crossfade, so toWhite has to span the WHOLE dial rather than only its
                // top half — otherwise a heal timed across the full 0..1 ramp would finish
                // visibly at the halfway mark and then sit still. At stress 0 and stress 1
                // both forms agree, so _FluorPresence can move without a seam at either end.
                float toFluor = smoothstep(0.0, _FluorPoint, stress) * _FluorPresence;
                float toWhite = lerp(smoothstep(0.0, 1.0, stress),
                                     smoothstep(_FluorPoint, 1.0, stress),
                                     _FluorPresence);
                float surgeBand = smoothstep(_FluorPoint * 0.55, _FluorPoint, stress) *
                                  (1.0 - smoothstep(_FluorPoint, _FluorPoint + (1.0 - _FluorPoint) * 0.6, stress));

                // Surface base. CRITICAL: as fluorescence rises the surface must fall DARK.
                // Fluorescence is viewed under blue/UV excitation with no white light, so
                // everything that isn't fluorescing goes black — and glow only reads as GLOW
                // against darkness. Keeping the bright natural base here makes peak look like a
                // white light instead of a fluorescing coral. Bleaching then takes it to skeleton.
                float3 skeleton = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb * _SkeletonColor.rgb;
                float3 baseCol = lerp(naturalCol * _NaturalBrightness, _DarkTissue.rgb, toFluor);
                baseCol = lerp(baseCol, skeleton, toWhite);
                float3 base = baseCol * depthShade * relief;

                // Fluorescent emission: ABSENT when natural, peaks mid-arc, drains when bleached.
                float3 emissionCol = Desaturate(fluorCol, toWhite);
                // toFluor already carries _FluorPresence, so the glow goes with the dark
                // base by construction — there is no way to darken and not glow.
                float intensity = _EmissionStrength * toFluor * (1.0 - toWhite);
                intensity *= lerp(1.0, _SurgeBoost, surgeBand);
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseDepth * surgeBand;
                intensity *= pulse;

                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                float3 emission = emissionCol * intensity * fluorMask * (0.6 + 0.4 * fresnel);

                float3 color = base + emission;

                // --- Loupe reveal (world-space proximity window) ---
                // reveal = 1 inside the loupe, feathering to 0 at its soft edge.
                // With _LOUPE_ON off (look-dev) everything is revealed.
            #if defined(_LOUPE_ON)
                float dLoupe = distance(IN.positionWS, _LoupeCenter.xyz);
                float reveal = 1.0 - smoothstep(_LoupeRadius - _LoupeSoftness, _LoupeRadius, dLoupe);
            #else
                float reveal = 1.0;
            #endif

                // --- AR transparency (emissionPresence) ---
                // Living tissue covers the real printed skeleton; ridges read a touch
                // more opaque than recessed floors. It drains to zero as the tissue
                // fully bleaches (toWhite -> 1), so the physical white print shows
                // through. reveal masks it to the loupe window. With opaque (One/Zero)
                // blend this alpha is simply ignored, so look-dev is unchanged.
                float coverage = lerp(0.85, 1.0, occ) * _TissueOpacity;
                float presence = coverage * (1.0 - toWhite);
                float alpha = saturate(reveal * presence);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
