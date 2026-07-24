#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CoralPolyps
{
    /// <summary>
    /// One-off Phase 0 scaffolding: iOS player settings for the AR build target.
    /// Bundle ID is a placeholder — change it to your real reverse-DNS ID before
    /// distributing/signing (Edit > Project Settings > Player > iOS > Identification).
    /// </summary>
    public static class ProjectSetup
    {
        [MenuItem("Window/CoralPolyps/Apply Phase 0 iOS Settings")]
        public static void ApplyPhase0IOSSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);

            PlayerSettings.iOS.cameraUsageDescription =
                "Used to track the coral print and reveal the AR polyp overlay.";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.thesis.coralpolyps");
            PlayerSettings.iOS.targetOSVersionString = "16.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPadOnly;

            Debug.Log("CoralPolyps: Phase 0 iOS player settings applied " +
                      "(bundle id is a placeholder — update it before signing/distributing).");
        }
    }
}
#endif
