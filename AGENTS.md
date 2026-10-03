# MercyMode agent notes

## 1. What MercyMode is and what it is supposed to do

MercyMode is a single-player tModLoader mod for Terraria that turns boss and (optionally) regular-enemy encounters into Deltarune-inspired turn-based battles. Everything happens in the battle (the old outside-battle ACT/SPARE/Heal Prayer keys and world TP bar were removed in 0.6). Battles pause the Terraria world, offer FIGHT / ACT / ITEM / SPARE / DEFEND turns, and use a dodge-bullet phase where bullets can damage the player's real health. Bosses still drop loot when spared.

If the player owns Deltarune, the mod can read sprites, fonts, sounds, and music from that local installation at startup. No Deltarune assets are shipped with this mod. Without those assets, the mod uses fallbacks. Multiplayer battles exist (party of up to 3, see MODLOG "Multiplayer party battles") but have only been tested on the server side in the headless lab.

## 2. Tech stack and exact commands

- C# / .NET SDK-style project targeting `net8.0`; tModLoader 2026.7.3.0 is recorded in `MODLOG.md`. `MercyMode.csproj` imports the sibling `..\tModLoader.targets`, which in turn imports the installed tModLoader `tMLMod.targets`.
- tModLoader's MSBuild target compiles the assembly and packages the mod as part of `dotnet build`.
- From the mod source directory (`%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\MercyMode`):

```powershell
dotnet build -c Release
```

A successful build packages `MercyMode.tmod` at `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\Mods\MercyMode.tmod`, where the regular tModLoader installation can load it.

- To install/update the isolated lab copy after building:

```powershell
Copy-Item "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\Mods\MercyMode.tmod" "%USERPROFILE%\tml-lab\Mods\MercyMode.tmod" -Force
```

- To launch the throwaway lab world (run from the tModLoader installation directory):

```powershell
Set-Location "C:\Program Files (x86)\Steam\steamapps\common\tModLoader"
dotnet tModLoader.dll -tmlsavedirectory "%USERPROFILE%\tml-lab" -skipselect "YourCharacter:MercyLab"
```

- Headless lab (automated battle tests, no window): `tools/lab/lab.sh test [scenario,...|all]` on Linux or `.\tools\lab\lab.ps1 test` on Windows. It builds, runs scripted battles on a throwaway dedicated server and exits 0 when every scenario passes. It covers battle rules, not drawing or sound. Scenarios live in `Lab/LabSystem.cs`; see MODLOG "Headless lab". `lab.ps1 client` opens the windowed lab below.
- Apart from the headless lab there is no test project. The verified test loop is to build, update the lab `.tmod` using the copy command above, launch the lab, then use the in-game chat command `/mmbattle kit` and `/mmbattle eye` (or `/mmbattle npc <id|name>`) to test. `/mmbattle` lists the commands; `/mmbattle eye` starts the Eye of Cthulhu battle; use `/mmbattle end` to end a test battle.
- `MODLOG.md` contains further lab setup, test commands, and recorded manual verification details. The recorded commands assume the named local tModLoader install, lab folder, character, and world still exist.

## 3. Project structure

