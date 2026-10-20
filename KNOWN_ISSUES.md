# Known Issues — Avatar Replacer

Status after the 2026-09-22 fix pass, revised after verification against the
decompiled game source (Assembly-CSharp). "Fixed" items describe what changed
and how to verify it; "Open" items are real, still-present bugs with diagnosis
and workarounds. Config keys mentioned below live in
`BepInEx/config/com.user.avatarreplacer.cfg` unless noted otherwise.

---

## 0. Verification against the game (ground truth)

The mod was cross-checked against the decompiled game code:

- **Expression names verified complete.** The game's expressions are exactly:
  eyes — `happy, serious, sad, angry, hurt, smug, shock, vore, shy, embarrased,
  laugh, cry, frown, screaming, mad, neutral tilt`; mouths — the same set plus
  `smile, a, i, u, e, o`; specials — `Blink, tilt, anger small, anger large,
  tears sad, tears laugh`. The mod's default maps cover all of them
  (`eatendeyes`/`eatendmouth` are AI *messages* that clear the vore override,
  not expressions). Several mod default entries (`Mouth Happy Alt`,
  `Eyes Sleepy`, …) match no game expression — dead but harmless.
- **Weights are 0–100**, eye expressions carry
  `weightMultiplier = (100 − blink)/100`; the mod's
  `(weight × weightMultiplier)/100` read is correct. The game writes its own
  blendshapes in `ExpressionManager.LateUpdate` — the mod only reads the
  weights, so there is no write conflict.
- **API surface verified**: `Giantess.animator/expressionManager/
  attentionManager.attentionEyeTransform/variation/scale/UpdateScale()`,
  `LootZoneManager.Instance.meterPerScale` (default 30 = mod's default),
  `Armature.humanBodyBones`, and `TryGetExpression` all exist as used.
- **Fixed while verifying**: `SyncBlink` used `GetExpression("Blink")`, which
  *throws* `KeyNotFoundException` for missing keys (the game's dictionary
  indexer) — every other call site already used `TryGetExpression`. Now
  `TryGetExpression` everywhere.

---

## 0.1 Custom expression template map (feature)

`config/expression_map.<avatar>.json` (per-VRM) and `config/expression_map.json`
(shared) are **visible maps** of how the mod drives your VRM's face. Every
expression the mod can drive is listed — including empty and unused rows —
with two reference columns:

- `gameExpression` — the giantess expression (do not edit these names).
- `defaultVrm` — the built-in default preset the mod uses when you don't
  override. This is the same table the runtime reads, so a generated file
  shows **exactly** what the mod does.
- `vrmBlendShape` — what is actually driven. **Empty = use `defaultVrm`.
  Filled = override.** To customize, just fill in a blendshape name.

- **Missing keys**: a newer build knows more expressions than an older file?
  Missing rows are appended automatically at load/scan; nothing else changes.
- **Stale skeleton** (a `{ "targetVrm": ... }` file with zero mappings rows,
  the broken output of a previous build): regenerated as a full visible
  template. Files with real rows are never rewritten, byte for byte.
- **Feature OFF** while every `vrmBlendShape` is empty: the runtime uses the
  same default table plus its auto-discovery of model-specific shapes.
- **Any row filled** → feature ON; each filled row **replaces** the default
  and auto-discovered mapping for that game expression.
- **Per-VRM files**: `expression_map.<avatarName>.json`, `targetVrm` guarded,
  case-insensitive. A filled per-VRM map replaces the shared map for that
  avatar; an empty per-VRM template falls through to the shared map.
- **Auto-generation at scan time**: every VRM scan creates the full visible
  template for each model found in the `vrm/` folder.
  **Existing files are never overwritten or deleted**; a new model only gains
  its own file, and user-edited maps keep every value.

File format (as auto-created — every row visible, all overrides empty):

```json
{
    "targetVrm": "avatar1",
    "mappings": [
        { "gameExpression": "happy eyes", "defaultVrm": "Neutral", "vrmBlendShape": "" },
        { "gameExpression": "hurt eyes", "defaultVrm": "Sorrow", "vrmBlendShape": "blendShape1.Eyelid_Sad_L" },
        { "gameExpression": "Blink", "defaultVrm": "", "vrmBlendShape": "" },
        { "gameExpression": "a mouth", "defaultVrm": "A", "vrmBlendShape": "aa" }
        ...
    ]
}
```

- To find blendshape names, open your `.vrm` in **Blender**
  (Object Data Properties → Shape Keys) and copy the names you want.
  Standard VRM preset names also work (`happy`, `angry`, `sad`,
  `aa/ih/ou/ee/oh`, `blink`, …) — those are exactly what `defaultVrm`
  contains.
- Mapping the special key `"Blink"` reroutes the blink pipeline itself.
- Unknown target names are logged and skipped at load; the row does not
  break the rest of the map. The map is read when the avatar loads —
  edit, then reload the avatar/scene.

## 0.2 In-game log findings (2026-09-25 run)

Diagnoses from the player's LogOutput.log + screenshot (red band across the
face while the giantess grows):

