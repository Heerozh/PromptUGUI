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
    /// <para><b>One capture serves every panel.</b> The cost is a fixed six blits at quarter
    /// resolution regardless of how many glass panels are on screen.</para>
    ///
    /// <para><b>HDR displays need one extra matrix.</b> Under URP's HDR Output the post-processed
    /// picture is already rotated into the display's gamut and scaled to paper-white nits, and the
    /// overlay UI is put through that same transform again at compositing. The downsample blit
    /// multiplies by the inverse (<see cref="GlassBackdropDecode"/>) so a glass panel reads as the
    /// scene behind it rather than as a white slab. On an SDR display the matrix is the
    /// identity.</para>
    /// </summary>
    internal static class GlassBackdropSystem
    {
        // The two blur levels the shader interpolates between with `frost`.
        private static readonly int BackdropAId = Shader.PropertyToID("_PUGUI_GlassBackdropA");
        private static readonly int BackdropBId = Shader.PropertyToID("_PUGUI_GlassBackdropB");
        private static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
        private static readonly int DecodeId = Shader.PropertyToID("_BackdropDecode");

        private const string BlurShaderPath = "PromptUGUI/Material/UI-GlassBlur";

        // UI-GlassBlur.shader: pass 0 is the bare Kawase kernel, pass 1 the same kernel followed by
        // the _BackdropDecode colour matrix. Only the downsample runs the matrix — it is the one
        // blit that reads the camera's picture, and the matrix is what puts an HDR display's
        // display-ready picture back into the space the overlay UI is composited in
        // (GlassBackdropDecode has the whole story). On an SDR display it is the identity.
        private const int BlurPass = 0;
        private const int DownsampleDecodePass = 1;

        /// <summary>
        /// Capture resolution divisor. A quarter on each axis is 1/16th the fragments — this is
        /// what keeps the whole chain under a third of a millisecond on mobile.
        /// </summary>
        private const int Downsample = 4;

        // Kawase tap offsets, in texels of each step's own source. Every blit is the same 4-tap
        // kernel (UI-GlassBlur.shader), and a 4-tap kernel is only a blur while each tap's bilinear
        // footprint reaches the next tap. Further apart than that it does not smear whatever is
        // thinner than the gap, it copies it once per tap — one bright star over the world map
        // came out as a 4x4 lattice of stars. (The same rule the ImageFx mip prefilter enforces,
        // image-fx spec §14.1.) Two consequences, and the numbers below are the ones a per-texel
        // rehearsal of the chain — bilinear semantics, 1D since every step is separable — found to
        // leave a point light with no rise anywhere on its falloff:
        //
        //  * The downsample tap sits 1 SOURCE texel off the block centre, so its four 2x2 bilinear
        //    reads tile the 4x4 block exactly — the box a mip level 2 would hold. At 2 texels the
        //    reads land on the block's corners and the two middle rows / columns are never read at
        //    all, so a small star blinked in and out as the map scrolled it across block edges.
        //  * Same-resolution passes use half-texel offsets (n + 0.5): every tap then sits on a
        //    texel corner and bilinear averages 2x2 for it, where an integer offset lands on texel
        //    centres and degenerates to a point sample. The radius grows pass by pass, each
        //    kernel's holes filled by the ones before it.
        //
        // A is the light frost (σ ≈ 3.5 screen px), B the heavy one (σ ≈ 16.5 px — what the old
        // chain's radii added up to, minus the holes). The rehearsal rejected the textbook
        // {0.5, 1.5, 2.5, 3.5}: a lone 1.5 pass straight after 0.5 still dips at the centre (0.29
        // of peak), and the full sequence keeps a 3% ripple; three 1.5s before the 2.5 do not.
        private const float DownsampleTaps = 1f;
        private static readonly float[] LightBlurTaps = { 0.5f };
        private static readonly float[] HeavyBlurTaps = { 1.5f, 1.5f, 1.5f, 2.5f };

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

        // One material per blit: the tap offset differs per pass, and a material each is both
        // cheaper and safer than threading property blocks through the blit utility.
        private static Material _downsampleMat;
        private static Material[] _lightMats;
        private static Material[] _heavyMats;
        // What _downsampleMat currently carries as _BackdropDecode; re-derived every frame so a
        // display switching HDR on or off, or a Tonemapping override changing paper white, takes
        // effect at once. Only pushed to the material when it actually changes.
        private static Matrix4x4 _decode = Matrix4x4.identity;

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
            // A target that still exists but no longer renders — disabled for a cutscene, or its
            // GameObject deactivated — never reaches RecordRenderGraph again. Saying so here is
            // immediate; the frame watchdog in GlassRuntime would otherwise take a frame or two.
            if (!target.isActiveAndEnabled)
            {
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
                _lightMats = NewMaterials(shader, LightBlurTaps.Length);
                _heavyMats = NewMaterials(shader, HeavyBlurTaps.Length);
                // A matrix uniform nobody has set is not the identity; start from one explicitly.
                _decode = Matrix4x4.identity;
                _downsampleMat.SetMatrix(DecodeId, _decode);
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
            SetOffsets(_lightMats, LightBlurTaps, width, height);
            SetOffsets(_heavyMats, HeavyBlurTaps, width, height);

            Shader.SetGlobalTexture(BackdropAId, _light.rt);
            Shader.SetGlobalTexture(BackdropBId, _heavy.rt);
            return true;
        }

        private static Material NewMaterial(Shader shader)
            => new(shader) { hideFlags = HideFlags.HideAndDontSave };

        private static Material[] NewMaterials(Shader shader, int count)
        {
            var mats = new Material[count];
            for (var i = 0; i < count; i++) mats[i] = NewMaterial(shader);
            return mats;
        }

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

        private static void SetOffsets(Material[] mats, float[] taps, int sourceWidth, int sourceHeight)
        {
            for (var i = 0; i < mats.Length; i++) SetOffset(mats[i], taps[i], sourceWidth, sourceHeight);
        }

        /// <summary>
        /// The colour matrix this frame's downsample has to apply. The two gates mirror the ones
        /// URP's own LUT pass converts for the display under (<c>ColorGradingLutPass</c>:
        /// <c>isHDROutputActive</c>, and post-processing on for this camera) — the capture reads
        /// what that pass, through Uber, left in the colour buffer, so it must undo exactly what
        /// was applied and nothing else. On an SDR display, or with post-processing off, that is
        /// nothing.
        /// </summary>
        private static Matrix4x4 DecodeFor(UniversalCameraData cameraData)
        {
            var overrideForTests = GlassRuntime.BackdropDecodeOverrideForTests;
            if (overrideForTests.HasValue) return overrideForTests.Value;

            var hdrOutput = cameraData.isHDROutputActive;
            var postProcessed = cameraData.postProcessEnabled;
            if (!hdrOutput || !postProcessed) return Matrix4x4.identity;

            return GlassBackdropDecode.For(hdrOutput, postProcessed,
                                           cameraData.hdrDisplayColorGamut, PaperWhiteNits(cameraData));
        }

        /// <summary>
        /// The paper white URP scales both the graded scene and the overlay UI by — mirrors its
        /// internal <c>GetHDROutputLuminanceParameters</c>: the Tonemapping override's value
        /// (300 nits by default) unless it asks for the display's own. Read from the volume stack
        /// URP has already evaluated for this camera, the same one the post-processing pass reads.
        /// </summary>
        private static float PaperWhiteNits(UniversalCameraData cameraData)
        {
            var tonemapping = VolumeManager.instance.stack?.GetComponent<Tonemapping>();
            if (tonemapping != null && !tonemapping.detectPaperWhite.value) return tonemapping.paperWhite.value;
            return cameraData.hdrDisplayInformation.paperWhiteNits;
        }

        private static void ApplyDecode(Matrix4x4 decode)
        {
            if (decode == _decode) return;
            _decode = decode;
            _downsampleMat.SetMatrix(DecodeId, decode);
        }

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
            DestroyMaterials(ref _lightMats);
            DestroyMaterials(ref _heavyMats);
            _pass = null;
        }

        private static void DestroyMaterial(ref Material mat)
        {
            if (mat == null) return;
            if (UnityEngine.Application.isPlaying) Object.Destroy(mat);
            else Object.DestroyImmediate(mat);
            mat = null;
        }

        private static void DestroyMaterials(ref Material[] mats)
        {
            if (mats == null) return;
            for (var i = 0; i < mats.Length; i++) DestroyMaterial(ref mats[i]);
            mats = null;
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag) return;
            flag = true;
            Debug.LogWarning(message);
        }

        /// <summary>
        /// Re-arms the warn-once diagnostics. Called from <see cref="GlassRuntime"/> on play start
        /// and on test reset — see the note there on domain reload being optional.
        /// </summary>
        internal static void ResetDiagnostics()
        {
            _warnedNoCamera = false;
            _warnedNotUniversal = false;
            _warnedNoRenderGraph = false;
            _enqueued = 0;
            _recorded = 0;
        }

        /// <summary>
        /// Downsample, the light passes into A, the heavy passes on into B — six blits at a
        /// sixteenth of the pixels, shared by every glass panel on screen.
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

                ApplyDecode(DecodeFor(frameData.Get<UniversalCameraData>()));

                var light = renderGraph.ImportTexture(_light);
                var heavy = renderGraph.ImportTexture(_heavy);
                var scratch = renderGraph.ImportTexture(_scratch);

                // The light passes must end in A and the heavy ones in B, bouncing off scratch in
                // between — so the downsample lands wherever leaves A's last pass writing A.
                var afterDownsample = LightBlurTaps.Length % 2 == 0 ? light : scratch;
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(source, afterDownsample, _downsampleMat,
                                                                DownsampleDecodePass),
                    "PromptUGUI Glass Downsample");
                Chain(renderGraph, _lightMats, afterDownsample, light, scratch, "PromptUGUI Glass Blur A");
                Chain(renderGraph, _heavyMats, light, heavy, scratch, "PromptUGUI Glass Blur B");

                GlassRuntime.SetBackdropAvailable(true);
            }

            /// <summary>
            /// Runs the passes in order starting from <paramref name="from"/>, alternating between
            /// <paramref name="final"/> and <paramref name="spare"/> so that the last one writes
            /// <paramref name="final"/>.
            /// </summary>
            private static void Chain(RenderGraph renderGraph, Material[] mats, TextureHandle from,
                TextureHandle final, TextureHandle spare, string name)
            {
                var src = from;
                for (var i = 0; i < mats.Length; i++)
                {
                    var dst = (mats.Length - 1 - i) % 2 == 0 ? final : spare;
                    renderGraph.AddBlitPass(
                        new RenderGraphUtils.BlitMaterialParameters(src, dst, mats[i], BlurPass), name);
                    src = dst;
                }
            }
        }
    }
}
#endif
