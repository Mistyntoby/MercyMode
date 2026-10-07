# Mercy Mode MODLOG

## Goal (2026-09-30)
Deltarune-style turn-based battle for bosses. Touching/hitting a supported boss freezes the world
(NPC + projectile PreAI -> false), locks the player, and opens a battle screen: FIGHT / ACT / ITEM /
SPARE / DEFEND on the player turn, a bullet box with the SOUL on the enemy turn. Keep the existing MERCY,
TP and data.win asset loader. Eye of Cthulhu first; the full loop has to work in game before any other boss.

## Paths
- Mod source: `Documents/My Games/Terraria/tModLoader/ModSources/MercyMode`
- tModLoader: `C:\Program Files (x86)\Steam\steamapps\common\tModLoader` (tML 2026.7.3.0), log in `tModLoader-Logs/client.log`
- Deltarune: `C:\Program Files (x86)\Steam\steamapps\common\DELTARUNE` (launcher `data.win` + `chapter1..5_windows/data.win`, `mus/*.ogg`)
- Decompiles (outside any repo): `~/deltarune-decomp/ch1/CodeEntries` (UTMT CLI 0.9.2.0 in `~/deltarune-decomp/utmt`),
  `~/tml-decomp` (ilspycmd 8.2 of tModLoader.dll types)
- Save backups: `um backup` names `tml-players`, `tml-worlds` (~/.universal-modder/backups). Restore:
  `um backup restore tml-worlds`.
- Lab: tModLoader launched with `-tmlsavedirectory` pointing at a separate lab folder + throwaway world.

## Route
Loader API (tModLoader): ModSystem state machine + UI layer, GlobalNPC/GlobalProjectile PreAI freeze,
ModPlayer control lock. Deltarune numbers read from the chapter 1 decompile, re-implemented (no GML copied).

## Bugs found in existing code
- Asset loader picked the launcher `data.win` (chapter 0, 8 sprites): with Chapter = 0 ("auto") the ordering
  treated chapter 0 as the *preferred* chapter. Fixed: auto = newest real chapter first, launcher last.

## Deltarune battle facts (chapter 1 decompile; 30 FPS -> Terraria 60 ticks = x2 time, /2 per-tick speed)
Resolution 640x480. All sizes below in those pixels.
- SOUL (`obj_heart`): `global.sp = 4` px/frame -> 2 px/tick. Holding X (button2): `ceil(4*0.5)=2` px/frame -> 1 px/tick.
  Sprite `spr_dodgeheart` 20x20 origin (0,0), mask `spr_dodgeheartmask` bbox L2 R17 T2 B17 (16x16 inset 2).
- Box (`obj_growtangle`, sprite `spr_battlebg_0` 75x75 origin 37,37): grows over `maxtimer = 15` frames (30 ticks)
  to scale 2 -> 150x150, rotating 180->360, alpha 0.5->1. SOUL clamp (Step_2): x in [left+5, right-22],
  y in [top+5, bottom-22] (SOUL x/y = sprite top-left).
- Invincibility: `scr_damage` sets `global.inv = global.invc * 40` (invc = 1) -> 80 ticks. Hittable only while inv < 0.
  Blink: image_speed 0.25 while inv > 0 -> frame flip every 4 frames = 8 ticks. `snd_hurt1` on hit.
- Graze (`obj_grazebox`, mask `spr_grazemask` 50x50 centred on SOUL centre = SOUL x+10,y+10). Only while inv < 0.
  First touch: `tension += grazepoints`, `turntimer -= timepoints`, `snd_graze`, grazetimer = 10.
  Still touching: `+grazepoints/20` and `-timepoints/20` per frame. Turn cut only while turntimer >= 10 frames.
  `obj_regularbullet` default grazepoints 5, timepoints 5, inv 60.
- TP: `maxtension = 250`. Mod TP is 0-100 -> x0.4. DEFEND: `scr_tensionheal(40)` = +16% TP, damage
  `ceil(2*dmg/3)` (charaction 10). FIGHT hit: `+round(points/10)` tension.
- FIGHT bar (`obj_attackpress`): bolt speed 8 px/frame (4 px/tick), single-member first bolt at frame 29
  (`30 + boltxoff`, boltxoff starts -1). Bolt x = bar.x + 80 + (boltframe - boltx)*8. Box 15*8 = 120 wide
  from x+78. Press window `close < 15 && close > -5` frames. Points: 0 off = 150 (yellow burst, crit),
  1 = 120, 2 = 110, >=3 = 100 - 2*off. Bolt dies when close < -5 -> MISS (0 points).
  Damage `round(AT*points/20 - DF*3)` (heroparent Alarm_1). Post-attack: timermax 50 frames then fade 0.08/frame.
- Enemy turn length (`global.turntimer`): most common 120, 150, 140, 180 frames -> ~300 ticks.
- HUD: bottom panel `bpy = 152` px tall, slides in 30 px/frame (ease when <40 left), border colour
  merge(purple, black, .7) then merge(.., dkgray, .5). Buttons spr_btfight/act/item/spare/defend 31x32 at
  x+15/50/85/120/155, y = 485 - bp. Text at (30, 376) with fnt_mainbig.
- Fonts: fnt_main (8bitoperator JVE 12), fnt_mainbig (24), fnt_small, fnt_tinynoelle, fnt_dotumche.
- Useful sprites: spr_heart 16x16 (menu cursor), spr_pressfront 75x38, spr_pressspot, spr_attackspot 10x38,
  spr_tensionbar 25x196, spr_tensionfilling, spr_grazeappear 50x50 (4f), spr_attack_cut1, spr_ponman_eyebullet 12x12,
  spr_smallbullet 16x16, spr_healsparkle, spr_sparestar, spr_numbersfontbig 20x20 (10f).
- Sounds: snd_menumove, snd_select, snd_hurt1, snd_graze, snd_damage, snd_laz_c, snd_criticalswing, snd_power,
  snd_spare, snd_error, snd_cantselect, snd_weaponpull_fast, snd_battleenter, snd_item. Music `mus/battle.ogg`.

## tModLoader API notes (2026.7)
- `Player.Hurt(PlayerDeathReason, int dmg, int dir, bool pvp=false, bool quiet=false, int cooldownCounter=-1, bool dodgeable=true, float armorPen=0, float scalingArmorPen=0, float knockback=4.5f)`,
  returns 0 if `immune` (cooldown -1) or `ModPlayer.ImmuneTo` true.
