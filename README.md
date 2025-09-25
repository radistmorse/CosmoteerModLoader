# CosmoteerModLoader

Inspired by [EML fork](https://github.com/ElectroJr/EnhancedModLoader).

Based (loosely) on [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop)

The managed C# project is built as a console app, so that dll had an entrypoint, but the resulting executable is not needed, only the dll.

The native C project is built using xmake. Rename the lib into winmm.dll.

## Usage

Put both libs into the astroneer Bin folder.

## Mod development

Mod Loader will load all the dlls it finds in the folders of the enabled mods (workshop, user or built-in). First it loads dll named 0Harmony.dll, if it exists somewhere in thoose folders. If more than one file with such name exists, it will pick the last one it encounters. Then all the others dlls are loaded. The loader then looks through the loaded assemblies for the following function signatures:

```
public static void AssemblyLoadInitializer()
```

This method would be called immediately on assembly load. No game components exist yet, but all the assemblies are already loaded. This method can be used for applying harmony patches.

```
public static void GameLoadInitializer()
public static void InitializePatches()
```

These methods are called after the game has started. They can be used to add callbacks to the game director or accessing other game components. They are exactly the same and the second function is left for compatibility with EML mods.

Methods marked with `[UnamagedCallersOnly]` are also supported, but this is not required and will not add any benefits.