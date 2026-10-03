# AvatarReplacer 6.0.0

AvatarReplacer replaces Lethal Fantasy's Giantess model with a VRM avatar. It supports VRM 0.x and VRM 1.0, synchronizes humanoid animation and expressions, and includes look-at and UniVRM SpringBone support.

## Requirements

- Lethal Fantasy with BepInEx installed.
- A VRM 0.x or VRM 1.0 avatar with a valid humanoid rig for full pose synchronization.
- The plugin's UniVRM libraries, included in the `lib/` directory.

## Install

1. Copy the `AvatarReplacer` plugin folder into `BepInEx/plugins/`.
2. Put `.vrm` files in `BepInEx/plugins/AvatarReplacer/vrm/`.
3. Launch the game. The plugin creates its BepInEx config and its per-avatar configuration templates automatically.

The first avatar is chosen from the available VRMs, using `VrmFileName` when a matching file exists. Press **F5** in game after adding or removing files to rescan the folder.

## Controls

| Key | Action |
|---|---|
| PageUp | Switch to the previous VRM |
| PageDown | Switch to the next VRM |
| F5 | Rescan the VRM folder and reload the selected avatar |
| F10 | Write diagnostics for the active avatar to the BepInEx log |
| Ctrl+1 … Ctrl+0 | Temporarily display a built-in debug expression; release Ctrl to clear it |

The switch and diagnostic keys can be changed in the BepInEx configuration. PageUp and PageDown are also accepted as built-in avatar-switch keys.

## Configuration

The BepInEx settings file is:

```text
BepInEx/config/com.user.avatarreplacer.cfg
```

The main options are grouped into these sections:

- **General:** `VrmFileName`, `EnableReplacement`, `HideOriginalMesh`, and `DebugMode`.
- **Controls:** avatar switching, rescanning, and diagnostic keys.
- **Transform:** global position and rotation offsets, plus the Giantess startup scale multiplier.
- **SpringBone:** optional authored-physics overrides and opt-in spring-bone generation for models without springs.
- **SmallScaleLocomotionFix:** stride and hip-smoothing compensation settings.
- **ShaderTesting:** optional live shader override and shader-bundle path. Only shaders loaded by the game or an available bundle can be used.

Per-avatar profiles and expression maps live here:

```text
BepInEx/plugins/AvatarReplacer/config/
```

Copy `_template.json` to `<avatar-file-name>.json` to set avatar-specific scale, offsets, or spring-bone values. Profile values take precedence over the corresponding global values.

## Custom expression mapping

The plugin generates an empty expression map for each discovered VRM. Edit the file for the avatar you want to tune:

```text
BepInEx/plugins/AvatarReplacer/config/expression_map.<avatar-name>.json
```

The shared map is `expression_map.json`. A populated per-avatar map takes precedence over the shared map for that avatar; an empty per-avatar template falls back to the shared map. Existing map files are not overwritten during rescans.

Each row connects an exact Lethal Fantasy expression name to a VRM expression clip name:

```json
{
  "targetVrm": "avatar1",
  "mappings": [
    { "gameExpression": "happy eyes", "vrmBlendShape": "JoyfulFace" },
    { "gameExpression": "a mouth", "vrmBlendShape": "aa" },
    { "gameExpression": "Blink", "vrmBlendShape": "Blink" }
  ]
}
```

- Copy `gameExpression` values exactly from the generated template; names and spelling must match the game expression key.
- Set `vrmBlendShape` to a clip name present in the VRM. Blender's Shape Keys list is a useful place to inspect model names, but the runtime clip name is what the map resolves against.
- An empty `vrmBlendShape` leaves the built-in or automatic mapping in place. At least one filled row activates the map.
- A filled row replaces the existing mapping for that game expression. `Blink` is routed through the blink handler.
- Edit the map and reload the avatar (F5 rescans and reloads) for changes to take effect.

If a row names a clip that the VRM does not expose, the plugin logs a warning and skips that row. Check `LogOutput.log` under `BepInEx` when troubleshooting mapping or loading issues.

## Project layout

- `Core/` — BepInEx lifecycle, Harmony hooks, and logging.
- `Loading/` — VRM format detection, loading, and material conversion.
- `Avatar/` — avatar runtime manager and switching controls.
- `Animation/` — pose synchronization between the game character and VRM.
- `Expressions/` — expression adapters, mapping, blink, and mesh/jaw fallbacks.
- `LookAt/` — eye tracking synchronization.
- `Physics/` — UniVRM SpringBone scaling and optional spring generation.
- `Configuration/` — avatar profiles, bone mappings, and expression-map templates.
- `Diagnostics/` — avatar, material, and shader diagnostics.
- `lib/` — UniVRM runtime assemblies used by the plugin.

## Build

Build `AvatarReplacer.sln` with the .NET Framework 4.7.2 targeting pack available. The project references the game's managed assemblies and BepInEx assemblies; set `GameDir` or `LF_GAME_DIR` to your Lethal Fantasy installation if the default path does not match your machine.

## License

MIT. See [LICENSE](LICENSE).
