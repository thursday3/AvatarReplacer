# AvatarReplacer 5.0

VRM avatar replacement for Lethal Fantasy.

## Overview

The AvatarReplacer 5.0 is a plugin for Lethal Fantasy that allows you to replace the in-game avatar with a VRM avatar. The plugin supports both VRM 0.x and VRM 1.0, and it uses UniVRM 0.131.0 runtime SpringBone scaling support.

## Project Structure

The project is structured into several folders:

- `Core/`: This folder contains the plugin lifecycle, logging, and Harmony hooks.
- `Loading/`: This folder contains the VRM detection, loading, and material conversion.
- `Avatar/`: This folder contains the avatar runtime manager.
- [Animation/]: This folder contains the Avatar-to-VRM pose synchronization.
- `Expressions/`: This folder contains the facial expressions, blink, mesh fallback, and jaw fallback.
- `Physics/`: This folder contains the UniVRM SpringBone and optional auto-generation.
- [LookAt/]: This folder contains the look-at synchronization.
- `Configuration/`: This folder contains the persistent avatar and bone configuration.
- [Debug/]: This folder contains the diagnostics.

## Usage

To use the AvatarReplacer 5.0 plugin, follow these steps:

1. Extract and place the `AvatarReplacer` folder in the `BepInEx/plugins/` directory of your Lethal Fantasy installation.
2. Place the VRM avatar file in the `BepInEx/plugins/AvatarReplacer/vrm` directory.
3. Configure the plugin by editing the `BepInEx/config/AvatarReplacer.cfg` file.

## Configuration

The plugin reads its settings from the generated config file at `BepInEx/config/com.user.avatarreplacer.cfg`.

When the mod starts for the first time, it creates this file automatically with the following sections and options.

### Controls

These keys let you switch avatars and trigger diagnostic actions while playing.

- `PreviousAvatarKey` (`PageUp` by default): switch to the previous avatar in the VRM list.
- `NextAvatarKey` (`PageDown` by default): switch to the next avatar in the VRM list.
- `RescanKey` (`F5` by default): refresh the VRM folder and detect newly added or removed avatars.
- `DiagnosticsKey` (`F10` by default): print a full VRM diagnostic report for the active avatar.

### General

These are the main avatar replacement options.

- `VrmFileName` (`avatar.vrm` by default): the default VRM file that should be loaded when the mod starts.
- `EnableReplacement` (`true` by default): turns VRM replacement on or off.
- `HideOriginalMesh` (`true` by default): hides the original in-game mesh so the VRM avatar is the one you see.
- `DebugMode` (`false` by default): enables verbose debug logging. Useful if you are troubleshooting model or shader issues.

### ShaderTesting

This section controls live shader testing for the VRM avatar while the game is running.

- `Enabled` (`true` by default): enables config-driven live shader testing.
- `ShaderOverride` (empty by default): exact shader name to apply. Leave it empty to restore the original/default game shader.
  - Example values include: `KiriToon/Giantess`, `VRM/MToon`, `VRM/UnlitTexture`, `VRM/UnlitCutout`, `VRM/UnlitTransparent`, `VRM/UnlitTransparentZWrite`, `Standard`, `VRM10/MToon10`, and others that are actually loaded by the game.

### Example

A typical setup might look like this:

```ini
[General]
VrmFileName = avatar.vrm
EnableReplacement = true
HideOriginalMesh = true
DebugMode = false

[ShaderTesting]
Enabled = true
ShaderOverride = lilToon
```



## Contributing

Contributions are welcome! If you find any issues or have suggestions for improvement, please open an issue or submit a pull request.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.