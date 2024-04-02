# AutoHookGenPatcher

Automatically generates [MonoMod.RuntimeDetour.HookGen's](https://github.com/MonoMod/MonoMod) `MMHOOK` files during the [BepInEx](https://github.com/BepInEx/BepInEx) preloader phase.

Manual Installation:
Put in `BepInEx\patchers` folder.
Make sure the `MonoMod.dll` and `MonoMod.RuntimeDetour.HookGen.dll` files are also present.

**This project is not officially linked with BepInEx nor with MonoMod.**

This software is based off of [HookGenPatcher](https://github.com/harbingerofme/Bepinex.Monomod.HookGenPatcher) by [harbingerofme](https://github.com/harbingerofme), which is also licensed under MIT.

## Differences to the original HookGenPatcher

- Instead of only having a fixed list of files to generate MMHOOK files for, AutoHookGenPatcher will get the MMHOOK file references from installed plugins, and generates those MMHOOK files if possible.
- AutoHookGenPatcher makes use of a cache file to quickly check if everything is still up to date, without needing to check every MMHOOK file for that information.
- Hook Generation is now multithreaded, meaning that generating multiple MMHOOK files takes less time. For example, generating an MMHOOK file for every file in the `Managed` directory of Lethal Company takes about **22.5 seconds** instead of **40.0 seconds** it would take with no multithreading, on my machine.

## Usage For Developers

Using AutoHookGenPatcher is really simple, and the only thing you need to do is tell it to generate the MMHOOK files you want in the first place. This can be by editing the config file `AutoHookGenPatcher.cfg`, and setting `Enabled` to `true`:

```toml
[Generate MMHOOK File for All Plugins]

## If enabled, AutoHookGenPatcher will generate MMHOOK files for all plugins
## even if their MMHOOK files were not referenced by other plugins.
## Use this for getting the MMHOOK files you need for your plugin.
# Setting type: Boolean
# Default value: false
Enabled = true

## Automatically disable the above setting after the MMHOOK files have been generated.
# Setting type: Boolean
# Default value: true
Disable After Generating = true
```
When you then publish your mod, make sure to add AutoHookGenPatcher as a dependency in the package you upload to e.g. Thunderstore. If AutoHookGenPatcher hasn't been uploaded on the platform you are releasing your mod, you can upload AutoHookGenPatcher there and add a dependency to that instead. If you do so, make sure to include this readme file and a copy of the license.

*Note that MonoMod files are needed for successful execution, and different licensing rules may apply to those.*