- `MercyMode.csproj` — project definition and tModLoader target import.
- `build.txt`, `description.txt`, `icon.png` — tModLoader mod metadata, player-facing description, and icon.
- `MercyMode.cs` — mod lifecycle, keybind registration, colors, boss lookup, and shared helpers.
- `MercyConfig.cs` — client-side settings for battles, enemy battles, FIGHT damage multiplier, and Deltarune asset loading/selection.
- `MercyPlayer.cs` — the player's TP (built and spent in battles).
- `MercyGlobalNPC.cs` — boss MERCY state, trust loss after damage, overhead MERCY display, and loot-preserving spare behavior.
- `ActLines.cs` — flavor ACT text keyed by Terraria NPC type.
- `Battle/` — battle state, UI and drawing, hero animation, world freeze/control lock, projectiles/bullets, attack patterns, and test chat command.
  - `BattleSystem.cs` — battle lifecycle, phases, input, combat, battle UI, and music.
  - `BattleSystem.Hero.cs` — Terraria player rendering, weapon pose, and hero-side battle effects/animations.
  - `BattleFreeze.cs` — NPC/projectile freeze and player lock/immune rules during battles.
  - `Encounter.cs` — encounter abstraction, registry, and shared battle behavior.
  - `Encounters/EyeOfCthulhu.cs` — Eye of Cthulhu encounter.
  - `Encounters/Bosses.cs` — custom encounters for supported vanilla bosses.
  - `Encounters/Enemies.cs` — regular-enemy encounter families and generic boss encounter.
  - `Patterns.cs` — reusable bullet-pattern generators.
  - `Bullet.cs`, `BattleEffects.cs`, `BattleConstants.cs`, `DrDraw.cs` — battle projectile/effect models, constants, and rendering helpers.
  - `BattleSystem.Enemies.cs` — enemy squads: gathering, formation, targeting, combined enemy turns, squad morale.
  - `Encounters/Armies.cs` — event armies (goblins, pirates, moons, Martians...).
  - `BattleCommand.cs` — `/mmbattle` developer/test commands.
  - `Net/BattleNet.cs`, `Net/NetSystems.cs`, `BattleSystem.Net.cs` — multiplayer party battles (server round: choose, act in order, shared bullet box; colours; join prompt; outside view).
  - `Net/DuelNet.cs`, `BattleSystem.Duel.cs` — PvP duels: a PvP hit opens a challenge (join key, 0/2 → 2/2); alternate turns against the other player (drawn live, backed by a stand-in NPC at index 200 that is never synced); the waiting player builds the bullet box attack live from pieces based on their weapons; real death, or SPARE at 100% MERCY.
- `Deltarune/` — local Deltarune asset discovery/loading, `/drassets`, and GameMaker `data.win` reader.
  - `DeltaruneAssets.cs` — discovers a local install and loads sprites, fonts, sounds, and music with fallbacks.
  - `DataWin.cs` — binary asset reader; its file header records the UndertaleModTool/GPL-3.0 basis.
  - `AssetsCommand.cs` — `/drassets` status and reload command.
- `Lab/LabSystem.cs`, `tools/lab/` — headless lab runner and launch scripts.
- `Localization/en-US_Mods.MercyMode.hjson` — English localization strings.
- `MODLOG.md` — project design/technical notes, decompile-derived gameplay values, lab instructions, verified scenarios, and known balance decisions.
- `MercyMode.txt` — local Claude Code transcript; intentionally ignored by Git and should not be committed.

## 4. Important decisions made and why

- Preserve the existing MERCY, TP, and Deltarune `data.win` asset-loader concepts while adding turn-based battles; this was the original feature request and is reflected in the current code.
- Re-implement observed Deltarune mechanics and numeric values from decompilation rather than copying GameMaker code. Convert the original 30-FPS timing to Terraria's 60 ticks per second. The values and rationale are in `MODLOG.md`.
- Freeze NPCs and projectiles with `GlobalNPC.PreAI` / `GlobalProjectile.PreAI`, also zeroing velocity because Terraria still applies velocity after AI; lock player controls while a battle runs.
- Start with Eye of Cthulhu and verify the complete loop in a separate lab world before adding more encounters. Custom and generic encounters were added only after the initial loop was verified.
- Multiplayer: each client runs its own battle screen; the server only owns the party, frozen NPCs, the ready barrier, MERCY, spares and part kills (`Battle/Net/BattleNet.cs`). Only the battle's NPCs freeze in multiplayer.
- Keep Deltarune assets external and user-provided; do not package them in the mod.
- In automatic asset selection, prefer the newest actual chapter and try the launcher `data.win` last because the launcher file only has chapter-select assets.
- Keep the lab save folder separate from the user's ordinary saves to reduce testing risk.

