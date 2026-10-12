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
                // x follows the complete blade from its base to its tip, while y
                // follows the age of the motion sample from newest to oldest.
                // Keep one continuous blade sheet here: cutting x into cells was
                // what made the old trail read as a row of stretched cylinders.
                half blade01 = saturate(input.uv.x);
                half age = saturate(input.uv.y);
                half time = _Time.y * (_FlameSpeed * 0.18h);
                half detail = max(5.0h, _FlameScale * 0.44h);
                half coarse = ValueNoise(half2(blade01 * detail + time * 0.31h, age * detail - time));
                half fine = ValueNoise(half2(blade01 * detail * 2.13h - time, age * detail * 1.47h + time * 0.57h));
                half fireNoise = coarse * 0.7h + fine * 0.3h;

                // The whole blade stretches, but its oldest boundary forms a
                // single tapered wedge.  The base ends first and every point gets
                // progressively more reach toward the blade tip, so the very top
                // is both the longest and the sharpest point of the silhouette.
                half tipCurve = pow(blade01, 1.65h);
                half maximumReach = lerp(0.46h, 1.0h, tipCurve);

                // Only erode inward from that outside boundary.  Noise vanishes at
                // the exact blade tip, preserving one clean, longest point instead
                // of rebuilding a collection of separate round flame tongues.
                half tipProtection = pow(saturate(1.0h - blade01), 0.55h);
                half edgeErosion = (1.0h - fireNoise) * 0.105h * _EdgeRoughness * tipProtection;
                maximumReach = saturate(maximumReach - edgeErosion);

                half edgeFeather = lerp(0.055h, 0.018h, tipCurve);
                half mask = 1.0h - smoothstep(maximumReach - edgeFeather, maximumReach + 0.006h, age);
                clip(mask - 0.025h);

                half trailingEdge = 1.0h - saturate(abs(age - maximumReach) / max(edgeFeather * 2.4h, 0.025h));
                half brightness = 0.92h + fireNoise * 0.22h + trailingEdge * 0.16h;
                half alpha = input.color.a * mask * _GlowColor.a;
                return half4(_GlowColor.rgb * _Intensity * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
