# Mercy Mode MODLOG

## Goal (2026-09-30)
Deltarune-style turn-based battle for bosses. Touching/hitting a supported boss freezes the world
(NPC + projectile PreAI -> false), locks the player, and opens a battle screen: FIGHT / ACT / ITEM /
SPARE / DEFEND on the player turn, a bullet box with the SOUL on the enemy turn. Keep the existing MERCY,
TP and data.win asset loader. Eye of Cthulhu first; the full loop has to work in game before any other boss.

## Paths
- Mod source: `Documents/My Games/Terraria/tModLoader/ModSources/MercyMode` (OneDrive Documents)
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
- Lab save dir `C:\Users\Nico\tml-lab` (Mods, Players/nick copy, Worlds/MercyLab small world, seed `mercylab`).
  Pristine world snapshot: `um backup restore mercylab-world --to C:/Users/Nico/tml-lab/Worlds --yes`.
- Launch: `dotnet tModLoader.dll -tmlsavedirectory "C:\Users\Nico\tml-lab" -skipselect "nick:MercyLab"` from the tML folder.
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
- Encounter.PoseForBattle poses composite parts for the draw (position, rotation, frame, spriteDirection; all
  restored). Arms/hands use their AI's rest spots, since the bones are drawn from the part toward fixed points by
  the head (Main.DrawNPCDirect: segments of 92 + 60 px aimed at head -200/-50 * ai[0], +130/+80) and come apart
  anywhere else: Skeletron hands (aiStyle 12) at head.Center.X - 120 * ai[0], head.position.Y + 230; Prime saw/vice
  (33/34) -200 * ai[0], +230; cannon/laser (35/36) -120 * ai[0], -100, rotated to aim left. Members only take arms
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
- Events: no regular-enemy battles during invasions, Pumpkin/Frost Moon, eclipse, Old One's Army or the pillars
  (`EncounterRegistry.EventActive`); bosses still start. Config `BattlesDuringEvents` (default off).
- Balance: boss ACT MERCY x0.65 (`Encounter.BossMercyScale`) so sparing a boss takes ~6-8 turns like beating it.
  FIGHT damage x sqrt(EnemyMaxLifeMultiplier): Expert fights ~1.4x Normal length, Master ~1.7x (not 2x/3x).
- Built in the cloud against tModLoader's release DLLs (0 warnings, 0 errors); beam collision unit-tested.
  NOT yet verified in game: needs the lab loop above.

## Log
- 2026-09-30: recon, decompile, numbers above. Implemented battle loop for Eye of Cthulhu, verified in lab (above).