## 5. What's done and working

The current source and `MODLOG.md` record the following as implemented; the log reports manual verification in tModLoader 2026.7.3.0 on 2026-09-30:

- Outside battle: boss MERCY, ACT flavor text, trust loss when a boss is hit, SPARE with boss loot/downed-state processing, TP, graze gain, Heal Prayer, keybinds, config, and status UI.
- Battle loop: touch/hit starts, world freeze, player controls are locked, player/enemy turns, FIGHT timing/damage, ACT, healing ITEM, SPARE, DEFEND, bullet dodging/grazing, player death handling, and return to the Terraria world.
- Encounter coverage: custom battles for Eye of Cthulhu, King Slime, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops, and Wall of Flesh; generic handling for other bosses; enemy-family battles can be disabled in config. The log records lab checks for the listed custom bosses, Twins, and several regular-enemy families.
- Deltarune asset discovery and loading, asset status/reload command, and fallbacks.
- Hero and enemy battle animations, including intro/outro glide, weapon positioning/animation, battle-start and delayed music timing, and music fade behavior. The final transcript says these latest changes were checked frame-by-frame in recordings and built into the local mod.
- `/mmbattle` test helpers and recorded manual test procedures.

The latest observed Release build completed successfully with 0 warnings and 0 errors.

## 6. What was in progress when the last session ended

The transcript ends after the existing glide/weapon/music changes had been reported as verified and committed. The next planned visual/audio pass had only just begun: inspect Deltarune chapter 2+ behavior, identify the gold number font, then add a yellow `+X%` MERCY popup with `snd_mercyadd`. A wider panel, shake adjustments, kill sounds, sound-redirection configuration, and volume calibration were also listed in a plan. The transcript reports rate-limit interruptions before those changes could be completed.

This plan is not present in the actual current source: `Deltarune/DeltaruneAssets.cs` has no `mercyadd` sound role and `MercyConfig.cs` has no sound-redirection setting. Treat these as unfinished, not implemented. The source is authoritative if later transcript details disagree.

## 7. Known bugs, errors, or TODOs

- FIGHT balance (2026-10-01): a perfect turn deals about 8 seconds of the weapon's Terraria DPS (`TurnSeconds` in `Battle/BattleSystem.Fight.cs`); `MercyConfig.FightDamageMultiplier` (default `1.0`) scales it. `MODLOG.md` lists estimated turns-to-kill; needs in-game playtesting.
- Multiplayer battles are untested with real clients.
- The MERCY popup and the planned sound-redirection/volume controls from the final transcript are not implemented in the current source.
- No automated tests are present; rely on the isolated in-game test loop and release build.
- `MercyMode.txt` is the full local transcript and is ignored; do not stage or commit it.
- The local `.git` repository had no remote configured during this handoff. The requested GitHub repository must be connected and its README-only history merged before pushing. Preserve the local version for any actual merge conflict.
- `MODLOG.md` records older known test/tooling issues and says they were fixed; consult its “Bugs found while testing (fixed)” notes before re-investigating those issues.

## 8. Next steps, in priority order

1. Finish the MERCY `+X%` popup by confirming the chapter 2+ font/sound assets and adding the effect to the actual battle action flow.
2. Review the planned wider battle panel, shake behavior, kill sounds, sound redirection, and volume calibration; implement only after comparing the current code with the decompile and testing each change.
3. Decide and document a sensible default for `FightDamageMultiplier`; test FIGHT balance against early-game weapons and boss health.
4. Run the Release build and repeat the isolated in-game regression loop after each change, including spare/loot, defeat, player death, enemy battles, and asset fallbacks.
5. Test multiplayer battles with a host and a second client (`tools/lab/lab.ps1 server` + clients, or Host & Play); fix what breaks; consider scaling enemy HP with party size.
