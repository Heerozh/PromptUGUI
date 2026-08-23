// 玻璃 backdrop 的模糊核：Kawase 4-tap，一次 blit 一档。GlassBackdropSystem 把它串成
// 「降采样 → 轻模糊(A) → 重模糊(B)」，玻璃 shader 再按 frost 在 A/B 之间插值。
//
// 刻意写成不含任何 URP / SRP 头文件的纯 CG：这个 shader 躺在 Runtime/Resources 里，会被
// 每一个装了本包的工程导入。一旦 #include "Packages/com.unity.render-pipelines.core/..."，
// 没装 URP 的工程一打开就是一条 shader 编译错误。全屏三角形的顶点数学在这里内联展开，
// 与 core 的 Blit.hlsl 逐行等价。
Shader "Hidden/PromptUGUI/GlassBlur"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            Name "KawaseBlur"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            // RenderGraphUtils.AddBlitPass 用这两个名字绑定源纹理与缩放偏移（默认属性 ID）。
            sampler2D _BlitTexture;
            float4 _BlitScaleBias;

            // 采样偏移，单位是「源纹理 UV」，由 C# 按两端分辨率算好 —— 依赖
            // _BlitTexture_TexelSize 会踩自动填充的坑（RenderGraph 绑的纹理不保证填）。
            float4 _BlurOffset;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // 全屏三角形：顶点 0/1/2 → (0,0) (2,0) (0,2)，覆盖整个 NDC 且无对角线接缝。
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);

                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                o.uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 o = _BlurOffset.xy;
                half4 c  = tex2D(_BlitTexture, i.uv + float2(-o.x, -o.y));
                c += tex2D(_BlitTexture, i.uv + float2(o.x, -o.y));
                c += tex2D(_BlitTexture, i.uv + float2(-o.x, o.y));
                c += tex2D(_BlitTexture, i.uv + float2(o.x, o.y));
                return c * 0.25;
            }
            ENDCG
        }
    }
    Fallback Off
}
