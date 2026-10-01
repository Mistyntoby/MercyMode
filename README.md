# MercyMode

MercyMode is a single-player tModLoader mod that turns Terraria boss and optional regular-enemy encounters into Deltarune-inspired turn-based battles. Outside battle, build MERCY with ACT, spare a boss at 100%, graze attacks to gain TP, and spend TP on Heal Prayer.

Battles pause the Terraria world and offer FIGHT, ACT, ITEM, SPARE, and DEFEND turns. On enemy turns, dodge bullets with the SOUL; bullets can hurt the player's real health, while grazing them builds TP. Spared bosses still drop loot.

## Features

- Custom encounters for the Eye of Cthulhu, King Slime, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops, and Wall of Flesh.
- Generic encounters for other bosses and configurable encounters for regular enemies.
- Boss-specific ACTs, healing items, timed FIGHT attacks, DEFEND, and loot-preserving SPARE.
- Optional local Deltarune sprites, fonts, sounds, and music, with fallbacks when those assets are unavailable.
- Single-player only; multiplayer is not supported.

## Build

Requirements: Windows, the .NET 8 SDK, and an installed tModLoader setup with its mod build targets. Open PowerShell in this mod's source directory and run:

```powershell
dotnet build -c Release
```

tModLoader's build target compiles and packages the mod. The package is written to the active tModLoader Mods folder. To build for testing, close tModLoader first; packaging may fail if the game is holding the mod file open.

## Controls

Battle keys can be rebound in **Settings > Controls**:

- Arrow keys: move through battle menus and move the SOUL during enemy turns.
- Z: confirm and use the FIGHT timing attack.
- X: go back in menus; hold during enemy turns to move the SOUL more slowly.
- Outside battle, ACT, SPARE, and Heal Prayer use configurable keybinds (default F, G, and V).

## Deltarune assets

If Deltarune is installed, the mod can load assets from that local installation; the mod does not include or distribute Deltarune assets. Use the Mercy Mode config to select the install folder, chapter, or disable asset loading. Run `/drassets` in chat to see the load status, or `/drassets reload` to reload assets.

## Manual testing

There is no automated test project. For in-game checks, use a separate tModLoader save directory so test battles do not affect regular saves. The `/mmbattle` chat command is a developer helper; `/mmbattle kit` gives test items, `/mmbattle` starts an Eye of Cthulhu battle, and `/mmbattle npc <id|name>` starts a battle with a selected NPC. Use `/mmbattle end` to end a test battle.

See [MODLOG.md](MODLOG.md) for the current implementation notes, the recorded test setup, and previously verified scenarios.

## Source and asset notes

`Deltarune/DataWin.cs` notes that its GameMaker data reader is based on UndertaleModTool and is licensed under GPL-3.0. No Deltarune game assets are included with this project.