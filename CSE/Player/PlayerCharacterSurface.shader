Shader "PaintWar/Player Character Surface"
{
    Properties
    {
        _SkinColor ("Skin Color", Color) = (0.89, 0.52, 0.33, 1)
        _PlayerColor ("Player Clothing Color", Color) = (0.16, 0.36, 0.8, 1)
        _RimColor ("Silhouette Rim Color", Color) = (0.01, 0.025, 0.06, 1)
        _RimStrength ("Silhouette Rim Strength", Range(0, 0.7)) = 0.38
        _Smoothness ("Smoothness", Range(0, 1)) = 0.42
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkinColor;
                half4 _PlayerColor;
                half4 _RimColor;
                half _RimStrength;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half clothingMask : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.clothingMask = saturate(input.color.r);
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 surfaceColor = lerp(
                    _SkinColor.rgb,
                    _PlayerColor.rgb,
                    input.clothingMask);

                Light mainLight = GetMainLight(input.shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 direct = mainLight.color * ndotl * mainLight.shadowAttenuation * 0.90h;
                half3 lighting = clamp(
                    ambient + direct,
                    half3(0.45h, 0.45h, 0.45h),
                    half3(1.20h, 1.20h, 1.20h));

                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half ndoth = saturate(dot(normalWS, halfDirection));
                half specularPower = lerp(20.0h, 58.0h, _Smoothness);
                half specular = pow(ndoth, specularPower) * 0.2h;

                half edge = 1.0h - saturate(dot(normalWS, viewDirection));
                half edge2 = edge * edge;
                half rim = edge2 * edge * _RimStrength;
                half3 rimmedColor = lerp(surfaceColor, _RimColor.rgb, rim);

                return half4(
                    rimmedColor * lighting +
                    mainLight.color * specular * mainLight.shadowAttenuation,
                    1.0h);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