- `NPC.SimpleStrikeNPC(int dmg, int dir, bool crit=false, float kb=0, DamageClass=null, bool variation=false, float luck=0, bool noPlayerInteraction=false)` applies defense.
- NPC update: `AI()` then `position += velocity` -> PreAI=false alone doesn't stop movement, zero velocity too.
- `dotnet build` in the mod folder compiles even while tML runs (only packaging fails with TML003).

## Lab / test loop
- Lab save dir `%USERPROFILE%\tml-lab` (Mods, Players/<character> copy, Worlds/MercyLab small world, seed `mercylab`).
  Pristine world snapshot: `um backup restore mercylab-world --to %USERPROFILE%/tml-lab/Worlds --yes`.
- Launch: `dotnet tModLoader.dll -tmlsavedirectory "%USERPROFILE%\tml-lab" -skipselect "YourCharacter:MercyLab"` from the tML folder.
  Build with `dotnet build -c Release` in the mod folder (game must be closed), then copy
  `Documents/My Games/Terraria/tModLoader/Mods/MercyMode.tmod` into the lab's Mods.
- `tml-lab/drive.sh` has helpers: `wait_phase`, `drive`, `shot`, `last` (reads `Battle phase ...` debug lines in client.log).
- `/mmbattle [spawn [dx dy] | kit | night | tp <0-100> | bosshp <n>]` for repeatable tests.
- Driving gotchas: two taps of the same key back to back merge into one press (put `hold 0x87 60` between);
  chat must be closed for battle keys to work.

## Verified in game (2026-09-30, tML 2026.7.3.0, assets from chapter 5)
- Start by touch ("by touch" in log, after the hitbox fix) and by hit ("by hit by Wooden Arrow", boss 2796/2800).
- FIGHT: miss after 34 frames; timed presses scored 110 and 120 points (TP +4.4 / +4.8 matches round(points/10)*0.4).
- ACT: Check/Stare/EyeDrops/Dawn raise MERCY 30/35/25, repeats halve; Heal Prayer uses 32% TP.
- ITEM: potions consumed, heal + "HP was maxed out" text. SPARE at 100% -> loot, "has been defeated", achievement.
- Defeat via FIGHT -> YOU WON, gore + loot. Death in battle -> battle closes, boss AI resumes.
- Grazing gives TP and shortens the turn; DEFEND +16% TP. HP frozen outside battle damage/heals.
- Rude Buster plays: game-audio capture cross-correlates with mus/battle.ogg at 0.77 (noise floor 0.01).

## Bugs found while testing (fixed)
- `GlobalNPC.CanHitPlayer` runs for every hostile NPC every tick *before* the hitbox check -> battle started the
  moment the Eye spawned. Now also requires `npc.Hitbox.Intersects(player.Hitbox)`.
- Natural life regen kept healing during battles -> battle owns `statLife` (only bullets / ITEM / Heal Prayer change it).
- Hidden chat layer: pressing Enter opened invisible chat and froze battle input -> chat layer stays visible.
- MercyUI's world TP gauge and SOUL drew over the battle -> skipped while a battle is active.
- Long Terraria item names overlapped in the 2-column ITEM grid -> single scrolling column.
- Toolkit (universal-modder, uncommitted): `um win shot` failed because ffmpeg master segfaults after writing the
  frame (accept a fresh file, add `-update 1`); `um win record` captured audio from the wrong `dotnet` PID
  (now prefers the process with a window).

