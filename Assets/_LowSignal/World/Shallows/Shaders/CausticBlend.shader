// Mixes two caustic frames into one, so CausticsAnimator can glide between frames every rendered frame
// instead of snapping from one texture to the next. Reads each frame's alpha and writes it to every channel.
Shader "Hidden/Low Signal/Caustic Blend"
{
    Properties
    {
        _MainTex ("From", 2D) = "white" {}
        _NextTex ("To", 2D) = "white" {}
        _Blend ("Blend", Range(0, 1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NextTex;
            float _Blend;

            fixed4 frag (v2f_img i) : SV_Target
            {
                // Cookie textures import as alpha-only, so their colour channels read as black; the pattern lives
                // in alpha. Copy it into every channel, because URP lights with the colour of a colour cookie.
                float strength = lerp(tex2D(_MainTex, i.uv).a, tex2D(_NextTex, i.uv).a, _Blend);
                return fixed4(strength, strength, strength, strength);
            }
            ENDCG
        }
    }
}
