Shader "Swordplay/Broken Edge Sky"
{
    Properties
    {
        _TopColor("Top", Color) = (0.24,0.30,0.52,1)
        _HorizonColor("Horizon", Color) = (0.90,0.62,0.62,1)
        _BottomColor("Below", Color) = (0.25,0.22,0.34,1)
        _HorizonSharpness("Horizon Sharpness", Range(0.2,8)) = 1.65
        _SunColor("Sun Wash", Color) = (1.0,0.77,0.55,1)
        _SunStrength("Sun Wash Strength", Range(0,1)) = 0.18
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _HorizonSharpness;
                half4 _SunColor;
                half _SunStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 direction = normalize(input.direction);
                half upper = pow(saturate(direction.y), _HorizonSharpness);
                half lower = pow(saturate(-direction.y), 0.7h);
                half3 color = lerp(_HorizonColor.rgb, _TopColor.rgb, upper);
                color = lerp(color, _BottomColor.rgb, lower);

                half3 sunDirection = normalize(half3(0.42h, 0.48h, -0.76h));
                half sunWash = pow(saturate(dot(direction, sunDirection)), 7.0h) * _SunStrength;
                color = lerp(color, _SunColor.rgb, sunWash);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
