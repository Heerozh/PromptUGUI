// <Image> / <Icon> 的 sprite 级效果：blur（本体像素模糊）、glow（自剪影外发光）、tint="linear"、
// 禁用去色 —— 四者折在一个 shader 里（spec 2026-09-02）。
//
// 为什么是"一个 shader 四件事"而不是四个材质轮流占 Graphic.material 槽：那个槽只有一个，
// tint 与灰度今天就是靠互相覆盖共存的，再加 blur/glow 就必然打架。折进来之后它们不再是槽，
// 而是参数，可以任意组合，且同参数集的实例仍共享一个材质、照常合批（FxMaterialCache）。
//
// 顶点侧由 FxMesh 供给：uv0 已按半径外推（会越出 sprite 的 UV 矩形，这是故意的），
// uv1 = sprite 自己的 UV 矩形，uv2.xy = uv/画布单位换算，uv2.zw = texel/画布单位换算
//（纹理没有可用的 mip 链时为 0）。矩形之外的 tap 一律当透明 —— 图集里那外面住着别的 sprite。
//
// 采样核是 25 tap 的均匀圆盘，tap 走 tex2Dlod：半径换成 texel 后，lod 取到让每个 tap 的
// 足迹恰好盖住 tap 间距（spec §14.3）。没有 mip 就是 lod 0 —— 与 M1 逐位相同，只是 R 超过
// ~3 texel 后细笔画会在每个 tap 处各画一份（重影）；C# 侧对此按纹理警告一次。
//
// 全部为 uniform 分支（`if (_Blur > 0)`），不设 shader 关键字：运行时 new Material 的变体
// 会被构建剥掉，而 uniform 分支全体 fragment 走同一路径，开销可忽略（UI-ProceduralPanel 同款）。
Shader "UI/ImageFx"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Blur ("Blur Radius (px)", Float) = 0
        _Glow ("Glow Radius (px)", Float) = 0
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _GlowSelf ("Glow Takes Sprite Colour", Float) = 1
        _TintLinear ("Linear Light Tint", Float) = 0
        _Desaturate ("Desaturate", Float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // alpha 通道走 One OneMinusSrcAlpha：HDR 显示输出下的离屏 UI 合成要求正确的直 alpha，
        // 理由见 UI-ProceduralPanel.shader 同一行的注释。
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "UI-ImageTint.cginc"
            #include "UI-PanelSDF.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;   // sprite 的 UV 矩形 (uMin, vMin, uMax, vMax)
                float4 texcoord2 : TEXCOORD2;   // (uv / 画布单位, texel / 画布单位)；zw 无 mip 时为 0
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 rect : TEXCOORD2;
                float4 scale : TEXCOORD3;       // xy = uv / 画布单位，zw = texel / 画布单位
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _Blur;
            float _Glow;
            fixed4 _GlowColor;
            float _GlowSelf;
            float _TintLinear;
            float _Desaturate;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.rect = v.texcoord1;
                OUT.scale = v.texcoord2;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 采样核：中心 + 24 点 Vogel 圆盘（黄金角螺旋），xy = 单位圆盘内的偏移、z = 权重。
            // 黄金角让点均匀铺满圆盘、无环状条纹；两轮（blur / glow）共用同一套点。
            // 半径按 px 给，偏移 = 单位偏移 × 半径 × uv/px —— 与图集大小、sprite 大小都无关。
            //
            // **权重一律为 1（均匀圆盘），不是高斯**。高斯（试过 exp(-2r²)）把权重堆在核心，
            // 于是覆盖率在离边缘 R/2 处就掉到看不见 —— 作者写 glow="8" 得到的是一圈 4px 的硬边，
            // 而不是 8px 的光晕。均匀圆盘的覆盖率是"圆盘落进剪影里的面积占比"，恰好在 d=R 处归零，
            // 与 SDF 面板 PuguiApplyOuterGlow 的 g² 手感对齐（spec §4.3）。
            // 代价是模糊比高斯略"平"，在 ≤12px 这一档看不出来。
            static const float3 kDisk[24] =
            {
                float3(+0.14434, +0.00000, 1.00000),
                float3(-0.18434, +0.16887, 1.00000),
                float3(+0.02822, -0.32151, 1.00000),
                float3(+0.23235, +0.30306, 1.00000),
                float3(-0.42639, -0.07542, 1.00000),
                float3(+0.40392, -0.25694, 1.00000),
                float3(-0.13510, +0.50257, 1.00000),
                float3(-0.25765, -0.49610, 1.00000),
                float3(+0.55901, +0.20415, 1.00000),
                float3(-0.58155, +0.24006, 1.00000),
                float3(+0.28035, -0.59909, 1.00000),
                float3(+0.20717, +0.66049, 1.00000),
                float3(-0.62441, -0.36186, 1.00000),
                float3(+0.73251, -0.16104, 1.00000),
                float3(-0.44704, +0.63586, 1.00000),
                float3(-0.10328, -0.79697, 1.00000),
                float3(+0.63401, +0.53435, 1.00000),
                float3(-0.85318, +0.03528, 1.00000),
                float3(+0.62233, -0.61930, 1.00000),
                float3(-0.04164, +0.90043, 1.00000),
                float3(-0.59215, -0.70959, 1.00000),
                float3(+0.93803, +0.12621, 1.00000),
                float3(-0.79479, +0.55300, 1.00000),
                float3(+0.21718, -0.96540, 1.00000),
            };

            // 25 tap 在圆盘里的平均间距 ≈ R·√(π/25)（spec §14.3）。与 C# 侧 FxMesh.TapSpacing 同值。
            #define PUGUI_FX_TAP_SPACING 0.3545
            // 边缘内缩时假设的 atlas padding（texel）：Unity SpriteAtlas 的最小档。
            #define PUGUI_FX_MIN_PADDING 2.0

            // sprite 矩形之外的一切都是**别的 sprite**（图集里紧邻的邻居），必须读成透明 ——
            // 不是 clamp 到边缘。这是整套 fx 敢把四边形外扩出 sprite 之外的唯一前提。
            inline half4 SampleClamped(float2 uv, float4 rect, float lod)
            {
                float2 inside = step(rect.xy, uv) * step(uv, rect.zw);
                return (tex2Dlod(_MainTex, float4(uv, 0.0, lod)) + _TextureSampleAdd) * (inside.x * inside.y);
            }

            // 一轮圆盘采样，返回**预乘**结果 (rgb·a, a) 的加权平均。
            //
            // 预乘是硬要求：透明像素的 RGB 常是黑或垃圾值，直接平均直 alpha 的颜色会把它们混进来，
            // 在边缘糊出一圈暗环（浅色底上尤其明显）。
            //
            // lod：半径换成 texel（scale.zw），取 log2(R_texel × 间距系数) —— 每个 tap 的 bilinear
            // 足迹恰好盖住相邻 tap 的间距，细笔画不再被逐 tap 复制。scale.zw 为 0（无 mip / Point）
            // 时 lod 0。半径不做补偿：试过按 mip 足迹收缩 tap 半径，误差反而翻倍。
            //
            // 内缩：lod L 的 bilinear 会读到 tap 两侧各 1.5·2^L texel，超出 padding 的那部分就是
            // 邻居的像素，而矩形钳制只钳得住 tap 中心。所以矩形先按 (1.5·2^L − padding) 收缩再钳
            // tap。lod ≤ 1 时为 0；有透明边距的图标看不出，全出血图片的模糊边缘略提前淡出。
            half4 DiskSample(float2 uv, float4 rect, float4 scale, float radius)
            {
                float texR = radius * max(scale.z, scale.w);
                float lod = texR > 0.0 ? max(0.0, log2(texR * PUGUI_FX_TAP_SPACING)) : 0.0;

                float insetTexels = max(0.0, 1.5 * exp2(lod) - PUGUI_FX_MIN_PADDING);
                float2 uvPerTexel = scale.xy / max(scale.zw, 1e-6);
                // 极小 sprite 配极大半径时别把矩形缩没：最多缩掉每边 1/4。
                float2 inset = min(insetTexels * uvPerTexel, (rect.zw - rect.xy) * 0.25);
                float4 r = float4(rect.xy + inset, rect.zw - inset);

                half4 c = SampleClamped(uv, r, lod);
                half4 sum = half4(c.rgb * c.a, c.a);
                half wsum = 1.0;

                [unroll]
                for (int i = 0; i < 24; i++)
                {
                    float2 o = kDisk[i].xy * radius * scale.xy;
                    half w = kDisk[i].z;
                    half4 s = SampleClamped(uv + o, r, lod);
                    sum += half4(s.rgb * s.a, s.a) * w;
                    wsum += w;
                }
                return sum / wsum;
            }

            // 本体的单 tap：不内缩、硬件 lod（缩小绘制时照常走 mip，不走样）。
            inline half4 SampleBody(float2 uv, float4 rect)
            {
                float2 inside = step(rect.xy, uv) * step(uv, rect.zw);
                return (tex2D(_MainTex, uv) + _TextureSampleAdd) * (inside.x * inside.y);
            }

            /// 反预乘：把 (rgb·a, a) 还原成直 alpha 的颜色。
            inline half4 Unpremultiply(half4 p)
            {
                return half4(p.a > 1e-4 ? p.rgb / p.a : half3(0, 0, 0), p.a);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 退化矩形 = 这份网格没经过 FxMesh（Sliced / Tiled / sprite mesh，或画布没开
                // TEXCOORD1/2）。此时既无从钳制也无从换算，老老实实按 UI/Default 画。
                bool hasRect = IN.rect.z > IN.rect.x && IN.rect.w > IN.rect.y;

                half4 image;
                half4 glow = half4(0, 0, 0, 0);

                if (!hasRect)
                {
                    image = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                }
                else
                {
                    image = SampleBody(IN.texcoord, IN.rect);

                    if (_Blur > 0.0)
                        image = Unpremultiply(DiskSample(IN.texcoord, IN.rect, IN.scale, _Blur));

                    if (_Glow > 0.0)
                    {
                        half4 g = DiskSample(IN.texcoord, IN.rect, IN.scale, _Glow);

                        // 覆盖率 → 衰减。剪影边缘覆盖率约 0.5，所以 ×2 把边缘顶到 1；平方让边缘更快
                        // 收住，更像光晕而不是色块 —— 与 SDF 面板的 PuguiApplyOuterGlow 同一条曲线。
                        half falloff = saturate(2.0 * g.a);
                        falloff *= falloff;

                        if (_GlowSelf > 0.5)
                        {
                            // 自体色：光晕就是图标自己在这个半径上的平均色，随 color= / 状态调制走；
                            // _GlowColor.a 是 glowColor="self/0.5" 给的强度（不写 = 1）。
                            half3 rgb = Unpremultiply(g).rgb * IN.color.rgb;
                            glow = half4(rgb, _GlowColor.a * falloff * IN.color.a);
                        }
                        else
                        {
                            // 作者指定的颜色不乘顶点色 —— 写 glowColor 就是要这个颜色；
                            // 只有透明度跟着元素一起淡出。
                            glow = half4(_GlowColor.rgb, _GlowColor.a * falloff * IN.color.a);
                        }
                    }
                }

                image = _TintLinear > 0.5 ? PuguiLinearLight(image, IN.color) : image * IN.color;

                // 本体在上、光晕在下：光晕本来就是"从剪影里渗出来的那一圈"。
                half4 color = PuguiOver(image, glow);

                if (_Desaturate > 0.5)
                {
                    // 整体去色（本体与光晕一起），与 ProceduralPanel 把 glow 一并 Desaturate 同一取舍。
                    half luma = dot(color.rgb, half3(0.299, 0.587, 0.114));
                    color.rgb = luma.xxx;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
