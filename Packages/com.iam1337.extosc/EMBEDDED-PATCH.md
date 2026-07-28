# This copy of extOSC is patched — read before updating

extOSC 1.21.0 by dr. ext (Vladimir Sigalkin), vendored here as an **embedded
package** rather than pulled from OpenUPM. This is deliberate.

**Why:** extOSC 1.21.0 (March 2025) does not compile on Unity 6000.5.
`Scripts/Editor/OSCHierarchyIcon.cs` called two APIs that Unity 6000.5 promoted
from obsolete-*warning* to obsolete-*error*:

- `EditorApplication.hierarchyWindowItemOnGUI` → CS0619
- `EditorUtility.InstanceIDToObject(int)` → CS0619

The whole `extOSC.Editor` assembly therefore failed to build, which put the
project into Safe Mode. A registry package lives in `Library/PackageCache/`,
which Unity overwrites on every resolve, so it cannot be patched in place.

**Changes from upstream (2026-07-28):**

1. `Scripts/Editor/OSCHierarchyIcon.cs` **deleted** (and its `.meta`). It only
   drew the extOSC icon beside OSC components in the Hierarchy window. Cosmetic;
   nothing depends on it. The custom inspectors are untouched.
2. `Examples~/` **deleted** — Unity ignores `~` folders, so it was 1.7 MB of
   repository weight with no effect on the build.

Nothing else is modified. Runtime code is stock.

**To return to the registry version:** delete this directory, then restore in
`Packages/manifest.json` the `package.openupm.com` scoped registry (scope
`com.iam1337.extosc`) and `"com.iam1337.extosc": "<version>"`. Confirm
`extOSC.Editor` compiles before committing — that is the whole reason this exists.

Context: `docs/CONTROL_INTEGRATION.md` §2. Upstream: https://github.com/Iam1337/extOSC
