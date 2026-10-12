Shader "Swordplay/Broken Edge Blade Aura"
{
    Properties
    {
        [HDR] _GlowColor("Living Cyan Flame", Color) = (0.03,1.5,2.5,0.94)
        [HDR] _CoreColor("White Blade Core", Color) = (0.9,1.15,1.25,0.82)
        _OutlineWidth("Outline Width", Range(0.002,0.08)) = 0.034
        _Intensity("Core Intensity", Range(0,3)) = 1.4
        _FlameAmplitude("Flame Writhe", Range(0,2)) = 1.28
        _FlameSpeed("Flame Speed", Range(0,12)) = 6.2
        _FlameScale("Flame Detail", Range(1,40)) = 16
        _BladeBaseOS("Blade Base", Vector) = (0,0,0,0)
        _BladeAxisOS("Blade Axis", Vector) = (0,1,0,0)
        _BladeWideAxisOS("Blade Wide Axis", Vector) = (1,0,0,0)
        _BladeThinAxisOS("Blade Thin Axis", Vector) = (0,0,1,0)
        _BladeLength("Blade Length", Float) = 1
        _BladeWideRadius("Blade Wide Radius", Float) = 0.05
        _BladeThinRadius("Blade Thin Radius", Float) = 0.008
        _BroadsideOutlineScale("Broadside Outline Scale", Float) = 1
        _ThicknessOutlineScale("Thickness Outline Scale", Float) = 0.18
        _BladeClipSoftness("Blade Clip Softness", Float) = 0.005
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+35" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "LivingBladeOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _CoreColor;
                half _OutlineWidth;
                half _Intensity;
                half _FlameAmplitude;
                half _FlameSpeed;
                half _FlameScale;
                float4 _BladeBaseOS;
                float4 _BladeAxisOS;
                float4 _BladeWideAxisOS;
                float4 _BladeThinAxisOS;
                float _BladeLength;
                float _BladeWideRadius;
                float _BladeThinRadius;
                float _BroadsideOutlineScale;
                float _ThicknessOutlineScale;
                float _BladeClipSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half flame : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 sourceOS : TEXCOORD2;
            };

            half Hash11(float value)
            {
                return frac(sin(value * 127.1h) * 43758.5453h);
            }

            half FlameTongue(float3 sourceOS, float3 normalOS, half time)
            {
                float3 relative = sourceOS - _BladeBaseOS.xyz;
                half along01 = saturate(dot(relative, _BladeAxisOS.xyz) / max(_BladeLength, 0.0001h));
                float3 radial = relative - _BladeAxisOS.xyz * dot(relative, _BladeAxisOS.xyz);
                half sideSeed = dot(radial, _BladeWideAxisOS.xyz) * 31.7h
                              + dot(radial, _BladeThinAxisOS.xyz) * 67.3h
                              + dot(normalOS, _BladeWideAxisOS.xyz) * 2.9h
                              + dot(normalOS, _BladeThinAxisOS.xyz) * 5.1h;
                half bands = max(5.0h, _FlameScale * 0.72h);
                half movingBand = along01 * bands - time * 0.22h + sideSeed;
                half cell = floor(movingBand);
                half randomHeight = Hash11(cell + 4.73h);
                half randomLean = Hash11(cell * 2.17h + 9.41h);
                half center = 0.18h + randomLean * 0.64h;
                half distanceToTongue = abs(frac(movingBand) - center);
                half tongue = pow(saturate(1.0h - distanceToTongue * (3.2h + randomHeight * 3.8h)), 2.5h);
                half secondary = pow(saturate(1.0h - abs(frac(movingBand * 1.91h + randomLean) - 0.5h) * 5.5h), 3.0h);
                return saturate(tongue * (0.35h + randomHeight * 0.9h) + secondary * 0.32h);
            }

            void ClipToBlade(float3 sourceOS)
            {
                float3 relative = sourceOS - _BladeBaseOS.xyz;
                float along = dot(relative, _BladeAxisOS.xyz);
                clip(along + _BladeClipSoftness);
                clip(_BladeLength + _BladeClipSoftness - along);
                float wide = dot(relative, _BladeWideAxisOS.xyz) / max(_BladeWideRadius, 0.0001);
                float thin = dot(relative, _BladeThinAxisOS.xyz) / max(_BladeThinRadius, 0.0001);
                clip(1.0 - (wide * wide + thin * thin));
            }

            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalOS = normalize(input.normalOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                half time = _Time.y * _FlameSpeed;
                half flame = FlameTongue(input.positionOS.xyz, input.normalOS, time);
                half flameHeight = 0.68h + flame * _FlameAmplitude * 1.9h;
                half wideFacing = abs(dot(normalOS, _BladeWideAxisOS.xyz));
                half thinFacing = abs(dot(normalOS, _BladeThinAxisOS.xyz));
                half axisFacing = abs(dot(normalOS, _BladeAxisOS.xyz));
                half ellipticalExpansion = wideFacing * _BroadsideOutlineScale
                                         + thinFacing * _ThicknessOutlineScale
                                         + axisFacing * 0.48h;
                positionWS += normalWS * (_OutlineWidth * flameHeight * max(0.08h, ellipticalExpansion));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.flame = flame;
                output.positionWS = positionWS;
                output.sourceOS = input.positionOS.xyz;
                return output;
            }

            half4 FragOutline(Varyings input) : SV_Target
            {
                ClipToBlade(input.sourceOS);
                half shimmer = 0.78h + 0.32h * sin(_Time.y * (_FlameSpeed * 1.9h) + dot(input.positionWS, float3(7.1,11.3,5.7)));
                half alpha = _GlowColor.a * saturate(0.86h + input.flame * 0.58h) * shimmer;
                return half4(_GlowColor.rgb * (0.95h + input.flame * 0.42h), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "WhiteHotBladeCore"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex VertCore
            #pragma fragment FragCore
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _CoreColor;
                half _OutlineWidth;
                half _Intensity;
                half _FlameAmplitude;
                half _FlameSpeed;
                half _FlameScale;
                float4 _BladeBaseOS;
                float4 _BladeAxisOS;
                float4 _BladeWideAxisOS;
                float4 _BladeThinAxisOS;
                float _BladeLength;
                float _BladeWideRadius;
                float _BladeThinRadius;
                float _BroadsideOutlineScale;
                float _ThicknessOutlineScale;
                float _BladeClipSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 sourceOS : TEXCOORD1; };

            void ClipToBlade(float3 sourceOS)
            {
                float3 relative = sourceOS - _BladeBaseOS.xyz;
                float along = dot(relative, _BladeAxisOS.xyz);
                clip(along + _BladeClipSoftness);
                clip(_BladeLength + _BladeClipSoftness - along);
                float wide = dot(relative, _BladeWideAxisOS.xyz) / max(_BladeWideRadius, 0.0001);
                float thin = dot(relative, _BladeThinAxisOS.xyz) / max(_BladeThinRadius, 0.0001);
                clip(1.0 - (wide * wide + thin * thin));
            }

            Varyings VertCore(Attributes input)
            {
                Varyings output;
                output.sourceOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 FragCore(Varyings input) : SV_Target
            {
                ClipToBlade(input.sourceOS);
                half flicker = 0.92h + 0.12h * sin(_Time.y * (_FlameSpeed * 2.3h) + dot(input.positionWS, float3(4.7,8.9,6.1)));
                return half4(_CoreColor.rgb * _Intensity * flicker, _CoreColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
