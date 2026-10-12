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
                // x spans the blade and y is motion-sample age. Split x into
                // discrete cylindrical flame roots, but give every root its own
                // non-zero reach through motion history. They all stretch without
                // merging into a solid rectangular sheet.
                half blade01 = saturate(input.uv.x);
                half age = saturate(input.uv.y);
                half time = _Time.y * (_FlameSpeed * 0.18h);
                half detail = max(5.0h, _FlameScale * 0.44h);
                half coarse = ValueNoise(half2(blade01 * detail + time * 0.31h, age * detail - time));
                half fine = ValueNoise(half2(blade01 * detail * 2.13h - time, age * detail * 1.47h + time * 0.57h));
                half fireNoise = coarse * 0.7h + fine * 0.3h;

                const half cylinderCount = 9.0h;
                half cylinderCoordinate = min(blade01, 0.9999h) * cylinderCount;
                half cylinderIndex = floor(cylinderCoordinate);
                half localAcross = frac(cylinderCoordinate) - 0.5h;
                half lengthSeed = Hash21(half2(cylinderIndex, 9.73h));
                half widthSeed = Hash21(half2(cylinderIndex, 17.41h));
                half leanSeed = Hash21(half2(cylinderIndex, 25.83h));

                // Every cylinder reaches well into the trail. Randomised limits
                // keep their ends organic, while the cylinder nearest the real
                // blade tip is guaranteed to be the longest one.
                half tipWeight = saturate(cylinderIndex / (cylinderCount - 1.0h));
                half cylinderReach = lerp(0.68h, 0.90h, lengthSeed);
                cylinderReach = lerp(cylinderReach, 1.0h, pow(tipWeight, 5.0h));
                half noisyAge = age + (fireNoise - 0.5h) * 0.055h * _EdgeRoughness;
                half alongCylinder = saturate(noisyAge / max(cylinderReach, 0.05h));
                half rootWidth = lerp(0.34h, 0.46h, widthSeed);
                // Every stretched cylinder narrows continuously toward its own
                // outermost end. A power above one removes the old rounded cutoff
                // and makes all nine afterimages finish as individual sharp tips.
                half taper = pow(saturate(1.0h - alongCylinder), 1.28h);
                half cylinderWidth = rootWidth * taper;
                half sideLean = (leanSeed - 0.5h) * age * 0.22h;
                half cylinderMask = 1.0h - smoothstep(
                    cylinderWidth * 0.68h,
                    cylinderWidth + 0.018h,
                    abs(localAcross - sideLean));
                half tailMask = 1.0h - smoothstep(cylinderReach - 0.045h, cylinderReach + 0.006h, noisyAge);
                half mask = cylinderMask * tailMask;
                clip(mask - 0.025h);

                half trailingEdge = 1.0h - saturate(abs(noisyAge - cylinderReach) / 0.14h);
                half brightness = 0.92h + fireNoise * 0.22h + trailingEdge * 0.16h;
                half alpha = input.color.a * mask * _GlowColor.a;
                return half4(_GlowColor.rgb * _Intensity * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
