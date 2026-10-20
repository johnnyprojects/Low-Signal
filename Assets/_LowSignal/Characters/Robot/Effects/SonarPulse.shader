// The sonar pulse: an expanding sphere drawn as a glowing shell. Only the edges, where the surface turns away
// from the camera, are bright, so it reads as a bubble of sound rather than a solid ball. Additive and two-sided,
// so it still shows as a ring of light around the screen once it has swept past the camera.
Shader "Low Signal/Sonar Pulse"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.92, 1, 1)
        _RimPower ("Rim Sharpness", Range(0.5, 8)) = 3
        _Fill ("Face Fill", Range(0, 1)) = 0.04
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _RimPower;
            float _Fill;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float facing = abs(dot(normalize(i.normal), normalize(i.viewDir)));
                float rim = pow(1 - facing, _RimPower);
                fixed4 color = _Color;
                color.a *= saturate(rim + _Fill);
                return color;
            }
            ENDCG
        }
    }
}
