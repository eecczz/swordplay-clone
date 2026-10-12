Shader "Swordplay/Broken Edge Dungeon"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Original Tint", Color) = (1,1,1,1)
        _Saturation("Retained Saturation", Range(0,1)) = 0.42
        _TextureContrast("Graphic Contrast", Range(0.5,2)) = 1.08
        _ColorSteps("Color Steps", Range(2,12)) = 4
        _ShadowColor("Ink Shadow", Color) = (0.16,0.19,0.28,1)
        _ShadowStrength("Colored Shadow", Range(0,1)) = 0.54
        _LightTint("Warm Light", Color) = (0.92,0.72,0.60,1)
        _LightTintStrength("Warm Light Strength", Range(0,1)) = 0.12
        _RimColor("Cool Edge", Color) = (0.28,0.78,0.82,1)
        _RimStrength("Cool Edge Strength", Range(0,1)) = 0.07
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.01
        [HideInInspector] _StoneColor("Legacy Surface", Color) = (1,1,1,1)
        [HideInInspector] _AccentColor("Legacy Accent", Color) = (1,1,1,1)
        [HideInInspector] _TextureInfluence("Legacy Texture Influence", Float) = 1
        [HideInInspector] _DetailStrength("Legacy Detail", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _LightTint;
                half4 _RimColor;
                half _Saturation;
                half _TextureContrast;
                half _ColorSteps;
                half _ShadowStrength;
                half _LightTintStrength;
                half _RimStrength;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fogFactor : TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }
            half3 SimplifyOriginalTexture(half3 source)
            {
                half luminance = dot(source, half3(0.2126h, 0.7152h, 0.0722h));
                half steps = max(2.0h, _ColorSteps);
                half valueBand = floor(luminance * steps + 0.5h) / steps;
                half3 hue = source / max(luminance, 0.08h);
                half3 simplified = lerp(valueBand.xxx, hue * valueBand, _Saturation);
                return saturate((simplified - 0.5h) * _TextureContrast + 0.5h);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 sampleColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(sampleColor.a - _Cutoff);
                half3 surface = SimplifyOriginalTexture(saturate(sampleColor.rgb));
                half3 normalWS = normalize(input.normalWS);
                // Broad WebGL-safe bands provide the Broken Edge-like coloured shadow.
                half3 mainLightDirection = normalize(_MainLightPosition.xyz);
                half lightAmount = saturate(dot(normalWS, mainLightDirection));
                half3 shadowBand = lerp(surface * 0.58h, _ShadowColor.rgb, _ShadowStrength);
                half3 middleBand = surface * 0.88h;
                half3 lightBand = lerp(surface, _LightTint.rgb, _LightTintStrength);
                half3 color = lightAmount < 0.30h ? shadowBand : (lightAmount < 0.72h ? middleBand : lightBand);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(saturate(1.0h - dot(normalWS, viewDirection)), 4.0h) * _RimStrength;
                color += _RimColor.rgb * rim;
                color = MixFog(color, input.fogFactor);
                return half4(color, sampleColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
