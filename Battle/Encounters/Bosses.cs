using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>Small helpers the boss encounters share.</summary>
	public static class BossKit
	{
		/// <summary>NPC sprites face left; flip them while they travel right.</summary>
		public static Bullet FaceTravel(this Bullet b)
		{
			b.RotateWithVelocity = false;
			b.OnUpdate += x => x.FlipX = x.Velocity.X > 0;
			return b;
		}

		public static Bullet Spin(this Bullet b, float speed)
		{
			b.RotateWithVelocity = false;
			b.OnUpdate += x => x.Rotation += speed;
			return b;
		}

		public static IEnumerable<NPC> OfTypes(params int[] types)
		{
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC n = Main.npc[i];
				if (n.active && types.Contains(n.type))
					yield return n;
			}
		}

		/// <summary>Worm sprites point up, so their rotation is a quarter turn ahead of their velocity.</summary>
		public const float WormRotation = MathHelper.PiOver2;
	}

	// ====================================================================== King Slime

	public class KingSlime : Encounter
	{
		public override string Name => "KING SLIME";
		public override string EncounterText => "* KING SLIME bounces into view!";

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* KING SLIME seems content to just wobble.";
			if (LifeRatio < 0.5f)
				return "* KING SLIME is getting noticeably smaller.";
			string[] lines = {
				"* KING SLIME wobbles regally.",
				"* Smells like gel and royalty.",
				"* KING SLIME adjusts its crown.",
				"* The ninja inside waves at you.",
			};
			return lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* The ruler of all slimes. Somebody is stuck inside."),
			MercyAct("Crown", "Compliment\nthe crown", 30f, ActLines.Get(NPCID.KingSlime, 0)),
			MercyAct("Bow", "Bow\npolitely", 30f, ActLines.Get(NPCID.KingSlime, 1)),
			MercyAct("Ninja", "Help the\nninja", 35f, ActLines.Get(NPCID.KingSlime, 2)),
			HealPrayerAct(),
		};

		private static Bullet Gel(Vector2 p, Vector2 v) =>
			Shots.Npc(NPCID.BlueSlime, p, v, 0.7f, 0.6f, new Vector2(14, 10), rotate: false).With(c: new Color(80, 140, 255, 200));

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			Bullet king(Vector2 p, Vector2 d) => Shots.Npc(NPCID.KingSlime, p, Vector2.Zero, 0.35f, 1.2f, new Vector2(40, 28), rotate: false);
			Bullet gelDrop(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(80, 140, 255), 0.6f, 1.1f);
			Bullet shuriken(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Shuriken, p, v, 1f, 0.7f, new Vector2(10, 10), spin: 0.3f);
			return Cycle(
				// Blue SOUL for the bouncy attacks: hop the gel
				() => new Bouncers(Gel, hard ? 18 : 26).WithSoul(SoulMode.Blue),
				// It jumps and lands on you: the floor ripples out gel both ways
				() => new Slam(king, gelDrop, hard ? 62 : 82) { Width = 48f, Shards = hard ? 3 : 2, FallSpeed = hard ? 10f : 8.5f }.WithSoul(SoulMode.Blue),
				// Its slimes hop in from both sides; jump them
				() => new Walkers((p, v) => Shots.Npc(NPCID.BlueSlime, p, v, 0.8f, 0.8f, new Vector2(16, 12), rotate: false), hard ? 22 : 30)
					{ Speed = 1.5f, HopSpeed = 3.6f }.WithSoul(SoulMode.Blue),
				// The ninja inside throws stars; later it surrounds you with them first
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new SideShots(shuriken, 16) { Side = 0, Speed = 3.6f },
						new Converge(shuriken, 70) { Count = 6, Speed = 4f })
					: new SideShots(shuriken, 18) { Side = 0, Speed = 3f },
				// Gel rains down in rows; find the gap as it drifts
				() => new GapRows(gelDrop, hard ? 30 : 38) { Speed = hard ? 1.9f : 1.6f, GapSize = hard ? 40f : 48f },
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Bouncers(Gel, 34),
					new Rain((p, v) => Shots.Ball(p, v, new Color(80, 140, 255), 0.5f), hard ? 12 : 18)));
		}
	}

	// ====================================================================== Eater of Worlds

	public class EaterOfWorlds : Encounter
	{
		public override string Name => "EATER OF WORLDS";
		public override string EncounterText => "* EATER OF WORLDS bursts out of the ground!";
		public override float DrawRotation(int time) => -BossKit.WormRotation;

		public override IEnumerable<NPC> Members() =>
			BossKit.OfTypes(NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail);

		public override NPC DrawNpc => BossKit.OfTypes(NPCID.EaterofWorldsHead).FirstOrDefault() ?? Members().FirstOrDefault();

		public override void Spare()
		{
			// Terraria flags the last segment as the boss in DropEoWLoot, right before its loot; do the same
			NPC keep = DrawNpc ?? Npc;
			foreach (NPC m in Members().ToList())
			{
				if (m.whoAmI == keep.whoAmI)
					continue;
				m.active = false;
				m.life = 0;
			}
			keep.boss = true;
			MercyGlobalNPC.Spare(keep);
		}

		public override NPC StrikeTarget()
		{
			var all = Members().Where(m => m.life > 0).ToList();
			return all.Count > 0 ? all[Main.rand.Next(all.Count)] : Npc;
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* EATER OF WORLDS has lost its appetite.";
			int worms = BossKit.OfTypes(NPCID.EaterofWorldsHead).Count();
			if (worms > 1)
				return $"* There are {worms} EATERS OF WORLDS now. They argue about which one is in charge.";
			string[] lines = {
				"* EATER OF WORLDS coils around the battlefield.",
				"* Smells like the Corruption.",
				"* EATER OF WORLDS chews thoughtfully on the scenery.",
			};
			return lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* Many worms pretending to be one worm."),
			MercyAct("Share", "Point at\nthe Corruption", 30f, ActLines.Get(NPCID.EaterofWorldsHead, 0)),
			MercyAct("Pat", "Pat a\nsegment", 30f, ActLines.Get(NPCID.EaterofWorldsHead, 1)),
			MercyAct("Snack", "Suggest\na snack", 35f, ActLines.Get(NPCID.EaterofWorldsHead, 2)),
			HealPrayerAct(),
		};

		private static Bullet Head(Vector2 p, Vector2 v) =>
			Shots.Npc(NPCID.EaterofWorldsHead, p, v, 0.7f, 1f, new Vector2(16, 16), rotationOffset: BossKit.WormRotation);

		private static Bullet Body(Vector2 p, Vector2 v) =>
			Shots.Npc(NPCID.EaterofWorldsBody, p, v, 0.7f, 0.8f, new Vector2(14, 14), rotationOffset: BossKit.WormRotation);

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			Bullet spit(Vector2 p, Vector2 v) => Shots.Ball(p, v, Shots.Green, 0.6f);
			Bullet bigSpit(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(160, 255, 90), 0.9f, 1.8f).Dripping(Shots.Green);
			return Cycle(
				// Eaters of Souls dive from above
				() => new Diver((p, v) => Shots.Npc(NPCID.EaterofSouls, p, v, 0.8f, 0.8f, new Vector2(16, 16), rotate: false), hard ? 26 : 36),
				() => new Snake(Head, Body, hard ? 70 : 100) { Segments = 9, Speed = hard ? 2.8f : 2.3f },
				// Corruption drips down in rows with a drifting gap
				() => new GapRows(spit, hard ? 28 : 36) { Speed = hard ? 1.7f : 1.4f, GapSize = hard ? 42f : 50f },
				// Vile spit lobbed in, bursting into a ring
				() => new Fireworks(bigSpit, spit, hard ? 38 : 52) { Count = hard ? 10 : 8, ShardSpeed = hard ? 2f : 1.7f },
				// Phase 2: two worms at once, one from each side
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new Snake(Head, Body, 95) { Segments = 7, Side = 1, Speed = 2.6f },
						new Snake(Head, Body, 95) { Segments = 7, Side = -1, Speed = 2.6f, FirstAt = 45 })
					: new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new Snake(Head, Body, 120) { Segments = 7 },
						new Rain(spit, 22) { SpeedMin = 1.2f, SpeedMax = 1.6f }));
		}
	}

	// ====================================================================== Brain of Cthulhu

	public class BrainOfCthulhu : Encounter
	{
		public override string Name => "BRAIN OF CTHULHU";
		public override string EncounterText => "* BRAIN OF CTHULHU invades your thoughts!";

		public override IEnumerable<NPC> Members()
		{
			if (Npc.active)
				yield return Npc;
			foreach (NPC c in BossKit.OfTypes(NPCID.Creeper))
				yield return c;
		}

		private bool CreepersLeft => BossKit.OfTypes(NPCID.Creeper).Any();

		public override NPC StrikeTarget()
		{
			NPC creeper = BossKit.OfTypes(NPCID.Creeper).FirstOrDefault();
			if (creeper != null)
				return creeper;
			// Its AI drops the shield once the Creepers are gone; the AI is paused, so do it here
			Npc.dontTakeDamage = false;
			return Npc;
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* BRAIN OF CTHULHU has run out of scary thoughts.";
			if (!CreepersLeft)
				return "* BRAIN OF CTHULHU is exposed and very nervous.";
			string[] lines = {
				"* The Creepers circle protectively.",
				"* You feel like you're being read like a book.",
				"* Smells like the Crimson.",
			};
			return lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* It knows what you're going to do. It just doesn't know why."),
			MercyAct("Calm", "Think calm\nthoughts", 30f, ActLines.Get(NPCID.BrainofCthulhu, 0)),
			MercyAct("Ask", "Ask what\nit's thinking", 30f, ActLines.Get(NPCID.BrainofCthulhu, 1)),
			MercyAct("Agree", "Agree\nwith it", 35f, ActLines.Get(NPCID.BrainofCthulhu, 2)),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = !CreepersLeft || LifeRatio < 0.5f;
			Bullet creeper(Vector2 p, Vector2 v) => Shots.Npc(NPCID.Creeper, p, v, 0.8f, 0.7f, new Vector2(14, 14), rotate: false);
			Bullet thought(Vector2 p, Vector2 v) => Shots.Ball(p, v, Shots.Red, 0.6f);
			return Cycle(
				// Purple SOUL: caught in its mind, Creepers crawl the strings while thoughts drift down
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new StringRunners(creeper, hard ? 20 : 28) { Speed = hard ? 3f : 2.5f },
					new Rain((p, v) => thought(p, v).Sparkly(Shots.Red), hard ? 26 : 36) { SpeedMin = 1.2f, SpeedMax = 1.6f }),
				() => new Orbiters(creeper, hard ? 90 : 120) { Count = hard ? 8 : 6, AngularSpeed = 0.03f },
				// Bad thoughts close in from every side
				() => new Converge(thought, hard ? 55 : 70) { Count = hard ? 10 : 8, Speed = hard ? 4.5f : 3.8f, Radius = 72f },
				() => new LaneDash((p, d) => Shots.Npc(NPCID.BrainofCthulhu, p, Vector2.Zero, 0.4f, 1f, new Vector2(30, 26), rotate: false),
					hard ? 50 : 70) { AllowVertical = true, Speed = hard ? 8f : 6.5f, LaunchSound = SoundID.ForceRoar },
				// A psychic spiral from the middle of the box (it fades in, so it can't hit you where it starts)
				() => new Sprinkler(thought)
				{
					Origin = new Vector2(BattleConstants.BoxCenterX, BattleConstants.BoxCenterY),
					Spiral = true, Arms = hard ? 4 : 3, Every = hard ? 7 : 9, Speed = 1.8f, TurnSpeed = 0.045f, ArmTicks = 20,
				},
				() => new Homing(thought, hard ? 24 : 34) { Speed = hard ? 2f : 1.6f },
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Orbiters(creeper, 140) { Count = 5 },
					new Homing(thought, 50)));
		}
	}

	// ====================================================================== Queen Bee

	public class QueenBee : Encounter
	{
		public override string Name => "QUEEN BEE";
		public override string EncounterText => "* QUEEN BEE buzzes furiously!";

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* QUEEN BEE hums a calm little tune.";
			string[] lines = {
				"* QUEEN BEE inspects you for honey.",
				"* The buzzing is deafening.",
				"* Smells like honey and anger.",
			};
			return LifeRatio < 0.5f ? "* QUEEN BEE's wings are getting tired." : lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* Royalty of the jungle. Very protective of her larva."),
			MercyAct("Promise", "Promise to\nbehave", 30f, ActLines.Get(NPCID.QueenBee, 0)),
			MercyAct("Hive", "Compliment\nthe hive", 30f, ActLines.Get(NPCID.QueenBee, 1)),
			MercyAct("Hum", "Hum\nalong", 35f, ActLines.Get(NPCID.QueenBee, 2)),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			Bullet bee(Vector2 p, Vector2 v) => Shots.Npc(NPCID.Bee, p, v, 1f, 0.5f, new Vector2(10, 10), rotate: false).FaceTravel();
			Bullet stinger(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Stinger, p, v, 1f, 0.6f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			return Cycle(
				() => new Homing(bee, hard ? 14 : 20) { Speed = hard ? 2.2f : 1.8f, Turn = 0.03f, SteerTicks = 70 },
				// Green SOUL: stingers from every side; turn the shield to block them
				() => new ShieldSpears(stinger, hard ? 16 : 22) { Speed = hard ? 2.8f : 2.3f },
				// She hovers above and sprays stingers in a sweeping fan
				() => new Sprinkler(stinger)
				{
					Every = hard ? 6 : 8, Arms = hard ? 2 : 1, Speed = 3f, TurnSpeed = 0.06f, FanSpread = hard ? 0.95f : 0.8f,
				},
				() => new LaneDash((p, d) => Shots.Npc(NPCID.QueenBee, p, Vector2.Zero, 0.45f, 1.2f, new Vector2(40, 30), rotate: false).FaceTravel(),
					hard ? 50 : 65) { Speed = hard ? 9f : 7.5f, LaunchSound = SoundID.Roar },
				() => new SideShots(stinger, hard ? 9 : 13) { Side = 0, Speed = 3.5f },
				// Stingers hang in the air around you, then dive
				() => new Converge(stinger, hard ? 52 : 68) { Count = hard ? 10 : 7, Speed = hard ? 4.8f : 4f, RotationOffset = MathHelper.PiOver2 },
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Homing(bee, 30) { Speed = 1.6f },
					new SideShots(stinger, 20) { Side = 0 }));
		}
	}

	// ====================================================================== Skeletron

	public class Skeletron : Encounter
	{
		public override string Name => "SKELETRON";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(300f, 220f);
		// Higher than the usual spot: its arm bones hang well below the hands and head
		public override Vector2 DrawCenter => new(500f, 150f);

		/// <summary>
		/// Head upright, hands raised beside it like during its spin (AI style 12 moves them to head centre
		/// - 120 * ai[0] across, 100 above the head's top then). Resting low (+230) looked like a zombie shuffle: the
		/// arm bones hang off the hands toward fixed points out to the side.
		/// </summary>
		public override void PoseForBattle(List<NPC> parts, NPC anchor, int time, float attacking)
		{
			foreach (NPC p in parts)
			{
				p.rotation = 0f;
				if (p.type != NPCID.SkeletronHand)
					continue;
				p.position.X = anchor.Center.X - 120f * p.ai[0] - p.width / 2f;
				p.position.Y = anchor.position.Y - 60f;
				// The sprite hangs fingers-down; raised, it turns over so the fingers point up and the wrist meets
				// the bones. Half a turn also mirrors it, so flip its facing back to keep the thumb on the same side.
				p.rotation = MathHelper.Pi;
				p.spriteDirection = (int)p.ai[0];
			}
		}
		public override string EncounterText => "* SKELETRON rises to guard the dungeon!";

		public override IEnumerable<NPC> Members()
		{
			if (Npc.active)
				yield return Npc;
			// Only this head's hands (ai[1] = the head), not another Skeletron's
			foreach (NPC h in BossKit.OfTypes(NPCID.SkeletronHand))
				if ((int)h.ai[1] == Npc.whoAmI)
					yield return h;
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* SKELETRON's bones have stopped rattling.";
			string[] lines = {
				"* SKELETRON's hands drum impatiently.",
				"* Smells like old bones and older curses.",
				"* SKELETRON spins its head. Showing off.",
			};
			return LifeRatio < 0.5f ? "* SKELETRON's curse is weakening." : lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* Cursed to guard the dungeon. The Clothier wants his life back."),
			MercyAct("Console", "It's not\nyour fault", 30f, ActLines.Get(NPCID.SkeletronHead, 0)),
			MercyAct("Clothier", "Talk about\nthe Clothier", 35f, ActLines.Get(NPCID.SkeletronHead, 1)),
			MercyAct("Rattle", "Rattle in\nsolidarity", 30f, ActLines.Get(NPCID.SkeletronHead, 2)),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			Bullet hand(Vector2 p, Vector2 d) => Shots.Npc(NPCID.SkeletronHand, p, Vector2.Zero, 0.9f, 1f, new Vector2(26, 26), rotate: false);
			Bullet head(Vector2 p, Vector2 d) => Shots.Npc(NPCID.SkeletronHead, p, Vector2.Zero, 0.5f, 1.2f, new Vector2(34, 34), rotate: false).Spin(0.35f);
			Bullet bone(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Bone, p, v, 1f, 0.6f, new Vector2(10, 10), spin: 0.25f);
			return Cycle(
				() => new LaneDash(hand, hard ? 45 : 60) { AllowVertical = true, Speed = hard ? 8f : 7f },
				// The spinning-head charge
				() => new LaneDash(head, hard ? 60 : 80) { AllowVertical = true, Speed = hard ? 7f : 5.5f, LaneWidth = 40f, LaunchSound = SoundID.Roar },
				// A hand slams the floor and scatters bones along it (blue SOUL: jump them)
				() => new Slam(hand, bone, hard ? 60 : 78) { Width = 40f, Shards = hard ? 3 : 2, ShardSpeed = 2.6f }.WithSoul(SoulMode.Blue),
				// Blue SOUL: thrown to the floor, then walls of bones to jump over and duck under
				() => new BoneWalls(hard ? 26 : 34) { Speed = hard ? 3.2f : 2.6f, Color = new Color(235, 225, 200) },
				// Bones fall in rows with a drifting gap
				() => new GapRows(bone, hard ? 30 : 38) { Speed = hard ? 2f : 1.7f, GapSize = hard ? 42f : 48f, Spacing = 16f },
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new Converge(bone, 60) { Count = 8, Speed = 4.4f },
						new LaneDash(hand, 90) { Speed = 7.5f })
					: new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new Rain(bone, 16) { SpeedMin = 2f, SpeedMax = 2.6f, Wobble = 0f },
						new LaneDash(hand, 80) { Speed = 7f }),
				// Phase 2, full screen: the curse cuts through the whole room while bones rain down
				() => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Slashes(66) { Color = new Color(235, 225, 200), PerBurst = 3 },
						new Rain(bone, 14) { SpeedMin = 2f, SpeedMax = 2.8f, Wobble = 0f }) { FullScreen = true }
					: new LaneDash(hand, 60) { AllowVertical = true, Speed = 7f });
		}
	}

	// ====================================================================== Deerclops

	public class Deerclops : Encounter
	{
		public override string Name => "DEERCLOPS";
		public override bool DrawWithTerraria => true;
		public override Vector2 CompositeSize => new(200f, 240f);

		/// <summary>
		/// Its frames (frame.Y is a cell 0-24 of a 5x5 sheet): 0 stand, 2-11 walk, 12-17 roar. It stomps in place while
		/// waiting and roars while it attacks.
		/// </summary>
		public override void PoseForBattle(List<NPC> parts, NPC anchor, int time, float attacking)
		{
			anchor.spriteDirection = -1; // facing the party
			anchor.frame.Y = attacking > 0.2f ? 12 + time / 5 % 6 : 2 + time / 9 % 10;
		}
		public override string EncounterText => "* DEERCLOPS lumbers out of the snow!";

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* DEERCLOPS is warming its hands. It's not interested in fighting.";
			string[] lines = {
				"* DEERCLOPS stares at you with its one big eye.",
				"* The air gets colder.",
				"* Shadows gather around DEERCLOPS's antlers.",
			};
			return LifeRatio < 0.5f ? "* DEERCLOPS is breathing hard. Frost covers its fur." : lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* An ancient beast from somewhere colder. It's afraid of the dark too."),
			MercyAct("Scarf", "Lend it\na scarf", 30f, ActLines.Get(NPCID.Deerclops, 0)),
			MercyAct("Antlers", "Compliment\nthe antlers", 30f, ActLines.Get(NPCID.Deerclops, 1)),
			MercyAct("Campfire", "Light a\ncampfire", 35f, ActLines.Get(NPCID.Deerclops, 2)),
			HealPrayerAct(),
		};

		/// <summary>
		/// One piece of Deerclops's rubble. Its texture is a grid (a column per piece shape, 4 rows), so draw one cell,
		/// like Main.DrawProj does for it.
		/// </summary>
		private static Bullet Rubble(Vector2 p, Vector2 v, float scale, float damage, Vector2 hitSize)
		{
			Main.instance.LoadProjectile(ProjectileID.DeerclopsRangedProjectile);
			var tex = Terraria.GameContent.TextureAssets.Projectile[ProjectileID.DeerclopsRangedProjectile].Value;
			int columns = Math.Max(1, Main.projFrames[ProjectileID.DeerclopsRangedProjectile]);
			var source = tex.Frame(columns, 4, Main.rand.Next(columns), Main.rand.Next(4));
			return new Bullet
			{
				Position = p,
				Velocity = v,
				Texture = tex,
				Source = source,
				Scale = scale,
				HitSize = hitSize,
				DamageMult = damage,
			}.Spin(Main.rand.NextBool() ? 0.12f : -0.12f);
		}

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			// Terraria's ice spike texture is a sheet of several spikes, so draw a single one
			Bullet spike(Vector2 p, Vector2 d) => new Bullet
			{
				Position = p,
				HitSize = new Vector2(12, 36),
				DamageMult = 1f,
				OnDraw = b =>
				{
					for (int i = 0; i < 12; i++)
					{
						float w = 14f * (1f - i / 12f);
						Color c = Color.Lerp(new Color(200, 240, 255), Shots.Ice, i / 12f);
						DrDraw.Rect(b.Position.X - w / 2f, b.Position.Y + 18 - (i + 1) * 3.5f, w, 3.5f, c);
					}
				},
			};
			Bullet hand(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.InsanityShadowHostile, p, v, 0.6f, 0.8f, new Vector2(14, 14), rotate: true);
			Bullet rock(Vector2 p, Vector2 v) => Rubble(p, v, 1f, 0.8f, new Vector2(14, 14));
			Bullet boulder(Vector2 p, Vector2 v) => Rubble(p, v, 1.8f, 1.2f, new Vector2(26, 26));
			Bullet iceShard(Vector2 p, Vector2 v) => Shots.Ball(p, v, Shots.Ice, 0.7f);
			return Cycle(
				() => new FloorSpikes(spike, hard ? 24 : 34) { Warn = hard ? 24 : 30 }.WithSoul(SoulMode.Blue),
				() => new Homing(hand, hard ? 26 : 36) { Speed = 1.5f, Turn = 0.05f, SteerTicks = 110 },
				// A boulder crashes down and breaks into rubble along the floor
				() => new Slam(boulder, rock, hard ? 58 : 76) { Width = 44f, Shards = hard ? 3 : 2, Debris = 4 }.WithSoul(SoulMode.Blue),
				// Frost gathers around you, then shatters inward
				() => new Converge(iceShard, hard ? 52 : 68) { Count = hard ? 10 : 8, Speed = hard ? 4.4f : 3.8f },
				() => new Rain(rock, hard ? 10 : 15) { SpeedMin = 2.2f, SpeedMax = 3f, Wobble = 0f },
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new FloorSpikes(spike, 45),
					new Rain(rock, 22) { Wobble = 0f }),
				// Phase 2, full screen: icy slashes across the whole field, rocks falling everywhere
				() => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Slashes(68) { Color = Shots.Ice, PerBurst = 3 },
						new Rain(rock, 16) { SpeedMin = 2.2f, SpeedMax = 3f, Wobble = 0f }) { FullScreen = true }
					: new FloorSpikes(spike, 34));
		}
	}

	// ====================================================================== Wall of Flesh

	public class WallOfFlesh : Encounter
	{
		public override string Name => "WALL OF FLESH";
		public override string EncounterText => "* The WALL OF FLESH closes in!";

		public override IEnumerable<NPC> Members()
		{
			foreach (NPC n in base.Members())
				yield return n;
			foreach (NPC n in BossKit.OfTypes(NPCID.TheHungry, NPCID.TheHungryII, NPCID.LeechHead, NPCID.LeechBody, NPCID.LeechTail))
				if (n.realLife != Npc.whoAmI)
					yield return n;
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* The WALL OF FLESH stops advancing.";
			string[] lines = {
				"* The WALL OF FLESH groans. The whole world seems to shake.",
				"* Smells like the Underworld.",
				"* The Hungry gnash their teeth.",
			};
			return LifeRatio < 0.5f ? "* The WALL OF FLESH is tearing apart." : lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* The world's last line of defense. Or its prison guard."),
			MercyAct("Reason", "Try to\nreason", 25f, ActLines.Get(NPCID.WallofFlesh, 0)),
			MercyAct("Teeth", "Compliment\nthe teeth", 30f, ActLines.Get(NPCID.WallofFlesh, 1)),
			MercyAct("Guide", "Talk about\nthe Guide", 40f, ActLines.Get(NPCID.WallofFlesh, 2)),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			Bullet laser(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.EyeLaser, p, v, 1f, 0.7f, new Vector2(14, 6), rotationOffset: MathHelper.PiOver2);
			Bullet hungry(Vector2 p, Vector2 v) => Shots.Npc(NPCID.TheHungry, p, v, 0.55f, 0.8f, new Vector2(12, 12), rotate: false);
			Bullet leechHead(Vector2 p, Vector2 v) => Shots.Npc(NPCID.LeechHead, p, v, 0.8f, 0.8f, new Vector2(12, 12), rotationOffset: BossKit.WormRotation);
			Bullet leechBody(Vector2 p, Vector2 v) => Shots.Npc(NPCID.LeechBody, p, v, 0.8f, 0.7f, new Vector2(10, 10), rotationOffset: BossKit.WormRotation);
			return Cycle(
				() => new SideShots(laser, hard ? 10 : 14) { Side = -1, Speed = 6f },
				// Its eyes lock on and fire big beams across the box
				() => new Beam(hard ? 46 : 62) { FixedAngle = 0f, Tilt = hard ? 0.35f : 0.2f, Width = hard ? 18f : 15f, Color = new Color(255, 80, 200), FireSound = SoundID.Item33 },
				() => new Walls(hungry, hard ? 55 : 70) { Side = -1, Speed = hard ? 2f : 1.6f, Spacing = 18f, GapSize = 42f },
				() => new Snake(leechHead, leechBody, hard ? 70 : 95) { Side = -1, Segments = 6, Speed = 2.6f },
				// Its mouth: teeth snap shut over the box, leaving one gap
				() => new Jaws((p, v) => Shots.Ball(p, v, new Color(255, 220, 220), 1f, 1.5f), hard ? 66 : 86) { GapSize = hard ? 34f : 40f },
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Beam(80) { FixedAngle = 0f, Tilt = 0.15f, Color = new Color(255, 80, 200), FireSound = SoundID.Item33 },
					new Walls(hungry, 90) { Side = -1, Speed = 1.5f, Spacing = 18f, GapSize = 46f }),
				// Full screen: the whole wall bears down, lasers cutting across while the Hungry sweep through
				() => new Combo(BattleConstants.FullScreenTurnTicks,
					new Slashes(hard ? 60 : 74) { Color = new Color(255, 80, 200), PerBurst = hard ? 4 : 3, Width = 18f },
					new Walls(hungry, hard ? 95 : 120) { Side = -1, Speed = 2.6f, Spacing = 20f, GapSize = 52f, FirstAt = 40 })
					{ FullScreen = true });
		}
	}

	// ====================================================================== every other boss

	/// <summary>Any boss without its own encounter, vanilla or modded.</summary>
	public class GenericBoss : Encounter
	{
		private static readonly int[] Twins = { NPCID.Retinazer, NPCID.Spazmatism };

		public override string EncounterText => $"* {Name} appears!";

		public override IEnumerable<NPC> Members()
		{
			foreach (NPC n in base.Members())
				yield return n;
			if (Twins.Contains(Npc.type))
				foreach (NPC t in BossKit.OfTypes(Twins))
					if (t.whoAmI != Npc.whoAmI)
						yield return t;
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return $"* {Name} doesn't seem to want to fight anymore.";
			if (LifeRatio < 0.3f)
				return $"* {Name} is badly hurt.";
			string[] lines = {
				$"* {Name} towers over you.",
				$"* {Name} is sizing you up.",
				"* The air feels heavy.",
				$"* {Name} prepares its next attack.",
			};
			return lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* A powerful foe. Maybe it can be talked down."),
			MercyAct("Talk", "Try\ntalking", 30f, ActLines.Get(Npc.type, 0)),
			MercyAct("Compliment", "Say\nsomething nice", 30f, ActLines.Get(Npc.type, 1)),
			MercyAct("Plead", "Ask it\nto stop", 30f, ActLines.Get(Npc.type, 2)),
			HealPrayerAct(),
		};

		/// <summary>The boss's own sprite, shrunk to about 50 px, charging through a lane.</summary>
		private Bullet Self(Vector2 p, Vector2 d)
		{
			int type = Npc.type;
			Main.instance.LoadNPC(type);
			var tex = Terraria.GameContent.TextureAssets.Npc[type].Value;
			float frameH = tex.Height / (float)Math.Max(1, Main.npcFrameCount[type]);
			float scale = MathHelper.Clamp(50f / Math.Max(frameH, tex.Width), 0.1f, 1.5f);
			return Shots.Npc(type, p, Vector2.Zero, scale, 1.1f, new Vector2(30, 24), rotate: false).FaceTravel();
		}

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f || Main.hardMode;
			Bullet red(Vector2 p, Vector2 v) => Shots.Ball(p, v, Shots.Red, 0.7f);
			Bullet purple(Vector2 p, Vector2 v) => Shots.Ball(p, v, Shots.Purple, 0.6f);
			return Cycle(
				() => new AimedBursts(red, hard ? 30 : 45) { Count = hard ? 5 : 3, Speed = hard ? 2.6f : 2.2f },
				() => new LaneDash(Self, hard ? 50 : 70) { AllowVertical = true, Speed = hard ? 8f : 6.5f, LaunchSound = SoundID.Roar },
				() => new Converge(red, hard ? 52 : 68) { Count = hard ? 10 : 8, Speed = hard ? 4.5f : 3.8f },
				() => new Rain(purple, hard ? 7 : 10),
				() => new Beam(hard ? 48 : 64) { Width = hard ? 16f : 14f },
				() => new ClosingRing(red, hard ? 60 : 80),
				() => new Sprinkler(purple) { Every = hard ? 6 : 8, Arms = hard ? 2 : 1, Speed = 2.6f, TurnSpeed = 0.055f },
				() => new Fireworks((p, v) => Shots.Ball(p, v, Color.White, 0.9f, 1.8f), red, hard ? 40 : 54) { Count = hard ? 10 : 8 },
				() => new Walls((p, v) => Shots.Ball(p, v, Color.White, 0.7f), hard ? 55 : 75) { Side = 0 },
				// Hardmode (or badly hurt): a full-screen onslaught
				() => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Slashes(62) { PerBurst = 4 },
						new AimedBursts(red, 50) { Count = 3, Speed = 2.4f, FirstAt = 40 }) { FullScreen = true }
					: new Converge(red, 68) { Count = 8, Speed = 3.8f });
		}
	}

	public static class BulletStyle
	{
		/// <summary>Tints a bullet; returns it for chaining.</summary>
		public static Bullet With(this Bullet b, Color c)
		{
			b.Color = c;
			return b;
		}
	}
}