## Open / next
- FIGHT damage is weapon damage x points/20 (Deltarune's AT formula). With a copper shortsword that's ~22 per hit
  vs 2800 HP; `FightDamageMultiplier` config exists. Decide on a default before adding more bosses.
- More bosses: add a `BossBattle` subclass + `BossBattle.Create/HasBattle` entries.

## Multi-monster battles (2026-09-30)
- Flicker: `screenFade` stepped past 1.0 and back every Deltarune frame (0.9/1.0), dimming veil + enemy
  on alternate frames. Measured from a recording (enemy region 82 vs 91). Now steps toward the target; frame-to-frame
  change 0.013 (bg) / 0.1 (enemy idle bob).
- `Encounter` (was BossBattle) + `EncounterRegistry`: Members (worm segments, Creepers, Skeletron hands, WoF parts,
  Twins), combined HP, StrikeTarget skips shielded parts, group Spare. Parts/minions resolve to their boss.
- Custom: EoC, King Slime, EoW, BoC, Queen Bee, Skeletron, Deerclops, WoF. GenericBoss for the rest.
  `Enemies.cs`: families by aiStyle (Slime, Fighter, Flier, Caster, Worm, Generic). Config `BattlesWithEnemies`.
- `Patterns.cs`: Rain, SideShots, AimedBursts, Bouncers, LaneDash, ClosingRing, Walls, Snake, Homing, FloorSpikes,
  Orbiters, Swoopers, Combo. Bullets can use any NPC/projectile texture (`Shots.Npc/Proj`).
- Facts: EoW only drops boss loot if the last segment has `boss = true` (set in `DropEoWLoot`) -> Spare sets it.
  Town NPC shots (Guide's arrows) have the local player as owner -> check `projectile.npcProj`.
  Slime textures are grey, tinted by `npc.color`. Daytime Skeletron sets damage 9999 -> bullets cap at 2x `defDamage`.
  Deerclops ice spike projectile texture is a sheet of several spikes -> drawn by hand.
  Command-spawned worms/BoC need ~20 ticks of AI to build their parts -> `/mmbattle npc` queues the start.
- Verified in lab: every boss above + Twins (generic), Blue/Green Slime, Zombie, Demon Eye, Cave Bat, Goblin Sorcerer,
  Giant Worm, Harpy, Antlion: battle starts, menu, enemy turn, no exceptions. EoW and BoC spares -> "has been defeated".
  BoC FIGHT hits a Creeper first. Slime: two ACTs -> 100% MERCY -> spare.
- `/mmbattle npc <id|name>`, `spawnnpc`, `end`, `heal`, `mercy <n>` for tests.

## Hero and Deltarune animations (2026-09-30)
- Player drawn on the battle screen where a lone Kris stands (scr_encountersetup: 80,140; feet ~116,216) with
  Main.PlayerRenderer.DrawPlayer at scale 1.5 inside our Deltarune-space matrix (Immediate sort for dyes). DrawPlayer
  scales around the hitbox bottom-centre and samples world lighting -> BattlePlayer.ModifyDrawInfo sets true colours
  while BattleSystem.DrawingHero. Poses via bodyFrame/legFrame (0 stand, 1-4 use, 5 jump) + weapon drawn by hand.
- From the decompile: obj_moveheart/obj_returnheart (8-frame flights from kris.x+10, kris.y+40; alpha +0.334/frame;
  obj_heartburst at start/end), obj_dmgwriter (vspeed -5..-7, hspeed 10 decaying, 2 bounces, stretch 0.2->1, fade
  after 35 frames; Kris colour merge(aqua, white, .5); spr_battlemsg 0 MISS 1 DOWN 2 MAX 3 UP),
  heroparent states (attack hit at alarm[1]=10 frames, item at 15, hurt shift -20+hurtindex*10 for 15 frames),
  obj_shake (4 px, flips, -1/frame), obj_spareanim, obj_deathanim (red, blocks peel right), obj_healanim (lime
  spr_sparestar_anim), obj_battleback (black + bg_battleback1 tiled twice: +0.5/frame at half alpha, -1/frame),
  obj_encounterbasic (party flies to battle spots over 10 frames), crit spr_lightfairy sparkles.
- Verified with a recorded take (frames at 20 fps): fly-in, heart out/back + bursts, hurt number on the hero,
  item rise + heal stars + MAX, FIGHT thrust + slash + aqua number, MISS, spare flash/stars/streak, death dissolve,
  fly-back at the end.

## Heal timing, SOUL death, outro lighting, attacks (2026-10-01)
- Heal: the sound plays in PlayHealFx with the stars + green number (was on the key press, ~0.5 s early). Item use
  frame 15 -> 8, potion rise 4 frames, pose 16 frames; Heal Prayer heals 4 frames into the ACT hop.
- Death: BattlePlayer.PreKill returns false during a battle and queues Phase.Death (deferred out of the bullet loop).
  Black over everything, SOUL alone, crack at frame 20 (snd_break1, spr_heartbreak), shatter at 50 (snd_break2,
  6 spr_heartshards with gravity), End(killPlayer) at 95 -> Player.KillMe in the world. Music cut on the hit.
  Terraria's other zero-HP checks (poison/drowning regen loops) call KillMe every tick -> PreKill keeps returning false.
- Music: starts at BattleMusicVolume, no fade-in; outro still fades out.
- Glide: hero/enemy colours blend toward Lighting.GetColor at their world spot by 1 - FlyProgress (intro start,
  outro end); hero stays solid. World copies hidden while Active (HideDrawLayers skipping headOnlyRender, which the
  nameplate portrait also goes through; GlobalNPC.PreDraw for encounter.DrawNpc). Outro waits for trail.Count == 0.
- Attacks (`PatternsAdvanced.cs`): Converge (spear ring on the SOUL), Beam (telegraph line, then beam; segment
  collision via Bullet.HitTest, DestroyOnHit false), Slam (column warning, drop, floor shockwave + debris, shake),
  Sprinkler (fan or spiral; harmless while fading in inside the box), GapRows (zigzag gap), Fireworks (shell -> ring).
  Bullets spawned from OnUpdate are queued until the bullet loop ends (List modified during foreach otherwise).
  Waiting (StartDelay) bullets no longer hit. Sounds: AttackSfx roles bulletappear/bulletfire/impact/explosion
  (Deltarune names guessed: snd_spearappear, snd_spearrise, snd_impact, snd_badexplosion; Terraria fallbacks),
  roars on charges (ForceRoar/Roar), Item12/Item33 lasers.
- Every custom boss got 5-6 attacks (generic boss 9) with harder phase-2 variants; enemy families got 1-2 more.
- Test: `/mmbattle turn <n>` picks the next attack, `/mmbattle hp <n>` (then take a hit) for the SOUL death.
  `/mmbattle heal` now also works mid-battle.
- Ambience (owls, birds, frogs, wind, rain, waterfalls) is all SoundType.Ambient, scaled by Main.ambientVolume
  (ActiveSound.Update and Main's ambient cues). `AmbienceMute` sets it to 0 from Start to End; an On_Main.SaveSettings
  detour writes the real value, since Terraria saves settings from ~10 places (exit, options menu...).
- Command icons (from the DELTAModKit decompile, scr_charbox): the nameplate draws spr_headkris at frame
  global.faceaction. Frames: 0 head, 1 FIGHT sword, 2 magic waves, 3 ITEM bag, 4 DEFEND shield, 5 hurt, 6 ACT waves,
  7 menu heart, 8 grey head, 9 Zzz (down), 10 SPARE X. Set when the command is confirmed, reset at the next player
  turn (scr_turn). 32x24, origin 0,0. Head 0 stays the player's own portrait.
- FIGHT bar: spr_pressfront (75x38) is Kris's head (left 40 px) + "Z"; only the Z part is drawn, with the player's
  head portrait where Kris's was.
- 60 fps: UpdateHudFrame/UpdateHeroFrame and every BattleEffect now run every tick, stepped by
  FrameStep = 0.5 Deltarune frames (BattleEffect.Frame() -> Step(dt)). Per-frame eases become
  EasePerTick(f) = 1 - (1 - f)^0.5 (same spot after a frame). TP bar slide uses its closed form
  -40 + 13n - n(n+1)/2 (n frames, stops at 38). Hurt shift uses hurttimer / 2 unrounded (GameMaker division
  isn't integer, so Deltarune slides it too). Shake swings with cos(pi * tick / 2) instead of jumping.
  Afterimages still spawn once per frame but fade per tick. Event timings (heal, crack, shatter) unchanged.
- Death timing: hard cut to black (no fade), SOUL alone 30 frames (1 s), crack, 30 more frames, shatter,
  then 45 frames of falling shards before Player.KillMe.
- Full-screen attacks (Roaring Knight style; the DELTAModKit decompile is chapters 1-2 only, so this is built
  from how the fight plays, not its code): EnemyAttack.FullScreen opens Box into FullScreenArena (10,10 620x460:
  the whole battle screen, inset 10 px) with an eased arenaBlend. Its border fades out as it opens (only the
  collision stays). Everything else, panel and TP bar included, is drawn first and covered in black; only the arena
  border, bullets and SOUL are drawn on top. Beams are 1640 px long so they cross the screen from anywhere.
  Patterns built on Box fill it. The outro holds until the arena has closed (clamping the SOUL in), then does the
  normal spin. `Slashes`: bursts of sharp beams (Beam.Make sharp: full width at once, thins out, only hurts for
  the first half), the first through the SOUL, staggered 9 ticks, FIGHT slash sound + shake. Used by EoC/Skeletron/
  Deerclops in phase 2, WoF always, generic bosses in Hardmode or under half HP. Turn 300 frames (~10 s).
- FIGHT rework (`BattleSystem.Fight.cs`): FIGHT -> weapon list -> enemy -> bar. `Weapons()` is the single source
  for the stat, the menu and the sprite (fixes held-vs-hotbar mismatch). Every damaging inventory item except
  ammo, accessories and summon staves (whips stay); tools sorted last; bare hands (5) if none.
  ShotDamage = GetWeaponDamage, or PickAmmo's damage (weapon + ammo) for useAmmo weapons. Bolts =
  clamp(round(45 / useAnimation), 1, 4); HitShare = (TurnSeconds 8 * 60 / useAnimation) / bolts, so a perfect turn
  = ~8 s of the weapon's Terraria DPS whatever its speed. Hit = ShotDamage * HitShare * points/150 * config,
  then SimpleStrikeNPC with Terraria's crit roll (GetWeaponCrit, x2) and the item's DamageClass; defense applies.
  Each pressed bolt pays: PickAmmo(dontConsume: false) (ammo-saving effects apply), ItemLoader.ConsumeItem for
  throwables, CheckMana(pay: true) (mana flower applies); unpaid = fizzle. Weapons with no ammo / mana are greyed.
  Bolts spaced 12 or 18 frames like obj_attackpress (boltframe 30 + boltxoff, diff 12); a press scores the alive
  bolt in the window with the lowest close (scr_boltcheck_onebutton). TP per hit / bolts. Menu: ATK = perfect-turn
  damage, arrows vs the current weapon, hits, crit, ammo/mana. Choosing a hotbar weapon selects it for real.
  Est. turns to kill EoC (normal, def 12) at ~80% timing: copper shortsword 20, gold broadsword 11, gold bow 14,
  musket 7, minishark 4.5. Tune TurnSeconds in BattleSystem.Fight.cs.
- Shots (guns, bows, spells, throwables): item.UseSound, recoil (weapon tips up, hero rocks back 6 frames), muzzle
  flash, the projectile sprite flying 10 frames to the enemy (with a streak for invisible magic shots), impact sparks.
  `/mmbattle kit` now also gives a Flintlock Pistol + Musket Balls, Wand of Sparking and Shurikens.
- Hardmode bosses (`Encounters/HardmodeBosses.cs`, base `HardmodeBoss`): Queen Slime, Twins, Destroyer, Skeletron
  Prime, Plantera, Golem, Duke Fishron, Empress of Light, Lunatic Cultist, Moon Lord. 6-7 attacks each from their
  real moves; most get a full-screen attack in phase 2 (Destroyer, Duke, Empress, Moon Lord always). Parts and
  minions resolve to their boss in `EncounterRegistry.ResolveRoot`. New pattern `SweepBeam` (rays rotating round a
  pivot): Moon Lord's deathray from above the screen, Empress's Sun Dance (rainbow arms round the arena centre).
  Facts: Moon Lord head/hands "die" by going back to full life with ai[0] = -2 (closed, dontTakeDamage, spawns a
  True Eye); the core goes back to full life with ai[0] = 2 and its AI plays the death + loot. Members() skips
  those, so the HP bar falls and the fight ends; the real death runs once the world unfreezes. Its parts have no
  contact damage -> Damage 70 x EnemyDamageMultiplier. Golem's body shield and Moon Lord's core shield are dropped
  in StrikeTarget (paused AI). Spare keeps the loot part (Golem body, Moon Lord core, Prime head).
  Multi-part / special-drawn bosses (Skeletron, Deerclops, Queen Slime, Twins, Prime, Golem, Empress, Moon Lord) set
  Encounter.DrawWithTerraria: `BattleSystem.EnemyDraw.cs` draws every DrawParts() NPC with Main.DrawNPCDirect in one
  batch whose matrix maps world pixels around the anchor NPC to the battle screen (scaled to fit ~230x210), with
  Main.screenPosition pointed at it (some draw code reads it instead of the argument) and Main.gameMenu set so
  Lighting.GetColor returns white (chains, arms). GlobalNPC.DrawEffects tints for the glide; PreDraw lets these
  through while BattleSystem.DrawingEnemy and hides the world copies otherwise. ForceOpaque (Empress) zeroes alpha
  for the draw: the battle can freeze her mid fade-in. No selection flash or afterimages on these; spare/death
  animations use the main part's sprite only.
- Composite sizing: hitboxes/frames badly underestimate what's drawn (Moon Lord's torso filled the whole screen),
  so Encounter.CompositeSize sets a minimum world size per boss, fitted into CompositeArea (default 260x250, capped
  at the hero's 1.5x). Measured from screenshots: Empress ~180x200, Golem ~250x250 (first guesses were ~2x too
  big, so they came out tiny). Now: Empress 190x200, Golem 260x250, Prime 320x260, Skeletron 300x220, Twins
  240x160, Deerclops 200x240, Queen Slime 180x150; Moon Lord 1400x1150 into 380x310 at (440,160). The overhead MERCY bar (MercyGlobalNPC.PostDraw) is skipped while DrawingEnemy.
- Moon Lord core: ai[0] = -1 for its first 60 ticks (rising), then it spawns the hands (800 px apart, 100 up) and
  head (400 up). Battles wait for ai[0] >= 0 (Eligible); `/mmbattle npc` retries for up to 5 s instead of starting
  at 20 ticks. Head and hands have npc.hide (the normal pass skips them; Main.CacheNPCDraws draws them with the
  core) but DrawNPCDirect draws them fine.
- Composite draw order matches Main.DrawNPCs: behindTiles pass first, each pass from slot 199 down to 0 (lower
  slots on top). Ascending order put Golem's head behind its body.
- Composite parts are drawn with IsABestiaryIconDummy set and an Immediate batch: the Empress's draw ends the batch
  and begins one with Main.Transform (world camera) for her dyed wings unless she's a Bestiary icon, which threw her
  body into the screen corner and broke our matrix. ForceOpaque also on Golem (head was half transparent).
- Single-sprite enemies: DrawScale = npc.scale x 1.5 (the hero's scale), capped to fit 220x200. Was hitbox height
  x 1.5 / frame height, which made most enemies (Eater of Souls...) too small since sprites exceed hitboxes.
- Music: boss battles (Encounter.IsBoss) don't create the Rude Buster instance, and BattleMusicScene (Music 0,
  BossHigh) is only active while that instance exists, so Terraria's own boss-track selection plays (vanilla,
  modded ModNPC.Music, Otherworldly). Config BossBattleMusic (default on); off = Rude Buster everywhere. Regular
  enemies keep Rude Buster.
- Config defaults from play-testing at Music 100%: BattleSoundVolume 0.45, BattleMusicVolume 0.25.
- Rude Buster: BattleMusicVolume x RudeBusterGain 0.45 (it was louder than the boss tracks at Music 100%). A constant
  rather than a new config default, since a saved config keeps its old value.
- Boss music boost: AmbienceMute.BoostMusic(config BossMusicBoost, default 1.6) raises Main.musicVolume for boss
  battles (capped at 1), restored at End unless the player moved it; the SaveSettings detour saves the real value.
- Explosion/impact SFX: DeltaruneAssets.PlayFading plays an instance (or a Terraria slot via ActiveSound.Volume)
  and fades it after a hold (explosion x0.5, hold 8, fade 14 ticks; impact x0.7, 10/16).
- Prime: one arm per type (this head's, nearest to it); leftovers from earlier Primes pointing at a reused head slot
  were floating around as extra cannon/saw/vice.
- The Dungeon Guardian never starts a battle (Eligible): vanilla behaviour. As a regular enemy it was 1000-damage
  bullets vs 9999 HP/defense, or a two-ACT spare that skipped the pre-Skeletron Dungeon barrier.
- Skeletron's DrawCenter raised to (500, 150): its bones hang below the parts the bounds measure.
- PoseForBattle runs before the bounds are measured (it used to run after, so centring used the unposed world
  layout and Skeletron sat low). Skeletron's hands: raised beside the head like its spin phase
  (-120 * ai[0], head.position.Y - 60); its rest spot (+230) looked like a zombie walk. Raised hands are turned
  over (rotation pi, spriteDirection = ai[0] instead of -ai[0]) so the fingers point up.
- Encounter.PoseForBattle poses composite parts for the draw (position, rotation, frame, spriteDirection; all
  restored). Arms/hands use their AI's rest spots, since the bones are drawn from the part toward fixed points by
  the head (Main.DrawNPCDirect: segments of 92 + 60 px aimed at head -200/-50 * ai[0], +130/+80) and come apart
  anywhere else: Skeletron hands (aiStyle 12) at head.Center.X - 120 * ai[0], head.position.Y + 230; Prime saw/vice
  (33/34) -200 * ai[0], +230; cannon/laser (35/36) -120 * ai[0], -100, rotated so the barrel points away from the
  first bone (aimed left at the party they came off their bones: the bone start doesn't follow the rotation). Members only take arms
  whose ai[1] is this head (a leftover second Prime's arms were being drawn too). Deerclops: frame.Y is a cell of a 5x5 sheet (Main.DrawNPCDirect_Deerclops:
  Frame(5,5,Y/5,Y%5)); FindFrame: 0 stand, 1 air, 2-11 walk (velocity-driven, so 0 while frozen), 12-17/18 roar
  attacks, 19-24 rubble attack. Battle: walk cycle in place, roar while attacking.
- Deerclops rubble (projectile 962): texture is Frame(projFrames, 4) (a column per shape, 4 rows; Main.DrawProj),
  drawn whole it showed a grid of rocks. Now one random cell.
- Composite idle animation: s * (1 + 0.015 sin(t/45)) breathing, and each non-anchor part offset by sin/cos sway of
  1.8% of the boss's size, applied to the NPCs' positions for the draw (restored after) so connectors follow.
- Widescreen / rounded-down scale: the bottom panel's black now reaches the window bottom (DrawPanel(bottom)).
- `/mmbattle npc` matches internal names (spaces ignored: "moon lord core") and, when display names collide
  ("Moon Lord" is the head, hands and core), prefers the boss / custom-encounter type.
- Enemy families: Water (Piranha, Jellyfish), Spider (Spider, Herpling), Mimic (+ biome mimics), Charger (Unicorn,
  Giant Tortoise, Sand Shark), Spirit (Cursed Skull, Dungeon Spirit, Ancient Vision), Blade (Enchanted Sword),
  Snapper (Man Eater, Antlion).
- Events: regular-enemy battles during invasions, Pumpkin/Frost Moon, eclipse and Old One's Army are army squads
  (below); config `EventBattles` (default on, replaces `BattlesDuringEvents`). Eternia Crystal and lane portals
  never start battles.
- Balance: boss ACT MERCY x0.65 (`Encounter.BossMercyScale`) so sparing a boss takes ~6-8 turns like beating it.
  FIGHT damage x sqrt(EnemyMaxLifeMultiplier): Expert fights ~1.4x Normal length, Master ~1.7x (not 2x/3x).
- Built in the cloud against tModLoader's release DLLs (0 warnings, 0 errors); beam collision unit-tested.
  NOT yet verified in game: needs the lab loop above.

## Enemy squads and armies (2026-10-01)
- Regular battles pull in up to 2 more eligible enemies within 640 px of the player (`BattleSystem.Enemies.cs`,
  `GatherEnemies`); during an event only the same army. Bosses always fight alone. Each enemy is a `BattleEnemy`
  (its encounter, glide-in spot, snapshot, spare/death animation, shake, afterimages, `Out`).
- Formation: 2 enemies at (470,135)/(545,235) fitting 160x120; 3 at (455,100)/(550,180)/(455,262) fitting 130x95
  (`Encounter.Slot`/`SlotArea`; `ScreenCenter` is the slot or the old DrawCenter).
- FIGHT/ACT/SPARE pick a target (Up/Down in the enemy list, one row per living enemy with HP and MERCY).
  Defeating or sparing one marks it Out; the battle goes on to the enemy turn while any are left; YOU WON only when
  none are. SPARE glows if anyone is spareable.
- Enemy turn: every living enemy attacks when there are 2, two random ones when there are 3, layered (`Combo`).
  `OwnedAttack` runs each with its own enemy as `battle.Encounter` and tags bullets (`Bullet.Owner`), so a hit uses
  the shooter's damage and name.
- Armies (`Encounters/Armies.cs`): Goblins, Pirates, Frost Legion, Martians, Pumpkin Moon, Frost Moon, Old One's
  Army, Eclipse, each with its own CHECK, lines, ACTs and attacks (per type: goblin archers shoot arrows, sorcerers
  homing chaos balls). Sparing one gives the rest of its squad +30 MERCY ("the war isn't worth it"); defeating one
  enrages the rest (`EnemyEncounter.Enraged`: attacks as Hard).
- Spared regular enemies go through `NPC.checkDead` (death sound muted) instead of `NPCLoot`, so they count toward
  invasion / event progress like kills. Bosses keep `NPCLoot`.
- Defeating one of a group skips the "was defeated" box and goes straight to the enemy turn; the rest of a multi-hit
  FIGHT carries on into the next living enemy. The slash only draws on the enemy hit.
- During a battle enemies can't be hit by items or projectiles (`CanBeHitByItem/Projectile`); only FIGHT's
  SimpleStrikeNPC hurts them. Starting a battle cancels the player's swing and removes their in-flight shots
  (the swing that started a battle used to keep hitting the frozen enemy in the background).
- `/mmbattle group 3 zombie` or `/mmbattle group goblin peon, goblin archer, goblin sorcerer` spawns a squad and
  starts the battle; `/mmbattle end` ends every enemy in it.

## Headless lab (2026-10-01)
- `tools/lab/lab.sh test [scenarios]` (Linux / the cloud) and `tools/lab/lab.ps1 test` (Windows) build the mod, start
  a tModLoader dedicated server on a throwaway world with `MERCYMODE_LAB=<scenarios|all>`, and print the report
  (also `lab-results.txt` in the lab folder). Exit code 0 = all passed. `lab.ps1 client` is the old windowed lab.
- `Lab/LabSystem.cs` only runs on a dedicated server with that variable set. A server only updates the world while a
  client is connected, so the lab runs `Main.DoUpdate` itself from `Main.OnTickForThirdPartySoftwareOnly`, keeps
  player 0 active as the local player (stubs `Netplay.UpdateConnectedClients`), counts as single-player
  (`MercyMode.IsSingleplayer`), feeds keys through `Main.keyState`, and gives every texture a 48x48 pixel-less
  stand-in (`Asset<Texture2D>.DefaultValue`). No drawing or sound is tested; every battle rule is.
- Scenarios: `squad-fight` (3 zombies, kill one at a time, 2 then 1 attackers), `goblin-squad-spare` (morale line,
  +30 MERCY, invasion 80 -> 77), `army-enrage` (pirates), `act-second-target`, `boss-fights-alone` (EoC ignores
  nearby zombies), `single-enemy`, `boss-kill`, `boss-kill-king-slime`, `boss-spare`, `no-world-hits`,
  `multi-hit-spills-over`. All pass (2026-10-01, tML 2026.8.3.0).
- Bugs the lab caught on the way: none in battle rules; server-only crashes (texture sizes, view matrix, fonts) now
  have fallbacks (`Main.dedServ`).

## SOUL modes, new attacks, effects (2026-10-01)
- `EnemyAttack.Soul` (`.WithSoul(mode)`), `Battle/BattleSystem.Soul.cs`. A `Combo` shares its parts' mode (green only when all
  parts are green, since the shield SOUL can't dodge).
  - Yellow is Deltarune's own (chapter 2, `scr_miscbattle_config` soul mode data + `obj_yheart_shot` in the DELTAModKit
    decompile): Z fires right at 8 px/tick, max 3 shots; release after holding 10-39 ticks also fires; hold 40 ticks and
    release for a big shot (4 px/tick, +0.1/tick, 4 damage, pierces). Sprites `spr_yellowheart` (frame 2 = charged),
    `spr_yheart_shot`, `spr_yheart_bigshot`; sounds `snd_heartshot_dr_b`, `snd_chargeshot_charge/_fire`. Shots break
    bullets (`Bullet.Toughness`, default 1, or 3 for big ones), +0.8 TP each.
  - Blue / green / purple are Undertale's (Deltarune has none of them), from how Undertale plays, not decompiled:
    blue gravity 0.18, jump 4.8 (cut to 1.5 on release), max fall 7: a held jump is ~54 px, a tap ~15 px; `SlamSoul()`
    throws it down. Green: fixed at the box centre, arrows turn a 30x6 shield 19 px out that destroys bullets (+0.6 TP).
    Purple: 3 strings at 1/4, 2/4, 3/4 of the box; Up/Down hop, Left/Right slide.
  - Who uses them: blue for King Slime / Queen Slime bounces and slams, Skeletron (bone walls + slam), Deerclops (spikes,
    boulder), Golem (slam, stone pillars), slimes' slam, zombies' horde, goblin/frost legion/Old One's Army marchers.
    Green: Queen Bee (stingers), Plantera (seeds). Purple: Brain of Cthulhu (Creepers on strings), spiders.
    Yellow: Twins (Retinazer), Destroyer (probes), Skeletron Prime (cannons), Martians (drones).
- New patterns (`PatternsSpecial.cs`): ShieldSpears (with tricksters that jump sides), StringRunners, BoneWalls,
  Gunships, Ricochet, Splitter, Lobs (ballistic, floor marker, splash), Diver, Blinker, Walkers, Jaws, `Phasing()`.
  Every family and army got a signature attack; EoC servants dive, EoW Eaters dive, WoF's mouth (Jaws), Cultist clones.
- Boss desperation: below 30% HP (one enemy left) a boss says "is fighting with everything it has left!" and its next
  turn is two of its attacks at once, 1.25x as long, bullets at 0.75x damage; once per battle.
- Effects (`AttackEffects.cs`, drawn with the bullets over the box): `Bullet.Trail` afterimages (dashes, slams, dives,
  homing, converging), `Sparks`, `Shockwave` rings, `Puff` smoke; emitters `.Smoking()` (rockets, cannonballs, bombs),
  `.Fiery()`, `.Sparkly(color)`, `.Dripping(color)`. Slams kick up dust and a ring; fireworks/splitters burst with a ring;
  bouncers puff on the floor; warnings fill in with a solid edge; a SOUL mode change rings and chimes.
- Fixed: `RepeatingAttack` never ran on tick 0 (the turn's first Update is tick 1), so every pattern started one interval
  late and attacks that spawn only once (spirits' Orbiters) spawned nothing. Found by the lab sweep.
- Lab: `soul-blue/green/purple/yellow`, `desperation`, and `attacks-enemies/armies/bosses` (every attack of every family,
  army and boss: spawns something, ends, no crash). `LAB_SPEED` runs the game faster than real time (default 8).

## Breakable bosses, stab, purple strings, yellow charge (2026-10-01)
- Bosses with parts that have their own health (`Encounter.TargetableParts`): Skeletron (head, LEFT/RIGHT HAND by
  hand ai[0]), the Twins (RETINAZER, SPAZMATISM; no core, both must go), Skeletron Prime (PRIME, CANNON, SAW, VICE,
  LASER), Golem (GOLEM body, HEAD, fists; the body is GUARDED until the head breaks), Moon Lord (HEAD, hands, HEART
  guarded until every eye is shut). FIGHT's enemy list shows one row per part with its own HP (3 rows visible,
  scrolling); ACT/SPARE still target the boss. The picked part is `Encounter.ChosenPart`; the other parts dim on screen.
  Breaking a part bursts it (shockwave, sparks, explosion) and the rest of a multi-hit FIGHT moves to the next part;
  breaking the core (`CorePart`) removes the rest. Hit effects land where the part was drawn (`partScreen`).
  Attacks follow: Skeletron's head does the hands' attacks once both are gone, a lone Twin only uses its own,
  each Prime arm's attacks go with it. Wall of Flesh isn't split: its eyes share the mouth's health.
- Shortsword / spear stab: the composite arm carries the blade (Quarter stretch wind-up, then ThreeQuarters/Full out,
  hold, back; 2/2/3/5 Deltarune frames), the body leans 4 px in, and a white streak flicks off the point.
- Purple SOUL strings stretch out from the SOUL as the turn starts (harp twang, Item26), quiver as a standing wave when
  plucked or landed on, and pull back into it as the turn ends.
- Yellow SOUL charge as in Deltarune's soul mode code: from z_hold 15 four spr_yheart_charge sparks spiral in from 35 px,
  from 35 the SOUL pulses (two glow layers), the snd_chargeshot_charge hum loops from 20, fading in and rising in
  pitch to 40; shots that hit play spr_yheart_shot_hit; max 3 shots including big ones.
- Worms (Destroyer, Eater of Worlds, regular worm enemies) are drawn whole with Terraria's renderer: `BossKit.WormChain`
  follows the segments from the head (ai[1] = segment ahead), shortened to 14 / 16 / 10 with the tail kept, and
  `PoseWorm` lays them out as a slithering S leading left. Before, only the head sprite showed. Lab `worm-chains`.
- Lab: `parts-skeletron`, `parts-twins`, `parts-golem`.

## Multiplayer party battles (2026-10-01, untested with real clients)
- `Battle/Net/BattleNet.cs` (packets + server round), `Battle/Net/NetSystems.cs` (colours, join prompt, outside view),
  `Battle/BattleSystem.Net.cs` (battle-screen side). Each client runs its own battle screen; the server runs the round.
- Starting: touching/hitting an enemy sends `RequestBattle`. The server pulls in up to 2 more players within 20 tiles of
  the starter (party of 3), freezes ONLY that battle's NPCs (roots + `Members()`, velocity restored
  after), and sends `JoinBattle` + `Party`. Touching a frozen enemy does nothing (no contact damage).
- Joining later: near a battle with room, outsiders see "Press J to join the battle!" (keybind "Join Battle"). They
  open the battle screen as watchers (`Pending`), see everything, and jump in at the next bullet box.
- Round: (1) Choosing: each player's menu pick is stored (`Commit`), not run; "* Waiting for X to choose...". (2)
  Acting: the server sends `TurnOf` to each player in party order; that client runs its action (FIGHT bar, ACT text,
  ITEM, SPARE, DEFEND) and its text lines go to the others' text boxes (`PartyText`); `ActionDone` passes the turn.
  (3) Enemy turn: `BeginEnemyTurn` with a shared seed and round; `Main.rand` is swapped for the seeded one while the
  attack is built and spawns, and every enemy's `Turn` is set to the round, so every screen gets the same attack.
  SOUL positions go out every 2 ticks (`SoulPos`) and are drawn in each player's colour. Timeouts: 60 s to choose
  (the rest go ahead), 45 s per action.
- Limits of the shared box: bullets aimed at "the SOUL" aim at YOUR soul on your screen, and anything a hit/graze
  changes (bullets destroyed on hit, effects) is local, so screens drift apart within an attack. Hits only count on
  your own screen. True lockstep would need every input round-tripped and would lag.
- Colours (`PartyColors`): config `PartyColor` (Automatic = by join slot: cyan, magenta, green, yellow, orange, blue,
  white). Replaces Kris cyan in your own UI (`KrisCyan` is now a property); others' boxes, HP bars and SOULs use their
  colour. Synced with `ModPlayer.SyncPlayer/SendClientChanges`.
- Synced: enemy HP (`SimpleStrikeNPC`), MERCY (delta to server, total broadcast), SPARE (server runs
  `Encounter.Spare()`, party gets `playerInteraction` for bags), core-part kills, FIGHT numbers (`PartyHit`; allies'
  attack pose).
- Outside view: `BattleState` goes to every client. Outsiders see fighters swing at the enemy when a hit lands (only
  their local copy of the remote player animates; nothing is hit), harmless dust "bullets" from the enemies at the
  fighters during the bullet box, and "IN BATTLE" over fighters. Frozen enemies can't be hit; fighters are immune.
- In multiplayer only the battle's NPCs freeze; projectiles and time keep going. `/mmbattle end` leaves the battle.
- Lab `mp-server`: party pick-up (near starter or enemy) and the 3 cap, freezing, choose barrier, turn order and that
  only the acting player can pass it, watcher queued then promoted at the bullet box, acting player leaving, both
  timeouts, MERCY cap, spare + bag credit, KillMembers scope, unfreeze. The client side has NOT been run.
- Known gaps: enemy HP isn't scaled for party size; enemy pose tweaks are local and can snap on NPC sync.

## Log
- 2026-09-30: recon, decompile, numbers above. Implemented battle loop for Eye of Cthulhu, verified in lab (above).
- 2026-10-01: enemy squads (up to 3 per battle), armies with squad morale, spares count toward events; headless lab.
- 2026-10-01: SOUL modes (Deltarune yellow; Undertale blue/green/purple), new patterns and effects, boss desperation; lab sweeps of every attack.
- 2026-10-01: breakable boss parts as FIGHT targets; shortsword stab; purple string and yellow charge animations.
- 2026-10-01: multiplayer party battles (server bookkeeping lab-tested; clients untested).
- 2026-10-02 (0.6): removed the outside-battle ACT/SPARE/Heal Prayer keybinds, the world TP gauge/SOUL overlay (MercyUI) and world grazing; TP is only built in battles now.
- 2026-10-02 (0.9): allies glide in/out, cheer on a win, walk off left when leaving mid-battle; party pull-in 20 tiles from the starter, join prompt 25 tiles from the battle; buildIgnore keeps notes/scripts out of the .tmod; Workshop description refreshed.
- 2026-10-02 (0.10): GPL-3.0 LICENSE; battle keys rebindable (MercyMode.BattleKeys, Deltarune keys mapped in Pressed/Held); party pull-in 7 tiles, prompt 8 tiles; won battles go to Stage.Over (no join, no IN BATTLE, no more turns: fixed a stray MISS from a queued FIGHT after the win); YOU WON! auto-continues after 5 s; allies always walk off when they leave first (also when they leave the game); 5 s no-battle grace after entering a world; FIGHT bar bolts/bursts fade with the bar; no waiting text when alone.

## PvP duels (0.59, untested with real clients)

- Start: a PvP hit makes the victim's client send DuelOffer; the server opens a 10 s challenge for both (50 tile range). Both press the Join Battle key (n/2) and the server sends DuelStart; the one who was hit goes first.
- The server only relays (DuelRelay) between the two and sends DuelEnd when one leaves or disconnects.
- The opponent is a stand-in NPC at Main.npc[200] (never updated, drawn or synced) so FIGHT, HP bars, MERCY and the glide reuse the enemy code. It is never struck; the hit goes to the other player as real damage (their defense already counted on the attacker's side). FIGHT and pieces do half damage against players.
- Turns alternate: A chooses and acts, then A dodges while B builds the attack live (click to place, drag to aim, ink meter, 15 s, DONE). Then B chooses, and so on. Pieces come from the builder's weapons (slash, thrust, arrows, spray, orb, minion, bounce, shot) and warn for 36 ticks before firing.
- Losing is a real death (Terraria's PvP death). SPARE at 100% MERCY ends it peacefully for both.
- 0.62: builder shapes (SINGLE/LINE/RING/RAIN/FAN/STREAM, Q/E or buttons; cost x2.2-3.2; pieces carry a Delay so lines sweep and rain falls in turn), scrollable weapon palette (wheel, 1-9, arrows), battle keys accept Mouse1-5 bindings, duel damage numbers in the attacker's colour.
- 0.63: composite arm angles times the facing (left-facing stab/defend/item arms), beam weapons (Last Prism, Charged Blaster Cannon) fire a beam in FIGHT and are held out (no blade pose), duel pieces BEAM and BOOM (rockets, grenades), spawn puffs and orb trails, the builder can force the dodger's SOUL mode (6 s cooldown), battle background scrolls on a real-time clock snapped to screen pixels.
- 0.64: beam weapons in FIGHT are HOLD Z (200 ticks, a hit every 16 while held, up to 6-12 hits, mana every use time, charge over ~110 ticks raises damage, width and pitch; Item15 hum quickening); the held Last Prism draws its spinning holdout aimed at the enemy; duels mirror the held beam (BeamState); FIGHT shots spark in their weapon kind's colour and explosives blow up on impact, like the duel pieces.
- 0.65: challenges carry an id (a press never carries over to the next challenge with the same player; that left both sides at 1/2), a completed challenge clears stale battle/duel entries instead of failing silently; beams drawn additively (DrDraw.Additive, DrDraw.Glow): six swirling Last Prism rays that close in and merge into one pulsing rainbow beam at full charge, glows at both ends, sparks and motes; the aim slides between targets; louder hum, a zap per hit, a flash and rumble when the rays meet.

## Deltarune extras (0.74)

- Signature attacks (Battle/Encounters/EnemySignatures.cs): ~35 enemies matched by internal name (variants together) get their own attack and speech lines; used on turns 0, 3, 6... instead of the family attack. Lab: attacks-signatures (26 types, 1 turn each) plus the family sweep (which hits 9 more).
- Speech bubbles: every regular enemy says a line as its turn opens (family lines, or its signature lines; "...so... tired..." when TIRED).
- TIRED: a regular enemy under 34% HP (or WornOut) shows its name blue, says it looks TIRED, and drifts z's. PACIFY (ACT, 16 TP) spares a TIRED enemy at once.
- RECRUITS: removed for now (0.90).
- Several enemies attacking together: each RepeatingAttack fires 1.6x/2.2x less often and their volleys are staggered (two zombie walls with separate gaps at once left no way through).
- FIGHT numbers stack per enemy (hits carried over to the next enemy start at its spot).
- 0.142: Skeletron has 19 moves. New: gravity flip (blue SOUL, floor and ceiling swap after an arrow), shrinking box (Grow below 1) with bone rows, hand clap on the SOUL's row, bouncing water bolts, Dungeon Guardian chase plus bone rain, bone wheel, cursed skull ring dive, closing bone cage, wall spikes, and a clockwise skull-blaster ring (SkullBlasters.Sweep). Eater: segments spaced by the body's hitbox width (as the worm AI does), the Underground turn lasts 1.5x with a 1.45x box, and each crack flashes purple over the dark for ~24 ticks before fading to its seam.
- 0.143: Skeletron has no full-screen (dark) arena any more. Blue SOUL gravity goes any of four ways (BattleSystem.Gravity, SetGravity) and the SOUL sprite turns so its point faces the way it falls. Bullet.Platform: the blue SOUL lands on platforms from above and rides them (gravity down only). New BonePlatforms (Undertale platform bit over a floor of bones) and SideFall (bones, then gravity swings right and the SOUL falls past Skeletron heads streaking by; also its one-time finish below 40% HP). Most Skeletron turns are now two patterns at once (18 moves). Lab: soul-gravity.
- 0.144: Skeletron's bullets hit 0.8x (DamageFactor), since most of its turns now run two patterns at once.
- 0.145: Skeletron's SideFall is a real fall now. Gravity swings right, the box stretches past both screen edges (fixed -1000..1700, so any widescreen and every party member get the same box), the SOUL is held still (only up/down) while the hero and Skeletron slide off left and the background pans. Bone walls with a wandering gap and skulls come from the right; Skeletron teleports in behind the box every 150 ticks and drifts back. After ~10 s the box's right wall arrives, the SOUL slams into it, and the box, hero and Skeletron come back. Camera API: BattleSystem.BoxOverride, HoldSoulX, HeroShift, EnemyShift, EnemyFade, SceneScroll (reset when the enemy turn ends). Lab: side-fall.
- 0.146: the fall's stretched box sits 60 px lower (SideFall.Lower) so Skeletron's face shows over it. EnemyAttack.Scripted: grazing no longer cuts the turn short (it ended the fall before the wall arrived); Combo, OwnedAttack and the desperation Sequence pass it on.
- 0.147: fall box 75 px lower, Skeletron raised 45-52 px while watching, so his whole skull shows over the box.
- 0.148: the fall is longer: gap walls and skulls (ticks 50-330 of the fall), then a bone tunnel (360-660): columns 12 px apart with a winding hole (52 px, 46 hard) that never moves faster than the SOUL can follow (2.6 px per column). The wall arrives after the tunnel; ~16 s of falling.
- 0.149: when the fall's SOUL hits the wall, every leftover fall bone keeps sliding left and fades out (no stragglers). Bones (BoneWalls.Bone) now draw with Bullet.Alpha.
