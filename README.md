# MercyMode

> **Deltarune Asset Notice**
>
> MercyMode does **not** include, package, or redistribute Deltarune game assets.
>
> If you have a legitimate local installation of Deltarune, MercyMode can optionally load compatible assets directly from that installation at runtime. The assets stay on your computer and are not included in the MercyMode source repository or mod package.
>
> Users are responsible for obtaining and installing Deltarune through an authorized source before using the optional Deltarune asset integration. MercyMode does not verify Steam ownership.
>
> **MercyMode is an unofficial fan-made project and is not affiliated with, endorsed by, or sponsored by Toby Fox or the creators of Deltarune.**

MercyMode is a tModLoader mod that turns Terraria boss and enemy encounters into Deltarune-inspired turn-based battles, either alone or as a multiplayer party of up to three.

Battles pause the Terraria world and use FIGHT, ACT, ITEM, SPARE, and DEFEND turns. During enemy turns, dodge bullets with the SOUL. Bullets can damage your real health, while grazing them builds TP. Spared bosses still drop their normal loot.

**Development Note:** This mod is heavily vibe-coded. If you're wondering how some of this works... no idea 😭.

## Features

* Custom encounters for major Terraria bosses, including the Eye of Cthulhu, King Slime, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops, Wall of Flesh, Queen Slime, the Twins, the Destroyer, Skeletron Prime, Plantera, Golem, Duke Fishron, Empress of Light, Lunatic Cultist, and Moon Lord.
* Generic encounters for other bosses, including modded bosses.
* Regular enemies can fight in squads of up to three.
* Event enemies can battle as groups from their invasion or event.
* Boss-specific ACTs, healing items, timed FIGHT attacks, DEFEND, and loot-preserving SPARE.
* Different SOUL modes can appear during battles, including blue, green, purple, and yellow behavior.
* Optional local Deltarune sprites, fonts, sound effects, SOUL assets, and battle music, with Terraria-built fallbacks when assets are unavailable.
* Multiplayer party battles for up to three players. Multiplayer is experimental and may have bugs.
* Most battle behavior, enemy battles, music, Deltarune asset loading, sound redirects, volumes, fight damage, and party color can be adjusted through the Mercy Mode client config.

## Controls

Battle keys can be rebound in **Settings > Controls**.

* Arrow keys: move through battle menus and move the SOUL during enemy turns.
* Z: confirm selections and perform the FIGHT timing attack. It also fires during the yellow SOUL mode.
* X: go back in menus; hold during enemy turns to move the SOUL more slowly.
* J: join a nearby multiplayer battle when available.
* ACT, SPARE, and Heal Prayer use configurable keybinds.
* Esc: open the normal pause menu.

## Deltarune Assets

Deltarune asset loading is optional and can be disabled in the **Mercy Mode client config**.

The config provides:

* **Use Deltarune Assets**: enables or disables the integration.
* **Deltarune Folder**: optionally specifies the local installation folder.
* **Chapter**: selects which chapter's data to prefer when multiple chapters are installed.
* **Battle Sound Volume** and **Battle Music Volume**: control the imported Deltarune audio levels.
* **Sound Redirect** options: choose whether different groups of battle sounds use Deltarune sounds, Terraria sounds, or silence.

When the folder is left blank, MercyMode automatically searches common Steam installation locations, including additional Steam libraries. The macOS Steam location is supported by the automatic finder as well.

MercyMode reads compatible data files from the local installation at runtime. It can extract the required sprites, fonts, sound effects, SOUL graphics, and battle music into memory for the current game session. It does not copy those Deltarune assets into the mod package.

If the required assets cannot be found or loaded, MercyMode uses built-in fallbacks instead.

Run:

```text
/drassets
```

in chat to see the current asset-loading status, including loaded sounds, sprites, fonts, SOUL variants, and battle music.

Run:

```text
/drassets reload
```

to reload the Deltarune assets after changing the relevant configuration.

## Steam Workshop

The intended Workshop package contains the MercyMode mod itself and does not contain Deltarune game assets.

Deltarune is a separate copyrighted work owned by its respective rights holders. MercyMode does not grant users access to Deltarune or provide copies of its assets.

The runtime asset-loading system is intended for users who already have a legitimate local Deltarune installation. The code does not authenticate a Steam purchase, so this is an intended-use requirement rather than an ownership check performed by the mod.

## Build

Requirements:

* Windows or macOS. Linux paths are also accounted for by the Steam-library finder, but are not currently a primary tested platform.
* .NET 8 SDK.
* An installed tModLoader setup with its mod build targets.

Open a terminal in this mod's source directory and run:

```bash
dotnet build -c Release
```

tModLoader's build target compiles and packages the mod. The package is written to the active tModLoader Mods folder.

For testing, close tModLoader before building if the existing mod package is currently being used by the game, since packaging can fail when the file is locked.

## Manual Testing

There is no automated test project. For in-game checks, use a separate tModLoader save directory so test battles do not affect regular saves.

The `/mmbattle` chat command is a developer helper:

* `/mmbattle kit` gives test items.
* `/mmbattle` starts an Eye of Cthulhu battle.
* `/mmbattle npc <id|name>` starts a battle with a selected NPC.
* `/mmbattle end` ends a test battle.

See [MODLOG.md](MODLOG.md) for current implementation notes, the recorded test setup, and previously verified scenarios.

## Source and Asset Notes

`Deltarune/DataWin.cs` contains a GameMaker `data.win` reader based on UndertaleModTool's file layout and QOI handling. The file is marked GPL-3.0, and this repository includes the GPL-3.0 license.

No Deltarune game assets are included with this project or its mod package.

MercyMode's source code and original assets are distributed under the GPL-3.0 license included in this repository. Deltarune remains the property of its respective rights holders.
