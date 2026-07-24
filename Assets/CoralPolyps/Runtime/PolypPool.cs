using System.Collections.Generic;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// Runtime consumer of the baked PolypScatterMap.
    ///
    /// Parent this to your Vuforia Model Target so polyps ride the tracked coral.
    /// It does NOT detect anything — it just reads baked positions and places a
    /// pool of polyp instances into the cups the loupe is currently looking at.
    ///
    /// Wire-up:
    ///   - Put this on a GameObject childed to the Model Target.
    ///   - Assign the baked map + a polyp prefab.
    ///   - Each frame, feed it the loupe center (in this transform's local space)
    ///     and radius via SetLoupe(). The proximity controller drives that.
    /// </summary>
    public class PolypPool : MonoBehaviour
    {
        [Tooltip("Baked output from the Corallite Baker.")]
        public PolypScatterMap map;

        [Tooltip("Your fluorescent polyp prefab (URP emissive shader with a _Stress property).")]
        public GameObject polypPrefab;

        [Tooltip("Max simultaneously-active polyps. Keep small for iPad thermals.")]
        public int poolSize = 300;

        [Tooltip("Global stress 0..1 driving the healthy->bleached arc. Push to the material.")]
        [Range(0f, 1f)] public float stress = 0f;

        [Tooltip("Minimum detection confidence to ever show a polyp (extra mask at runtime).")]
        [Range(0f, 1f)] public float minConfidence = 0.15f;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<int> _activeIndices = new List<int>();
        private MaterialPropertyBlock _mpb;
        private static readonly int StressID = Shader.PropertyToID("_Stress");

        // Loupe state, in this transform's local space.
        private Vector3 _loupeLocalCenter;
        private float   _loupeLocalRadius = -1f; // <0 = loupe closed, show nothing

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(polypPrefab, transform);
                go.SetActive(false);
                _pool.Add(go);
            }
        }

        /// <summary>Called by the proximity controller. Center/radius in THIS transform's local space.</summary>
        public void SetLoupe(Vector3 localCenter, float localRadius)
        {
            _loupeLocalCenter = localCenter;
            _loupeLocalRadius = localRadius;
        }

        /// <summary>Loupe fully closed — hide everything.</summary>
        public void CloseLoupe() => _loupeLocalRadius = -1f;

        private void LateUpdate()
        {
            if (map == null || _pool.Count == 0) return;

            // Decide which baked corallites fall inside the loupe this frame.
            _activeIndices.Clear();
            if (_loupeLocalRadius > 0f)
            {
                float rSqr = _loupeLocalRadius * _loupeLocalRadius;
                var cups = map.corallites;
                for (int i = 0; i < cups.Count && _activeIndices.Count < poolSize; i++)
                {
                    if (cups[i].confidence < minConfidence) continue;
                    if ((cups[i].localPosition - _loupeLocalCenter).sqrMagnitude <= rSqr)
                        _activeIndices.Add(i);
                }
            }

            // Assign pooled instances to the active cups; deactivate the rest.
            for (int p = 0; p < _pool.Count; p++)
            {
                if (p < _activeIndices.Count)
                {
                    var cup = map.corallites[_activeIndices[p]];
                    var go = _pool[p];
                    if (!go.activeSelf) go.SetActive(true);

                    var tr = go.transform;
                    tr.localPosition = cup.localPosition;
                    // Align "up" of the polyp to the surface normal so it grows out of the cup.
                    tr.localRotation = Quaternion.FromToRotation(Vector3.up, cup.localNormal)
                                       * Quaternion.Euler(0f, cup.randomSeed * 360f, 0f); // random yaw
                    tr.localScale = Vector3.one * cup.scale;

                    // Push the shared stress parameter without breaking instancing.
                    var rend = go.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        rend.GetPropertyBlock(_mpb);
                        _mpb.SetFloat(StressID, stress);
                        rend.SetPropertyBlock(_mpb);
                    }
                }
                else if (_pool[p].activeSelf)
                {
                    _pool[p].SetActive(false);
                }
            }
        }

#if UNITY_EDITOR
        // Draw baked cups in the editor so you can eyeball detection quality before running.
        private void OnDrawGizmosSelected()
        {
            if (map == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (var c in map.corallites)
            {
                Gizmos.color = Color.Lerp(Color.red, Color.cyan, c.confidence);
                Gizmos.DrawSphere(c.localPosition, c.scale * 0.3f);
                Gizmos.DrawRay(c.localPosition, c.localNormal * c.scale);
            }
        }
#endif
    }
}
