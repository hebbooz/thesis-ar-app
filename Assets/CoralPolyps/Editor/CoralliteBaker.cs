#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// Editor tool: reads a coral mesh, DETECTS corallite cups from geometry,
    /// and bakes one vetted polyp position per cup into a PolypScatterMap asset.
    ///
    /// Core idea: a corallite is a concave pit. We don't fit polyps to the surface,
    /// we FIND the cups (concave local minima) and drop one polyp into each.
    ///
    /// Open via:  Window > CoralPolyps > Corallite Baker
    ///
    /// Pipeline (matches your 4-step plan):
    ///   1. Concavity per vertex  -> where the cups are
    ///   2. Non-max suppression   -> one point per cup, not a cluster
    ///   3. Normal alignment      -> each polyp faces outward along local normal
    ///   4. Masking               -> reject base/edges/low-confidence cups
    ///   + honest scale/rotation variation baked in
    /// </summary>
    public class CoralliteBaker : EditorWindow
    {
        // ---- Inputs ----
        private Mesh   _mesh;
        private string _outputPath = "Assets/CoralPolyps/PolypScatterMap.asset";

        // ---- Detection tuning ----
        [Tooltip("How concave a vertex must be to count as a cup floor. Higher = only deep pits.")]
        private float _concavityThreshold = 0.12f;

        [Tooltip("Minimum spacing between two polyps, as a fraction of mesh size. Stops clusters in one cup.")]
        private float _minSpacingFrac = 0.015f;

        [Tooltip("Reject cups whose normal points too far downward (the base/underside the coral sits on).")]
        private float _maxDownwardDot = 0.35f;  // reject if normal.y < -this

        [Tooltip("Reject cups within this fraction of the mesh's bottom (the sawn/dead base).")]
        private float _baseCutoffFrac = 0.08f;

        // ---- Variation (honest legibility) ----
        private float _scaleJitter    = 0.25f;  // +/- 25% size variation
        private float _scaleMultiplier = 1.0f;  // global size (slight exaggeration lives here)

        private Vector2 _scroll;
        private string  _status = "";

        [MenuItem("Window/CoralPolyps/Corallite Baker")]
        public static void Open() => GetWindow<CoralliteBaker>("Corallite Baker");

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _mesh = (Mesh)EditorGUILayout.ObjectField(
                new GUIContent("Coral Mesh", "The mesh you PRINT and TRACK. Map and anchor must be the same coral."),
                _mesh, typeof(Mesh), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detection", EditorStyles.boldLabel);
            _concavityThreshold = EditorGUILayout.Slider(
                new GUIContent("Concavity Threshold", "Higher = only deep cups counted."),
                _concavityThreshold, 0.02f, 0.4f);
            _minSpacingFrac = EditorGUILayout.Slider(
                new GUIContent("Min Spacing", "Min gap between polyps (fraction of mesh size)."),
                _minSpacingFrac, 0.005f, 0.05f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Masking (reject bad cups)", EditorStyles.boldLabel);
            _maxDownwardDot = EditorGUILayout.Slider(
                new GUIContent("Underside Reject", "Reject cups facing this far down."),
                _maxDownwardDot, 0f, 1f);
            _baseCutoffFrac = EditorGUILayout.Slider(
                new GUIContent("Base Cutoff", "Reject cups in the bottom fraction of the coral."),
                _baseCutoffFrac, 0f, 0.3f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Variation", EditorStyles.boldLabel);
            _scaleMultiplier = EditorGUILayout.Slider(
                new GUIContent("Global Scale", "Overall polyp size. Slight >1 = honest exaggeration for legibility."),
                _scaleMultiplier, 0.5f, 2f);
            _scaleJitter = EditorGUILayout.Slider(
                new GUIContent("Scale Jitter", "Per-polyp random size variation."),
                _scaleJitter, 0f, 0.6f);

            EditorGUILayout.Space();
            _outputPath = EditorGUILayout.TextField("Output Asset", _outputPath);

            EditorGUILayout.Space();
            GUI.enabled = _mesh != null;
            if (GUILayout.Button("Detect & Bake", GUILayout.Height(32)))
                Bake();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private void Bake()
        {
            var verts   = _mesh.vertices;
            var normals = _mesh.normals;
            var tris    = _mesh.triangles;

            if (normals == null || normals.Length != verts.Length)
            {
                _mesh.RecalculateNormals();
                normals = _mesh.normals;
            }

            var bounds   = _mesh.bounds;
            float meshSize = bounds.size.magnitude;
            float minSpacing = _minSpacingFrac * meshSize;
            float baseY = bounds.min.y + _baseCutoffFrac * bounds.size.y;

            // --- Build vertex adjacency so we can measure local concavity ---
            var neighbours = BuildAdjacency(verts.Length, tris);

            // --- Step 1: concavity score per vertex ---
            // A vertex is concave if it sits BELOW the average plane of its neighbours,
            // measured along its own normal. Cup floors score high; ridges score negative.
            var concavity = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var nbrs = neighbours[i];
                if (nbrs == null || nbrs.Count == 0) { concavity[i] = 0f; continue; }

                Vector3 avg = Vector3.zero;
                foreach (int j in nbrs) avg += verts[j];
                avg /= nbrs.Count;

                // Positive when the vertex is recessed relative to its neighbours (a pit floor).
                Vector3 toAvg = avg - verts[i];
                concavity[i] = Vector3.Dot(toAvg.normalized, normals[i]) *
                               (toAvg.magnitude / meshSize) * 40f; // scaled to a friendly 0..~1 range
            }

            // --- Step 2: gather candidate cup centers (local concavity maxima) ---
            var candidates = new List<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (concavity[i] < _concavityThreshold) continue;
                bool isLocalMax = true;
                foreach (int j in neighbours[i])
                    if (concavity[j] > concavity[i]) { isLocalMax = false; break; }
                if (isLocalMax) candidates.Add(i);
            }

            // Sort strongest first so non-max suppression keeps the best cup in each cluster.
            candidates.Sort((a, b) => concavity[b].CompareTo(concavity[a]));

            // --- Step 3 + 4: non-max suppression + masking, then build corallites ---
            var accepted = new List<PolypScatterMap.Corallite>();
            var acceptedPos = new List<Vector3>();
            float minSpacingSqr = minSpacing * minSpacing;

            foreach (int idx in candidates)
            {
                Vector3 p = verts[idx];
                // Smoothed OUTWARD normal from the surrounding surface, not the single
                // noisy pit-floor vertex — otherwise polyps point every which way and
                // the underside mask (below) can't be trusted.
                Vector3 n = SmoothedNormal(p, minSpacing, verts, normals, bounds.center);

                // MASK: reject undersides
                if (n.y < -_maxDownwardDot) continue;
                // MASK: reject the sawn/dead base
                if (p.y < baseY) continue;

                // Non-max suppression: too close to an already-accepted polyp?
                bool tooClose = false;
                for (int k = 0; k < acceptedPos.Count; k++)
                    if ((acceptedPos[k] - p).sqrMagnitude < minSpacingSqr) { tooClose = true; break; }
                if (tooClose) continue;

                float seed = Random.value;
                float jitter = 1f + Random.Range(-_scaleJitter, _scaleJitter);

                accepted.Add(new PolypScatterMap.Corallite
                {
                    localPosition = p,
                    localNormal   = n.normalized,
                    scale         = minSpacing * 0.5f * _scaleMultiplier * jitter,
                    confidence    = Mathf.Clamp01(concavity[idx]),
                    randomSeed    = seed
                });
                acceptedPos.Add(p);
            }

            // --- Write the asset ---
            var map = ScriptableObject.CreateInstance<PolypScatterMap>();
            map.sourceMeshName = _mesh.name;
            map.sourceBounds   = bounds;
            map.corallites     = accepted;

            var dir = System.IO.Path.GetDirectoryName(_outputPath);
            if (!AssetDatabase.IsValidFolder(dir))
                System.IO.Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(map, _outputPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(map);

            _status = $"Baked {accepted.Count} polyps from {candidates.Count} cup candidates.\n" +
                      $"Mesh: {_mesh.name}  •  size {meshSize:F2}  •  spacing {minSpacing:F3}\n" +
                      $"If you're getting too few/many, adjust Concavity Threshold first.";
        }

        /// <summary>Build a per-vertex list of connected neighbour vertices from the triangle list.</summary>
        private static List<int>[] BuildAdjacency(int vertexCount, int[] tris)
        {
            var adj = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) adj[i] = new List<int>(6);

            void Link(int a, int b)
            {
                if (!adj[a].Contains(b)) adj[a].Add(b);
            }

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Link(a, b); Link(a, c);
                Link(b, a); Link(b, c);
                Link(c, a); Link(c, b);
            }
            return adj;
        }

        /// <summary>
        /// Outward-facing normal for a cup, averaged over the surrounding surface within
        /// <paramref name="radius"/>. A coral scan's per-vertex normals are noisy and the
        /// chosen vertex sits deep in a pit, so its raw normal points along the pit wall.
        /// Averaging the neighbourhood recovers the macro surface direction; we then force
        /// it to point away from the mesh centre (the coral is a convex-ish boulder) so
        /// every polyp grows outward. Falls back to centre-outward if the average cancels.
        /// </summary>
        private static Vector3 SmoothedNormal(Vector3 p, float radius,
            Vector3[] verts, Vector3[] normals, Vector3 meshCenter)
        {
            Vector3 sum = Vector3.zero;
            float r2 = radius * radius;
            for (int i = 0; i < verts.Length; i++)
                if ((verts[i] - p).sqrMagnitude <= r2) sum += normals[i];

            if (sum.sqrMagnitude > 1e-6f)
            {
                Vector3 n = sum.normalized;
                if (Vector3.Dot(n, p - meshCenter) < 0f) n = -n; // keep it outward
                return n;
            }

            Vector3 outward = p - meshCenter;
            return outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
        }
    }
}
#endif
