Shader "PaintWar/Rounded Tile Surface"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Paint Smoothness", Range(0, 1)) = 0.72
        _WideHighlightStrength ("Wide Highlight", Range(0, 0.6)) = 0.2
        _SpecularStrength ("Sharp Highlight", Range(0, 1.5)) = 0.68
        _RimStrength ("Edge Sheen", Range(0, 0.35)) = 0.08
        _SideShade ("Side Brightness", Range(0.5, 1)) = 0.78
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                half _Smoothness;
                half _WideHighlightStrength;
                half _SpecularStrength;
                half _RimStrength;
                half _SideShade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half topFacing = saturate(normalWS.y);
                half surfaceBrightness = lerp(_SideShade, 1.0h, topFacing);
                half3 albedo = _BaseColor.rgb * surfaceBrightness;

                Light mainLight = GetMainLight(input.shadowCoord);
                half shadow = mainLight.shadowAttenuation;
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient +
                                 mainLight.color * ndotl * shadow;
                lighting = max(
                    lighting,
                    half3(0.50h, 0.50h, 0.50h));

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half ndoth = saturate(dot(normalWS, halfDirection));
                half ndoth2 = ndoth * ndoth;
                half wideHighlight = ndoth2 * ndoth2 * _WideHighlightStrength;
                half sharpPower = lerp(24.0h, 84.0h, _Smoothness);
                half sharpHighlight = pow(ndoth, sharpPower) * _SpecularStrength;

                half edge = 1.0h - saturate(dot(normalWS, viewDirection));
                half edge2 = edge * edge;
                half edgeSheen = edge2 * edge2 * _RimStrength;
                half3 tintedCoat = lerp(albedo, mainLight.color, 0.16h);
                half3 coat = tintedCoat * wideHighlight * shadow +
                             mainLight.color * sharpHighlight * shadow +
                             albedo * edgeSheen;

                return half4(albedo * lighting + coat, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Simple Lit"
}
