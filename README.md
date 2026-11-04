# CutContent

Attempts to restore multiple cut or phased out features from old versions of Tarkov.

**Author:** mizmii  
**Version:** 1.0.0  
**SPT Version:** 4.1.6  
**License:** Unlicense

This mod has a server component (`editDatabase`) and a client component (`harmonyPatch`).

## Build

Open `CutContent.sln` or run:

```sh
dotnet build CutContent.sln -c Release
```

The server project builds the client project first and creates `CutContent.zip` in this folder. The archive contains both DLLs and, if the server template needs it, `config.json`.

## Install

Extract `CutContent.zip` into the SPT game folder. The archive places the server DLL under `SPT_Runtime/user/mods/CutContent/` and the client DLL under `BepInEx/plugins/`.

The client project's build target also copies its DLL to the SPT installation selected in the wizard.

## Projects

- `Server/CutContent.csproj`: .NET 10 server mod
- `Client/CutContent.csproj`: BepInEx client plugin

Edit `Server/` and `Client/` independently. They share the mod name and version, but each has its own entry point.
