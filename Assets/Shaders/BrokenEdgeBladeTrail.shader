Shader "Swordplay/Broken Edge Blade Trail"
{
    Properties
    {
        [HDR] _GlowColor("Outline Flame Color", Color) = (0.03,1.5,2.5,0.94)
        _Intensity("Intensity", Range(0,3)) = 1.4
        _FlameSpeed("Flame Speed", Range(0,15)) = 8
        _FlameScale("Flame Detail", Range(1,60)) = 24
        _EdgeRoughness("Fire Shape", Range(0,1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+34" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StretchedOutlineFire"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity;
                half _FlameSpeed;
                half _FlameScale;
                half _EdgeRoughness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            half Hash21(half2 value)
            {
                value = frac(value * half2(123.34h, 456.21h));
                value += dot(value, value + 45.32h);
                return frac(value.x * value.y);
            }

            half ValueNoise(half2 value)
            {
                half2 cell = floor(value);
                half2 fraction = frac(value);
                fraction = fraction * fraction * (3.0h - 2.0h * fraction);
                half a = Hash21(cell);
                half b = Hash21(cell + half2(1,0));
                half c = Hash21(cell + half2(0,1));
                half d = Hash21(cell + half2(1,1));
                return lerp(lerp(a,b,fraction.x), lerp(c,d,fraction.x), fraction.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color;
                output.positionWS = positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // x only spans the short rear shoulder at the blade tip now; y is
                // motion-sample age. Shape that small strip into one continuous
                // stretched tongue instead of turning the complete blade into a
                // broad afterimage sheet.
                half blade01 = saturate(input.uv.x);
                half age = saturate(input.uv.y);
                half time = _Time.y * (_FlameSpeed * 0.18h);
                half detail = max(5.0h, _FlameScale * 0.44h);
                half coarse = ValueNoise(half2(blade01 * detail + time * 0.31h, age * detail - time));
                half fine = ValueNoise(half2(blade01 * detail * 2.13h - time, age * detail * 1.47h + time * 0.57h));
                half fireNoise = coarse * 0.7h + fine * 0.3h;

                // A single oval/cylindrical tongue. As it stretches backward its
                // cross-section narrows and its centre moves toward x=1, which is
                // the real blade apex. The oldest end therefore finishes as one
                // clean pointed tip rather than a row of separate discs.
                half ageCurve = pow(age, 0.72h);
                half tongueCenter = lerp(0.50h, 1.0h, ageCurve);
                half tongueHalfWidth = 0.515h * pow(saturate(1.0h - age), 0.62h) + 0.008h;
                half lateralDistance = abs(blade01 - tongueCenter);
                half sideMask = 1.0h - smoothstep(
                    tongueHalfWidth * 0.82h,
                    tongueHalfWidth + 0.008h,
                    lateralDistance);

                half maximumReach = 1.0h - (1.0h - fireNoise) * 0.07h * _EdgeRoughness;
                half tailMask = 1.0h - smoothstep(maximumReach - 0.085h, maximumReach + 0.008h, age);
                half mask = sideMask * tailMask;
                clip(mask - 0.025h);

                half trailingEdge = 1.0h - saturate(abs(age - maximumReach) / 0.14h);
                half brightness = 0.92h + fireNoise * 0.22h + trailingEdge * 0.16h;
                half alpha = input.color.a * mask * _GlowColor.a;
                return half4(_GlowColor.rgb * _Intensity * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
