using UnityEngine;
using UnityEditor;

namespace CoralPolyps.EditorTools
{
    /// <summary>
    /// One-click: write the tuned natural + fluorescent palette and arc settings into
    /// CoralTissue.mat, so they don't have to be typed into the Inspector by hand.
    ///
    /// Colours are authored here as sRGB hex (what you'd eyedropper from a photo) and
    /// converted to LINEAR before being written, because material colours live in linear
    /// space in a linear-colour-space project. Typing hex straight into a .mat file skips
    /// that conversion and is why hand-set colours come out washed-out or muddy.
    ///
    /// Deliberately does NOT touch the Phase-3 look-dev tuning (_ColorSplit, _Depth,
    /// _ReliefStrength, _AOContrast, _FloorGlow, _WallGlow) — those are hand-tuned.
    ///
    /// Menu: Window > CoralPolyps > Apply Coral Palette to Material
    /// </summary>
    public static class ApplyCoralPalette
    {
        private const string MaterialPath = "Assets/CoralPolyps/Coral/CoralTissue.mat";

        [MenuItem("Window/CoralPolyps/Apply Coral Palette to Material")]
        public static void Apply()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Debug.LogError($"[CoralPalette] Material not found at {MaterialPath}");
                return;
            }

            Undo.RecordObject(mat, "Apply Coral Palette");

            // --- Stage 1: natural (daylight-ish living coral, warm golden yellow) ---
            SetSrgb(mat, "_NaturalWallColor",  "#EBD394"); // pale golden cream — raised ridges
            SetSrgb(mat, "_NaturalFloorColor", "#C9A04C"); // golden amber      — valleys / cups

            // --- Stage 2: fluorescent (from actinic-lit reference photography) ---
            // Vivid ORANGE-RED body with NEON GREEN polyp mouths — the classic RFP-body /
            // GFP-mouth combination. Both keep two channels low, so they stay saturated when
            // driven hard instead of clipping to white the way gold/yellow does.
            SetSrgb(mat, "_WallColor",  "#FF4310");        // vivid orange-red — walls / septa
            SetSrgb(mat, "_FloorColor", "#A8FF1F");        // neon green      — cups / mouths

            // --- Arc / intensity ---
            SetFloat(mat, "_NaturalBrightness", 1.0f);  // >1.2 clips the natural stage to white
            SetFloat(mat, "_EmissionStrength",  2.8f);  // orange/green have headroom, so can be driven
            SetFloat(mat, "_FluorPoint",        0.5f);  // peak fluorescence sits mid-arc
            SetFloat(mat, "_FluorPresence",     1.0f);  // runtime recovery suppressor; 1 = normal
            SetFloat(mat, "_Stress",            0.0f);  // rest at natural

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CoralPalette] Applied palette to {MaterialPath} " +
                      $"(colour space: {QualitySettings.activeColorSpace}). " +
                      $"Drag _Stress 0 -> 0.5 -> 1 to check natural -> fluorescent -> bleached.", mat);
        }

        /// <summary>Parse an sRGB hex string and store it in the material's (linear) colour space.</summary>
        private static void SetSrgb(Material m, string prop, string hex)
        {
            if (!m.HasProperty(prop))
            {
                Debug.LogWarning($"[CoralPalette] Shader has no property '{prop}' — skipped. " +
                                 "(If several of these warn, the shader probably failed to compile.)");
                return;
            }
            if (!ColorUtility.TryParseHtmlString(hex, out Color srgb))
            {
                Debug.LogWarning($"[CoralPalette] Could not parse hex '{hex}' for {prop}.");
                return;
            }
            m.SetColor(prop, QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb);
        }

        private static void SetFloat(Material m, string prop, float value)
        {
            if (!m.HasProperty(prop))
            {
                Debug.LogWarning($"[CoralPalette] Shader has no property '{prop}' — skipped.");
                return;
            }
            m.SetFloat(prop, value);
        }
    }
}
