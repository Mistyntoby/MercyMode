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

## Log
- 2026-09-30: recon, decompile, numbers above. Next: implement battle loop for Eye of Cthulhu.
