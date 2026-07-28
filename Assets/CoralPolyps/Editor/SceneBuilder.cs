#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoralPolyps
{
    /// <summary>
    /// Builds the AR scene from code, and saves it.
    ///
    /// WHY THIS EXISTS. The July scene was lost because the wiring only ever existed
    /// inside the Editor and was never in a tracked file: `SampleScene.unity` is the
    /// stock scene in every commit, and the build list still pointed at a Vuforia
    /// sample scene that had been deleted. Hand-wiring it again would reproduce
    /// exactly that fragility. With the wiring here instead, it is diffable,
    /// reviewable in a PR, and reproducible after any future loss — and the saved
    /// scene becomes an artefact of this script rather than the only copy of the
    /// knowledge.
    ///
    /// It is idempotent in the sense that matters: it always builds a fresh scene and
    /// overwrites the target path, so running it twice gives the same result.
    ///
    /// WHAT IT CANNOT DO. Two steps are genuinely interactive and are reported at the
    /// end rather than faked:
    ///   * choosing the Model Target's database/target in the inspector, and
    ///   * "Add Target Representation" + Window > CoralPolyps > Align Coral To Model
    ///     Target, which is a visual alignment against the print.
    /// Everything else — object graph, components, references, materials, volume — is
    /// constructed here.
    ///
    /// CONSEQUENCE OF THE FULL PREFAB UNPACK — read before retraining anything.
    /// The coral is unpacked completely so that all wiring serializes into the scene
    /// file (that is what makes it diffable, and it is the whole reason this script
    /// exists). The cost is that there is NO PREFAB LINK left: the saved scene is a
    /// SNAPSHOT. Nothing upstream propagates into it — in particular, **retraining the
    /// `coral-rendering` Model Target database will not update the saved scene**, and
    /// the coral's alignment will still be the one fitted to the OLD target
    /// representation, which is the failure that matters because it is invisible until
    /// the overlay is off on device.
    ///
    /// The recovery is to RE-RUN THIS SCRIPT and redo the two interactive steps above.
    /// That is cheap now that the builder exists — but only if whoever retrains the
    /// database knows to, which is exactly the class of knowledge that was lost with
    /// the July scene. If you retrain, re-run.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/CoralAR.unity";
        const string CoralModelPath = "Assets/CoralPolyps/Coral/astraea_favistella.obj";
        const string CoralMaterialPath = "Assets/CoralPolyps/Coral/CoralTissue.mat";
        const string MagnifierMaterialPath = "Assets/CoralPolyps/Coral/MagnifierLoupe.mat";
        const string BloomProfilePath = "Assets/CoralPolyps/CoralBloomProfile.asset";
        const string MagnifierShader = "CoralPolyps/MagnifierLoupe";

        static readonly List<string> _manual = new List<string>();

        [MenuItem("Window/CoralPolyps/Rebuild AR Scene")]
        public static void RebuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild AR scene?",
                    $"This creates a fresh scene and saves it to {ScenePath}, replacing any file " +
                    "already there, and points the build list at it.\n\n" +
                    "Any unsaved changes in the open scene will be lost.",
                    "Rebuild", "Cancel"))
                return;

            _manual.Clear();

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject arCamera = CreateVuforia("GameObject/Vuforia Engine/AR Camera", "ARCamera");
            GameObject modelTarget = CreateVuforia("GameObject/Vuforia Engine/Model Target", "ModelTarget");

            GameObject coralRoot = BuildCoral(modelTarget);
            Renderer coralRenderer = coralRoot != null ? coralRoot.GetComponentInChildren<MeshRenderer>() : null;
            Renderer magnifierRenderer = coralRenderer != null ? BuildMagnifierLayer(coralRenderer) : null;

            BuildLighting();
            BuildControl(arCamera, coralRenderer, magnifierRenderer);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            SetBuildSceneList();
            Report();
        }

        // ------------------------------------------------------------------ Vuforia

        /// <summary>
        /// Create a Vuforia object through Vuforia's own menu item rather than by
        /// referencing its types. That keeps this assembly free of a Vuforia
        /// dependency, and — more usefully — gets whatever wiring Vuforia's creation
        /// path does today instead of a reimplementation that rots when the package
        /// updates. Falls back to a named placeholder so the rest of the scene still
        /// builds and the gap is reported instead of throwing.
        /// </summary>
        static GameObject CreateVuforia(string menuPath, string fallbackName)
        {
            Selection.activeGameObject = null;
            bool ok = EditorApplication.ExecuteMenuItem(menuPath);
            GameObject created = Selection.activeGameObject;

            if (ok && created != null)
            {
                Debug.Log($"[scene] created {created.name} via \"{menuPath}\"");
                return created;
            }

            var placeholder = new GameObject(fallbackName);
            _manual.Add($"\"{menuPath}\" did not run — '{fallbackName}' is an EMPTY PLACEHOLDER. " +
                        "Add the real Vuforia object and re-parent the coral under it.");
            return placeholder;
        }

        // --------------------------------------------------------------------- Coral

        // ─────────────────────────────────────────────────────────────────────
        // Measured alignment of Coral onto the Model Target's representation.
        //
        // Captured 2026-07-28 from a hand-checked align (Window > CoralPolyps >
        // Align Coral To Model Target, then rotation corrected by eye against the
        // corallites). LOCAL to ModelTarget.
        //
        // These are constants of the **coral-rendering database**, not of the mesh:
        // the .obj carries one baked orientation and Vuforia's Model Target Generator
        // re-centres and re-orients its own copy, so the offset between them is fixed
        // for as long as that database is. Baking it here is what makes a rebuild
        // reproduce the alignment instead of restarting it by hand — the scene was
        // lost once already (see docs/PROGRESS.md).
        //
        // ⚠️ RETRAINING THE MODEL TARGET DATABASE INVALIDATES THESE. It will not
        // announce itself: the coral will simply sit wrong. If you regenerate
        // `coral-rendering`, re-run the align tool and replace these three lines.
        // The rotation is not a clean 90° multiple and is not meant to be — it is the
        // product of two independent baked orientations.
        static readonly Vector3 AlignPosition = new Vector3(-0.1212f, 0.1312f, 0.0431f);
        static readonly Vector3 AlignRotation = new Vector3(304.5564f, 247.5180f, 259.5213f);
        static readonly Vector3 AlignScale    = new Vector3(1.37f, 1.37f, 1.37f);

        static void ApplyMeasuredAlignment(Transform coral)
        {
            coral.localPosition = AlignPosition;
            coral.localEulerAngles = AlignRotation;
            coral.localScale = AlignScale;
        }

        static GameObject BuildCoral(GameObject modelTarget)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CoralModelPath);
            if (model == null)
            {
                _manual.Add($"Coral mesh not found at {CoralModelPath} — no coral in the scene.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            // Unpack so every component and reference below is serialized into the
            // scene file itself. A model-prefab instance would keep the wiring as
            // overrides, which is precisely the un-diffable state we are escaping.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Coral";
            if (modelTarget != null)
            {
                instance.transform.SetParent(modelTarget.transform, false);
                ApplyMeasuredAlignment(instance.transform);
            }

            var renderer = instance.GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                _manual.Add("The coral model has no MeshRenderer — check the .obj import.");
                return instance;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(CoralMaterialPath);
            if (material != null) renderer.sharedMaterial = material;
            else _manual.Add($"{CoralMaterialPath} not found — the coral has no tissue material.");

            // The loupe centres on a ray through the screen centre; without a collider
            // it falls back to the nearest point on the bounds.
            if (renderer.GetComponent<MeshCollider>() == null)
                renderer.gameObject.AddComponent<MeshCollider>();

            return instance;
        }

        /// <summary>
        /// The magnifier layer: a duplicate of the coral mesh carrying
        /// MagnifierLoupe.shader. Parented to the coral mesh with an IDENTITY local
        /// transform on purpose — that makes its object space identical to the coral's,
        /// which is what the shader's object-space projection depends on, and it
        /// inherits the magnification scale for free.
        /// </summary>
        static Renderer BuildMagnifierLayer(Renderer coralRenderer)
        {
            var filter = coralRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                _manual.Add("Coral has no MeshFilter — magnifier layer not created.");
                return null;
            }

            var go = new GameObject("MagnifierLayer");
            go.transform.SetParent(coralRenderer.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = LoadOrCreateMagnifierMaterial();
            return renderer;
        }

        static Material LoadOrCreateMagnifierMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MagnifierMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(MagnifierShader);
            if (shader == null)
            {
                _manual.Add($"Shader \"{MagnifierShader}\" not found — magnifier layer has no material.");
                return null;
            }

            var material = new Material(shader) { name = "MagnifierLoupe" };
            AssetDatabase.CreateAsset(material, MagnifierMaterialPath);
            Debug.Log($"[scene] created {MagnifierMaterialPath}");
            return material;
        }

        // ------------------------------------------------------------------ Lighting

        static void BuildLighting()
        {
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Bloom is not decoration: the tissue shader's emission only reads as GLOW
            // because of it. Without this volume the fluorescent stage looks like flat
            // bright paint.
            var volumeGO = new GameObject("Global Volume");
            var volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = LoadOrCreateBloomProfile();
        }

        static VolumeProfile LoadOrCreateBloomProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BloomProfilePath);
            if (existing != null) return existing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, BloomProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.0f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.6f;
            bloom.hideFlags = HideFlags.HideInHierarchy;   // as Unity's own profile editor does
            AssetDatabase.AddObjectToAsset(bloom, profile);

            EditorUtility.SetDirty(profile);
            Debug.Log($"[scene] created {BloomProfilePath} (Bloom threshold 1.0, intensity 0.6)");
            return profile;
        }

        // ------------------------------------------------------------------- Control

        static void BuildControl(GameObject arCamera, Renderer coralRenderer, Renderer magnifierRenderer)
        {
            Camera cam = arCamera != null ? arCamera.GetComponentInChildren<Camera>() : null;
            if (cam != null)
            {
                cam.tag = "MainCamera";
                // Post-processing must be on for the bloom volume to do anything.
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }
            else
            {
                _manual.Add("No Camera found under the AR camera object — assign one and enable " +
                            "Post Processing on it, or the glow will not read.");
            }

            // One object owning the installation link, so "is this app talking to the
            // server?" is answerable by selecting a single thing in the hierarchy.
            var control = new GameObject("CoralControl");
            var listener = control.AddComponent<CoralOscListener>();

            var appearance = control.AddComponent<CoralAppearance>();
            appearance.listener = listener;
            appearance.coralRenderer = coralRenderer;

            var magnifier = control.AddComponent<CoralMagnifier>();
            magnifier.listener = listener;
            magnifier.magnifierRenderer = magnifierRenderer;

            var hud = control.AddComponent<CoralHud>();
            hud.listener = listener;
            hud.appearance = appearance;
            hud.magnifier = magnifier;

            if (coralRenderer == null) return;

            // The controller lives on the coral, as it did before: it measures to the
            // coral's bounds and drives that transform.
            var proximity = coralRenderer.gameObject.AddComponent<ProximityRevealController>();
            proximity.coralRenderer = coralRenderer;
            proximity.cam = cam;
            proximity.coralCollider = coralRenderer.GetComponent<MeshCollider>();
            proximity.loupeTargets = magnifierRenderer != null
                ? new[] { magnifierRenderer }
                : new Renderer[0];

            // Registration beats zoom: scaling the tracked coral slides it off the
            // print. Left at 1 deliberately — raise it only as a considered trade.
            proximity.maxMagnification = 1f;
        }

        // -------------------------------------------------------------------- Output

        static void SetBuildSceneList()
        {
            // The old list pointed at Assets/SamplesResources/Scenes/0-Main.unity, which
            // no longer exists — a build would have shipped whatever Unity fell back to.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[scene] build list set to {ScenePath}");
        }

        static void Report()
        {
            Debug.Log($"[scene] built and saved {ScenePath}");

            if (_manual.Count == 0)
            {
                Debug.Log("[scene] REMAINING MANUAL STEPS:\n" +
                          "  1. Select ModelTarget -> choose the 'coral-rendering' database and target.\n" +
                          "  2. On ModelTarget click 'Add Target Representation'.\n" +
                          "  3. Alignment is ALREADY APPLIED from the measured constants at the " +
                          "top of SceneBuilder.cs — you should not need the align tool. VERIFY it: " +
                          "the coral's cups should sit in the representation's, checked at the RIM " +
                          "and from a profile view. If it is off, the database was retrained — re-run " +
                          "Window > CoralPolyps > Align Coral To Model Target and update those constants.\n" +
                          "     Then DEACTIVATE the 'coral-rendering Target Representation' GameObject " +
                          "— Editor-only calibration scaffolding. (The loupe's MeshCollider is on the " +
                          "Coral renderer, so nothing at runtime refers to the representation.)\n" +
                          "  4. Play, or build to device.");
                return;
            }

            Debug.LogWarning("[scene] built WITH GAPS — these need doing by hand:\n  - " +
                             string.Join("\n  - ", _manual) +
                             "\n  ...then the Model Target database/representation/alignment steps.");
        }
    }
}
#endif
