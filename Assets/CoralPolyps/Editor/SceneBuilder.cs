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
        const string ScatterMapPath = "Assets/CoralPolyps/PolypScatterMap.asset";
        const string MagnifierShader = "CoralPolyps/MagnifierLoupe";
        const string FullscreenShaderPath = "Assets/CoralPolyps/Runtime/MagnifierFullscreen.shader";

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
            HideTargetRepresentations();

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
                var camData = cam.GetUniversalAdditionalCameraData();
                camData.renderPostProcessing = true;

                // And the depth texture for MagnifierDefocus: Gaussian DoF derives its circle
                // of confusion from _CameraDepthTexture, and Mobile_RPAsset has it off. Left
                // on UsePipelineSettings the blur is a silent no-op — full weight, no error,
                // nothing on screen. Claimed on the camera rather than in the pipeline asset
                // so the cost stays attached to the feature and survives a quality-level
                // change. MagnifierDefocus also asserts this at runtime.
                camData.requiresDepthTexture = true;
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

            // The takeover layer. It sits beside CoralMagnifier and reads the pair the
            // magnifier already resolved, so both beats of the reveal show the same
            // footage at the same blend. Builds its own overlay canvas at runtime.
            var fullscreen = control.AddComponent<FullscreenMagnifier>();

            // Assign the shader as a hard reference, not by name. Shader.Find works in
            // the Editor and then returns null in a player build, because a shader
            // nothing references is stripped — so the takeover would test fine and be
            // silently absent on device, which is exactly how it was first missed.
            var fsShader = AssetDatabase.LoadAssetAtPath<Shader>(FullscreenShaderPath);
            if (fsShader != null) fullscreen.fullscreenShader = fsShader;
            else _manual.Add($"{FullscreenShaderPath} not found — assign Fullscreen Shader " +
                             "on CoralControl by hand, or the takeover will be stripped from the build.");

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

            // THE CORAL GROWS WITH THE POLYPS. Off for a long time because scaling the
            // tracked coral slides it off the print — but that was a property of the
            // ANCHOR, not of scaling: both old anchors were re-derived from the camera
            // each frame, so the scale's fixed point wandered. Anchored on the pinned
            // corallite it does not move at all, and the divergence from the print is
            // zero exactly where the viewer is looking.
            //
            // The arc FINISHES with the takeover so the two arrive together, but STARTS
            // inside it. Matching both ends looks right on paper and is wrong in the eye:
            // the iris begins under 2 mm across and is imperceptible for its first
            // centimetres, while a scale change on the whole coral is visible immediately —
            // so started together the coral swells before anything has opened, and the
            // magnification arrives without its reason.
            proximity.magnifyAnchor = ProximityRevealController.MagnifyAnchor.PinnedCorallite;
            proximity.magnifyStartDistance = 0.12f;   // INSIDE fullscreenStartDistance (0.17)
            proximity.magnifyFullDistance = 0.05f;    // == fullscreenFullDistance
            proximity.maxMagnification = 2.5f;
            proximity.magnifyCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(1f, 1f, 2.5f, 0f));

            // THE ARC. Set here rather than left to the scene, because the numbers are
            // not free parameters — two hard constraints pin them:
            //
            //   1. CONTIGUOUS. The takeover must begin exactly where the loupe finishes.
            //      The previous tuning left 7.5 cm of dead travel between them (loupe
            //      done at 12 cm, takeover starting at 4.5 cm) in which the viewer moved
            //      and nothing changed at all — which teaches them that moving does
            //      nothing, and makes the eventual onset read as an event that happened
            //      TO them rather than something they were driving.
            //
            //   2. FULL COVER BEFORE TRACKING DIES. Vuforia gives up on a ~10 cm Model
            //      Target somewhere around 5 cm, so the takeover has to be total by then
            //      or the dropout happens in plain view. The previous tuning had the
            //      whole takeover arc (4.5 -> 2 cm) sitting INSIDE that range: tracking
            //      was already gone before the footage started covering anything, which
            //      is most of where the strobing at closest range came from. Finishing
            //      at 5.5 cm puts the failure safely behind an opaque screen.
            //
            // The span between start and full IS the emergence. 7.5 cm of travel reads
            // as a movement the hand is making; the 2.5 cm it replaces is a wrist twitch
            // that can only ever read as a cut, however well the mapping is pinned.
            // WHICH END TO SPEND WHEN THE EMERGENCE FEELS TOO FAST.
            //
            // The near end is NOT a free parameter. Full coverage has to be reached before
            // Vuforia drops the target, because that is the whole reason the dropout is
            // invisible. Set fullscreenFullDistance inside the tracking limit and the reveal
            // freezes partway when tracking dies — a frozen 80% iris with a blurred rim, and
            // leaning closer cannot finish it, because there is no measurement left to
            // finish it with. So it sits just outside the ~5 cm give-up point and no nearer.
            //
            // The FAR end is free. Moving fullscreenStartDistance outward lengthens the arc
            // at no cost to anything. That is where to spend, and this revision spends 4 cm
            // there against 5 mm at the near end: 12 cm of takeover travel, up from 7.5.
            //
            // ~5 cm is an estimate from Vuforia's behaviour on a ~10 cm Model Target, not a
            // measurement of THIS print under THIS lighting. The HUD reports `vuforia
            // trk/EXT` beside `cover`: if EXT ever appears before cover reads FULL, the real
            // limit is further out than assumed and this number has to come back up.
            proximity.loupeStartDistance = 0.30f;
            proximity.loupeFullDistance = 0.17f;
            proximity.fullscreenStartDistance = 0.17f;   // == loupeFullDistance, no dead travel
            proximity.fullscreenFullDistance = 0.05f;    // at Vuforia's ~5 cm give-up point

            // ACCELERATING, not S-shaped. The default ease-in-out is slow at both ends and
            // fastest through the middle, which is wrong for a lens: approach should feel
            // like it is drawing you in, gaining rather than easing off.
            //
            // It bites hardest at the START, because this curve also drives the iris world
            // radius (4 mm -> 70 mm). Linear, a tenth of the way along the arc has already
            // nearly tripled the magnified spot — so the first small lean does most of the
            // visible work and the rest of the approach has little left to give. Flat here
            // keeps the opening at roughly one corallite for the first half of the travel,
            // which is also the reading the piece wants: you are looking into a single cup
            // until you commit.
            //
            // Zero outgoing tangent at 0, steep incoming tangent at 1 — a quadratic-ish
            // ease-in. Raise the 2.5 for more bite; it is a plain AnimationCurve, so it can
            // be dragged in the Inspector without touching this file.
            proximity.fullscreenCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(1f, 1f, 2.5f, 0f));

            fullscreen.proximity = proximity;
            proximity.fullscreen = fullscreen;   // so the coral is hidden on REAL coverage

            // The iris has to be able to outgrow the screen at the distance the arc now
            // finishes. At 5.5 cm a 4 cm world radius projects to roughly the corner
            // distance, which leaves the opaque core short of it and the screen edges
            // permanently inside the soft rim. 7 cm clears it with margin.
            fullscreen.irisWorldRadiusEnd = 0.07f;

            // AND THE OPENING MUST FIT IN ONE CUP. The old 0.003 (a 6 mm opening) was
            // justified by "a Goniastrea corallite is roughly 8 mm across" — but the baked
            // map for THIS scan has a median nearest-neighbour pitch of 4.2 mm, so a 6 mm
            // opening straddles two or three cups at the exact moment it is supposed to sit
            // inside one. That was survivable while the centre was a free-aimed cursor; now
            // that the reveal pins to a specific corallite, an opening wider than the cup
            // contradicts the claim it is making.
            fullscreen.irisWorldRadiusStart = 0.0018f;

            // The pin itself. Without the map the loupe silently falls back to the old
            // per-frame raycast, which is the sliding behaviour, so a missing asset is
            // worth a loud line rather than a shrug.
            var scatter = AssetDatabase.LoadAssetAtPath<PolypScatterMap>(ScatterMapPath);
            if (scatter != null) proximity.corallites = scatter;
            else _manual.Add($"{ScatterMapPath} not found — assign a PolypScatterMap to " +
                             "ProximityRevealController, or the polyps will not pin to a cup " +
                             "and the emergence point will slide as the viewer moves.");

            // Everything except the footage falls out of focus as the polyps emerge.
            // Builds its own DoF volume, so there is nothing to wire in the scene.
            var defocus = control.AddComponent<MagnifierDefocus>();
            defocus.proximity = proximity;
            defocus.fullscreen = fullscreen;

            hud.proximity = proximity;
            hud.defocus = defocus;
        }

        /// <summary>
        /// Deactivate Vuforia's Target Representation if it is still switched on.
        ///
        /// It is the white print mesh spawned by "Add Target Representation" purely so
        /// the coral can be aligned against it. Left active it renders at runtime, and
        /// because it occupies the same space as the virtual coral it shows through
        /// every gap in the tissue as flat white patches — which reads as a broken
        /// material rather than as a stray object, so it is diagnosed as anything but
        /// what it is.
        ///
        /// The builder cannot create it (that step is interactive and lives in Vuforia's
        /// own inspector), but it can make sure it is never left on. This is here
        /// because the manual instruction to deactivate it was already missed once.
        /// </summary>
        static void HideTargetRepresentations()
        {
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include))
            {
                if (!go.activeSelf) continue;
                if (!go.name.EndsWith("Target Representation", System.StringComparison.Ordinal)) continue;
                go.SetActive(false);
                Debug.Log($"[SceneBuilder] deactivated '{go.name}' (editor-only alignment aid).");
            }
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
