Shader "Swordplay/Warrior Showcase Tinted Broken Edge"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Original Tint", Color) = (1,1,1,1)
        _WarriorTintColor("Character Tint", Color) = (1,0.2,0.2,1)
        _WarriorTintStrength("Character Tint Strength", Range(0,1)) = 0.9
        _ColorSteps("Painted Color Steps", Range(2,12)) = 4
        _ShadowColor("Colored Shadow", Color) = (0.08,0.02,0.02,1)
        _ShadowStrength("Colored Shadow Strength", Range(0,1)) = 0.52
        _LightTint("Painted Light", Color) = (1,0.7,0.7,1)
        _LightTintStrength("Light Strength", Range(0,1)) = 0.03
        _RimColor("Edge Light", Color) = (1,0.6,0.6,1)
        _RimStrength("Edge Strength", Range(0,1)) = 0.04
        _OutlineColor("Ink Line", Color) = (0.08,0.01,0.01,1)
        _OutlineWidth("Ink Line Width", Range(0,0.03)) = 0.012
        _PaperStrength("Painted Variation", Range(0,0.15)) = 0.022
        _BandWobble("Hand Painted Light Edge", Range(0,0.25)) = 0.11
        _HatchStrength("Pencil Hatch Strength", Range(0,0.3)) = 0.07
        _InkVariation("Broken Ink Variation", Range(0,1)) = 0.32
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
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _WarriorTintColor;
                half4 _ShadowColor;
                half4 _LightTint;
                half4 _RimColor;
                half4 _OutlineColor;
                half _WarriorTintStrength;
                half _ColorSteps;
                half _ShadowStrength;
                half _LightTintStrength;
                half _RimStrength;
                half _OutlineWidth;
                half _PaperStrength;
                half _BandWobble;
                half _HatchStrength;
                half _InkVariation;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            half Wobble(float3 positionWS)
            {
                half broad = frac(sin(dot(floor(positionWS * 5.0h), float3(12.9898h, 78.233h, 37.719h))) * 43758.5453h);
                half fine = frac(sin(dot(floor(positionWS * 19.0h), float3(39.346h, 11.135h, 83.155h))) * 24634.6345h);
                return saturate(broad * 0.68h + fine * 0.32h);
            }
            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS += normalWS * (_OutlineWidth * lerp(0.70h, 1.30h, Wobble(positionWS)));
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
                half4 _WarriorTintColor;
                half4 _ShadowColor;
                half4 _LightTint;
                half4 _RimColor;
                half4 _OutlineColor;
                half _WarriorTintStrength;
                half _ColorSteps;
                half _ShadowStrength;
                half _LightTintStrength;
                half _RimStrength;
                half _OutlineWidth;
                half _PaperStrength;
                half _BandWobble;
                half _HatchStrength;
                half _InkVariation;
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
            half3 RecolorPreservingPattern(half3 source)
            {
                half luminance = dot(source, half3(0.2126h, 0.7152h, 0.0722h));
                // A raised minimum lets black and coloured texels receive the tint,
                // while the luminance curve retains seams, studs and cloth folds.
                half patternValue = saturate(0.16h + pow(max(luminance, 0.001h), 0.72h) * 0.92h);
                half steps = clamp(_ColorSteps, 3.0h, 8.0h);
                patternValue = floor(patternValue * steps + 0.5h) / steps;
                half3 tintedPattern = _WarriorTintColor.rgb * patternValue;
                return lerp(source, tintedPattern, _WarriorTintStrength);
            }
            half Noise(float3 positionWS)
            {
                return frac(sin(dot(floor(positionWS * 17.0h), float3(12.9898h, 78.233h, 45.164h))) * 43758.5453h) - 0.5h;
            }
            half HandDrawnHatch(float3 positionWS, half3 normalWS)
            {
                float3 p = positionWS * 15.0h;
                half3 weights = pow(abs(normalWS), 4.0h);
                weights /= max(weights.x + weights.y + weights.z, 0.001h);

                half xy = 1.0h - smoothstep(0.025h, 0.085h, abs(frac((p.x + p.y * 0.74h) * 0.5h) - 0.5h));
                half yz = 1.0h - smoothstep(0.025h, 0.085h, abs(frac((p.y + p.z * 0.74h) * 0.5h) - 0.5h));
                half xz = 1.0h - smoothstep(0.025h, 0.085h, abs(frac((p.x + p.z * 0.74h) * 0.5h) - 0.5h));
                half hatchLine = xy * weights.z + yz * weights.x + xz * weights.y;
                half brokenStroke = step(-0.28h, Noise(positionWS * 0.83h));
                return hatchLine * brokenStroke;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 sampleColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(sampleColor.a - _Cutoff);
                half3 surface = RecolorPreservingPattern(saturate(sampleColor.rgb));
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                // Use broad, hard painted regions instead of a continuous response.
                // This deliberately avoids the curved highlight stripe that makes
                // armour read as glossy metal or plastic.
                half shadowMask = 1.0h - step(0.55h, mainLight.shadowAttenuation);
                half paintedEdge = Noise(input.positionWS * 0.37h) * (2.0h * _BandWobble);
                half warpedNdotL = saturate(ndotl + paintedEdge);
                half darkBand = max(1.0h - step(0.32h, warpedNdotL), shadowMask);
                half brightBand = step(0.72h, warpedNdotL) * (1.0h - shadowMask);
                half3 paintedShadow = lerp(surface, _ShadowColor.rgb, _ShadowStrength);
                half3 paintedMid = surface * 0.90h;
                half3 color = lerp(paintedMid, surface, brightBand);
                color = lerp(color, paintedShadow, darkBand);
                color = lerp(color, _LightTint.rgb, brightBand * _LightTintStrength);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = step(0.82h, saturate(1.0h - dot(normalWS, viewDirection)));
                color = lerp(color, _RimColor.rgb, rim * _RimStrength);
                half paper = Noise(input.positionWS);
                color *= 1.0h + paper * (_PaperStrength * 2.0h);
                half hatch = HandDrawnHatch(input.positionWS, normalWS);
                half hatchMask = saturate(darkBand + (1.0h - brightBand) * 0.22h);
                color = lerp(color, _OutlineColor.rgb, hatch * hatchMask * _HatchStrength);

                float2 texel = _BaseMap_TexelSize.xy * 1.25h;
                half3 left = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(texel.x, 0)).rgb;
                half3 right = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(texel.x, 0)).rgb;
                half3 down = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(0, texel.y)).rgb;
                half3 up = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(0, texel.y)).rgb;
                half centerLum = dot(sampleColor.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half neighbourLum = dot((left + right + down + up) * 0.25h, half3(0.2126h, 0.7152h, 0.0722h));
                half inkNoise = Noise(input.positionWS * 0.71h);
                half inkStart = 0.065h + inkNoise * (_InkVariation * 0.04h);
                half ink = smoothstep(inkStart, inkStart + 0.13h, abs(centerLum - neighbourLum));
                half brokenInk = lerp(1.0h, step(-0.38h, Noise(input.positionWS * 1.63h)), _InkVariation);
                color = lerp(color, _OutlineColor.rgb, ink * brokenInk * 0.54h);
                color = MixFog(color, input.fogFactor);
                return half4(saturate(color), sampleColor.a);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    Fallback Off
}
