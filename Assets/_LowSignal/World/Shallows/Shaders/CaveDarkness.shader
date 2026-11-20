// A flat, unlit, see-through colour for the cave's stacked darkness sheets. Up close they add up to black; with
// distance they take on the water's fog colour like everything else, so the fog always wins over the darkness.
Shader "Low Signal/Cave Darkness"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 0.18)
        // Above 1 the darkness gives way to fog sooner than the scenery does.
        _FogBoost ("Fog Boost", Range(1, 4)) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FogBoost;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vertex(float4 positionOS : POSITION)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // How much of the scene's fog would cover this point, made stronger so the dark fades out first.
                // The Shallows uses exponential-squared fog; unity_FogParams.x is its density over sqrt(ln 2), and zero
                // when fog is off. Worked out here from the camera distance rather than through fog shader variants.
                float density = unity_FogParams.x * distance(input.positionWS, _WorldSpaceCameraPos);
                half visibility = exp2(-density * density);
                half fogged = saturate(1 - (1 - visibility) * _FogBoost);
                half3 colour = lerp(unity_FogColor.rgb, _Color.rgb, fogged);
                return half4(colour, _Color.a);
            }
            ENDHLSL
        }
    }
}
