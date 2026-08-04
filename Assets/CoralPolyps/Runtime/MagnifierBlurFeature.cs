/// <summary>
/// URP renderer feature that runs MagnifierRadialBlur over the finished frame.
///
/// This is the one place the project accepts a ScriptableRendererFeature, and it is worth
/// saying why, because FullscreenMagnifier explicitly declined to be one. That case was a
/// single quad that uGUI could draw just as well, so the feature bought nothing but a
/// renderer-asset edit invisible in the scene diff. This case cannot be done any other
/// way: it has to READ the finished colour buffer and write back a modified copy, and
/// nothing in the scene graph can do that.
///
/// SETUP (one click, and it does not happen by itself)
///   1. Select Assets/Settings/Mobile_Renderer.asset (and PC_Renderer.asset if you use it)
///   2. Add Renderer Feature -> Magnifier Blur Feature
///   3. Assign Assets/CoralPolyps/Runtime/MagnifierRadialBlur.shader to its Shader field
///
/// Step 3 is not optional for a device build. A shader reachable only through Shader.Find
/// is stripped on iOS — the same trap that once silently removed the fullscreen takeover
/// from a build while it worked perfectly in the editor. The Shader.Find fallback below
/// exists so the editor still works if you forget, and it logs loudly when it fires.
///
/// With no MagnifierDefocus in the scene the globals are never set, strength stays 0, and
/// the fragment shader returns the frame untouched on its first branch. Leaving the
/// feature enabled costs one full-screen pass with an early-out; it is not a hazard.
/// </summary>
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace CoralPolyps
{
    public class MagnifierBlurFeature : ScriptableRendererFeature
    {
        [Tooltip("Assign MagnifierRadialBlur.shader. Required for device builds — a shader " +
                 "only reachable via Shader.Find gets stripped on iOS.")]
        public Shader shader;

        [Tooltip("After post-processing, so the blur applies to the graded image rather than " +
                 "being graded after being blurred. The takeover's overlay canvas is composited " +
                 "later still by uGUI, so the emerging footage stays sharp regardless.")]
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        const string ShaderName = "CoralPolyps/MagnifierRadialBlur";

        Material _material;
        BlurPass _pass;

        public override void Create()
        {
            var s = shader != null ? shader : Shader.Find(ShaderName);
            if (s == null)
            {
                Debug.LogError($"[{nameof(MagnifierBlurFeature)}] {ShaderName} not found — " +
                               "assign it on this feature. The radial blur will not run.");
                return;
            }
            if (shader == null)
                Debug.LogWarning($"[{nameof(MagnifierBlurFeature)}] shader found by name, not by " +
                                 "reference. Assign it on the feature or it will be stripped " +
                                 "from the iOS build and the blur will be silently absent.");

            _material = CoreUtils.CreateEngineMaterial(s);
            _pass = new BlurPass(_material) { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;

            // Game/scene views only. A blur on the preview cameras makes the alignment work
            // in AlignCoralWindow impossible to judge, and costs a pass per extra camera.
            var type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }

        class BlurPass : ScriptableRenderPass
        {
            readonly Material _mat;

            public BlurPass(Material mat)
            {
                _mat = mat;
                profilingSampler = new ProfilingSampler("MagnifierRadialBlur");

                // Reading the camera colour means we cannot be rendering straight into the
                // backbuffer — URP has to allocate an intermediate target. Without this the
                // pass is skipped on exactly the mobile configurations that would otherwise
                // take the fast path, which is the worst possible place to lose it.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_mat == null) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;

                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid()) return;

                // Same format and size as the source, no depth: this is a colour-only copy.
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "MagnifierRadialBlur";
                desc.clearBuffer = false;
                desc.depthBufferBits = 0;

                TextureHandle destination = renderGraph.CreateTexture(desc);

                var blit = new RenderGraphUtils.BlitMaterialParameters(source, destination, _mat, 0);
                renderGraph.AddBlitPass(blit, "MagnifierRadialBlur");

                // Hand the blurred copy back as the camera colour so everything downstream
                // (and the final present) picks it up.
                resources.cameraColor = destination;
            }
        }
    }
}
