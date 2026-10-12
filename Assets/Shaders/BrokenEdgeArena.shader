Shader "Swordplay/Broken Edge Arena"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.42,0.48,0.56,1)
        _ShadowColor("Ink Shadow", Color) = (0.16,0.20,0.29,1)
        _HighlightColor("Sunlit Color", Color) = (0.86,0.68,0.56,1)
        _RimColor("Edge Light", Color) = (0.33,0.83,0.80,1)
        _RimStrength("Edge Light Strength", Range(0,1)) = 0.18
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _HighlightColor;
                half4 _RimColor;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half steppedLight = ndl < 0.32h ? 0.42h : (ndl < 0.72h ? 0.72h : 1.0h);
                steppedLight *= lerp(0.48h, 1.0h, mainLight.shadowAttenuation);

                half3 tileColor = _BaseColor.rgb * input.color.rgb;
                half3 litColor = lerp(_ShadowColor.rgb * input.color.rgb, tileColor, steppedLight);
                litColor = lerp(litColor, _HighlightColor.rgb * input.color.rgb, saturate((ndl - 0.72h) * 3.5h));

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(saturate(1.0h - dot(normalWS, viewDirection)), 3.0h) * _RimStrength;
                litColor += _RimColor.rgb * rim;
                litColor = MixFog(litColor, input.fogFactor);
                return half4(litColor, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    Fallback "Universal Render Pipeline/Simple Lit"
}
