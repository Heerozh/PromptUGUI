#if PROMPTUGUI_HAS_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace PromptUGUI.Application.Glass
{
    /// <summary>
    /// Captures the camera image once per frame and publishes two blur levels as global textures for
    /// the glass shader to sample.
    ///
    /// <para><b>No setup required.</b> The pass is injected at runtime through
    /// <see cref="RenderPipelineManager.beginCameraRendering"/> rather than shipped as a
    /// <c>ScriptableRendererFeature</c>, so a project using this package never has to touch its URP
    /// Renderer asset. URP clears the pass queue every frame, so it is re-enqueued each time.</para>
    ///
    /// <para><b>It only exists while glass does.</b> <see cref="GlassRuntime"/> starts it when the
    /// first glass panel appears and stops it when the last one goes away — no glass on screen means
    /// no subscription, no render targets and no blit work at all.</para>
    ///
    /// <para><b>One capture serves every panel.</b> The cost is a fixed three blits at quarter
    /// resolution regardless of how many glass panels are on screen.</para>
    /// </summary>
    internal static class GlassBackdropSystem
    {
        // The two blur levels the shader interpolates between with `frost`.
        private static readonly int BackdropAId = Shader.PropertyToID("_PUGUI_GlassBackdropA");
        private static readonly int BackdropBId = Shader.PropertyToID("_PUGUI_GlassBackdropB");
        private static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");

        private const string BlurShaderPath = "PromptUGUI/Material/UI-GlassBlur";

        /// <summary>
        /// Capture resolution divisor. A quarter on each axis is 1/16th the fragments, and the
        /// bilinear downsample is itself most of the blur — this is what keeps the whole chain
        /// under a third of a millisecond on mobile.
        /// </summary>
        private const int Downsample = 4;

        // Kawase tap offsets, in texels of each step's own source.
        private const float DownsampleTaps = 2f;
        private const float LightBlurTaps = 2f;
        private const float HeavyBlurTaps = 3.5f;

        private static bool _active;
        private static GlassBackdropPass _pass;

        // Three persistent targets rather than RenderGraph transients: the glass panels sample these
        // from an Overlay canvas, which draws outside the graph entirely — a transient texture would
        // already be recycled by then.
        private static RTHandle _light;    // A: what `frost=0` reads
        private static RTHandle _heavy;    // B: what `frost=1` reads
        private static RTHandle _scratch;
        private static int _width;
        private static int _height;

        // One material per step: the tap offset differs per blit, and a material each is both
        // cheaper and safer than threading property blocks through the blit utility.
        private static Material _downsampleMat;
        private static Material _lightMat;
        private static Material _heavyMat;

        private static bool _warnedNoCamera;
        private static bool _warnedNotUniversal;
        private static bool _warnedNoRenderGraph;

        // Enqueued-vs-recorded counters, watching for "the pass goes in but never runs".
        private const int NoRecordGraceFrames = 60;
        private static int _enqueued;
        private static int _recorded;

        internal static void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;

            if (active)
            {
                // The pipeline check has to happen HERE and not only inside the camera callback:
                // under the Built-in pipeline `beginCameraRendering` never fires at all, so a check
                // that lives in the callback can never run — in precisely the case that most needs
                // explaining. `PROMPTUGUI_HAS_URP` only says the *package* is installed; a project
                // can have URP in its manifest and still render with Built-in because no URP asset
                // is assigned, and then glass degrades in total silence.
                WarnIfNotUniversal();
                // Subscribe regardless: the event is inert under Built-in, and staying subscribed
                // means glass starts working on its own if the pipeline is swapped later.
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                return;
            }

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _enqueued = 0;
            _recorded = 0;
            ReleaseResources();
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Edit mode would otherwise drive this from the scene view and every inspector repaint.
            // Design-time preview of glass is not worth that, so outside play mode panels draw their
            // fallback — except for the render tests, which drive Camera.Render() by hand.
            if (!UnityEngine.Application.isPlaying
                && !GlassRuntime.RenderOutsidePlayModeForTests) return;

            var target = GlassRuntime.Camera != null ? GlassRuntime.Camera : Camera.main;
            if (target == null)
            {
                WarnOnce(ref _warnedNoCamera,
                    "PromptUGUI: glass is in use but there is no capture camera — tag one camera as " +
                    "MainCamera or set UI.Glass.Camera. Glass panels are drawing their fallback.");
                GlassRuntime.SetBackdropAvailable(false);
                return;
            }
            if (camera != target) return;

            if (!WarnIfNotUniversal()) return;

            if (!EnsureResources(target)) return;

            var data = target.GetUniversalAdditionalCameraData();
            var renderer = data != null ? data.scriptableRenderer : null;
            if (renderer == null) return;

            renderer.EnqueuePass(_pass ??= new GlassBackdropPass());
            _enqueued++;
            WarnIfNothingEverRecords();
        }

        /// <summary>
        /// Returns true when URP is the pipeline actually rendering, warning once if it is not.
        /// </summary>
        private static bool WarnIfNotUniversal()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) return true;

            WarnOnce(ref _warnedNotUniversal,
                "PromptUGUI: glass needs the Universal Render Pipeline to be the pipeline actually " +
                "rendering, and it is not — every glass panel is drawing its fallback. Having the URP " +
                "package installed is not enough: assign a Universal Render Pipeline Asset under " +
                "Project Settings > Graphics (and per-level under Quality). A project with URP in its " +
                "manifest but no asset assigned renders with Built-in, where the capture hook " +
                "(RenderPipelineManager.beginCameraRendering) never fires at all.");
            GlassRuntime.SetBackdropAvailable(false);
            return false;
        }

        /// <summary>
        /// Catches "the pass is enqueued every frame but never actually runs".
        ///
        /// The known cause is URP's Compatibility Mode (Render Graph disabled), where only the
        /// deprecated <c>Execute</c> path is called and <c>RecordRenderGraph</c>
        /// never is — so nothing publishes a backdrop and every glass panel silently draws its
        /// fallback. This is checked by observation rather than by reading
        /// <c>RenderGraphSettings.enableRenderCompatibilityMode</c>: that property is
        /// <c>[Obsolete]</c> from 6000.4 on (it still means something on 6000.0–6000.3, which is
        /// exactly where this bites), and counting covers any other reason the pass might be
        /// dropped too.
        /// </summary>
        private static void WarnIfNothingEverRecords()
        {
            if (_warnedNoRenderGraph || _recorded > 0 || _enqueued < NoRecordGraceFrames) return;
            _warnedNoRenderGraph = true;
            Debug.LogWarning(
                $"PromptUGUI: the glass backdrop pass has been enqueued for {_enqueued} frames but " +
                "has never been recorded, so no backdrop is being produced and every glass panel is " +
                "drawing its fallback. The usual cause is URP running in Compatibility Mode " +
                "(Project Settings > Graphics > Render Graph, or on Unity 6000.0–6000.3 the " +
                "'Compatibility Mode (Render Graph Disabled)' toggle): glass needs Render Graph. " +
                "Enable it, or set UI.Glass.Enabled = false to opt out cleanly.");
        }

        /// <summary>
        /// Allocates the capture targets for this camera's resolution, reallocating on resize.
        /// Returns false if anything needed is missing.
        /// </summary>
        private static bool EnsureResources(Camera camera)
        {
            if (_downsampleMat == null)
            {
                var shader = Resources.Load<Shader>(BlurShaderPath);
                if (shader == null) return false;
                _downsampleMat = NewMaterial(shader);
                _lightMat = NewMaterial(shader);
                _heavyMat = NewMaterial(shader);
            }

            var width = Mathf.Max(1, camera.pixelWidth / Downsample);
            var height = Mathf.Max(1, camera.pixelHeight / Downsample);
            if (_light != null && _width == width && _height == height) return true;

            ReleaseTargets();
            _width = width;
            _height = height;

            // HDR keeps bright scene highlights from clipping before the blur spreads them, which is
            // most of what makes glass over a lit scene look lit rather than washed out.
            var format = SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.HDR);
            _light = AllocTarget(width, height, format, "PromptUGUI_GlassBackdropA");
            _heavy = AllocTarget(width, height, format, "PromptUGUI_GlassBackdropB");
            _scratch = AllocTarget(width, height, format, "PromptUGUI_GlassBackdropScratch");

            // Tap offsets are expressed in each step's own source UV, so they have to be recomputed
            // whenever the resolution changes.
            SetOffset(_downsampleMat, DownsampleTaps, camera.pixelWidth, camera.pixelHeight);
            SetOffset(_lightMat, LightBlurTaps, width, height);
            SetOffset(_heavyMat, HeavyBlurTaps, width, height);

            Shader.SetGlobalTexture(BackdropAId, _light.rt);
            Shader.SetGlobalTexture(BackdropBId, _heavy.rt);
            return true;
        }

        private static Material NewMaterial(Shader shader)
            => new(shader) { hideFlags = HideFlags.HideAndDontSave };

        private static RTHandle AllocTarget(int width, int height,
            UnityEngine.Experimental.Rendering.GraphicsFormat format, string name)
            => RTHandles.Alloc(width, height,
                colorFormat: format,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp,
                name: name);

        private static void SetOffset(Material mat, float taps, int sourceWidth, int sourceHeight)
            => mat.SetVector(BlurOffsetId,
                new Vector4(taps / Mathf.Max(1, sourceWidth), taps / Mathf.Max(1, sourceHeight), 0f, 0f));

        private static void ReleaseTargets()
        {
            _light?.Release();
            _heavy?.Release();
            _scratch?.Release();
            _light = _heavy = _scratch = null;
            _width = _height = 0;
        }

        private static void ReleaseResources()
        {
            ReleaseTargets();
            DestroyMaterial(ref _downsampleMat);
            DestroyMaterial(ref _lightMat);
            DestroyMaterial(ref _heavyMat);
            _pass = null;
        }

        private static void DestroyMaterial(ref Material mat)
        {
            if (mat == null) return;
            if (UnityEngine.Application.isPlaying) Object.Destroy(mat);
            else Object.DestroyImmediate(mat);
            mat = null;
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag) return;
            flag = true;
            Debug.LogWarning(message);
        }

        /// <summary>
        /// Downsample, blur, blur again — three blits at a sixteenth of the pixels, shared by every
        /// glass panel on screen.
        /// </summary>
        private sealed class GlassBackdropPass : ScriptableRenderPass
        {
            public GlassBackdropPass()
            {
                // After post-processing, so glass shows the graded image the player actually sees.
                // This also makes URP resolve post-processing into an intermediate colour texture,
                // which is what makes activeColorTexture readable here at all.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_light == null || _heavy == null || _scratch == null) return;

                _recorded++;

                var resources = frameData.Get<UniversalResourceData>();
                var source = resources.activeColorTexture;
                if (!source.IsValid()) return;

                var light = renderGraph.ImportTexture(_light);
                var heavy = renderGraph.ImportTexture(_heavy);
                var scratch = renderGraph.ImportTexture(_scratch);

                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(source, light, _downsampleMat, 0),
                    "PromptUGUI Glass Downsample");
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(light, scratch, _lightMat, 0),
                    "PromptUGUI Glass Blur");
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(scratch, heavy, _heavyMat, 0),
                    "PromptUGUI Glass Blur Heavy");

                GlassRuntime.SetBackdropAvailable(true);
            }
        }
    }
}
#endif
