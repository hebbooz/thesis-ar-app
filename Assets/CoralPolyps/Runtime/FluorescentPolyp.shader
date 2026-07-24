Shader "CoralPolyps/FluorescentPolyp"
{
    // Emissive fluorescence for virtual coral polyps.
    // Glow comes FROM WITHIN (emission), not from reflected light, so it reads
    // as fluorescence rather than a lit surface. One parameter, _Stress (0..1),
    // drives the whole distress arc so the shader and polyp retraction stay in sync.
    //
    // Arc (2-stage, matches your design):
    //   0.00-0.40  healthy         : steady cyan-green, full emission
    //   0.40-0.70  colourful bleach: emission surges + saturates, hue shifts warm, slow pulse
    //   0.70-1.00  bleached        : emission desaturates and drains toward white, then dims
    //
    // Drive _Stress from PolypPool via MaterialPropertyBlock (already wired).
    // Colours are authored as EMISSION under blue/UV excitation, not daylight albedo.

    Properties
    {
        [Header(Fluorescence)]
        [HDR] _HealthyColor ("Healthy Emission (cyan-green)", Color) = (0.15, 1.0, 0.7, 1)
        [HDR] _StressColor  ("Colourful-Bleach Emission (warm)", Color) = (1.0, 0.55, 0.2, 1)
        _EmissionStrength   ("Base Emission Strength", Range(0, 8)) = 3.0

        [Header(Stress 0 to 1)]
        _Stress             ("Stress", Range(0, 1)) = 0.0

        [Header(Surge and Pulse)]
        _SurgeBoost         ("Colourful-Bleach Surge", Range(1, 4)) = 2.2
        _PulseSpeed         ("Pulse Speed", Range(0, 12)) = 4.0
        _PulseDepth         ("Pulse Depth", Range(0, 1)) = 0.35

        [Header(Surface)]
        _BaseColor          ("Base Tint (unlit fill)", Color) = (0.05, 0.08, 0.08, 1)
        _FresnelPower       ("Rim Glow Power", Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Per-instance so each polyp can carry its own stress/phase if desired.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Stress)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _HealthyColor;
                float4 _StressColor;
                float  _EmissionStrength;
                float  _SurgeBoost;
                float  _PulseSpeed;
                float  _PulseDepth;
                float4 _BaseColor;
                float  _FresnelPower;
            CBUFFER_END

            // Luminance-preserving desaturation toward white.
            float3 Desaturate(float3 c, float amount)
            {
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                return lerp(c, float3(l, l, l) + (1.0 - l) * amount, amount);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceViewDir(pos.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float stress = UNITY_ACCESS_INSTANCED_PROP(Props, _Stress);

                // --- Stage weights from the single stress value ---
                // healthy -> colourful-bleach happens over 0.0..0.7
                float toStress   = smoothstep(0.0, 0.7, stress);
                // drain-to-white ramps in over 0.7..1.0
                float toWhite    = smoothstep(0.7, 1.0, stress);
                // the surge peaks in the middle band, then fades as we head to white
                float surgeBand  = smoothstep(0.35, 0.55, stress) * (1.0 - smoothstep(0.7, 0.9, stress));

                // --- Colour: healthy -> warm stress, then desaturate toward white ---
                float3 emissionCol = lerp(_HealthyColor.rgb, _StressColor.rgb, toStress);
                emissionCol = Desaturate(emissionCol, toWhite);

                // --- Intensity: base, surge in the colourful-bleach band, dim as it bleaches out ---
                float intensity = _EmissionStrength;
                intensity *= lerp(1.0, _SurgeBoost, surgeBand);   // brighten during colourful bleaching
                intensity *= (1.0 - 0.75 * toWhite);              // drain: emission fades as tissue dies

                // --- Pulse: only during stress, strongest mid-arc ---
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseDepth * surgeBand;
                intensity *= pulse;

                // --- Fresnel rim so the glow reads as volumetric from within ---
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                float3 emission = emissionCol * intensity * (0.6 + 0.4 * fresnel);
                float3 col = _BaseColor.rgb + emission;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
