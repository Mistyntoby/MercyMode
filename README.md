# MercyMode

> **Deltarune Asset Notice**
>
> MercyMode does **not** include, package, or redistribute Deltarune game assets.
>
> If you have a legitimate local installation of Deltarune, MercyMode can optionally load compatible assets directly from that installation. These assets remain on your computer and are **not included in the MercyMode Workshop download**.
>
> Users are responsible for owning and installing Deltarune through an authorized source before using the optional Deltarune asset integration.
>
> **MercyMode is an unofficial fan-made project and is not affiliated with, endorsed by, or sponsored by Toby Fox or the creators of Deltarune.**

MercyMode is a tModLoader mod that turns Terraria boss and optional regular-enemy encounters into Deltarune-inspired turn-based battles, alone or as a multiplayer party of up to three.

Battles pause the Terraria world and offer FIGHT, ACT, ITEM, SPARE, and DEFEND turns. On enemy turns, dodge bullets with the SOUL; bullets can hurt the player's real health, while grazing them builds TP. Spared bosses still drop loot.

## Features

* Custom encounters for the Eye of Cthulhu, King Slime, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops, and Wall of Flesh.
* Generic encounters for other bosses and configurable encounters for regular enemies.
* Boss-specific ACTs, healing items, timed FIGHT attacks, DEFEND, and loot-preserving SPARE.
* Optional local Deltarune sprites, fonts, sounds, and music, with fallbacks when those assets are unavailable.
* Multiplayer party battles (up to three players; still being tested).

## Build

Requirements: Windows (or Mac, currently untested as of writing), the .NET 8 SDK, and an installed tModLoader setup with its mod build targets.

Open a terminal in this mod's source directory and run:

```bash
dotnet build -c Release
```

tModLoader's build target compiles and packages the mod. The package is written to the active tModLoader Mods folder. To build for testing, close tModLoader first; packaging may fail if the game is holding the mod file open.

## Controls

Battle keys can be rebound in **Settings > Controls**:

* Arrow keys: move through battle menus and move the SOUL during enemy turns.
* Z: confirm and use the FIGHT timing attack.
* X: go back in menus; hold during enemy turns to move the SOUL more slowly.
* Outside battle, ACT, SPARE, and Heal Prayer use configurable keybinds (default F, G, and V).

## Deltarune Assets

Use the Mercy Mode config to select the local Deltarune installation folder, select a chapter, or disable asset loading entirely.

Run:

```text
/drassets
```

in chat to see the current asset-loading status, or:

```text
/drassets reload
```

to reload assets.

### Mac Users

If MercyMode does not automatically find your Deltarune installation on macOS, you can manually specify the location with:

```text
/drassets path <path>
```

For example:

```text
/drassets path /Users/YourName/Library/Application Support/Steam/steamapps/common/DELTARUNE
```

Replace `YourName` with your macOS username and adjust the path if your Steam library is installed somewhere else.

You can use `/drassets` afterward to verify whether the assets were detected.

If the required local assets are unavailable, MercyMode uses its built-in fallbacks instead.

## Steam Workshop

The Workshop release contains the MercyMode mod itself and does not contain Deltarune game assets.

Deltarune is a separate copyrighted work owned by its respective rights holders. MercyMode does not grant users access to Deltarune or provide copies of its assets.

## Manual Testing

There is no automated test project. For in-game checks, use a separate tModLoader save directory so test battles do not affect regular saves.

The `/mmbattle` chat command is a developer helper:

* `/mmbattle kit` gives test items.
* `/mmbattle` starts an Eye of Cthulhu battle.
* `/mmbattle npc <id|name>` starts a battle with a selected NPC.
* `/mmbattle end` ends a test battle.

See [MODLOG.md](MODLOG.md) for the current implementation notes, the recorded test setup, and previously verified scenarios.

## Source and Asset Notes

`Deltarune/DataWin.cs` notes that its GameMaker data reader is based on UndertaleModTool and is licensed under GPL-3.0.

No Deltarune game assets are included with this project or its Workshop package.

MercyMode's own source code and original assets are distributed according to this project's applicable license and terms. Deltarune remains the property of its respective rights holders.
