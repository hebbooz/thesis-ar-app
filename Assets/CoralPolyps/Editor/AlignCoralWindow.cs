using UnityEngine;
using UnityEditor;

namespace CoralPolyps.EditorTools
{
    /// <summary>
    /// One-click aligner: snaps the virtual fluorescent coral onto the Vuforia Model
    /// Target's representation mesh so their geometry coincides — no eyeballing cups.
    ///
    /// It works by matching **world render bounds** (position + uniform scale) and
    /// orientation, NOT transform pivots — so it's immune to the recentre/rescale the
    /// Model Target Generator bakes in. The only requirement is that both objects are
    /// the SAME scan (they are: both come from astraea_favistella).
    ///
    /// Usage:
    ///   1. Select the ModelTarget and click "Add Target Representation" — this spawns
    ///      the white print mesh as a child, placed exactly where the print will track.
    ///      SceneBuilder does not create this; it is a Vuforia inspector button.
    ///   2. Window > CoralPolyps > Align Coral To Model Target.
    ///   3. Reference (print)  = "coral-rendering Target Representation" (the white mesh
    ///                            from step 1 — the ROOT of it, not its _primitive_0 child).
    ///      Source (virtual)   = "Coral".
    ///      NB: pick the object named **Coral**, not "astraea_favistella" — SceneBuilder
    ///      instantiates that .obj and renames it (SceneBuilder.cs, instance.name =
    ///      "Coral"). Its "default" child holds the renderer; align the PARENT so the
    ///      MagnifierLayer travels with it.
    ///   4. Align. Then DEACTIVATE the representation GameObject so only the glow shows.
    ///      It is Editor-only calibration scaffolding: the loupe's MeshCollider is added
    ///      to the *Coral* renderer by SceneBuilder, and the occlusion depth mesh is not
    ///      built yet, so nothing at runtime refers to the representation. (An earlier
    ///      revision said "disable the renderer, keep the object" — that described the
    ///      hand-built scene, where the representation was to double as the collider.)
    /// </summary>
    public class AlignCoralWindow : EditorWindow
    {
        [SerializeField] private Transform reference; // Model Target representation (the print)
        [SerializeField] private Transform source;    // astraea_favistella (virtual coral)
        [SerializeField] private bool matchRotation = true;

        [MenuItem("Window/CoralPolyps/Align Coral To Model Target")]
        public static void Open() => GetWindow<AlignCoralWindow>("Align Coral");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Snaps the virtual coral onto the Model Target's representation mesh so their " +
                "geometry coincides — matches world render bounds (position + uniform scale) and " +
                "orientation. Pivot/scale independent; both must be the same scan.\n\n" +
                "1. On ModelTarget, click 'Add Target Representation' (spawns the white print mesh).\n" +
                "2. Reference = 'coral-rendering Target Representation' (that object's ROOT).\n" +
                "3. Source = 'Coral'  (SceneBuilder renames the astraea_favistella .obj to this;\n" +
                "   pick the parent, not its 'default' child, so MagnifierLayer travels with it).\n" +
                "4. Align. Then DEACTIVATE the representation GameObject — it is Editor-only\n" +
                "   calibration scaffolding and nothing runtime refers to it.",
                MessageType.Info);

            reference = (Transform)EditorGUILayout.ObjectField(
                "Reference (print)", reference, typeof(Transform), true);
            source = (Transform)EditorGUILayout.ObjectField(
                "Source (virtual coral)", source, typeof(Transform), true);
            matchRotation = EditorGUILayout.Toggle("Match rotation", matchRotation);

            using (new EditorGUI.DisabledScope(reference == null || source == null || reference == source))
            {
                if (GUILayout.Button("Align Source to Reference"))
                    Align();
            }
        }

        private void Align()
        {
            if (!TryGetWorldBounds(reference, out Bounds refB))
            {
                Debug.LogError("[AlignCoral] Reference has no renderers to measure.");
                return;
            }

            Undo.RecordObject(source, "Align Coral To Model Target");

            // 1. Match orientation. Same mesh + same rotation lines up the contours.
            //    Solve so the SOURCE RENDERER's world rotation equals the reference
            //    renderer's, even if the renderer sits on a child of `source`.
            if (matchRotation)
            {
                Renderer srcRend = source.GetComponentInChildren<Renderer>();
                Renderer refRend = reference.GetComponentInChildren<Renderer>();
                if (srcRend != null && refRend != null)
                {
                    Quaternion childLocal = Quaternion.Inverse(source.rotation) * srcRend.transform.rotation;
                    source.rotation = refRend.transform.rotation * Quaternion.Inverse(childLocal);
                }
            }

            // 2. Uniform scale so the sizes match (measured AFTER the rotation).
            if (TryGetWorldBounds(source, out Bounds srcB0))
            {
                float refSize = Mathf.Max(refB.size.x, refB.size.y, refB.size.z);
                float srcSize = Mathf.Max(srcB0.size.x, srcB0.size.y, srcB0.size.z);
                if (srcSize > 1e-6f)
                    source.localScale *= refSize / srcSize;
            }

            // 3. Translate so the bounds centres coincide (measured AFTER the scale).
            if (TryGetWorldBounds(source, out Bounds srcB1))
                source.position += refB.center - srcB1.center;

            EditorUtility.SetDirty(source);
            Debug.Log($"[AlignCoral] Aligned '{source.name}' onto '{reference.name}'.", source);
        }

        /// <summary>World-space AABB enclosing every (non-particle) renderer under root.</summary>
        private static bool TryGetWorldBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }
    }
}
