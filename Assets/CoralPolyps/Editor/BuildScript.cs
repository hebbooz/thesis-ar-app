#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// Headless iOS build entry point for Phase 0 verification. Invoke via:
    ///   Unity -batchmode -quit -projectPath . -executeMethod CoralPolyps.BuildScript.BuildiOS
    /// Builds the default scene list to Builds/iOS as an Xcode project.
    /// </summary>
    public static class BuildScript
    {
        /// <summary>
        /// Phase 0 fix: collapse the URP/Lit fragment shader variant space by turning
        /// off render features this project doesn't use. Without this, the fragment
        /// pass enumerates ~72 billion variants and crashes the build before stripping.
        /// Safe here: polyps use a custom emissive shader; the coral is depth-only.
        /// </summary>
        public static void ReduceShaderVariants()
        {
            string[] urpAssets =
            {
                "Assets/Settings/Mobile_RPAsset.asset",
                "Assets/Settings/PC_RPAsset.asset",
            };

            // Boolean feature toggles to disable (each removes keyword dimensions).
            string[] boolsOff =
            {
                "m_SupportsHDR",
                "m_MainLightShadowsSupported",
                "m_AdditionalLightShadowsSupported",
                "m_SoftShadowsSupported",
                "m_SupportsMixedLighting",
                "m_ReflectionProbeBlending",
                "m_ReflectionProbeBoxProjection",
                "m_UseRenderingLayers",
                "m_SupportsLightCookies",
                "m_SupportsTerrainHoles",
            };

            foreach (var path in urpAssets)
            {
                var obj = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
                if (obj == null) { Debug.LogWarning($"[ReduceShaderVariants] missing {path}"); continue; }

                var so = new SerializedObject(obj);
                int changed = 0;

                foreach (var name in boolsOff)
                {
                    var p = so.FindProperty(name);
                    if (p != null && p.propertyType == SerializedPropertyType.Boolean && p.boolValue)
                    {
                        p.boolValue = false; changed++;
                    }
                }

                // Additional lights: PerPixel(1)/PerVertex(2) -> Disabled(0). Biggest single win.
                var addl = so.FindProperty("m_AdditionalLightsRenderingMode");
                if (addl != null && addl.intValue != 0) { addl.intValue = 0; changed++; }

                // Adaptive Probe Volumes add keyword dimensions; force legacy light probes (0).
                var lps = so.FindProperty("m_LightProbeSystem");
                if (lps != null && lps.intValue != 0) { lps.intValue = 0; changed++; }

                // Single shadow cascade (fewer cascade keywords).
                var cascades = so.FindProperty("m_ShadowCascadeCount");
                if (cascades != null && cascades.intValue > 1) { cascades.intValue = 1; changed++; }

                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[ReduceShaderVariants] {path}: {changed} settings reduced.");
            }

            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[ReduceShaderVariants] Done. URP feature set minimized for iOS build.");
        }

        /// <summary>
        /// Phase 0 fix: the QualitySettings reset to Unity stock defaults during the
        /// version upgrade, leaving NO URP pipeline asset assigned. Without an active
        /// URP asset, shader "settings filtering" can't strip variants and URP/Lit
        /// enumerates ~72B variants (build crash). Assign the reduced Mobile asset as
        /// the active render pipeline everywhere so stripping has a config to read.
        /// </summary>
        public static void AssignRenderPipeline()
        {
            var mobile = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                "Assets/Settings/Mobile_RPAsset.asset");
            if (mobile == null) { Debug.LogError("[AssignRenderPipeline] Mobile_RPAsset not found."); EditorApplication.Exit(1); return; }

            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = mobile;

            int levels = QualitySettings.names.Length;
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = mobile;
            }

            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[AssignRenderPipeline] Mobile_RPAsset assigned as default + across {levels} quality levels.");
        }

        public static void BuildiOS()
        {
            var scenes = System.Array.ConvertAll(
                EditorBuildSettings.scenes,
                s => s.path);

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None
            };

            Debug.Log($"[BuildScript] Starting iOS build with {scenes.Length} scene(s).");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;

            Debug.Log($"[BuildScript] Result: {summary.result}, " +
                      $"errors: {summary.totalErrors}, warnings: {summary.totalWarnings}, " +
                      $"time: {summary.totalTime}, size: {summary.totalSize} bytes");

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
