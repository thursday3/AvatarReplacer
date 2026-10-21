# AvatarReplacer 6.0.0

AvatarReplacer replaces Lethal Fantasy's Giantess model with a VRM avatar. It supports VRM 0.x and VRM 1.0, synchronizes humanoid animation and expressions, and includes look-at and UniVRM SpringBone support.

## Requirements

- Lethal Fantasy with BepInEx installed.
- A VRM 0.x or VRM 1.0 avatar with a valid humanoid rig for full pose synchronization.
- When building from source, the matching UniVRM assemblies must be present in `lib/`.

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

Copy `_template.json` to `<avatar-file-name>.json` to set avatar-specific scale, offsets, or spring-bone values. Profile values take precedence over the corresponding global values. (`_template.json` is regenerated once at startup and exists to be copied — it is not read as a profile itself.) Profile and expression-map files accept `//` comments, trailing commas, and unknown keys; a file with broken syntax is reported in `LogOutput.log` and left untouched rather than regenerated over your edits.

## Custom expression mapping

The plugin generates a **visible map** of how it drives your VRM's face — every Lethal Fantasy expression is listed, including empty and unused rows, with each row's built-in default shown beside it. Edit the file for the avatar you want to tune:

```text
BepInEx/plugins/AvatarReplacer/config/expression_map.<avatar-name>.json
```

The shared map is `expression_map.json`. A populated per-avatar map takes precedence over the shared map for that avatar; an empty per-avatar template falls back to the shared map. Map files with real rows are never rewritten during rescans; only missing expression rows are appended and empty skeleton files regenerated. A map file whose JSON is broken is reported in `LogOutput.log` and left byte-for-byte untouched (fix or delete it to regenerate).

Each row shows what the mod actually uses:

```json
{
  "targetVrm": "avatar1",
  "mappings": [
    { "gameExpression": "happy eyes", "defaultVrm": "Neutral", "vrmBlendShape": "" },
    { "gameExpression": "hurt eyes", "defaultVrm": "Sorrow", "vrmBlendShape": "blendShape1.Eyelid_Sad_L" },
    { "gameExpression": "a mouth", "defaultVrm": "A", "vrmBlendShape": "aa" },
    { "gameExpression": "Blink", "defaultVrm": "", "vrmBlendShape": "Blink" }
  ]
}
```

- `gameExpression` — the giantess expression key; copy exactly, names and spelling must match the generated template.
- `defaultVrm` — the built-in default preset the mod uses when you don't override; generated from the same table the runtime reads, so the file always shows the real behaviour.
- `vrmBlendShape` — the clip actually driven on the VRM. **Empty = use `defaultVrm`; filled = override.** Blender's Shape Keys list is a useful place to inspect model names, but the runtime clip name is what the map resolves against.
- A filled row replaces the existing mapping for that game expression. `Blink` is routed through the blink handler.
- At least one filled row activates the map.
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

Build `AvatarReplacer.sln` with the .NET Framework 4.7.2 targeting pack available. The build embeds the UniVRM assemblies from `lib/` inside `AvatarReplacer.dll`; the plugin loads them from the embedded resources at runtime, so you install one plugin DLL instead of the separate UniVRM DLLs. Keep the exact UniVRM input assemblies in `lib/` while building.

The plugin still uses the game's Unity and managed assemblies and BepInEx/Harmony at runtime; those are not bundled. Set `GameDir` or `LF_GAME_DIR` to your Lethal Fantasy installation if the project default does not match your machine. The VRM avatar files and editable configuration files also remain separate from the plugin DLL.

## License

MIT. See [LICENSE](LICENSE).