- **Fixed — "red mask" face during growth.** The game's grow behaviour
  (`GiantessBehaviourGrow.BehaviourStart`) forces the `Hurt` expression, and
  `GiantessExpressionManager.LateUpdate` drives that avatar's tear/blush
  **overlay planes** (blendshapes + `_OverlayTexture*` material textures). The
  mod's material converter forced **every** VRM material — including the
  avatar's own transparent overlay materials (`Face_trans_mtl`,
  `Hair_trans_01/02_mtl`) — onto the *opaque* `KiriToon/Giantess` shader, so
  those planes rendered as solid colored rectangles. Transparent glTF materials
  (`alphaMode` MASK/BLEND) and `*_trans`-named materials are now routed to the
  game's own `KiriToon/Transparent Uber` shader (with queue/keyword fallback).
- **Fixed — `_MainTex` error spam (12 errors per load).**
  `SimpleMaterialGenerator` registered a `_MainTex` texture slot on materials
  using `KiriToon/Giantess`, which has no such property (its slot is
  `_BaseMap`). Texture slots are now registered only when the target shader
  actually exposes them.
- **Fixed — massive log spam from ShaderTest.** With `ShaderOverride` empty,
  the shader-test lab still captured all materials, enumerated 38–43 candidate
  shaders at Info level, and printed the whole shader catalog twice per load.
  With no override configured the lab now stays idle (one Debug line), the
  candidate list is logged at Debug level, and the per-frame expression
  clear-write spam lines were removed.
- **Fixed — `AttentionEyeTransform not found` warning at init.** The look-at
  bridge required `giantess.attentionManager` to exist during `Initialize`, but
  the game assigns it late; init also hard-returned when it was missing. The
  bridge now keeps working and re-resolves the attention target every frame.
- **Mitigated — `FileNotFoundException: UnityEditor.CoreModule` on unload.**
  UniVRM's `VRMBlendShapeProxy.OnDestroy` →
  `BlendShapeMerger.RestoreMaterialInitialValues` touches editor-only APIs; in
  the player build this throws on every avatar teardown. A Harmony finalizer
  suppresses exactly that failure (logged once).
## 1. VRM eye movement

### Fixed — VRM 1.0 gaze barely moved (degree/radian confusion)
`VrmLookAtBridge.TryApplyVrm10LookAt` multiplied its degree angles by
`Mathf.Deg2Rad` before calling UniVRM10's `SetLookAtYawPitch`. That API expects
**degrees** and converts internally, so VRM 1.0 avatars received degree-valued
radians — a maximum of ~1.0 instead of ~60. Eye movement was nearly invisible
on every VRM1 avatar. The conversion is removed; gaze now moves with the full
`MAX_YAW_ANGLE`/`MAX_PITCH_ANGLE` range (±60°/±45°).

### Fixed — LookUp/LookDown weights were inverted
Pitch was computed as **down-positive**
(`-Asin(localDirection.y)`) but the weight mapping treated positive pitch as
"look up". Targets above the head produced LookDown and vice versa. Pitch is
now up-positive end-to-end (computation, blendshape weights, eye-bone
fallback). The eye-bone fallback still negates pitch when building its
`Quaternion.Euler`, because Unity's positive X rotation pitches *down*.

### Fixed — legacy VRM 0.x `VRMLookAtHead` was never driven
The bridge only fed UniVRM10's runtime; VRM 0.x avatars that expose
`VRMLookAtHead` never received the computed yaw/pitch (only blendshape/bone
fallbacks ran). The legacy component is now discovered and driven via
reflection (radians, down-positive pitch, matching its original convention).

**Known limitation (by design):** when an avatar *has* look blendshapes, the
bone fallback is intentionally skipped, and both the VRM0/VRM1 look-at paths
can run alongside the blendshape path. If an avatar has both, gaze is
double-represented; disable whichever channel looks wrong by removing the
corresponding look blendshapes in the avatar.

---

## 2. Expression overlap / wrong expressions

### Fixed — VRM expression clips and raw mesh blendshapes were both written
`CompositeVrmExpressionAdapter.ImmediatelySetValue` previously pushed the same
weight to **both** the VRM expression backend *and* the raw mesh blendshape
backend on every frame. Because a VRM expression clip *drives* the same mesh
blendshapes it represents, two independent writers raced each frame
(last writer wins), which surfaced as stacked faces (Joy on top of Neutral),
flicker, and expressions sticking when one writer lagged behind the other.
The composite now routes each value to **exactly one** backend: the VRM
expression clip when it exists, the mesh only as a true fallback.

