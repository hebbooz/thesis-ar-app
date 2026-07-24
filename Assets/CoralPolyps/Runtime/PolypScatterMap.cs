using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// The baked output of the corallite detection pass.
    /// One entry per detected cup: where the polyp sits, which way it faces,
    /// how big it is, and how confident the detector was.
    ///
    /// This asset is the ONLY thing the runtime needs. Detection never runs on device.
    /// All positions/normals are stored in the coral mesh's LOCAL space, so the map
    /// stays valid no matter where Vuforia places the tracked coral in the world.
    /// </summary>
    [CreateAssetMenu(fileName = "PolypScatterMap", menuName = "CoralPolyps/Scatter Map")]
    public class PolypScatterMap : ScriptableObject
    {
        [Serializable]
        public struct Corallite
        {
            public Vector3 localPosition;   // cup center, in mesh local space
            public Vector3 localNormal;     // outward surface normal at the cup
            public float   scale;           // derived from cup radius (spacing to neighbours)
            public float   confidence;      // 0..1 detection confidence (mask uses this)
            public float   randomSeed;      // per-polyp 0..1 for sway/variation phase
        }

        [Tooltip("Name of the mesh this map was baked from. A safety check against feeding the wrong coral.")]
        public string sourceMeshName;

        [Tooltip("Bounds of the source mesh in local space, for sanity/gizmos.")]
        public Bounds sourceBounds;

        public List<Corallite> corallites = new List<Corallite>();

        public int Count => corallites.Count;
    }
}
