Shader "Swordplay/Wii Panoramic Sky"
{
    Properties
    {
        _MainTex ("Sky Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1
        _Rotation ("Horizontal Rotation", Range(0, 360)) = 0
        _VerticalOffset ("Vertical Offset", Range(-0.4, 0.4)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float _VerticalOffset;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float angle = radians(_Rotation);
                float sineAngle;
                float cosineAngle;
                sincos(angle, sineAngle, cosineAngle);
                direction.xz = float2(
                    cosineAngle * direction.x - sineAngle * direction.z,
                    sineAngle * direction.x + cosineAngle * direction.z);

                const float inverseTwoPi = 0.15915494309;
                const float inversePi = 0.31830988618;
                float2 uv;
                uv.x = frac(atan2(direction.x, direction.z) * inverseTwoPi + 0.5);
                uv.y = asin(clamp(direction.y, -1.0, 1.0)) * inversePi + 0.5;
                uv.y = clamp(uv.y + _VerticalOffset, 0.001, 0.999);

                half3 sky = tex2D(_MainTex, uv).rgb;
                return half4(sky * _Tint.rgb * _Exposure, 1.0h);
            }
            ENDCG
        }
    }
    Fallback Off
}