### Fixed — VRM 1.0 standard expressions stored as `custom` never applied
`Vrm1ExpressionAdapter.ImmediatelySetValue(preset, …)` wrote only the standard
`ExpressionPreset` key. Exporters that store `happy`/`blink`/etc. under the
`custom` preset (common) silently no-op'd. The adapter now falls back to the
`custom`-preset key with the same name when the standard key is absent, for
both writes and `HasClip`.

### Fixed — auto-discovery could double-drive standard presets
The fuzzy auto-mapper could map game expression keys onto VRM clips named like
standard presets (`happy`, `aa`, …) as *custom* mappings, so the same
expression was driven by two competing sources per frame. Auto-discovery now
skips any clip whose normalized name matches a standard VRM 0.x/1.0 preset
(`IsStandardPresetName`).

### Fixed — "standard-named" VRM 1.0 clips were unreachable by string key
`MapStringToPresetKey` only recognized look/blink names. A VRM1 avatar with a
standard-named clip (`happy`) addressed by string would be treated as a custom
key and miss. The map now also recognizes `neutral/happy/angry/sad/relaxed`
and the vowel keys `aa/ih/ou/ee/oh`.

### Fixed — every-frame zero rewrites (expression spam)
`UpdateWeight`/`UpdateCustomWeight` re-wrote `0` into every tracked expression
every frame even when idle (the "shouldForceZero" branch was always true at
rest). Now each expression gets one final zero-write when it clears and the
bridge goes quiet until a weight is non-trivial again. This also removes
constant fighting between the expression bridge and the look-at bridge over
Look* weights.

### Workaround available — per-avatar expression tuning
If a specific game expression maps to an awkward VRM clip on one avatar, the
mapping is chosen automatically; there is currently no per-avatar expression
mapping override in the JSON profile (see Open issues).

---

## 3. Auto spring bone generator

### Fixed — new config option to disable it entirely
`SpringBone.AutoGenerate` now defaults to **false**. Set it to `true` only for
avatars that have no VRM spring bones at all.

New companion option:

- `SpringBone.AutoGenerateOnAvatarsWithBones` (default `false`) — when
  `AutoGenerate` is on, this additionally allows generation on avatars that
  already contain spring bones (previously the generator silently skipped
  such avatars; now that behavior is the default and this flag re-enables it
  explicitly).

### Why it broke avatars
The generator picks bones purely by keyword over *every* transform, so a bone
like `Hair_Root` under the head, or a chain containing `String`, got springs
with global stiffness/gravity (5.0/3.0) that pulled head-adjacent geometry —
visible as stretched scalps, floating collars, or inverted skirts.

### Fixed — global spring params no longer clobber authored physics
`VrmSpringBoneController` used to write the global/default stiffness, gravity
and drag (5.0 / 3.0 / 1.0) into **every** VRM0 spring bone on every avatar,
overwriting the physics authored in the VRM. Overrides now apply only when
they were actually configured — a customized `SpringBone.Stiffness/Gravity/
Drag` in the BepInEx config, or per-avatar profile values
(`springBoneStiffness` etc. in `config/<avatar>.json`). Unset fields are
skipped per-field (previously unset `-1` sentinels could be written into
fields too).

---

## 4. Bug list (found, not yet fixed)

1. **Cache doesn't restore hidden original renderers / offsets (stale state).**
   `InitializeVrmAsync`'s cache-hit path calls `SetVisibility(true)` and
   reapplies position/rotation offsets, but never reapplies
   `HideOriginalAvatar`'s renderer disabling or the original `variation`
   component's enabled state. If the original renderer got re-enabled by game
   code between cache swaps, the replacement shows both characters.

2. **`SwitchAvatar`/reload path can double-destroy cached avatars.**
   `ScanForVrms` destroys everything in `loadedVrmCache`, but `ReloadCurrent-
   Avatar` → `InitializeVrmAsync` can run concurrently with a cache lookup
   for the same path; the token check aborts stale *loads* but cache *hits*
   are not token-checked, so a slow reload can re-show an avatar that
   `ScanForVrms` just destroyed (then re-created), producing duplicate
   `VrmManager` state on the same Giantess.

3. **Per-avatar `scaleOverride` bypasses spring bone reconstruction.**
   `GetEffectiveScale` is applied by setting `vrmRoot.transform.localScale`
   *before* `VrmManager.Setup`; `VrmSpringBoneController.Initialize` runs
   after, so the first-frame scale is fine — but any *later* profile edit
   requiring reload goes through the same path as a fresh load, and the
   controller's `lastScale` starts at the new scale, masking the change. Low
   impact, listed for completeness.

