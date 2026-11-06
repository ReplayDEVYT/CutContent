# Re-Classic

SPT (Single Player Tarkov) mod for Escape from Tarkov that restores features that were cut from the game or never finished. Each restored feature is its own unit of work, built by using the alpha client as reference.

## Reference assemblies

- `Ref/Alpha/Assembly-CSharp.dll`: game code from the **alpha** client. Use it as the reference for how a cut feature originally worked.
- `Ref/Modern/Assembly-CSharp.dll`: the **current** game assembly that the mod patches. Target types and methods come from here.

These are compiled DLLs. Decompile them to read them (`ilspycmd` is installed at `~/.dotnet/tools/ilspycmd.exe`, e.g. `ilspycmd -p -o <outdir> Ref/Modern/Assembly-CSharp.dll`). Put decompiled output outside the repo, don't commit it. Expect the alpha to use different names (many obfuscated `GClass`/`GInterface` types), so match by behavior and strings, not by name.

## Projects

- `Client/`: BepInEx plugin. Harmony patches inherit `SPT.Reflection.Patching.ModulePatch` (see `Client/Patches/ExamplePatch.cs`) and are enabled in `Client/Plugin.cs`.
- `Server/`: .NET 10 SPT server mod (database edits).

Build: `dotnet build ReClassic.sln -c Release`. The build also creates `ReClassic.zip`.
