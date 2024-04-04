# AutoHookGenPatcher

Automatically generates [MonoMod.RuntimeDetour.HookGen's](https://github.com/MonoMod/MonoMod) `MMHOOK` files during the [BepInEx](https://github.com/BepInEx/BepInEx) preloader phase.

Manual Installation:
Move the `BepInEx` folder from the ZIP file to the `BepInEx` folder of the game.

**This project is not officially linked with BepInEx nor with MonoMod.**

This software is based off of [HookGenPatcher](https://github.com/harbingerofme/Bepinex.Monomod.HookGenPatcher) by [HarbingerOfMe](https://github.com/harbingerofme), which is also licensed under MIT.

## Differences to the original HookGenPatcher

- Instead of only having a fixed list of files to generate MMHOOK files for, AutoHookGenPatcher will get the MMHOOK file references from installed plugins, and generates those MMHOOK files if possible.
- AutoHookGenPatcher makes use of a cache file to quickly check if everything is still up to date, without needing to check every MMHOOK file for that information.
- Hook Generation is now multithreaded, meaning that generating multiple MMHOOK files takes less time. For example, generating an MMHOOK file for every file in the `Managed` directory of Lethal Company takes about **22.5 seconds** instead of **40.0 seconds** it would take with no multithreading, on my machine.

## Usage For Developers

**Note:** By default, AutoHookGenPatcher already generates an `MMHOOK` assembly for `Assembly-CSharp.dll`. So if you only need `MMHOOK_Assembly-CSharp.dll`, you don't need to do anything.

Using AutoHookGenPatcher is really simple, and the only thing you need to do is tell it to generate the MMHOOK files you want in the first place. This can be by editing the config file `AutoHookGenPatcher.cfg`, and setting the `[Generate MMHOOK File for All Plugins]` setting's `Enabled` value to `true`:

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
When you publish your mod, make sure to add AutoHookGenPatcher as a dependency in the package you upload to e.g. Thunderstore.

If AutoHookGenPatcher hasn't been uploaded on the platform/community you are releasing your mod on, you can message me on Discord *(@hamunii)* or reupload AutoHookGenPatcher yourself. If you do so, make sure to link to this GitHub repository. **Note that newer versions of AutoHookGenPatcher might get released which could add or modify functionality, and fix potential bugs.**

*Also note that MonoMod.dll and MonoMod.RuntimeDetour.HookGen.dll assemblies are needed for successful execution, and different licensing rules may apply to those.*

### Q&A
#### How does AutoHookGenPatcher figure out which MMHOOK files my mod references?
- During the BepInEx Preloader phase, AutoHookGenPatcher will open and read the metadata of every DLL file\* for referenced assemblies and looks for references that start with `MMHOOK_`. It will then check if any installed assemblies match the rest of the name, and will run MonoMod's HookGen on those assemblies if they exist.

\*This is only done if the *date modified* metadata of the assembly on disk is newer than previous known date in AutoHookGenPatcher's cache file, if a cache entry exists for the assembly. Referenced `MMHOOK` assemblies are also saved in the cache.