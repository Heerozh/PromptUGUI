// 玻璃 backdrop 的模糊核：Kawase 4-tap，一次 blit 一档。GlassBackdropSystem 把它串成
// 「降采样 → 轻模糊(A) → 重模糊(B)」，玻璃 shader 再按 frost 在 A/B 之间插值。
// tap 偏移由 C# 定（GlassBackdropSystem 常量处有推导）：每个 tap 的 bilinear 足迹必须够到
// 相邻 tap —— 降采样偏移 1 个源纹素恰好拼成 4×4 box，同分辨率的 pass 用半纹素偏移让每个
// tap 落在纹素角上。偏移一大，四个 tap 就不再是模糊而是四份复制。
//
// 两个 pass 同一个核：pass 0 是纯模糊，同分辨率各档都用它；pass 1 只给降采样那一次 blit，
// 在核之后多乘一个 3×3 颜色矩阵 _BackdropDecode。HDR 显示输出下 URP 的后处理交出来的是
// 已按显示器色域旋转、按纸白亮度放大到 nit 的画面，而 overlay UI 合成时会对 UI 像素再做
// 一遍同样的变换 —— 玻璃采样到的图必须先逆回去（矩阵由 GlassBackdropDecode 推导，SDR 下是
// 单位阵）。矩阵与平均都是线性的，所以乘在四个 tap 平均之后：每个输出纹素一次 mul，
// 四分之一分辨率，可以忽略。
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

        CGINCLUDE
        #include "UnityCG.cginc"

        // RenderGraphUtils.AddBlitPass 用这两个名字绑定源纹理与缩放偏移（默认属性 ID）。
        sampler2D _BlitTexture;
        float4 _BlitScaleBias;

        // 采样偏移，单位是「源纹理 UV」，由 C# 按两端分辨率算好 —— 依赖
        // _BlitTexture_TexelSize 会踩自动填充的坑（RenderGraph 绑的纹理不保证填）。
        float4 _BlurOffset;

        // 降采样专用（pass 1）：采样结果左乘的颜色矩阵，只用左上 3×3。
        float4x4 _BackdropDecode;

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

        half4 Kawase(float2 uv)
        {
            float2 o = _BlurOffset.xy;
            half4 c  = tex2D(_BlitTexture, uv + float2(-o.x, -o.y));
            c += tex2D(_BlitTexture, uv + float2(o.x, -o.y));
            c += tex2D(_BlitTexture, uv + float2(-o.x, o.y));
            c += tex2D(_BlitTexture, uv + float2(o.x, o.y));
            return c * 0.25;
        }

        half4 FragBlur(Varyings i) : SV_Target
        {
            return Kawase(i.uv);
        }

        half4 FragDownsampleDecode(Varyings i) : SV_Target
        {
            half4 c = Kawase(i.uv);
            // 色域逆旋转会把显示器色域里、Rec709 之外的颜色变成负分量。UI 目标是 UNORM，
            // 到那里终归会被截成 0；在这里截掉，后面的模糊与玻璃的亮度/饱和度运算就不会
            // 看到负值。>1 的高光保留 —— 模糊靠它们散开。
            c.rgb = max(mul((float3x3)_BackdropDecode, c.rgb), 0.0);
            return c;
        }
        ENDCG

        Pass
        {
            Name "KawaseBlur"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Name "KawaseDownsampleDecode"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsampleDecode
            #pragma target 3.0
            ENDCG
        }
    }
    Fallback Off
}
