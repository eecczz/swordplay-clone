Shader "Swordplay/Broken Edge Textured Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Original Tint", Color) = (1,1,1,1)
        _Saturation("Retained Saturation", Range(0,1.5)) = 1.05
        _TextureContrast("Graphic Contrast", Range(0.5,2)) = 0.92
        _ColorSteps("Painted Color Steps", Range(2,12)) = 5
        _ShadowColor("Colored Shadow", Color) = (0.20,0.33,0.38,1)
        _ShadowStrength("Colored Shadow Strength", Range(0,1)) = 0.58
        _LightTint("Warm Painted Light", Color) = (1,0.67,0.63,1)
        _LightTintStrength("Warm Light Strength", Range(0,1)) = 0.14
        _RimColor("Cool Edge Light", Color) = (0.18,0.96,0.77,1)
        _RimStrength("Cool Edge Strength", Range(0,1)) = 0.12
        _OutlineColor("Pencil Line", Color) = (0.18,0.06,0.14,1)
        _OutlineWidth("Pencil Line Width", Range(0,0.03)) = 0.012
        _PaperStrength("Hand Painted Variation", Range(0,0.15)) = 0.035
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.01
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "HandDrawnOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _LightTint;
                half4 _RimColor;
                half4 _OutlineColor;
                half _Saturation;
                half _TextureContrast;
                half _ColorSteps;
                half _ShadowStrength;
                half _LightTintStrength;
                half _RimStrength;
                half _OutlineWidth;
                half _PaperStrength;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            half StaticWobble(float3 positionWS)
            {
                return frac(sin(dot(floor(positionWS * 7.0h), float3(12.9898h, 78.233h, 37.719h))) * 43758.5453h);
            }
            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                half wobble = lerp(0.82h, 1.18h, StaticWobble(positionWS));
                positionWS += normalWS * (_OutlineWidth * wobble);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 FragOutline(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                return half4(_OutlineColor.rgb, _OutlineColor.a * alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _LightTint;
                half4 _RimColor;
                half4 _OutlineColor;
                half _Saturation;
                half _TextureContrast;
                half _ColorSteps;
                half _ShadowStrength;
                half _LightTintStrength;
                half _RimStrength;
                half _OutlineWidth;
                half _PaperStrength;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.shadowCoord = GetShadowCoord(positions);
                return output;
            }
            half3 PaintedAlbedo(half3 source)
            {
                half luminance = dot(source, half3(0.2126h, 0.7152h, 0.0722h));
                half lifted = saturate(0.045h + pow(max(luminance, 0.001h), 0.84h) * 0.94h);
                half steps = clamp(_ColorSteps, 3.0h, 8.0h);
                half band = floor(lifted * steps + 0.5h) / steps;
                half3 hue = min(source / max(luminance, 0.055h), 2.4h);
                half3 painted = lerp(band.xxx, hue * band, _Saturation);
                return saturate((painted - 0.5h) * _TextureContrast + 0.5h);
            }
            half PaperNoise(float3 positionWS)
            {
                half coarse = frac(sin(dot(floor(positionWS * 9.0h), float3(12.9898h, 78.233h, 45.164h))) * 43758.5453h);
                half fine = frac(sin(dot(floor(positionWS * 23.0h), float3(39.346h, 11.135h, 83.155h))) * 23421.631h);
                return (coarse * 0.65h + fine * 0.35h) - 0.5h;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 sampleColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(sampleColor.a - _Cutoff);
                half3 surface = PaintedAlbedo(saturate(sampleColor.rgb));
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));

                // Neutral dungeon stone receives a deliberate pastel art direction,
                // while already-coloured wood, moss, cloth and props keep their hue.
                half sourceMax = max(sampleColor.r, max(sampleColor.g, sampleColor.b));
                half sourceMin = min(sampleColor.r, min(sampleColor.g, sampleColor.b));
                half neutralMask = 1.0h - smoothstep(0.06h, 0.22h, sourceMax - sourceMin);
                half surfaceValue = dot(surface, half3(0.2126h, 0.7152h, 0.0722h));
                half3 wallPastel = half3(0.28h, 0.54h, 0.68h);
                half3 floorPastel = half3(0.72h, 0.44h, 0.58h);
                half upward = saturate(normalWS.y * 0.75h + 0.25h);
                half3 neutralPastel = lerp(wallPastel, floorPastel, upward) * lerp(0.56h, 0.98h, surfaceValue);
                surface = lerp(surface, neutralPastel, neutralMask * 0.80h);
                half litBand = step(0.56h, ndotl);
                half middleBand = step(0.20h, ndotl) - litBand;
                half castShadow = 1.0h - mainLight.shadowAttenuation;
                half shadowAmount = saturate((1.0h - litBand - middleBand * 0.48h) + castShadow * 0.55h);
                half3 coloredShadow = surface * 0.70h + _ShadowColor.rgb * 0.30h;
                half3 color = lerp(surface, coloredShadow, shadowAmount * _ShadowStrength);
                color *= lerp(0.96h, 1.045h, litBand);
                color = lerp(color, color * 0.72h + _LightTint.rgb * 0.28h, litBand * _LightTintStrength);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(saturate(1.0h - dot(normalWS, viewDirection)), 3.5h) * _RimStrength;
                color += _RimColor.rgb * rim;
                color += PaperNoise(input.positionWS) * _PaperStrength;

                // Pick out authored cracks, boards and masonry seams like ink laid over
                // a painted cel.  The texture remains untouched; only strong local
                // boundaries receive the pencil colour.
                float2 texel = _BaseMap_TexelSize.xy * 1.35h;
                half3 texLeft = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(texel.x, 0)).rgb;
                half3 texRight = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(texel.x, 0)).rgb;
                half3 texDown = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(0, texel.y)).rgb;
                half3 texUp = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(0, texel.y)).rgb;
                half centerLum = dot(sampleColor.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half neighbourLum = dot((texLeft + texRight + texDown + texUp) * 0.25h, half3(0.2126h, 0.7152h, 0.0722h));
                half ink = smoothstep(0.055h, 0.19h, abs(centerLum - neighbourLum));
                color = lerp(color, _OutlineColor.rgb, ink * 0.75h);
                half3 fogged = MixFog(color, input.fogFactor);
                color = lerp(color, fogged, 0.08h);
                return half4(saturate(color), sampleColor.a);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    Fallback Off
}
