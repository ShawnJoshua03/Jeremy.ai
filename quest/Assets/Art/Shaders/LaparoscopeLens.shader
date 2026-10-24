// Desktop preview only: the look of a laparoscopic video frame (bloom off wet highlights, warm xenon/LED grade,
// slight lateral chromatic aberration, rounded dark vignette, faint sensor noise). Used by ScopeLens.cs.
Shader "Hidden/Jeremy/LaparoscopeLens"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _Bloom, _Defocus;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float _Dof, _Focus, _FocusRange;
    float4 _BlurDir;
    float _Threshold, _BloomIntensity, _Aberration, _Vignette, _Warmth, _Contrast, _Saturation, _Exposure, _Grain;

    float4 Prefilter(v2f_img i) : SV_Target
    {
        float2 o = _MainTex_TexelSize.xy;
        float3 c = (tex2D(_MainTex, i.uv + o * float2(-1, -1)).rgb + tex2D(_MainTex, i.uv + o * float2(1, -1)).rgb
                  + tex2D(_MainTex, i.uv + o * float2(-1, 1)).rgb + tex2D(_MainTex, i.uv + o * float2(1, 1)).rgb) * 0.25;
        float b = max(c.r, max(c.g, c.b));
        float k = saturate((b - _Threshold) / max(1e-4, 1 - _Threshold));
        return float4(c * k * k, 1);
    }

    float4 Blur(v2f_img i) : SV_Target
    {
        float2 d = _BlurDir.xy * _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv).rgb * 0.227;
        c += (tex2D(_MainTex, i.uv + d * 1.38).rgb + tex2D(_MainTex, i.uv - d * 1.38).rgb) * 0.316;
        c += (tex2D(_MainTex, i.uv + d * 3.23).rgb + tex2D(_MainTex, i.uv - d * 3.23).rgb) * 0.07;
        return float4(c, 1);
    }

    float4 Downsample(v2f_img i) : SV_Target
    {
        float2 o = _MainTex_TexelSize.xy;
        return (tex2D(_MainTex, i.uv + o * float2(-0.5, -0.5)) + tex2D(_MainTex, i.uv + o * float2(0.5, -0.5))
              + tex2D(_MainTex, i.uv + o * float2(-0.5, 0.5)) + tex2D(_MainTex, i.uv + o * float2(0.5, 0.5))) * 0.25;
    }

    float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

    float4 Composite(v2f_img i) : SV_Target
    {
        float2 uv = i.uv, d = uv - 0.5;
        float r2 = dot(d, d);
        float2 shift = d * r2 * _Aberration;
        float3 c;
        c.r = tex2D(_MainTex, uv + shift).r;
        c.g = tex2D(_MainTex, uv).g;
        c.b = tex2D(_MainTex, uv - shift).b;
        if (_Dof > 0)
        {
            float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
            float coc = saturate((abs(depth - _Focus) - _FocusRange * 0.5) / _FocusRange) * _Dof;
            c = lerp(c, tex2D(_Defocus, uv).rgb, coc);
        }
        c += tex2D(_Bloom, uv).rgb * _BloomIntensity;
        c *= _Exposure;
        c *= float3(1 + 0.08 * _Warmth, 1 - 0.01 * _Warmth, 1 - 0.1 * _Warmth);
        float l = dot(c, float3(0.299, 0.587, 0.114));
        c = lerp(l.xxx, c, _Saturation);
        c = saturate(c);
        c = lerp(c, c * c * (3 - 2 * c), _Contrast);
        // Rounded-corner scope mask: an ellipse stretched to the frame, soft edge.
        float2 a = abs(d) * 2;
        float corner = pow(pow(a.x, 4) + pow(a.y * 1.05, 4), 0.25);
        c *= lerp(1, smoothstep(1.16, 0.55, corner), _Vignette);
        c += (Hash(uv * _MainTex_TexelSize.zw + _Time.y * 61) - 0.5) * _Grain;
        return float4(c, 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Prefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Blur
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Composite
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Downsample
            ENDCG }
    }
}
