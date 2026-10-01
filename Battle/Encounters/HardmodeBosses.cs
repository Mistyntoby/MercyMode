using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>Shared shape for the Hardmode boss encounters: three themed ACTs, flavour text, phase 2 below half HP.</summary>
	public abstract class HardmodeBoss : Encounter
	{
		protected abstract string Check { get; }
		protected abstract (string name, string desc)[] ActNames { get; }
		protected abstract string[] Lines { get; }
		protected virtual string HurtLine => $"* {Name} is badly hurt.";
		protected virtual string SpareLine => $"* {Name} doesn't want to fight anymore.";
		/// <summary>The NPC whose ACT lines (ActLines.cs) these are.</summary>
		protected virtual int ActLineType => Npc.type;

		protected bool Hard => LifeRatio < 0.5f;
		protected const int TurnTicks = BattleConstants.DefaultEnemyTurnTicks;
		protected const int FullTurn = BattleConstants.FullScreenTurnTicks;

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return SpareLine;
			if (LifeRatio < 0.3f)
				return HurtLine;
			return Lines[Turn % Lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle)
		{
			var acts = new List<ActOption> { CheckAct(Check) };
			float[] amounts = { 30f, 30f, 35f };
			for (int i = 0; i < ActNames.Length; i++)
				acts.Add(MercyAct(ActNames[i].name, ActNames[i].desc, amounts[Math.Min(i, 2)], ActLines.Get(ActLineType, i)));
			acts.Add(HealPrayerAct());
			return acts;
		}

		/// <summary>A full-screen attack (Roaring Knight style) built from parts.</summary>
		protected static EnemyAttack FullScreen(params EnemyAttack[] parts) => new Combo(FullTurn, parts) { FullScreen = true };
	}

	// ====================================================================== Queen Slime

	public class QueenSlime : HardmodeBoss
	{
		public override string Name => "QUEEN SLIME";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(220f, 200f);
		public override string EncounterText => "* QUEEN SLIME descends in a shower of crystals!";
		protected override string Check => "* The Hallow's royal slime. Sharper than she looks.";
		protected override (string, string)[] ActNames => new[] { ("Admire", "Call her\npretty"), ("Compare", "Unlike King\nSlime..."), ("Tiara", "Offer a\ntiara") };
		protected override string[] Lines => new[] {
			"* QUEEN SLIME sparkles menacingly.",
			"* Crystals chime somewhere inside her.",
			"* Smells like the Hallow and gelatin.",
		};

		private static readonly Color Crystal = new(255, 130, 230);

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet gem(Vector2 p, Vector2 v) => Shots.Ball(p, v, Crystal, 0.7f, 1.1f);
			Bullet blueGem(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(120, 180, 255), 0.7f, 1.1f);
			Bullet queen(Vector2 p, Vector2 d) => Shots.Npc(NPCID.QueenSlimeBoss, p, Vector2.Zero, 0.3f, 1.2f, new Vector2(44, 32), rotate: false);
			Bullet flier(Vector2 p, Vector2 v) => Shots.Npc(NPCID.QueenSlimeMinionPurple, p, v, 0.9f, 0.8f, new Vector2(14, 12), rotate: false).FaceTravel();
			return Cycle(
				() => new Bouncers(gem, Hard ? 18 : 24),
				// She leaps and lands on you; crystal gel ripples out along the floor
				() => new Slam(queen, blueGem, Hard ? 58 : 76) { Width = 50f, Shards = Hard ? 3 : 2, FallSpeed = 10f },
				() => new Converge(gem, Hard ? 50 : 64) { Count = Hard ? 10 : 8, Speed = Hard ? 4.6f : 4f },
				() => new Swoopers(flier, Hard ? 16 : 22) { Speed = Hard ? 2.8f : 2.3f },
				() => new GapRows(blueGem, Hard ? 28 : 34) { Speed = Hard ? 2f : 1.7f, GapSize = 44f },
				() => Hard
					? FullScreen(new Slashes(62) { Color = Crystal, PerBurst = 3 }, new Bouncers(gem, 30) { FirstAt = 40 })
					: new Fireworks((p, v) => Shots.Ball(p, v, Color.White, 0.9f, 1.8f), gem, 50) { Count = 8 });
		}
	}

	// ====================================================================== The Twins

	public class Twins : HardmodeBoss
	{
		private static readonly int[] Types = { NPCID.Retinazer, NPCID.Spazmatism };

		public override string Name => "THE TWINS";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(300f, 200f);
		public override string EncounterText => "* THE TWINS blink in perfect sync.";
		protected override string Check => "* Retinazer aims. Spazmatism burns. Neither listens.";
		protected override (string, string)[] ActNames => new[] { ("Stop", "Ask for\na break"), ("Focus", "Praise\nthe focus"), ("Pick", "Pick a\nfavourite") };
		protected override string[] Lines => new[] {
			"* THE TWINS circle each other.",
			"* Retinazer is charging something.",
			"* Spazmatism can't sit still.",
			"* Smells like hot metal and cursed flame.",
		};

		public override IEnumerable<NPC> Members() => BossKit.OfTypes(Types);

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet laser(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.EyeLaser, p, v, 1f, 0.7f, new Vector2(10, 6), rotationOffset: MathHelper.PiOver2);
			Bullet flame(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(120, 255, 60), 0.6f, 1.2f);
			Bullet spaz(Vector2 p, Vector2 d) => Shots.Npc(NPCID.Spazmatism, p, Vector2.Zero, 0.35f, 1.2f, new Vector2(30, 30), rotate: false);
			var red = new Color(255, 60, 60);
			return Cycle(
				// Retinazer locks on and fires
				() => new Beam(Hard ? 36 : 48) { Width = 10f, Warn = Hard ? 32 : 40, Active = 16, Color = red, FireSound = SoundID.Item33 },
				// Spazmatism's flamethrower sweeps from above
				() => new Sprinkler(flame) { Every = Hard ? 4 : 6, Arms = Hard ? 2 : 1, Speed = 2.6f, TurnSpeed = 0.07f, FanSpread = 0.9f },
				() => new LaneDash(spaz, Hard ? 46 : 60) { AllowVertical = true, Speed = Hard ? 9f : 7.5f, LaunchSound = SoundID.ForceRoar },
				() => new AimedBursts(laser, Hard ? 26 : 36) { Count = Hard ? 4 : 3, Speed = 3.2f, Spread = 0.25f },
				() => new Combo(TurnTicks,
					new Beam(60) { Width = 10f, Color = red, FireSound = SoundID.Item33 },
					new Sprinkler(flame) { Every = 9, Speed = 2.2f, TurnSpeed = 0.05f }),
				// Phase 2, full screen: both at once, everywhere
				() => Hard
					? FullScreen(new Slashes(60) { Color = red, PerBurst = 3 }, new Sprinkler(flame) { Every = 8, Arms = 2, Speed = 2.4f, TurnSpeed = 0.05f, FirstAt = 40 })
					: new AimedBursts(laser, 34) { Count = 3, Speed = 3f });
		}
	}

	// ====================================================================== The Destroyer

	public class Destroyer : HardmodeBoss
	{
		public override string Name => "THE DESTROYER";
		public override string EncounterText => "* THE DESTROYER tunnels in, segment after segment...";
		public override float DrawRotation(int time) => -BossKit.WormRotation;
		protected override string Check => "* A mechanical worm. Every segment has a laser. Every one.";
		protected override (string, string)[] ActNames => new[] { ("Ask", "Destroy\nless?"), ("Count", "Count the\nsegments"), ("Paint", "Praise the\npaint job") };
		protected override string[] Lines => new[] {
			"* THE DESTROYER hums with machinery.",
			"* Probes buzz around like angry flies.",
			"* It's still coming. There's more of it.",
		};

		public override NPC DrawNpc => BossKit.OfTypes(NPCID.TheDestroyer).FirstOrDefault() ?? base.DrawNpc;

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet head(Vector2 p, Vector2 v) => Shots.Npc(NPCID.TheDestroyer, p, v, 0.45f, 1.1f, new Vector2(18, 18), rotationOffset: BossKit.WormRotation);
			Bullet body(Vector2 p, Vector2 v) => Shots.Npc(NPCID.TheDestroyerBody, p, v, 0.45f, 0.9f, new Vector2(16, 16), rotationOffset: BossKit.WormRotation);
			Bullet laser(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.DeathLaser, p, v, 1f, 0.7f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			Bullet probe(Vector2 p, Vector2 v) => Shots.Npc(NPCID.Probe, p, v, 0.8f, 0.7f, new Vector2(14, 14), rotate: false);
			return Cycle(
				() => new Snake(head, body, Hard ? 70 : 95) { Segments = 12, SegmentLag = 5, Speed = Hard ? 3f : 2.5f },
				// Every segment fires down at once
				() => new Rain(laser, Hard ? 7 : 10) { SpeedMin = 2.6f, SpeedMax = 3.4f, Wobble = 0f },
				() => new Homing(probe, Hard ? 24 : 34) { Speed = 1.8f, Turn = 0.045f, SteerTicks = 100 },
				() => new Walls(laser, Hard ? 60 : 75) { Side = 0, Speed = 2.2f, Spacing = 14f, GapSize = 42f },
				() => new Combo(TurnTicks,
					new Snake(head, body, 110) { Segments = 10, Speed = 2.5f },
					new Rain(laser, 16) { SpeedMin = 2.4f, SpeedMax = 3f, Wobble = 0f }),
				// Full screen: it coils through the whole field from both sides while the lasers fall
				() => FullScreen(
					new Snake(head, body, Hard ? 80 : 100) { Segments = 14, Side = 1, Speed = 3f, FirstAt = 30 },
					new Snake(head, body, Hard ? 80 : 100) { Segments = 14, Side = -1, Speed = 3f, FirstAt = 70 },
					new Rain(laser, Hard ? 12 : 16) { SpeedMin = 2.6f, SpeedMax = 3.2f, Wobble = 0f, FirstAt = 40 }));
		}
	}

	// ====================================================================== Skeletron Prime

	public class SkeletronPrime : HardmodeBoss
	{
		private static readonly int[] Arms = { NPCID.PrimeCannon, NPCID.PrimeSaw, NPCID.PrimeVice, NPCID.PrimeLaser };

		public override string Name => "SKELETRON PRIME";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(560f, 420f);
		public override string EncounterText => "* SKELETRON PRIME whirs to life, four arms ready!";
		protected override string Check => "* Skeletron, but upgraded. Somebody gave it a saw.";
		protected override (string, string)[] ActNames => new[] { ("Arms", "Ask about\nthe arms"), ("Oil", "Oil a\njoint"), ("Upgrade", "Praise the\nupgrade") };
		protected override string[] Lines => new[] {
			"* SKELETRON PRIME's saw revs.",
			"* The cannon arm is aiming at you.",
			"* Smells like oil and bone dust.",
		};

		public override IEnumerable<NPC> Members()
		{
			if (Npc.active)
				yield return Npc;
			foreach (NPC arm in BossKit.OfTypes(Arms))
				yield return arm;
		}

		public override NPC DrawNpc => Npc.active ? Npc : base.DrawNpc;

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet saw(Vector2 p, Vector2 d) => Shots.Npc(NPCID.PrimeSaw, p, Vector2.Zero, 0.7f, 1.1f, new Vector2(24, 24), rotate: false).Spin(0.4f);
			Bullet head(Vector2 p, Vector2 d) => Shots.Npc(NPCID.SkeletronPrime, p, Vector2.Zero, 0.5f, 1.2f, new Vector2(34, 34), rotate: false).Spin(0.35f);
			Bullet rocket(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.RocketSkeleton, p, v, 1f, 0.9f, new Vector2(10, 10), rotationOffset: MathHelper.PiOver2);
			Bullet bomb(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.BombSkeletronPrime, p, v, 1f, 0.9f, new Vector2(14, 14), spin: 0.2f);
			Bullet spark(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(255, 170, 60), 0.6f);
			return Cycle(
				() => new LaneDash(saw, Hard ? 44 : 58) { AllowVertical = true, Speed = Hard ? 9f : 7.5f },
				() => new Homing(rocket, Hard ? 26 : 36) { Speed = 2f, Turn = 0.04f, SteerTicks = 80 },
				() => new Beam(Hard ? 40 : 52) { Width = 9f, Warn = 36, Active = 14, Color = new Color(255, 60, 60), FireSound = SoundID.Item33 },
				// The cannon lobs bombs that burst
				() => new Fireworks(bomb, spark, Hard ? 40 : 54) { Count = Hard ? 10 : 8 },
				() => new LaneDash(head, Hard ? 56 : 72) { AllowVertical = true, Speed = Hard ? 7.5f : 6f, LaneWidth = 40f, LaunchSound = SoundID.Roar },
				() => Hard
					? FullScreen(new Slashes(62) { Color = new Color(230, 230, 230), PerBurst = 4 }, new Homing(rocket, 50) { Speed = 2f, FirstAt = 40 })
					: new Combo(TurnTicks, new LaneDash(saw, 80) { Speed = 7f }, new Homing(rocket, 60) { Speed = 1.8f }));
		}
	}

	// ====================================================================== Plantera

	public class Plantera : HardmodeBoss
	{
		public override string Name => "PLANTERA";
		public override string EncounterText => "* PLANTERA bursts from its bulb!";
		protected override string Check => "* The jungle's guardian. Hooked into the walls. Very hungry.";
		protected override (string, string)[] ActNames => new[] { ("Water", "Water\nit"), ("Promise", "No more\nbulbs"), ("Sun", "Talk about\nsunlight") };
		protected override string[] Lines => new[] {
			"* PLANTERA's vines creak.",
			"* The jungle holds its breath.",
			"* Smells like pollen and teeth.",
		};
		protected override string HurtLine => "* PLANTERA's petals are falling off. Its jaws are out.";

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet seed(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.SeedPlantera, p, v, 1f, 0.7f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			Bullet poison(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.PoisonSeedPlantera, p, v, 1f, 0.8f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			Bullet thorn(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.ThornBall, p, v, 1f, 0.9f, new Vector2(14, 14), spin: 0.1f);
			Bullet tentacle(Vector2 p, Vector2 v) => Shots.Npc(NPCID.PlanterasTentacle, p, v, 0.8f, 0.8f, new Vector2(14, 14), rotate: false);
			Bullet bite(Vector2 p, Vector2 d) => Shots.Npc(NPCID.Plantera, p, Vector2.Zero, 0.35f, 1.2f, new Vector2(40, 40), rotate: false);
			return Cycle(
				() => new AimedBursts(seed, Hard ? 22 : 32) { Count = Hard ? 5 : 3, Speed = 2.8f, Spread = 0.3f },
				() => new Bouncers(thorn, Hard ? 26 : 34) { Bounce = 1f },
				() => new Converge(poison, Hard ? 50 : 66) { Count = Hard ? 10 : 8, Speed = Hard ? 4.4f : 3.8f, RotationOffset = MathHelper.PiOver2 },
				() => new Orbiters(tentacle, Hard ? 90 : 120) { Count = Hard ? 8 : 6, AngularSpeed = 0.03f },
				// Phase 2: it lunges with its jaws
				() => Hard
					? new LaneDash(bite, 46) { AllowVertical = true, Speed = 9f, LaunchSound = SoundID.Roar }
					: new GapRows(seed, 36) { Speed = 1.8f },
				() => Hard
					? FullScreen(new Slashes(62) { Color = new Color(120, 255, 80), PerBurst = 3 }, new AimedBursts(poison, 46) { Count = 3, Speed = 2.6f, FirstAt = 40 })
					: new Combo(TurnTicks, new Orbiters(tentacle, 130) { Count = 5 }, new AimedBursts(seed, 44) { Count = 3 }));
		}
	}

	// ====================================================================== Golem

	public class Golem : HardmodeBoss
	{
		private static readonly int[] Parts = { NPCID.Golem, NPCID.GolemHead, NPCID.GolemFistLeft, NPCID.GolemFistRight, NPCID.GolemHeadFree };

		public override string Name => "GOLEM";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(440f, 380f);
		public override IEnumerable<NPC> DrawParts() => BossKit.OfTypes(Parts);
		public override string EncounterText => "* GOLEM stirs in the temple's heart!";
		protected override string Check => "* An ancient Lihzahrd idol. Has been guarding for centuries.";
		protected override (string, string)[] ActNames => new[] { ("Stone", "Praise the\nstonework"), ("Cell", "Offer a\npower cell"), ("Rest", "Suggest\na break") };
		protected override string[] Lines => new[] {
			"* GOLEM's eyes glow like coals.",
			"* The temple shakes with every punch.",
			"* Smells like sunbaked stone.",
		};

		public override IEnumerable<NPC> Members() => BossKit.OfTypes(Parts);
		/// <summary>The body: it carries the loot, and it's the big sprite.</summary>
		public override NPC DrawNpc => BossKit.OfTypes(NPCID.Golem).FirstOrDefault() ?? base.DrawNpc;

		public override NPC StrikeTarget()
		{
			// The head comes first; the body's shield drops when it's gone (its AI is paused, so do it here)
			NPC body = BossKit.OfTypes(NPCID.Golem).FirstOrDefault();
			NPC head = BossKit.OfTypes(NPCID.GolemHead).FirstOrDefault(h => h.life > 0);
			if (head != null && !head.dontTakeDamage)
				return head;
			if (body != null)
			{
				body.dontTakeDamage = false;
				return body;
			}
			return base.StrikeTarget();
		}

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet fist(Vector2 p, Vector2 d) => Shots.Npc(NPCID.GolemFistLeft, p, Vector2.Zero, 0.8f, 1.1f, new Vector2(26, 22), rotate: false).FaceTravel();
			Bullet golem(Vector2 p, Vector2 d) => Shots.Npc(NPCID.Golem, p, Vector2.Zero, 0.3f, 1.2f, new Vector2(50, 40), rotate: false);
			Bullet fireball(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Fireball, p, v, 1f, 0.8f, new Vector2(10, 10), spin: 0.15f);
			Bullet stone(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(200, 140, 70), 0.7f, 1.2f);
			var orange = new Color(255, 160, 40);
			return Cycle(
				// Rocket punches across the box
				() => new LaneDash(fist, Hard ? 40 : 54) { Speed = Hard ? 10f : 8.5f, LaneWidth = 34f },
				// A ground pound: stone shockwaves along the floor
				() => new Slam(golem, stone, Hard ? 60 : 76) { Width = 56f, Shards = 3, Debris = 4, FallSpeed = 11f },
				// Eye beams
				() => new Beam(Hard ? 38 : 50) { Width = 12f, Color = orange, FireSound = SoundID.Item33 },
				() => new Rain(fireball, Hard ? 9 : 13) { SpeedMin = 2.2f, SpeedMax = 3f, Wobble = 0f },
				() => new Combo(TurnTicks, new LaneDash(fist, 70) { Speed = 9f }, new Rain(fireball, 18) { Wobble = 0f }),
				() => Hard
					? FullScreen(new Slashes(60) { Color = orange, PerBurst = 3 }, new Rain(fireball, 14) { Wobble = 0f, FirstAt = 40 })
					: new Slam(golem, stone, 70) { Width = 56f, Shards = 3 });
		}
	}

	// ====================================================================== Duke Fishron

	public class DukeFishron : HardmodeBoss
	{
		public override string Name => "DUKE FISHRON";
		public override string EncounterText => "* DUKE FISHRON rises from the sea, furious!";
		protected override string Check => "* Pig, fish and dragon. All of them are angry about the worm.";
		protected override (string, string)[] ActNames => new[] { ("Sorry", "Apologize\nfor the worm"), ("Stache", "Praise the\nmustache"), ("Release", "Free a\nfish") };
		protected override string[] Lines => new[] {
			"* DUKE FISHRON snorts seawater.",
			"* Bubbles drift past your face.",
			"* Smells like the ocean. And bacon?",
		};
		protected override string HurtLine => "* DUKE FISHRON's eyes are glowing. It's not holding back anymore.";

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet duke(Vector2 p, Vector2 d) => Shots.Npc(NPCID.DukeFishron, p, Vector2.Zero, 0.3f, 1.2f, new Vector2(46, 30), rotate: false).FaceTravel();
			Bullet bubble(Vector2 p, Vector2 v) => Shots.Npc(NPCID.DetonatingBubble, p, v, 0.9f, 0.7f, new Vector2(12, 12), rotate: false);
			Bullet shark(Vector2 p, Vector2 v) => Shots.Npc(NPCID.Sharkron, p, v, 0.8f, 0.9f, new Vector2(20, 12), rotate: false).FaceTravel();
			Bullet water(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(80, 180, 255), 0.7f, 1.1f);
			return Cycle(
				// Its charges, back and forth
				() => new LaneDash(duke, Hard ? 38 : 50) { AllowVertical = true, Speed = Hard ? 11f : 9f, Warn = Hard ? 26 : 32, LaneWidth = 36f, LaunchSound = SoundID.Roar },
				// Bubbles spiral out from the middle
				() => new Sprinkler(bubble)
				{
					Origin = new Vector2(BattleConstants.BoxCenterX, BattleConstants.BoxCenterY),
					Spiral = true, Arms = Hard ? 4 : 3, Every = Hard ? 9 : 12, Speed = 1.6f, TurnSpeed = 0.05f, ArmTicks = 22,
				},
				() => new Swoopers(shark, Hard ? 16 : 22) { Speed = Hard ? 3f : 2.5f, Amplitude = 30f },
				() => new GapRows(water, Hard ? 28 : 34) { Speed = 2f, GapSize = 44f },
				() => new Combo(TurnTicks, new LaneDash(duke, 70) { Speed = 9f }, new Swoopers(shark, 30) { Speed = 2.4f }),
				// Full screen: a storm; sharks everywhere while it keeps charging
				() => FullScreen(
					new Slashes(Hard ? 56 : 70) { Color = new Color(80, 200, 255), PerBurst = Hard ? 4 : 3 },
					new Swoopers(shark, Hard ? 22 : 30) { Speed = 3f, FirstAt = 40 }));
		}
	}

	// ====================================================================== Empress of Light

	public class EmpressOfLight : HardmodeBoss
	{
		public override string Name => "EMPRESS OF LIGHT";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(560f, 420f);
		// The battle can freeze her mid fade-in
		public override bool ForceOpaque => true;
		public override string EncounterText => "* The EMPRESS OF LIGHT unfurls her wings.";
		protected override string Check => "* The Hallow's sovereign. Don't fight her in daylight.";
		protected override (string, string)[] ActNames => new[] { ("Wings", "Admire the\nwings"), ("Watch", "Enjoy the\nlight show"), ("Bow", "Bow\npolitely") };
		protected override string[] Lines => new[] {
			"* The EMPRESS OF LIGHT hums a lullaby.",
			"* Rainbows bend around her.",
			"* Smells like starlight and butterflies.",
		};
		protected override string HurtLine => "* The EMPRESS OF LIGHT is shining brighter. That's not good.";

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet bolt(Vector2 p, Vector2 v) => Shots.Ball(p, v, Main.hslToRgb(Main.rand.NextFloat(), 1f, 0.7f), 0.7f, 1.1f);
			Bullet lance(Vector2 p, Vector2 v) => Shots.Ball(p, v, Main.hslToRgb(Main.rand.NextFloat(), 1f, 0.75f), 0.8f, 1.3f);
			Bullet empress(Vector2 p, Vector2 d) => Shots.Npc(NPCID.HallowBoss, p, Vector2.Zero, 0.22f, 1.2f, new Vector2(40, 40), rotate: false);
			return Cycle(
				// Prismatic bolts spiral from the middle
				() => new Sprinkler(bolt)
				{
					Origin = new Vector2(BattleConstants.BoxCenterX, BattleConstants.BoxCenterY),
					Spiral = true, Arms = Hard ? 5 : 4, Every = Hard ? 7 : 9, Speed = 1.7f, TurnSpeed = 0.06f, ArmTicks = 20,
				},
				// Ethereal lances: lines flash, then strike along them
				() => new Beam(Hard ? 18 : 26) { Width = 10f, Warn = 34, Active = 12, Aimed = false, Color = new Color(255, 180, 255), FireSound = SoundID.Item163 },
				() => new LaneDash(empress, Hard ? 44 : 58) { AllowVertical = true, Speed = Hard ? 10f : 8f, LaunchSound = SoundID.Item160 },
				() => new Converge(lance, Hard ? 48 : 62) { Count = Hard ? 12 : 9, Speed = Hard ? 4.8f : 4.2f },
				// Everlasting rainbow: bolts circle in on you
				() => new Orbiters(bolt, Hard ? 90 : 120) { Count = Hard ? 10 : 8, AngularSpeed = 0.03f },
				// Sun Dance, full screen: rainbow rays wheel around the field
				() => FullScreen(
					new SweepBeam(150) { Arms = Hard ? 8 : 6, AngularSpeed = Hard ? 0.011f : 0.008f, Width = 18f, Rainbow = true, AimFirst = false, FirstAt = 36, Active = 120, FireSound = SoundID.Item163 },
					new Converge(lance, Hard ? 70 : 90) { Count = 8, Speed = 4f, FirstAt = 80 }));
		}
	}

	// ====================================================================== Lunatic Cultist

	public class LunaticCultist : HardmodeBoss
	{
		public override string Name => "LUNATIC CULTIST";
		public override string EncounterText => "* The LUNATIC CULTIST begins the ritual!";
		protected override string Check => "* Worships something that doesn't love it back.";
		protected override (string, string)[] ActNames => new[] { ("Question", "Question\nthe cult"), ("Hobby", "Suggest\na hobby"), ("Meetings", "Ask about\nmeetings") };
		protected override string[] Lines => new[] {
			"* The LUNATIC CULTIST chants under its breath.",
			"* Something ancient is listening.",
			"* Smells like incense and moon dust.",
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet fire(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.CultistBossFireBall, p, v, 0.8f, 0.8f, new Vector2(12, 12), rotate: false);
			Bullet ice(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(160, 230, 255), 0.7f, 1.3f);
			Bullet light(Vector2 p, Vector2 v) => Shots.Npc(NPCID.AncientLight, p, v, 0.8f, 0.7f, new Vector2(12, 12), rotate: false);
			Bullet dragonHead(Vector2 p, Vector2 v) => Shots.Npc(NPCID.CultistDragonHead, p, v, 0.45f, 1f, new Vector2(18, 18), rotationOffset: BossKit.WormRotation);
			Bullet dragonBody(Vector2 p, Vector2 v) => Shots.Npc(NPCID.CultistDragonBody1, p, v, 0.45f, 0.8f, new Vector2(16, 16), rotationOffset: BossKit.WormRotation);
			var cyan = new Color(140, 220, 255);
			return Cycle(
				() => new AimedBursts(fire, Hard ? 26 : 36) { Count = Hard ? 5 : 3, Speed = 2.6f },
				// Ice mist: shards gather around you, then close in
				() => new Converge(ice, Hard ? 50 : 64) { Count = Hard ? 10 : 8, Speed = Hard ? 4.4f : 3.8f },
				// Lightning from the orb
				() => new Beam(Hard ? 34 : 46) { Width = 8f, Warn = 34, Active = 12, Color = cyan, FireSound = SoundID.Item122 },
				() => new Homing(light, Hard ? 20 : 28) { Speed = 1.9f, Turn = 0.045f, SteerTicks = 90 },
				// The phantasm dragon
				() => new Snake(dragonHead, dragonBody, Hard ? 80 : 110) { Segments = 10, Speed = 2.8f },
				() => Hard
					? FullScreen(new Slashes(60) { Color = cyan, PerBurst = 3 }, new Homing(light, 40) { Speed = 1.8f, FirstAt = 40 })
					: new Combo(TurnTicks, new AimedBursts(fire, 46) { Count = 3 }, new Homing(light, 44) { Speed = 1.7f }));
		}
	}

	// ====================================================================== Moon Lord

	public class MoonLord : HardmodeBoss
	{
		private static readonly int[] Parts = { NPCID.MoonLordCore, NPCID.MoonLordHead, NPCID.MoonLordHand };

		public override string Name => "MOON LORD";
		public override bool DrawWithTerraria => true;
		// Torso, head and outstretched hands: well over a thousand pixels across. It looms behind the fight.
		public override Vector2 CompositeSize => new(1400f, 1150f);
		// Fitted between the TP bar and the right edge, above the bottom panel
		public override Vector2 CompositeArea => new(380f, 310f);
		public override Vector2 DrawCenter => new(440f, 160f);
		// Frozen mid-rise, its parts can still be faded
		public override bool ForceOpaque => true;
		// Closed eyes are out of the fight but still part of the body
		public override IEnumerable<NPC> DrawParts() => BossKit.OfTypes(Parts);
		public override string EncounterText => "* The MOON LORD has awoken.";
		protected override string Check => "* The final enemy. Has more eyes than reasons to be here.";
		protected override (string, string)[] ActNames => new[] { ("Stare", "Look into\nits eyes"), ("Fine", "World's\nfine"), ("Hands", "Praise\nthe hands") };
		protected override string[] Lines => new[] {
			"* The MOON LORD looms over everything.",
			"* Reality is a little thin right now.",
			"* Smells like the end of the world.",
			"* You feel very small.",
		};
		protected override string HurtLine => "* The MOON LORD's heart is exposed.";
		protected override int ActLineType => NPCID.MoonLordCore;

		/// <summary>
		/// Terraria never really kills these parts: a "dead" eye (head or hand) goes back to full health with
		/// ai[0] = -2 and closes, and a "dead" core goes back to full health with ai[0] = 2 and plays its death
		/// animation (its AI kills it, drops the loot). So closed eyes and a dying core aren't in the fight any more.
		/// </summary>
		public override IEnumerable<NPC> Members()
		{
			foreach (NPC n in BossKit.OfTypes(Parts))
			{
				if (n.type == NPCID.MoonLordCore && n.ai[0] == 2f)
					continue;
				if ((n.type == NPCID.MoonLordHead || n.type == NPCID.MoonLordHand) && n.ai[0] == -2f)
					continue;
				yield return n;
			}
		}

		/// <summary>The head is the face you fight; the core carries the loot and the last of its health.</summary>
		public override NPC DrawNpc => BossKit.OfTypes(NPCID.MoonLordHead).FirstOrDefault() ?? Core ?? base.DrawNpc;
		private static NPC Core => BossKit.OfTypes(NPCID.MoonLordCore).FirstOrDefault();

		/// <summary>Its parts don't deal contact damage, so bullets use its Phantasmal Bolt damage instead.</summary>
		public override int Damage => (int)(70 * Main.GameModeInfo.EnemyDamageMultiplier);

		public override NPC StrikeTarget()
		{
			// Its eyes first; the core opens once they're all shut (its AI is paused, so do it here)
			NPC eye = Members().FirstOrDefault(n => n.type != NPCID.MoonLordCore && !n.dontTakeDamage);
			if (eye != null)
				return eye;
			NPC core = Core;
			if (core != null)
			{
				core.dontTakeDamage = false;
				return core;
			}
			return base.StrikeTarget();
		}

		public override void Spare()
		{
			// The loot is on the core; every other part (closed eyes and True Eyes included) goes
			NPC keep = Core ?? Npc;
			foreach (NPC m in BossKit.OfTypes(NPCID.MoonLordHead, NPCID.MoonLordHand, NPCID.MoonLordFreeEye, NPCID.MoonLordLeechBlob).ToList())
			{
				if (m.whoAmI == keep.whoAmI)
					continue;
				m.active = false;
				m.life = 0;
			}
			MercyGlobalNPC.Spare(keep);
		}

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			Bullet bolt(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.PhantasmalBolt, p, v, 1f, 0.8f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			Bullet sphere(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.PhantasmalSphere, p, v, 0.6f, 1f, new Vector2(18, 18), rotate: false);
			Bullet eye(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.PhantasmalEye, p, v, 0.9f, 0.9f, new Vector2(12, 12), rotationOffset: MathHelper.PiOver2);
			var cyan = new Color(120, 255, 230);
			return Cycle(
				() => new AimedBursts(bolt, Hard ? 18 : 26) { Count = Hard ? 6 : 4, Speed = 3.4f, Spread = 0.3f },
				() => new Orbiters(sphere, Hard ? 90 : 110) { Count = Hard ? 10 : 8, AngularSpeed = 0.028f, Shrink = 0.4f },
				() => new Homing(eye, Hard ? 18 : 26) { Speed = 2.2f, Turn = 0.05f, SteerTicks = 90 },
				// The Phantasmal Deathray sweeps the whole field from above
				() => FullScreen(
					new SweepBeam(150) { Pivot = new Vector2(320f, -20f), AngularSpeed = Hard ? 0.014f : 0.011f, Width = 30f, Color = cyan, Active = 130, FirstAt = 36 },
					new AimedBursts(bolt, Hard ? 40 : 54) { Count = 4, Speed = 3f, FirstAt = 60 }),
				() => new Converge(eye, Hard ? 46 : 60) { Count = Hard ? 12 : 9, Speed = Hard ? 5f : 4.4f, RotationOffset = MathHelper.PiOver2 },
				() => new Combo(TurnTicks, new AimedBursts(bolt, 34) { Count = 4, Speed = 3f }, new Orbiters(sphere, 150) { Count = 6 }),
				// Phase 2, full screen: the true eyes slash the world apart
				() => Hard
					? FullScreen(new Slashes(54) { Color = cyan, PerBurst = 4 }, new Homing(eye, 36) { Speed = 2.2f, FirstAt = 40 })
					: new Homing(eye, 24) { Speed = 2f });
		}
	}
}