4. **`GetOrLoadProfile` cache ignores config file changes.**
   Per-avatar profiles are cached forever once loaded; editing
   `config/<avatar>.json` requires a full rescan (F5) or game restart to take
   effect. `ScanForVrms` clears `profileCache`, but `ReloadCurrentAvatar`
   alone does not.

5. **`HideOriginalAvatar` catches the VRM's own renderers if the VRM loads
   first.** Hiding walks `giantess.GetComponentsInChildren<Renderer>(true)`
   and excludes only transforms under the VRM root. If `SetParent` has run,
   the VRM is under the giantess and excluded correctly — but any *other*
   dynamically added child (e.g. a weapon mesh attached by the game at
   runtime after replacement) will be hidden on the next swap. Latent, not
   observed in normal play.

6. **`VrmMeshBlendshapeAdapter` frame gating is incomplete.**
   `BeginFrame`/`EndFrame` decay logic runs only via
   `VrmMeshExpressionApplicator` (the queued path). The direct
   `ImmediatelySetValue` path used by blink (`SyncBlink`) and the look-at
   bridge writes `_lastAppliedWeights` but never decays those keys, so if
   those two writers disagree for one frame the stale weight persists until
   the same key is written again.

7. **Key handling: hardcoded PageUp/PageDown alternates ignore remaps.**
   `VrmManager.Update` treats `KeyCode.PageUp`/`PageDown` as permanent
   alternates for the avatar-switch keys. If a user rebinds
   `PreviousAvatarKey`/`NextAvatarKey` to something else *and* uses PageUp/
   PageDown for another mod, both fire.

8. **`VrmAnimationBridge.AddLink` captures rest pose on the load frame.**
   `baseRestPoseOffset` is computed from the source/target rotation at
   `Setup` time. If the Giantess was mid-animation when the VRM finished
   loading (two `WaitForEndOfFrame`s later), the captured "rest" offsets are
   pose-dependent; the pose-handler path mostly self-corrects, but the
   non-humanoid fallback path (no pose handler) uses these offsets verbatim
   and can show a slightly rotated root pose permanently.

9. **`GetHeight` uses renderer bounds of *enabled* renderers only, in world
   space, before scaling.** During scale matching this is measured on the
   unscaled VRM; if the VRM ships with renderers disabled by default
   (`m_Enabled=0`, rare but real in some exported models), bounds fall back
   to 1.8 m and the avatar loads with the wrong height. `GetBoneHeight` is
   tried first, so impact is limited to non-humanoid edge cases.

10. **`ScanForVrms` can run from the Update loop while a load is in flight
    and is not guarded by the load token.** It sets `loadingVrm = true`
    during the (synchronous) scan, which makes `InitializeVrmAsync` bail
    entirely for the duration; an in-flight async load completes and its
    `finally` resets `loadingVrm = false`, re-enabling loads while the scan
    is logically still "in progress". Consequence is cosmetic (log noise,
    possible duplicate load), not a crash.

11. **Legacy VRM folder scan is not recursive and misses nested files.**
    Only `plugins/AvatarReplacer/*.vrm` and `plugins/AvatarReplacer/vrm/*.vrm`
    top-level files are found; subfolders (common for organizing large
    collections) are ignored. Users must flatten files.

12. **`SafeDestroyAvatar` nulls `BlendShapeAvatar` on the shared proxy.**
    Destroying a cached-but-still-alive avatar sets
    `proxy.BlendShapeAvatar = null` before `Destroy`; if the same VRM asset
    object is shared (VRM 0.x reload of the same file reuses the loaded
    proxy object in some UniVRM versions), the *next* load of that file can
    come up expression-less until a full game restart.

13. **`forceMaterialSync` writes `_OutlineWidth` to every renderer.**
    Materials that don't define `_OutlineWidth` still get the property block
    entry; harmless for most shaders but it can produce shader-feature
    recompiles / warnings with some lilToon variants when the property
    doesn't exist. Cosmetic.

14. **Diagnostics key (F10) logs the *manager's* view of the avatar, not the
    GTS-side state.** `VrmDiagnostics.LogAvatar(gameObject, giantess, …)` is
    fine, but there is no diagnostic for "why is my avatar T-posed"
    (no dump of animator humanoid mapping validity vs. the actual bone
    links), which is the most common user-visible failure.

15. **`LootZoneManager.Instance` null-check races scene teardown.**
    `GetCurrentHeightMeters` reads `LootZoneManager.Instance.meterPerScale`;
    between scene unload and manager destroy Unity's fake-null makes the
    property non-null but destroyed in edge frames. Very low probability,
    theoretical NRE.

---

## 5. Already documented elsewhere

- README/`csproj` version skew and config filename inconsistencies were fixed
  in the code pass (csproj now 6.0.0 to match `PluginInfo`).
- Full architecture, frame order, config reference and design invariants:
  see `ARCHITECTURE.md`.
