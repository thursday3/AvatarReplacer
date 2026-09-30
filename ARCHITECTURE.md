# AvatarReplacer — Deep Documentation

> **Deep-dive technical documentation for the AvatarReplacer mod.**
> For a quick-start (install, usage, config samples) see [`README.md`](README.md).
> This document explains *what every part does, why it exists, and how the pieces fit together*, based on a full read of the source (v6.0.0 code).

---

## Table of Contents

1. [What this project is](#1-what-this-project-is)
2. [Tech stack & build system](#2-tech-stack--build-system)
3. [Repository layout](#3-repository-layout)
4. [Runtime file layout](#4-runtime-file-layout)
5. [Architecture overview](#5-architecture-overview)
6. [End-to-end lifecycle](#6-end-to-end-lifecycle)
7. [Module deep dives](#7-module-deep-dives)
   - 7.1 [Core — plugin entry, config, cache, hooks](#71-core)
   - 7.2 [Loading — detection, loading, materials](#72-loading)
   - 7.3 [Avatar — the composition root](#73-avatar)
   - 7.4 [Animation — pose transfer](#74-animation)
   - 7.5 [Expressions — face sync](#75-expressions)
   - 7.6 [LookAt — gaze](#76-lookat)
   - 7.7 [Physics — SpringBone](#77-physics)
   - 7.8 [Configuration — profiles & bone maps](#78-configuration)
   - 7.9 [Diagnostics](#79-diagnostics)
   - 7.10 [Editor — shader bundle builder](#710-editor)
8. [Configuration reference](#8-configuration-reference)
9. [In-game controls](#9-in-game-controls)
10. [Key design decisions & invariants](#10-key-design-decisions--invariants)
11. [Quirks, sharp edges & observations](#11-quirks-sharp-edges--observations)
12. [Glossary](#12-glossary)

---

## 1. What this project is

**AvatarReplacer** is a [BepInEx](https://babelsoft.net/apps/bepinex.htm)-style Unity plugin (C#, .NET Framework 4.7.2) for the game **Lethal Fantasy**. The game stars a character class called a *Giantess* (the code's `Giantess` type) that grows and shrinks during play. This mod:

1. Hooks the game's `Giantess.Awake` via Harmony.
2. Loads a **VRM avatar** (VRM 0.x **and** VRM 1.0) from disk at runtime with UniVRM 0.131.0.
3. Matches the VRM's size to the game character, parents it to the `Giantess` (so it inherits growth), and hides the original mesh.
4. Reconstructs everything the original character did, frame by frame:
   - **animation** (full humanoid pose transfer),
   - **facial expressions** (preset + custom + blink + lip-sync),
   - **eye tracking** (with saccades),
   - **SpringBone physics** (hair/skirt/cloth, scale-aware),
   - **shadows, layers, outline width, visibility**.
5. Adds mod conveniences: multi-avatar switching, per-avatar JSON profiles, auto-generated SpringBones for models that ship without them, live shader testing from the config file, and rich diagnostics.

It is *not* a model importer for the game itself — it's a runtime "second rig" that mirrors the game character onto any VRM you drop into a folder.

---

## 2. Tech stack & build system

| Aspect | Value |
|---|---|
| Language | C# (`LangVersion latest`, `AllowUnsafeBlocks`) |
| Target | `net472` (Unity player mono) |
| Mod loader | BepInEx 5 (`BaseUnityPlugin`) + Harmony patching (`0Harmony.dll`) |
| VRM runtime | **UniVRM 0.131.0** — `VRM.dll` (0.x), `VRM10.dll`, `UniGLTF`, `UniGLTF.Utils`, `UniHumanoid`, `MToon`, `VrmLib`, `VRM10.MToon10` (all referenced from local `lib\`) |
| Game refs | `Assembly-CSharp.dll` plus 10 `UnityEngine.*.dll` modules from `LethalFantasy_Data\Managed` |
| Output | `AvatarReplacer.dll` (solution `AvatarReplacer.sln`) |

### Building

The project has **no NuGet packages**; every reference points into an actual game install:

```
dotnet build -p:GameDir="C:\Path\To\Lethal Fantasy"
```

Resolution order for the game folder (`AvatarReplacer.csproj`):

1. `-p:GameDir=...` MSBuild property,
2. `LF_GAME_DIR` environment variable,
3. fallback constant `C:\PUT_YOUR_GAME_PATH_HERE`.

`BepInExCoreDir` defaults to `<GameDir>\BepInEx\core`, with a legacy fallback path check.

### Optional embedded lilToon bundle

If a compiled lilToon AssetBundle exists at `RuntimeShaders\liltoonshader` (or `-p:LilToonBundlePath`), it is embedded into the DLL as a managed resource named `AvatarReplacer.Resources.liltoonshader` and loaded from memory at runtime (see [7.9](#79-diagnostics)). A pre-build step logs the outcome or warns when the bundle is missing.

> **Note:** building requires a real game install; there are no test projects and no CI config in the repo.

---

## 3. Repository layout

```
AvatarReplacer.sln
AvatarReplacer.csproj          # net472, game-relative references, embedded shader bundle logic
README.md                      # user-facing quick start + config docs
ARCHITECTURE.md                # this document

Core/
  AvatarReplacerPlugin.cs      # BepInEx entry point, Harmony hook, load orchestration, cache
  ModLog.cs                    # Logging facade (BepInEx ManualLogSource; Debug gated by config)

Loading/
  VrmDetector.cs               # GLB header + JSON-chunk sniffing → VRM 0.x vs 1.0
  VrmLoader.cs                 # UniVRM loading + SimpleMaterialGenerator + shader selection

Avatar/
  VrmManager.cs                # Per-avatar composition root; attaches all bridges; hotkeys

Animation/
  VrmAnimationBridge.cs        # Humanoid pose transfer, hips/feet anchoring, small-scale fix

Expressions/
  VrmAdapters.cs               # IVrmExpressionAdapter / IVrmHeadAdapter + VRM0/VRM1 impls + Composite
  VrmExpressionBridge.cs       # Game-expression → VRM blendshape mapping & smoothing
  VrmMeshBlendshapeAdapter.cs  # Raw SkinnedMeshRenderer blendshape fallback
  VrmMeshExpressionApplicator.cs # Queued, ordered writer for the mesh adapter
  VrmBoneExpressionFallback.cs # Jaw-bone rotation fallback when no mouth blendshapes

LookAt/
  VrmLookAtBridge.cs           # Manual lifelike gaze: saccades, clamps, 3 application backends

Physics/
  VrmSpringBoneController.cs   # Scale-aware SpringBone runtime support (VRM0 + VRM1)
  AutoSpringBoneGenerator.cs   # Generates VRMSpringBone from bone-name keywords

Configuration/
  VrmAvatarProfile.cs          # Per-avatar JSON profile (scale, offsets, physics overrides)
  BoneMappingConfig.cs         # bone_mapping.json (custom bone name mapping)

Diagnostics/
  VrmDiagnostics.cs            # One-shot avatar summary (heights, rig, expressions)
  VrmShaderCatalog.cs          # Runtime shader registry + embedded/external bundle loading
  VrmShaderTester.cs           # Live, config-driven shader swapping with state restore
  VrmMaterialDiagnostics.cs    # Logs every UniVRM-created material + shader usage counts

Editor/
  BuildVrmShaderBundle.cs      # Unity editor menu: build UniVRM shader AssetBundle
```

---

## 4. Runtime file layout

All paths relative to the game install:

```
Lethal Fantasy/
├─ BepInEx/
│  ├─ core/                                 # BepInEx.dll, 0Harmony.dll
│  ├─ config/
│  │  └─ com.user.avatarreplacer.cfg        # BepInEx config (auto-created; GUID-based name)
│  └─ plugins/
│     └─ AvatarReplacer/
│        ├─ AvatarReplacer.dll              # the mod
│        ├─ vrm/                            # ← drop .vrm files here (scanned recursively? no: flat)
│        │  └─ *.vrm
│        ├─ config/                         # per-avatar profiles (auto-created)
│        │  ├─ _template.json               # copied template on first run
│        │  └─ <AvatarName>.json            # optional per-avatar overrides
│        ├─ bone_mapping.json               # custom bone map (auto-created default)
│        └─ shaders/                        # optional external shader bundle
│           └─ vrm_shaders.bundle
└─ LethalFantasy_Data/Managed/              # game assemblies (reference-only)
```

*Legacy compatibility:* `.vrm` files placed directly in `plugins/AvatarReplacer/` (the plugin root) are also detected, matching older versions of the mod.

---

## 5. Architecture overview

The design is a **hub-and-spoke composition**: one plugin object owns discovery + loading; one `VrmManager` per loaded avatar owns and wires every subsystem onto the avatar's GameObject. No subsystem knows about another except through `VrmManager`.

```
                          BepInEx
                             │
                   ┌─────────▼──────────┐
                   │ AvatarReplacerPlugin│  Core/ — config, Harmony hook,
                   │  (BaseUnityPlugin)  │  VRM discovery, LRU cache, async loads
                   └───┬────────────┬───┘
        Harmony postfix│            │config polling
     on Giantess.Awake │            ▼
                       │       VrmShaderTester (Diagnostics/)
                       ▼
                 ┌─────────────┐        ┌──────────────────┐
                 │ VrmLoader   │───────▶│ VrmDetector      │
                 │ (Loading/)  │        │ VrmShaderCatalog │
                 └──────┬──────┘        └──────────────────┘
                        │ GameObject (VRM root)
                        ▼
              ┌───────────────────┐
              │    VrmManager     │  Avatar/ — composition root
              │  (per VRM root)   │
              └─┬────┬────┬────┬──┘
                │    │    │    └────────────────┐
   ┌────────────▼┐ ┌─▼──────┐ ┌─▼─────────────┐ ┌─▼──────────────┐
   │ VrmAnimation│ │VrmExpr.│ │VrmSpringBone  │ │VrmLookAtBridge │
   │ Bridge      │ │Bridge  │ │Controller     │ │ (LookAt/)      │
   │(Animation/) │ │ + jaw  │ │ + AutoSpring  │ └───────┬────────┘
   └──────┬──────┘ │ fallback│ │BoneGenerator  │         │
          │        │(Expr./) │ │ (Physics/)    │         │
          │        └──┬──────┘ └───────────────┘         │
          │           │                                   │
          │    ┌──────▼─────────────────────────┐         │
          │    │ IVrmExpressionAdapter          │◀────────┘
          │    │  ├ Vrm0ExpressionAdapter       │
          │    │  ├ Vrm1ExpressionAdapter       │
          │    │  ├ VrmMeshBlendshapeAdapter    │ (mesh fallback)
          │    │  └ CompositeVrmExpressionAdapter│
          │    └────────────────────────────────┘
          │
   ┌──────▼───────────────┐
   │ Vrm10Instance /      │   UniVRM owns: humanoid avatar, blendshape
   │ VRMBlendShapeProxy   │   proxies, SpringBone simulation, MToon materials
   └──────────────────────┘
```

**Data flow per frame (LateUpdate ordering):**

```
game Animator (Giantess)
   └─▶ VrmAnimationBridge.LateUpdate
         ├─ muscle smoothing + HumanPoseHandler.SetHumanPose (VRM pose)
         ├─ root (hips) rotation & foot-midpoint position anchoring
         └─▶ VrmSpringBoneController.ProcessAfterAnimationFrame
               └─▶ VRM1 Runtime.Process() / VRM0 springs simulate
                     └─▶ VrmMeshExpressionApplicator.LateUpdate (order 100)
                           └─▶ VrmLookAtBridge.LateUpdate (gaze writes)
```

---

## 6. End-to-end lifecycle

### 6.1 Startup (`AvatarReplacerPlugin.Awake`)

1. Bind **all** BepInEx config entries (see [§8](#8-configuration-reference)).
2. `ModLog.SetLogger(Logger)` — all later logging goes through the facade.
3. `Harmony.CreateAndPatchAll(typeof(AvatarReplacerPlugin))` — installs the `Giantess.Awake` postfix.
4. Create `plugins/AvatarReplacer/config/` and write `_template.json` (per-avatar profile template).
5. `ScanForVrms()`:
   - Destroys every cached avatar and clears the LRU order and profile cache.
   - Scans `plugins/AvatarReplacer/vrm/*.vrm` **and** legacy `plugins/AvatarReplacer/*.vrm`.
   - Tries to keep the previously active avatar by filename; else the configured `VrmFileName`; else index 0; else `-1` (nothing).

### 6.2 Game character spawns (`Giantess.Awake` postfix)

Per `Giantess` instance:

1. **Scene-change detection** — if `Giantess.Awake` fires in a different scene than last time, `ClearCache()` destroys all cached VRMs and resets the shader tester (Unity scene objects die on scene unload, so the cache must go).
2. **Companion skip** — any `Giantess` whose name contains `kiritan` is left alone (the game's companion character uses the same class).
3. The instance becomes `currentGiantessTarget`.
4. **`ScaleOverride`** — if `> 0.0001`, the game character's own `scale` is multiplied and `UpdateScale()` called *before* the VRM loads, so the VRM matches the modified size.
5. A VRM path is chosen (current index in the scanned list, else `VrmFileName` inside the plugin folder).
6. If the plugin is active, `InitializeVrmDelayed` runs as a coroutine waiting **two `WaitForEndOfFrame`** cycles before loading (gives the game's own spawn logic time to finish), else it loads immediately.

### 6.3 Load pipeline (`InitializeVrmAsync`)

Guarded by two independent mechanisms:

- **`loadingVrm` flag** — coarse re-entrancy guard.
- **`loadToken`** — a monotonically increasing token; when the load finishes, if the token is stale (a newer switch/rescan happened) *or* the target `Giantess` changed/destroyed, the freshly loaded GameObject is destroyed and the load aborted. This makes rapid avatar switching safe despite async file IO.

Steps:

1. **Cache hit?** If `loadedVrmCache[path]` exists and is bound to the *same* `Giantess`: deactivate the previous active manager, reactivate the cached one, apply profile position/rotation offsets, touch the LRU order, point the shader tester at it, and return. If bound to a *different* `Giantess`, destroy it and rebuild (avatars are parented to one specific `Giantess`).
2. `VrmLoader.LoadVrmAsync(path)` → `GameObject vrmRoot` (see [7.2](#72-loading)).
3. Load the **per-avatar profile** (`GetOrLoadProfile`, cached per path).
4. **Auto SpringBone** — if `AutoGenerate` is enabled (default **off**: keyword-guessed springs can visibly deform avatars), `AutoSpringBoneGenerator.TryGenerate` runs *before* setup (see [7.7](#77-physics)).
5. **Animator validation** — a humanoid `Animator` must exist on the VRM root or children; otherwise the load aborts and the root is destroyed.
6. **Scale matching** (the core sizing algorithm):
   - `vrmBaseHeight` = VRM bone height (Head Y − average of both feet Y, world space) or, if bones are missing, renderer-bounds height.
   - `sourceBaseHeight` = the `Giantess`'s measured world height **divided by its parent `lossyScale.y`** → a scale-independent local height.
   - `intrinsicRatio = sourceBaseHeight / vrmBaseHeight`; falls back to `Animator.humanScale` ratio when both rigs are humanoid but measurements are implausible.
   - Multiplied by `profile.scaleMultiplier`; if `profile.scaleOverride > 0.01` it replaces the ratio entirely.
   - `vrmRoot.localScale = ratio` → then `SetParent(giantess.transform, false)` with `localPosition = 0`, `localRotation = identity`. **The VRM inherits all future growth/shrink of the Giantess for free.**
7. `vrmRoot.AddComponent<VrmManager>()` → all cached managers deactivated → **LRU eviction** to `MAX_CACHED_AVATARS = 3` (oldest by access order destroyed) → cache registration.
8. Load `bone_mapping.json`, call `VrmManager.Setup(...)` (see [7.3](#73-avatar)), set the shader-tester target.
9. **Shadow sync** — the first original renderer's `shadowCastingMode`/`receiveShadows` are copied to every VRM renderer; `SkinnedMeshRenderer.updateWhenOffscreen = true` (prevents culling artifacts on animated skinned meshes).
10. **Hide the original** — every `Renderer` on the Giantess *not* under the VRM root is disabled; the `Giantess.variation` component is disabled via reflection (`enabled` property by name). The hidden list is stored on the manager for visibility toggling.

### 6.4 Runtime update loops

| Loop | Owner | Purpose |
|---|---|---|
| `Update` (plugin) | `AvatarReplacerPlugin` | Polls `ShaderOverride` config value; on change (and if shader testing enabled) applies it live via `VrmShaderTester`. Intentionally config-driven, no hotkeys. |
| `Update` (per avatar) | `VrmManager` | Hotkeys: prev/next avatar, rescan, diagnostics; Ctrl+1..0 debug expression presets. |
| `Update` (animation) | `VrmAnimationBridge` | Foot-armature IK shadow syncing. |
| `LateUpdate` (animation) | `VrmAnimationBridge` | Pose read from game Animator → smooth → write to VRM → hips anchoring → triggers SpringBone processing. |
| `LateUpdate` (expressions) | `VrmExpressionBridge` / `VrmMeshExpressionApplicator` (order 100) / `VrmLookAtBridge` / `VrmBoneExpressionFallback` | Face, gaze, jaw. |

### 6.5 Teardown & safety

- `SafeDestroyAvatar` first sets `VRMBlendShapeProxy.BlendShapeAvatar = null` (via a try/catch) before `Destroy` — preventing UniVRM update callbacks from throwing against a dying proxy.
- Scene changes trigger full cache invalidation.
- Every cached avatar is destroyed on rescan.

---

## 7. Module deep dives

### 7.1 Core

#### `Core/AvatarReplacerPlugin.cs`

The single BepInEx plugin class. Responsibilities beyond the lifecycle above:

- **Static state hub** — other modules read config through static properties (`IsDebugEnabled`, `SpringBoneStiffnessValue`, `SmallScaleLocomotionFix*Value`, `ShaderOverrideValue`, …). This is deliberate: bridges are `MonoBehaviour`s added dynamically and can't easily receive injected config objects.
- **Avatar switching** — `SwitchAvatar(direction)` wraps the index around the scanned list and calls `InitializeVrm` for the new path while keeping the same `Giantess` target.
- **`ReloadCurrentAvatar`** — re-runs initialization for the current path (used after rescan).
- **`ClearCache(reason)`** — resets the shader tester, destroys all cached avatars, clears profile cache.
- **`GetBoneHeight` / `GetHeight`** — measurement helpers used by scale matching (returns 1.8 m as a last-resort default).
- **`SyncShadowSettings` / `HideOriginalAvatar`** — covered in the lifecycle.

Version note: `PluginInfo.PLUGIN_VERSION = "6.0.0"` in code; the `.csproj` says `5.0.0` and the README title says `5.0` (see [§11](#11-quirks-sharp-edges--observations)).

#### `Core/ModLog.cs`

Tiny facade over `BepInEx.Logging.ManualLogSource`: `Info/Warning/Error/ErrorException` always log; **`Debug` logs only when `DebugMode` is enabled** in config (prefixed `[D]`). Falls back to `UnityEngine.Debug.Log*` when the logger isn't initialized yet. All modules log exclusively through this.

---

### 7.2 Loading

#### `Loading/VrmDetector.cs`

Zero-dependency GLB inspector used to pick the right UniVRM loader:

- Validates the GLB container: magic `glTF` (`0x46546C67`), version 2, declared length ≤ file length.
- Walks chunks until the JSON chunk (`0x4E4F534A`, "JSON"), reads it, and classifies:
  - contains `VRMC_vrm` → **`Vrm10`**,
  - contains `VRM` → **`Vrm0X`**,
  - else `Unknown` (loaders will probe).
- Never throws — errors are surfaced as `result.Error` and the loader falls back to probing both formats.

#### `Loading/VrmLoader.cs`

The heart of import:

1. Runs `VrmDetector`; warns on detection errors but continues.
2. Initializes `VrmShaderCatalog` (shader registry — see [7.9](#79-diagnostics)) and logs diagnostics if `ShaderTesting.Diagnostics` is on.
3. **Selects a conversion shader** via `FindGameRuntimeShader`, in priority order:
   1. the user's configured `ShaderOverride` (if actually loaded),
   2. `KiriToon/Giantess` (the game's own character shader),
   3. any loaded shader whose name contains `KiriToon`,
   4. *last resort:* the shader of any live `SkinnedMeshRenderer` in memory.

   **Why this exists:** Unity player builds strip unreferenced shader assets; `Shader.Find("Standard")`/`Shader.Find("VRM/MToon")` return null even when equivalent shaders are in use. A shader object taken from a *live renderer* is guaranteed to be present and functional.
4. Attempts loading in order: **VRM 1.0** (`UniVRM10.Vrm10.LoadPathAsync`, `canLoadVrm0X:false`, `ImmediateCaller` — synchronous-on-main-thread awaiter) → **VRM 0.x** (`VRM.VrmUtility.LoadAsync` + `ShowMeshes` + `EnableUpdateWhenOffscreen`) → cross-fallbacks if the detector was wrong. Both paths inject the same material generator.
5. Logs material diagnostics and returns the root.

#### `SimpleMaterialGenerator` (`VrmLoader.cs`)

An `IMaterialDescriptorGenerator` that converts glTF PBR data **onto the selected game shader** instead of requiring VRM shaders to exist in the build:

- `baseColorFactor` → `_Color` + `_BaseColor`
- `metallicFactor` → `_Metallic`
- `roughnessFactor` → `1 - roughness` → `_Smoothness`
- `baseColorTexture` → `_MainTex` + `_BaseMap` (via `GltfPbrTextureImporter.TryBaseColorTexture`)
- Unknown/untextured materials → `GetGltfDefault` (white base color).

Deliberately **does not** disable outlines, emission, or other game-shader features — it preserves the shader's own defaults. UniVRM still *owns* material creation; this only reroutes which shader receives the data. The trade-off: authored MToon toon-shading is approximated by the game's toon shader rather than rendered natively.

---

### 7.3 Avatar

#### `Avatar/VrmManager.cs`

Added to the VRM root by the plugin; `Setup(...)` is the **composition root** for everything per-avatar:

1. **Format detection**: `Vrm10Instance` present → VRM 1.0 path (`Vrm1HeadAdapter`, `Vrm1ExpressionAdapter`); else VRM 0.x (`Vrm0HeadAdapter` over the `Animator` + `VRMLookAtHead`, `Vrm0ExpressionAdapter` over `VRMBlendShapeProxy`).
2. **Mesh-expression fallback wrapping**: scans all blendshapes with `VrmMeshBlendshapeAdapter`; if any discovered, adds a `VrmMeshExpressionApplicator` and wraps the primary adapter in a `CompositeVrmExpressionAdapter` so preset writes can fall through to raw mesh blendshapes (e.g. models with non-standard names).
3. Adds and initializes, in order:
   - `VrmAnimationBridge` (pose sync; small-scale fix parameters resolved profile → global),
   - `VrmExpressionBridge` + `VrmBoneExpressionFallback` (jaw),
   - `VrmSpringBoneController` (physics; profile-overridable stiffness/gravity/drag),
   - `VrmLookAtBridge` (only if both head and expression adapters exist),
   - `VrmDiagnostics.LogAvatar` on first setup when `DebugMode` is on.
4. **Layer sync** — the whole VRM hierarchy is forced onto the `Giantess`'s layer (important for game raycasts/camera masks).
5. Caches all renderers and runs `ForceMaterialSync`: sets `_OutlineWidth` via `MaterialPropertyBlock` to `clamp(giantess.scale * 0.01, 0.001, 0.1)` so outlines scale with the character.
6. Runs `VrmManager.Update` forever: hotkeys and Ctrl+1..0 expression debug overrides (delegating to `VrmExpressionBridge.SetDebugOverride`/`ClearDebugOverride`).

Public surface: `BoundGiantess`, `SetVisibility`, `ApplyGlobalPosition/ApplyGlobalOffset` (→ animation-bridge global offsets), `ResetOffsets`, `GetBoneLinks`, `SetOriginalRenderers`, `ToggleAvatarVisibility` (VRM ⇄ original swap; currently not wired to any key).

---

### 7.4 Animation

#### `Animation/VrmAnimationBridge.cs` (~490 lines, the largest module)

Copies the game character's animation onto the VRM every frame. Strategy:

**Initialization**

- VRM 1.0: sets `Vrm10Instance.UpdateType = None` → UniVRM stops self-updating; the bridge drives it manually (see below) so pose writes land before physics.
- Disables the VRM's own `runtimeAnimatorController`; sets `cullingMode = AlwaysAnimate`, `updateMode = Normal`.
- Builds **bone links**:
  - *Humanoid pass*: every `HumanBodyBones` (except `LastBone`) present on both rigs, `isRoot = (bone == Hips)`.
  - *Custom pass*: entries from `bone_mapping.json` matched by **name-contains** on both hierarchies (duplicates skipped).
  - Each link records a **rest-pose offset** at init: `baseRestPoseOffset = Inverse(source.rotation) * target.rotation`, `baseRestPosOffset = source.position - target.position`.
- Creates two `HumanPoseHandler`s (source avatar + target avatar) for muscle-space pose transfer.
- Finds the game's `Armature` components for left/right feet and records their offsets relative to the source feet (these armatures are the game's IK shadow rig).

**Per-frame (`LateUpdate`)**

1. **Muscle transfer**: `GetHumanPose(source)` → every muscle value is lerped toward the target (`Time.deltaTime * effectiveSmooth`, base speed 40, halved when `giantessScale > 50` — smoother at giant scales) → `SetHumanPose(target)`. Muscle-array-length changes are handled defensively.
2. **Root (hips) handling**, before and after the muscle write:
   - Rotation: `target = globalRotOffset * source.rotation * baseRestPoseOffset`, applied with a slerp (speed 5 if scale > 100, else 20, further divided by the small-scale smoothing factor).
   - Position — **foot-midpoint anchoring**: `target.position = sourceFootMid + (target.position − vrmFootMid) + globalPositionOffset`. This preserves the VRM's *internal* hip-to-feet proportion while pinning it to where the game's feet actually are — the mechanism that keeps giant feet planted and strides believable. A second pass re-anchors against a **smoothed** source foot midpoint (speed 10 if scale > 50, else 25). Fallback (no feet found): `source.position − scaled rest offset`.
3. **Small-scale locomotion fix** — at normal "giant" sizes the game's animations read fine on a human-proportioned VRM; at small sizes they look floaty. The bridge computes:
   - `meters = giantess.scale * meterPerScale` (`LootZoneManager.Instance.meterPerScale` when valid, default 30),
   - `strength = (ThresholdMeters − meters) / ThresholdMeters` clamped to [0,1] when below the threshold (default 60 m),
   - `strideFactor = lerp(1, StrideFactorMin=0.75, strength)` — shortens foot-armature offsets (smaller steps),
   - `hipSmoothMultiplier = lerp(1, 1.25, strength)` — *divides* all smoothing speeds for extra-stable hips at small sizes.
4. **`Update` → `SyncFootArmatures`**: repositions the game `Armature` IK-shadow transforms from the *source* feet each frame (`TransformPoint` of the rest offset scaled by `lossyScale.x / initialVrmScale × strideFactor`), so the game's own IK shadow follows along.
5. At the **end** of `LateUpdate`, calls `VrmSpringBoneController.ProcessAfterAnimationFrame()` (or, if that's missing, `Vrm10Instance.Runtime.Process()` directly) — guaranteeing SpringBone runs after the final pose.
6. Debug telemetry every 300 frames (hips pos, scale, heights, small-scale strengths).

`OnDestroy` disposes both `HumanPoseHandler`s.

---

### 7.5 Expressions

The expression stack is a **layered fallback chain**:

```
GiantessExpressionManager (game state)
        │
        ▼
VrmExpressionBridge ──mapping──▶ IVrmExpressionAdapter
                                   ├─ primary: Vrm0/Vrm1 preset adapter
                                   ├─ fallback: VrmMeshBlendshapeAdapter (raw blendshapes)
                                   └─ composite: CompositeVrmExpressionAdapter (both)
        │
        └─▶ VrmBoneExpressionFallback (jaw bone, only if no vowel blendshapes)
```

#### `Expressions/VrmAdapters.cs`

- `IVrmExpressionAdapter`: `ImmediatelySetValue(preset|key, weight)`, `GetClipNames()`, `HasClip(preset)`.
- `Vrm0ExpressionAdapter`: wraps `VRMBlendShapeProxy`; keys via `BlendShapeKey.CreateFromPreset/CreateUnknown`.
- `Vrm1ExpressionAdapter`: wraps `Vrm10Instance.Runtime.Expression`; maps VRM0-era presets to `ExpressionKey` presets (`A→aa`, `I→ih`, `U→ou`, `E→ee`, `O→oh`, `Joy→happy`, `Sorrow→sad`, `Fun→relaxed`, …); string keys resolve known preset names, else become `custom` expressions.
- `IVrmHeadAdapter` / `Vrm0HeadAdapter` / `Vrm1HeadAdapter`: uniform head-bone access for LookAt.
- `CompositeVrmExpressionAdapter`: writes to the primary when it has the preset; **additionally** queues mesh-level writes through the applicator; when the primary has no clips at all, the mesh adapter is the sole writer. Standard key names (`blink`, `aa`, `happy`, `lookleft`, …) bridge the two worlds.

#### `Expressions/VrmExpressionBridge.cs`

Reads game expressions as `(expr.weight × expr.weightMultiplier) / 100`.

- **Default mappings**: a large hardcoded table from game expression keys to VRM presets — eyes (`"happy eyes"`→Neutral, `"sad eyes"`→Sorrow, `"angry eyes"`→Angry, `"smug/shock/screaming eyes"`→Fun, tears, tilt, `"Eyes Bored/Disgust/Empty/Sad 3/Screaming/Sleepy"`…) and mouths (`"happy mouth"`→Joy, `"a/i/u/e/o mouth"`→A/I/U/E/O, `"vore mouth"`→A, `"shock/screaming mouth"`→O, `"Mouth Happy Alt"`, `"Mouth Sad 3"`, `"Mouth Disgust"`→Angry, `"Mouth Empty"`→Neutral, …).
- **Auto-discovery**: reflects *every instance field* of `GiantessExpressionManager` and harvests string keys from string-keyed dictionaries, enumerables, and plain objects (`displayName` / `blendShapeName` / `name`). Unmatched keys are mapped to VRM clips by normalized equality, then substring fuzzy match (≥3 chars). Keys that look like gaze directions (`LookLeft` etc.) are **skipped** — those belong to the LookAt bridge. Eye-ish keys (contain "eyes"/"look") go to the eye custom map; mouth keys to the mouth custom map; everything else defaults to eyes.
- **Per-frame sync (`SyncExpressions`)**:
  1. Zero all target weights; compute `maxLipSyncWeight` over vowel-shaped mouth keys (`a/i/u/e/o`, `"a mouth"`, …).
  2. Merge eye maps (preset + custom) with max().
  3. Mouth presets: non-lip-sync weights are **attenuated by `(1 − maxLipSyncWeight)`** (talking overrides emoting); `Joy` is additionally scaled ×0.4 (full-width happy mouths look manic on most VRMs).
  4. All weights ramp via `MoveTowards` at 15 units/sec; values ≤0.001 are force-zeroed (and logged at debug) so stale expressions can't stick.
- **Blink (`SyncBlink`)**: game `"Blink"` expression smoothed faster (35/sec); routed to `Blink` preset, else the `Blink_L`+`Blink_R` pair, else a fuzzy-found string clip (`blink` / `eyeclose` / `closeeye`).
- **Debug overrides**: `SetDebugOverride(preset | string)` — while active, all other expressions are zeroed and the override is forced (Joy at 0.4). Driven by Ctrl+1..0 in `VrmManager`.

#### `Expressions/VrmMeshBlendshapeAdapter.cs`

A `IVrmExpressionAdapter` that talks **directly to SkinnedMeshRenderers** (for models whose blendshapes aren't registered as VRM clips):

- Registers every blendshape of every SkinnedMeshRenderer under both its full name and its "simple" name (text after the first `_`) — multiple renderers per name supported.
- Classifies each into `Eye` / `Mouth` / `Emotion` / `Unknown` by keyword (emotion names, vowel patterns, eye words, mouth words).
- `ApplyWithPriority(key, value, category)`:
  - Emotion writes above 0.5 suppress eye writes that frame (squinting wins over wide-open eyes).
  - *Strict matching*: normalized equality with category-aware disambiguation — e.g. `blink` must match `blink`/`eyeclose` and respect left/right suffixes; vowels must match `aa/ih/ou/ee/oh`, `mouthA`/`mtha`, `vowelA`-style names; emotions match happy/smile/joy, angry, sad/sorrow families; then a generic containment fallback.
- Weights are written as `Clamp01 × 100`.
- `BeginFrame`/`EndFrame` track what was applied; anything previously above 0.001 that *wasn't* applied this frame **decays** at 8/sec instead of snapping off.

#### `Expressions/VrmMeshExpressionApplicator.cs`

`[DefaultExecutionOrder(100)]` component that queues `(key, weight)` writes and flushes them in its own `LateUpdate` (BeginFrame → apply all → EndFrame). This decouples *who* wants a blendshape from *when* it's written, and gives the decay logic a consistent frame boundary.

#### `Expressions/VrmBoneExpressionFallback.cs`

Last-resort mouth movement for VRMs with **no mouth blendshapes**: rotates the `Jaw` humanoid bone up to 22° based on the game's mouth expressions, using a phoneme open-amount table (`"a mouth"` 0.95, `"o mouth"` 0.8, `"u mouth"` 0.5, `"vore mouth"` 0.9, `"screaming mouth"` 0.9, …), smoothed at 14/sec. **Self-disables** if the VRM has any of A/O/U/I (blendshapes win) or if there is no Jaw bone.

---

### 7.6 LookAt

#### `LookAt/VrmLookAtBridge.cs`

Deliberately **manual and "lifelike"** — it does not hand the VRM to UniVRM's auto gaze solver. Per frame:

1. **Target selection**: `giantess.attentionManager.attentionEyeTransform` (the game's own gaze anchor) → `Camera.main` → head-forward point.
2. **Lifelike behavior**: random **saccades** every 0.5–2.5 s (offset magnitude 0.5) make the gaze feel organic; a glance-away feature exists but is disabled (`GLANCE_CHANCE = 0.0`). The target position itself is lerped at 5/sec.
3. **Angle computation**: yaw/pitch from the head-space direction to the target, clamped to ±60° yaw / ±45° pitch, smoothed at 12/sec.
4. **Application, in priority order**:
   1. VRM 1.0: `Runtime.LookAt.SetLookAtYawPitch(yaw, pitch)` invoked via reflection (radians).
   2. Look blendshapes: `LookLeft`/`LookRight`/`LookUp`/`LookDown` presets (or equivalent string clips) — weight = `(angle / max) × sensitivity(2.0)`, mutually exclusive directions (opposite side zeroed).
   3. Eye-bone fallback: if the model has `LeftEye`/`RightEye` bones but *no* look blendshapes, rotates the eye bones directly (`restRotation * Euler(−pitch, yaw)`).
5. Debug telemetry every 300 frames.

---

### 7.7 Physics

#### `Physics/VrmSpringBoneController.cs`

The header comment states the design intent: *"keeps UniVRM SpringBone aware of runtime avatar scaling — intentionally does not fake physics by changing stiffness, drag, gravity, or collider radii."* Everything is reflection-based to support both UniVRM versions from one codebase:

- **VRM 0.x**: finds all components named exactly `VRMSpringBone`; applies configured `m_stiffnessForce` / `m_gravityPower` / `m_gravityDir` (down) / `m_dragForce`; sets `UseRuntimeScalingSupport = true` via reflection property.
- **VRM 1.0**: resolves `UniVRM10.Vrm10Instance` → `Runtime` → `SpringBone`; captures `Process`, `SetModelLevel`, `ReconstructSpringBone`, `RestoreInitialTransform` `MethodInfo`s; constructs the model-level settings object and sets `SupportsScalingAtRuntime = true`, `StopSpringBoneWriteback = false`, `ExternalForce = zero` (handles both 1- and 2-arg `SetModelLevel` signatures).
- **Per scale frame** (`ProcessAfterAnimationFrame`, called at the end of the animation bridge's `LateUpdate`):
  - Detects `giantess.scale` change; re-runs scaling configuration.
  - **Large changes** (ratio ≥ 1.5× or ≤ 0.667×, throttled to 0.15 s) trigger `ReconstructAfterScaleChange`: VRM0 springs get `ReconstructSpringBone()` (fallbacks: `Setup(bool)` → `Setup()`); VRM1 gets `ReconstructSpringBone()` and — only for **extreme** jumps (≥3× or ≤⅓×) — `RestoreInitialTransform()` to clear velocities (normal growth should keep hair mid-motion).
  - Calls VRM1 `Runtime.Process()` manually (the animation bridge set `UpdateType = None`).
  - Emits diagnostics every 2 s in debug mode (scale, lossy scale, delta ratio, configured states).

#### `Physics/AutoSpringBoneGenerator.cs`

Gives motionless hair/skirts life when a VRM ships without SpringBones:

1. **No-op** if the avatar already has any component whose name contains `SpringBone`, or if a VRM1 runtime reports existing joints (checked by probing `Joints`/`Springs`/`SpringBones` properties). Both guards are bypassed only when `SpringBone.AutoGenerateOnAvatarsWithBones` is enabled.
2. Collects candidate bones by keyword: bones whose lowercase name matches any keyword (config CSV; default: `hair, skirt, tail, cape, cloth, dress, accessory, ribbon, sleeve, belt, twintail, frill, lace, string`), minus an exclusion list of body/face/limb keywords (`body, face, head, eye, arm, hand, leg, spine, chest, hips`, …).
3. Keeps only **top-most** matches (a bone whose parent also matched is skipped), capped at `AutoMaxRoots` (16).
4. Adds a `VRM.VRMSpringBone` (resolved via `Type.GetType("VRM.VRMSpringBone, VRM")`), assigns root bones (`m_rootBones` / `RootBones`), stiffness/gravity/drag from config, `m_hitRadius = 0.02`, `m_center = vrmRoot`.

Runs *before* `VrmManager.Setup`, so the controller discovers the generated springs normally.

---

### 7.8 Configuration

#### `Configuration/VrmAvatarProfile.cs`

Optional per-avatar JSON at `plugins/AvatarReplacer/config/<AvatarName>.json` (loaded by filename without extension, cached per VRM path). Semantics: **negative values mean "inherit global config"**.

| Field | Effect |
|---|---|
| `scaleOverride` | `> 0.01` replaces the computed scale ratio entirely; else ignored |
| `scaleMultiplier` | multiplies the computed ratio (≤ 0.001 treated as 1.0) |
| `positionOffset` / `rotationOffset` | per-avatar transform offsets (fall back to global Transform config when null) |
| `springBoneStiffness` / `springBoneGravityPower` / `springBoneDrag` | `> 0` overrides the global SpringBone section |
| `smallScaleThresholdMeters` / `smallScaleStrideFactorMin` / `smallScaleHipSmoothMultiplier` | `> 0` overrides the locomotion-fix section |

`CreateTemplateProfile` writes `_template.json` once at startup so users can copy it.

#### `Configuration/BoneMappingConfig.cs`

`bone_mapping.json`: a list of `BoneMap { originalBoneName, vrmBoneName, rotationOffset, copyPosition, isHips }` plus `globalRotationOffset` and `useHumanoidMapping`. The loader matches by **name-contains** on both hierarchies for extra bones the humanoid pass misses. Ships with 17 default `J_Bip_*` mappings (standard VRM 0.x bone names) so the file is usable as-is.

> Note: in the current code the bridge consumes only the two name fields and `isHips`; `rotationOffset`, `copyPosition`, `globalRotationOffset`, and `useHumanoidMapping` are declared but not read (see [§11](#11-quirks-sharp-edges--observations)).

---

### 7.9 Diagnostics

#### `Diagnostics/VrmDiagnostics.cs`

`LogAvatar(avatar, giantess, reason)` prints a one-shot block: detected format (VRM1.0 vs 0.x), humanoid presence + `humanScale`, measured height (bone-based, bounds fallback), lossy scale, renderer/blendshape/SpringBone counts, and expression count (VRM1 via reflection over `Runtime.Expression.ExpressionKeys`, VRM0 via `VRMBlendShapeProxy`). Triggered on setup (debug), and manually via the diagnostics hotkey.

#### `Diagnostics/VrmShaderCatalog.cs`

A runtime shader registry solving the "shaders are stripped in player builds" problem:

- **Known list**: `VRM/MToon`, `VRM/Unlit*`, `Standard`, `UniGLTF/UniUnlit`, `VRM_USE_GLTFSHADER`, `VRM10/MToon10`, `VRM10/Universal Render Pipeline/MToon10`, `KiriToon/Giantess`.
- **Refresh** builds the registry from three sources: `Shader.Find` over the known list, **all** `Resources.FindObjectsOfTypeAll<Shader>()`, and every shader used by any live renderer (how `KiriToon/Giantess` is actually found in the shipped game).
- **Render-pipeline detection** (once per session): logs Built-in vs URP and warns that `VRM/MToon` (0.x) has no URP variant — prefer `VRM10/.../MToon10` with a VRM 1.0 file.
- **Bundle loading**, in order:
  1. *Embedded*: streams the `AvatarReplacer.Resources.liltoonshader` manifest resource (the lilToon bundle compiled into the DLL) into memory via `AssetBundle.LoadFromMemory`, and validates it contains Shader assets (a streamed-scene bundle is reported as an error). Confirms with "EMBEDDED SHADER INJECTION CONFIRMED".
  2. *External*: `ShaderTesting.ShaderBundlePath` (relative to the plugin folder) via `AssetBundle.LoadFromFile` — the legacy/development path.
- `Find(name)` checks the cache, then `Shader.Find`, then forces a `Refresh` before giving up. `LogDiagnostics` prints the whole availability table — intentionally at `Info` level *exactly when a configured shader fails to apply*, because that's the moment the user needs it.

#### `Diagnostics/VrmShaderTester.cs`

A **state-preserving shader laboratory** for the active VRM:

- `SetTarget(root)`: captures every VRM material's full state (`MaterialState`: original shader, shader keywords, render queue, instancing, double-sided GI, and every texture/color/float among a curated property list — `_MainTex`, `_BaseMap`, `_BumpMap`, `_EmissionMap`, `_Color`, `_BaseColor`, `_ShadeColor`, `_Metallic`, `_Smoothness`, `_OutlineWidth`, `_ShadeToony`, …), then discovers candidates (all loaded shaders minus `Hidden/` + `Internal/`, with `KiriToon/Giantess` pinned first) and applies the configured override.
- `ApplyConfiguredOverride`: empty value → restore originals; unknown name → *no change* + full availability table; known name → apply to all materials.
- `ApplyIndex` **re-applies the captured properties after every switch** (`RestoreCommonPropertiesBeforeSwitch` + per-material restore) so one incompatible shader can't poison the next test; failures per material are caught and logged.
- `RestoreOriginalMaterials` puts back shader, keywords, queue, instancing, GI, and all captured properties.
- Driven entirely from the plugin's `Update` polling of `ShaderOverride` — edit the config file while playing and the active avatar re-shades live.

#### `Diagnostics/VrmMaterialDiagnostics.cs`

Read-only: logs every renderer/material/shader-name pair on the loaded VRM plus unique-shader usage counts. Useful to confirm what UniVRM's material generator actually produced.

---

### 7.10 Editor

#### `Editor/BuildVrmShaderBundle.cs`

`#if UNITY_EDITOR` menu command (**AvatarReplacer → Build VRM Shader Bundle**): finds all shaders in the open Unity project matching the known UniVRM list, builds them into an AssetBundle (`vrm_shaders`, chunk-based compression, standalone-Windows64 fallback), and copies the result to `BepInEx/plugins/AvatarReplacer/shaders/vrm_shaders.bundle`. This is how the external (and, historically, embedded) shader bundles are produced.

---

## 8. Configuration reference

BepInEx file: `BepInEx/config/com.user.avatarreplacer.cfg` (created on first run).

### `[General]`

| Key | Default | Meaning |
|---|---|---|
| `VrmFileName` | `avatar.vrm` | Fallback/default VRM (filename only; resolved against the plugin folder) |
| `EnableReplacement` | `true` | Master switch; `false` makes the Giantess hook a no-op |
| `HideOriginalMesh` | `true` | Disable original renderers + variation component after replacement |
| `DebugMode` | `false` | Verbose `[D]` logging everywhere (also enables SpringBone/look diagnostics) |

### `[Controls]`

| Key | Default | Meaning |
|---|---|---|
| `PreviousAvatarKey` | `PageUp` | Switch to previous VRM |
| `NextAvatarKey` | `PageDown` | Switch to next VRM |
| `RescanKey` | `F5` | Rescan VRM folder + reload current avatar |
| `DiagnosticsKey` | `F10` | Print full avatar diagnostics |

### `[Transform]`

| Key | Default | Meaning |
|---|---|---|
| `PosX/PosY/PosZ` | `0` | VRM position offset (global) |
| `RotX/RotY/RotZ` | `0` | VRM rotation offset (global, applied to root link) |
| `ScaleOverride` | `0` | Startup multiplier on the Giantess's own scale (0 = no change) — the VRM follows automatically |

### `[SpringBone]`

| Key | Default | Meaning |
|---|---|---|
| `Stiffness` | `5.0` | VRM0 spring stiffness force (override applies **only when changed from default**; authored VRM values are otherwise kept) |
| `GravityPower` | `3.0` | VRM0 spring gravity (same override rule) |
| `Drag` | `1.0` | VRM0 spring drag (same override rule) |
| `AutoGenerate` | `false` | Auto-create springs for avatars without any. Off by default: keyword-guessed chains near the head/body can visibly deform the avatar |
| `AutoGenerateOnAvatarsWithBones` | `false` | Also run the generator on avatars that already have spring bones |
| `AutoKeywords` | CSV (see [7.7](#77-physics)) | Bone-name keywords for auto generation |
| `AutoMaxRoots` | `16` | Cap on generated root bones |

### `[SmallScaleLocomotionFix]`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Master switch for the small-scale compensation |
| `ThresholdMeters` | `60` | Compensation ramps in below this giantess height |
| `StrideFactorMin` | `0.75` | Minimum stride scale at full compensation |
| `HipSmoothMultiplier` | `1.25` | Extra smoothing factor applied at full compensation |

### `[ShaderTesting]`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Config-driven live shader testing |
| `ShaderOverride` | *(empty)* | Exact shader name to force onto VRM materials; empty = UniVRM defaults. Applied live when the file changes |
| `ShaderBundlePath` | *(empty)* | Optional external AssetBundle (relative to plugin dir) providing VRM shaders |
| `Diagnostics` | `true` | Log shader availability when a VRM loads |

### Per-avatar profiles

See [7.8](#78-configuration). Copy `_template.json` → `<AvatarName>.json` (matching the `.vrm` filename) and set only the fields you need; `-1`/`null` inherit globals. Precedence everywhere: **profile value → global value**.

---

## 9. In-game controls

| Input | Action |
|---|---|
| `PageUp` / `PageDown` (or configured keys) | Previous / next VRM (wraps around) |
| `F5` (or configured) | Rescan VRM folder; cached avatars are dropped; current avatar reloads |
| `F10` (or configured) | Full diagnostic dump of the active avatar |
| `Ctrl` + `1..0` | Force a debug expression (Neutral, Joy, Angry, Sorrow, Fun, Blink, LookLeft, LookRight, mouth A, mouth O); release Ctrl to clear |

---

## 10. Key design decisions & invariants

1. **UniVRM owns materials and physics.** The mod never wholesale-replaces materials (it *converts glTF PBR data onto a game shader*) and never fakes spring physics (it configures UniVRM's own springs and rebuilds them on scale changes). This keeps visual/physics behavior authored, and upgrades safe.
2. **Live-renderer shader discovery.** Because player builds strip shader assets, the only trustworthy shader source is one the game is *already using*. The catalog is built from live objects, and the loader prefers `KiriToon/Giantess`.
3. **Parenting over scaling.** The VRM is a child of the `Giantess` with local scale = measured ratio; growth/shrink of the game character propagates for free. The SpringBone controller exists precisely to keep UniVRM's physics sane across that inherited scaling.
4. **Feet anchor the hips.** Hip position is derived from the midpoint of the *source* feet plus the VRM's own hip-to-foot vector — the single most important trick for believable locomotion across a 1000× scale range.
5. **Everything has a fallback.** VRM1 → VRM0 → detection-agnostic probing; preset adapters → mesh blendshape adapter → jaw bone; look blendshapes → eye bones; bone height → bounds height → `humanScale` → 1.8 m.
6. **Cache aggressively, invalidate precisely.** Up to 3 loaded avatars (LRU) make switching instant; scene change and rescan are the only invalidation triggers; stale async loads are aborted via tokens.
7. **Config-driven over key-driven for experiments.** Shader testing has no hotkeys by design — it follows the config file so changes persist and are reproducible; avatar switching (a play-time concern) uses keys.
8. **Reflection at the seams.** Every UniVRM-version-specific API that isn't safely shared (`UseRuntimeScalingSupport`, `SetModelLevel`, `SetLookAtYawPitch`, `Giantess.variation.enabled`, spring bone internals) is reached via reflection with per-call try/catch, so a UniVRM update degrades gracefully instead of hard-crashing.

---

## 11. Quirks, sharp edges & observations

Honest notes from reading the code (not judgments — things to know when working here):

1. **Version inconsistency.** `PluginInfo.PLUGIN_VERSION = "6.0.0"` (code) vs `5.0.0` (`csproj`) vs "AvatarReplacer 5.0" (README title). BepInEx reports the code constant.
2. **README structure naming.** The README describes a `Debug/` folder; the code has `Diagnostics/` (and an `Avatar/` folder the README calls "avatar runtime manager" without listing). The README also shows two different config filenames (`AvatarReplacer.cfg` vs `com.user.avatarreplacer.cfg`); the actual file is GUID-based: `com.user.avatarreplacer.cfg`.
3. **Dead config fields.** `BoneMappingConfig.globalRotationOffset`, `useHumanoidMapping`, `BoneMap.rotationOffset`, and `BoneMap.copyPosition` are declared (and serialized) but never read by the animation bridge. Likewise `VrmManager.ResetOffsets`, `GetBoneLinks`, and `ToggleAvatarVisibility` currently have no callers.
4. **Hardcoded hotkey alternates.** `VrmManager.Update` accepts the configured switch keys *or* literal `PageUp`/`PageDown` — remapping those two keys in config can't actually disable the defaults.
5. **LookAt glancing disabled.** The glance-away behavior is fully implemented but `GLANCE_CHANCE = 0.0` turns it off; saccades remain active.
6. **Companion skip is name-based.** Any `Giantess` whose object name contains `kiritan` (case-insensitive) is never replaced.
7. **Two-frame spawn delay.** Loading waits two `WaitForEndOfFrame` cycles; if the plugin object is inactive at that moment the load happens synchronously instead (slightly different timing semantics).
8. **`GetBoneHeight` default.** When an animator has no Head bone the height defaults to 1.8 m — pathological rigs could scale surprisingly.
9. **Outline width heuristic.** `_OutlineWidth = clamp(scale × 0.01, 0.001, 0.1)` via property block — only meaningful for shaders that expose that property.
10. **`weightMultiplier` contract.** Expression weights assume the game's `(weight × weightMultiplier) / 100` semantics; a game update changing that formula would silently shift all facial intensity.
11. **VRM folder scan is flat.** `Directory.GetFiles(vrmDir, "*.vrm")` — subfolders inside `vrm/` are not scanned.
12. **Cache bound to one Giantess.** A cached avatar reused for a *different* `Giantess` is destroyed and rebuilt rather than re-parented (correct, but means multi-Giantess scenes re-load per character).
13. **No tests.** There are no test projects or CI; correctness currently rests on in-game diagnostics tooling (`F10`, `DebugMode`, shader tables).

---

## 12. Glossary

| Term | Meaning |
|---|---|
| **VRM** | glTF-based avatar format (0.x and 1.0 dialects) with humanoid standardization, blendshape presets, SpringBones, look-at, and MToon materials |
| **UniVRM** | Unity runtime library (v0.131.0 here) that loads/renders VRM files |
| **Giantess** | The game's player character class in Lethal Fantasy; source of animation, expressions, gaze, and scale |
| **GTS scale** | `Giantess.scale` — the game's size scalar, multiplied by `meterPerScale` (≈30) for real-world meters |
| **HumanPoseHandler** | Unity API to read/write an Animator's pose in muscle space, used here to copy animation between rigs |
| **Humanoid muscle** | Normalized −1..1 joint-rotation channels that make cross-rig animation transfer possible |
| **SpringBone** | Verlet-style chain physics UniVRM applies to hair/skirt/tail bones |
| **BlendShapePreset / ExpressionKey** | Standardized blendshape slots (A, I, U, E, O, Blink, Joy, Angry, Sorrow, Fun, LookLeft, …) in VRM 0.x / 1.0 respectively |
| **MToon / MToon10** | VRM's toon shaders (0.x / 1.0 naming); not guaranteed present in a stripped player build |
| **KiriToon/Giantess** | The game's own character shader; the preferred conversion target because it is guaranteed loaded |
| **LRU cache** | The plugin's 3-slot most-recently-used store of loaded VRM instances for instant switching |
| **Harmony patch** | Runtime method hook (here: a postfix on `Giantess.Awake`) installed by BepInEx's Harmony |
| **Armature** | The game's IK-shadow foot component that this mod repositions so ground contact shadows follow the VRM |

---

*Generated from a full source read of all 24 tracked files (23 C# sources + README) — see file map in [§3](#3-repository-layout) for line-level coverage.*
