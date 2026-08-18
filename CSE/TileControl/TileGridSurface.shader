Shader "PaintWar/Tile Grid Surface"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Color", Color) = (1, 1, 1, 1)
        _GridColor ("Grid Color", Color) = (0.72, 0.74, 0.76, 1)
        _GridWidth ("Grid Width", Range(0.001, 0.08)) = 0.018
        _GridSoftness ("Grid Softness", Range(0.001, 0.08)) = 0.018
        _TileSize ("Tile Size", Float) = 1
        _Smoothness ("Paint Smoothness", Range(0, 1)) = 0.72
        _SpecularStrength ("Paint Highlight", Range(0, 1.5)) = 0.62
        _BevelWidth ("Fake Rounded Edge Width", Range(0, 0.25)) = 0.12
        _BevelStrength ("Fake Rounded Edge Strength", Range(0, 1)) = 0.48
        _RimStrength ("Edge Sheen", Range(0, 0.5)) = 0.12
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
                half4 _GridColor;
                half _GridWidth;
                half _GridSoftness;
                half _TileSize;
                half _Smoothness;
                half _SpecularStrength;
                half _BevelWidth;
                half _BevelStrength;
                half _RimStrength;
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

            half GetGridMask(float2 worldXZ, half3 normalWS)
            {
                half topMask = saturate((normalWS.y - 0.55h) * 6.0h);
                float tileSize = max((float)_TileSize, 0.001);
                float2 cell = frac((worldXZ / tileSize) + 0.5);
                float2 distanceToEdge = min(cell, 1.0 - cell) * tileSize;
                float edgeDistance = min(distanceToEdge.x, distanceToEdge.y);
                float mask = 1.0 - smoothstep(_GridWidth, _GridWidth + _GridSoftness, edgeDistance);
                return (half)mask * topMask;
            }

            // Gives the flat cube top a softly rounded, coated appearance without
            // adding vertices to every tile. The collider and silhouette stay flat.
            half3 GetCoatedNormal(float2 worldXZ, half3 geometricNormal)
            {
                half topMask = saturate((geometricNormal.y - 0.55h) * 6.0h);
                float tileSize = max((float)_TileSize, 0.001);
                float2 cell = frac((worldXZ / tileSize) + 0.5) - 0.5;
                half2 edgePosition = (half2)(abs(cell) * 2.0);
                half bevelStart = 1.0h - (_BevelWidth * 2.0h);
                half2 bevel = smoothstep(bevelStart, 1.0h, edgePosition);
                half2 outwardSlope = sign((half2)cell) * bevel * _BevelStrength;
                half3 roundedNormal = normalize(half3(outwardSlope.x, 1.0h, outwardSlope.y));
                return normalize(lerp(geometricNormal, roundedNormal, topMask));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 geometricNormal = normalize(input.normalWS);
                half gridMask = GetGridMask(input.positionWS.xz, geometricNormal);
                half3 grooveColor = _BaseColor.rgb * _GridColor.rgb;
                half3 albedo = lerp(_BaseColor.rgb, grooveColor, gridMask);

                half3 normalWS = GetCoatedNormal(input.positionWS.xz, geometricNormal);

                Light mainLight = GetMainLight(input.shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + (mainLight.color * ndotl * mainLight.shadowAttenuation);
                lighting = max(lighting, half3(0.50h, 0.50h, 0.50h));

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half specularPower = lerp(12.0h, 96.0h, _Smoothness);
                half highlight = pow(saturate(dot(normalWS, halfDirection)), specularPower);
                highlight *= _SpecularStrength * mainLight.shadowAttenuation;

                half viewFacing = saturate(dot(normalWS, viewDirection));
                half edgeSheen = pow(1.0h - viewFacing, 3.0h) * _RimStrength;
                half3 coat = mainLight.color * highlight + edgeSheen;

                return half4(albedo * lighting + coat, _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
