# AvatarReplacer

AvatarReplacer replaces selected Lethal Fantasy Giantess models with VRM avatars. It supports VRM 0.x and VRM 1.0, humanoid animation retargeting, facial expression mapping, eye tracking, and UniVRM spring bones.

## Compatibility

- Target: **Lethal Fantasy beta v1.0.2**.
- Runtime: Unity and BepInEx. Install a BepInEx version compatible with your game installation before installing the mod.
- Avatar format: VRM 0.x or VRM 1.0. A valid humanoid rig is needed for full animation retargeting.
- The plugin was compiled against the beta v1.0.2 game assembly. Runtime verification on that game version is pending.

## Install

1. Install BepInEx for the game and launch once so it creates its plugin folders.
2. Build the plugin using the instructions below, or use a prebuilt plugin DLL from a release package.
3. Copy the resulting `AvatarReplacer.dll` into `LethalFantasy/BepInEx/plugins/`.
4. Create `LethalFantasy/BepInEx/plugins/AvatarReplacer/vrm/` and put your `.vrm` files there.
5. Launch the game. The mod creates its config files as it starts and as Giantess characters spawn.

The source project packages its selected UniVRM dependencies inside `AvatarReplacer.dll`; do not copy the files in `lib/` separately when installing. This project does **not** use a NuGet assembly-merging package: MSBuild adds the dependency DLLs as embedded resources, and the included `EmbeddedAssemblyLoader` loads them at runtime while preserving their assembly identities. The game assemblies, BepInEx, and Harmony are provided by the game and BepInEx installation.

## Assign avatars

Open `LethalFantasy/BepInEx/config/com.user.avatarreplacer.cfg`. The mod creates a per-character assignment under `[AvatarOverrides]` when each character is detected. The first-run values are blank:

```ini
[AvatarOverrides]
Akipo =
RedTwin =
BlueTwin =
```

Enter a VRM **filename** that exists in `BepInEx/plugins/AvatarReplacer/vrm/` to assign it. Leave the value blank to keep that character's original model. All three assignments start blank, and the mod does not ship with personal avatar filenames or a default custom avatar.

The controls below are disabled by default. Set `DebugMode = true` under `[General]` in the BepInEx config before launching the game to enable them. With DebugMode off, assignments remain unchanged and the original model is used for blank entries.

## Controls

| Key | Action |
|---|---|
| Page Up | Switch each assigned character to its previous VRM; requires DebugMode |
| Page Down | Switch each assigned character to its next VRM; requires DebugMode |
| F5 | Rescan the VRM folder and reload configured assignments; requires DebugMode |
| F10 | Write avatar diagnostics to the BepInEx log; requires DebugMode |
| Hold Right Alt + mouse wheel | Cycle through the active character's mapped game expressions; requires DebugMode |

DebugMode defaults to `false`, so these shortcuts do nothing until the user enables it. The configurable bindings are in the `[Controls]` section. Page Up and Page Down are also recognized as built-in switching keys. If a character's assignment is blank, Page Down selects the first VRM and Page Up selects the last; until a switch key is pressed, the original model stays visible. While Right Alt is held, the wheel cycles the active character's selectable game expressions that have a mapped VRM target. Releasing Right Alt clears the temporary expression and restores normal game expression syncing. Hotkeys support Unity input and raw keyboard polling for Wine setups.

## Settings

The main settings file is `BepInEx/config/com.user.avatarreplacer.cfg`.

| Section | What it controls |
|---|---|
| `[General]` | EnableReplacement, DebugMode, and Diagnostics |
| `[Controls]` | Previous/next avatar, rescan, and diagnostics keys |
| `[AvatarOverrides]` | VRM filename for each detected Giantess; blank keeps the game model |
| `[Transform]` | Shared VRM position and rotation offsets, plus optional Giantess startup scale multiplier |
| `[SpringBone]` | Authored spring parameters and optional automatic spring generation |
| `[SmallScaleLocomotionFix]` | Stride and hip-motion compensation for smaller characters |
| `[ShaderTesting]` | Optional runtime shader override and shader diagnostics |

DebugMode is off by default and must be enabled to use avatar switching, rescan, F10 avatar reports, or expression-test hotkeys. `Diagnostics` is a separate `[General]` setting, off by default; enable it to log spring-bone scaling and look-at tracking details without enabling DebugMode. A non-empty avatar assignment replaces that character's original model automatically; a blank assignment keeps the original model. There is no separate hide-original-mesh option. Automatic spring generation is also off by default; enable it only when an avatar lacks usable spring bones, because guessed chains can affect unintended bones. Shader overrides only work for shaders already loaded by the game or supplied by a compatible AssetBundle.

## Per-avatar profiles

Profiles and expression maps are stored in:

```text
BepInEx/plugins/AvatarReplacer/config/
```

The mod creates `_template.json` as a profile example. Copy it to `<vrm-filename-without-extension>.json` to configure one avatar. Profile fields include:

- `scaleOverride`: positive value overrides the global VRM scale setting.
- `scaleMultiplier`: additional per-avatar scale multiplier; defaults to `1.0`.
- `positionOffset` and `rotationOffset`: per-avatar offsets with `x`, `y`, and `z` values.
- `springBoneStiffness`, `springBoneGravityPower`, and `springBoneDrag`: per-avatar spring settings.
- `smallScaleThresholdMeters`, `smallScaleStrideFactorMin`, and `smallScaleHipSmoothMultiplier`: per-avatar locomotion compensation settings.

Unset profile values use the corresponding global setting. The template is for copying; it is not itself loaded as an avatar profile.

## Custom expression mapping

The mod creates a shared expression map and maps for individual VRM files in the config directory:

```text
expression_map.json
expression_map.<vrm-filename>.json
```

The mod checks the per-avatar map first. If it has no non-empty overrides, the shared map can apply. To add an override, a mapping row needs only `gameExpression` and `vrmBlendShape`:

```json
{
  "gameExpression": "happy eyes",
  "vrmBlendShape": "Happy"
}
```

`gameExpression` must match the game's expression name. `vrmBlendShape` must match a clip exposed by the VRM. If `vrmBlendShape` is empty, that row is ignored as an override; the mod's existing built-in preset mapping or automatic mapping remains in effect. It does not disable that game expression. `Blink` is handled through the blink controller.

Generated templates use the same two fields. Empty `vrmBlendShape` values mean “keep the built-in or automatic mapping.” When a target VRM provides a clearly named eye- or mouth-specific clip, the translator prefers that clip for the matching game expression; otherwise it falls back to its built-in preset or automatic match.

Example empty row (no custom override):

```json
{
  "gameExpression": "happy eyes",
  "vrmBlendShape": ""
}
```

Use the expression names and spelling from the generated file. If a configured clip does not exist on the avatar, the mod logs a warning and skips that override. Press F5 to reload an avatar after editing its map.

Map files accept `//` comments and trailing commas. The mod preserves existing populated maps while updating missing rows; malformed JSON is logged and left untouched so edits are not overwritten.

## Troubleshooting

- **No avatar appears:** confirm the assignment matches a `.vrm` filename in the `vrm/` folder, enable DebugMode, then press F5. A blank assignment intentionally leaves the original model visible.
- **The mod does not load:** confirm BepInEx started and check `BepInEx/LogOutput.log` for plugin loading errors.
- **Right Alt + mouse wheel does nothing:** confirm `DebugMode = true`, then check the log for the `Debug input Harmony patch active` entries and `Debug input callback is active`. These confirm the game hooks and input callback are running.
- **Animation looks wrong:** check that the VRM has a valid humanoid rig. Use F10 diagnostics with DebugMode enabled for details.
- **Expressions do not match:** inspect the generated expression map and confirm the VRM clip names. Enable DebugMode and reload with F5 after editing.
- **Shader appears pink or missing:** the required shader may not be loaded. Review `[ShaderTesting]` and shader diagnostics in the BepInEx log.
- **Spring bones behave unexpectedly:** disable automatic generation first. Authored VRM spring bones are used by default; automatic generation can be limited with its keyword and root settings.

## Build from source

Requirements: .NET Framework 4.7.2 targeting pack, a compatible .NET SDK, the game's managed assemblies, BepInEx/Harmony assemblies, and the UniVRM reference assemblies in `lib/`.

Build from the repository root, passing the game install directory and BepInEx core directory:

```sh
dotnet build AvatarReplacer.sln \
  -p:GameDir="/path/to/LethalFantasy" \
  -p:BepInExCoreDir="/path/to/LethalFantasy/BepInEx/core"
```

`GameDir` must contain `LethalFantasy_Data/Managed/Assembly-CSharp.dll`. The project compiles against the installed game assemblies. During the build, the selected UniVRM DLLs from `lib/` are embedded as resources in the plugin; they are not merged into the plugin's assembly metadata. The generated plugin DLL is a local build artifact; it is not included in the source repository.

## Source layout

- `Core/` — BepInEx lifecycle, configuration, Harmony hooks, and logging.
- `Loading/` — VRM format detection, loading, and material conversion.
- `Avatar/` — avatar runtime setup and visibility.
- `Animation/` — humanoid pose synchronization.
- `Expressions/` — expression adapters, maps, and fallbacks.
- `LookAt/` — eye tracking synchronization.
- `Physics/` — spring-bone controls and optional spring generation.
- `Configuration/` — avatar profiles, bone mappings, and expression maps.
- `Diagnostics/` — avatar, material, and shader diagnostics.
- `lib/` — UniVRM reference assemblies embedded into the built plugin.

## License

No license file is currently included in this repository. Check the repository's license terms before redistributing modified source or binaries.
