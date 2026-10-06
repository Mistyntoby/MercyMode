using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
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

		/// <summary>
		/// A worm's segments in order from its head (each follower's ai[1] is the segment ahead of it), cut down to
		/// <paramref name="show"/>: the first ones and then the tail, so the battle shows a whole worm, just shorter.
		/// </summary>
		public static List<NPC> WormChain(NPC head, int show)
		{
			var chain = new List<NPC>();
			if (head == null || !head.active)
				return chain;
			chain.Add(head);
			var used = new HashSet<int> { head.whoAmI };
			NPC cur = head;
			while (chain.Count < 300)
			{
				NPC next = null;
				int byAi0 = (int)cur.ai[0];
				if (byAi0 > 0 && byAi0 < Main.maxNPCs && Main.npc[byAi0].active && !used.Contains(byAi0) && (int)Main.npc[byAi0].ai[1] == cur.whoAmI)
					next = Main.npc[byAi0];
				for (int i = 0; next == null && i < Main.maxNPCs; i++)
				{
					NPC n = Main.npc[i];
					if (n.active && !used.Contains(i) && (int)n.ai[1] == cur.whoAmI && (n.realLife == head.whoAmI || n.realLife == head.realLife || n.type != head.type))
						next = n;
				}
				if (next == null)
					break;
				chain.Add(next);
				used.Add(next.whoAmI);
				cur = next;
			}
			if (chain.Count <= show)
				return chain;
			var cut = chain.Take(show - 1).ToList();
			cut.Add(chain[^1]);
			return cut;
		}

		/// <summary>
		/// Lays a worm out as an S with a gentle wave running down it: the head stays put and leads to the left (toward the party), each
		/// segment follows a travelling wave behind it, turned along the body like Terraria turns them.
		/// </summary>
		public static void PoseWorm(List<NPC> chain, int time, float attacking)
		{
			if (chain.Count == 0)
				return;
			NPC head = chain[0];
			// Only the wiggle moves: a gentle wave down the body, the same while attacking
			float t = time / 32f;
			// Segments sit one width apart, as Terraria's worm AI keeps them (the sprite sheet's frame height can be
			// several segments tall, which left gaps)
			float SpriteLength(NPC n) => n.width * n.scale;
			var at = new Vector2[chain.Count];
			at[0] = head.Center;
			float along = 0f;
			for (int i = 1; i < chain.Count; i++)
			{
				along += (SpriteLength(chain[i - 1]) + SpriteLength(chain[i])) / 2f;
				float amp = SpriteLength(head) * 0.45f;
				at[i] = head.Center + new Vector2(along, (float)(Math.Sin(i * 0.55f - t) - Math.Sin(-t)) * amp);
			}
			for (int i = 0; i < chain.Count; i++)
			{
				NPC n = chain[i];
				Vector2 ahead = i == 0 ? at[0] + (at[0] - at[Math.Min(1, chain.Count - 1)]) : at[i - 1];
				n.position = at[i] - n.Size / 2f;
				n.rotation = (ahead - at[i]).ToRotation() + WormRotation;
			}
		}
	}

	// ====================================================================== King Slime

	public class KingSlime : Encounter
	{
		public override string Name => "KING SLIME";
		public override string EncounterText => "* KING SLIME bounces into view!";

		// Terraria draws the ninja and the crown separately from the slime's sheet (Main.DrawNPCDirect, type 50).
		// At scale 1 the sheet's 120 px frames sit 4 px low on the 92 px hitbox, so the frame's centre is 10 px above
		// the NPC's: the ninja sits 10 px below the frame's centre, the crown 70 px above the NPC's centre, less a
		// little per frame as the slime squashes and stretches.
		private static readonly float[] NinjaLift = { 0f, -2f, 0f, 2f, 6f, 0f };
		private static readonly float[] CrownLift = { 2f, -6f, 2f, 10f, 2f, 0f };

		private static void DrawExtra(Texture2D tex, Vector2 pos, Vector2 offset, float rotation, Vector2 scale, Color color)
		{
			Vector2 at = pos + (offset * scale).RotatedBy(rotation);
			DrDraw.Sb.Draw(tex, at, null, color, rotation, tex.Size() / 2f, scale, SpriteEffects.None, 0f);
		}

		public override void DrawBehindSprite(Vector2 pos, int frame, float rotation, Vector2 scale, Color color)
		{
			if (Main.dedServ)
				return;
			int f = Math.Clamp(frame, 0, NinjaLift.Length - 1);
			DrawExtra(TextureAssets.Ninja.Value, pos, new Vector2(0f, 10f - NinjaLift[f]), rotation, scale, color);
		}

		public override void DrawOverSprite(Vector2 pos, int frame, float rotation, Vector2 scale, Color color)
		{
			if (Main.dedServ)
				return;
			int f = Math.Clamp(frame, 0, CrownLift.Length - 1);
			DrawExtra(TextureAssets.Extra[39].Value, pos, new Vector2(0f, 10f - (70f - CrownLift[f])), rotation, scale, color);
		}

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* KING SLIME seems content to just wobble.";
			if (LifeRatio < 0.5f && Turn % 3 == 0)
				return "* KING SLIME is getting noticeably smaller.";
			string[] lines = {
				"* KING SLIME wobbles regally.",
				"* Smells like gel and royalty.",
				"* KING SLIME adjusts its crown.",
				"* The ninja inside waves at you.",
			};
			return Dialogue.Flavor(this, lines, Turn);
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
				// It slams down and the shock ripples out in rings, each with one way through
				// (Slams far enough apart that the last one's rings have mostly passed; hurt, he only speeds up a little)
				() => new ShockwaveRings(hard ? 90 : 100) { RingsPerSlam = 2, RingGap = 32, GrowSpeed = hard ? 1.4f : 1.3f, Warn = 36, Thickness = 6f },
				// It jumps and lands on you: the floor ripples out gel both ways
				() => new Slam(king, gelDrop, hard ? 62 : 82) { Width = 48f, Shards = hard ? 3 : 2, FallSpeed = hard ? 10f : 8.5f }.WithSoul(SoulMode.Blue),
				// Its slimes hop in from both sides; jump them
				() => new Walkers((p, v) => Shots.Npc(NPCID.BlueSlime, p, v, 0.8f, 0.8f, new Vector2(16, 12), rotate: false), hard ? 22 : 30)
					{ Speed = 1.5f, HopSpeed = 3.6f }.WithSoul(SoulMode.Blue),
				// The ninja inside throws stars; later it surrounds you with them first
				// (Fewer, slower stars from each side in turn; a small ring of them once it's hurt)
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new SideShots(shuriken, 30) { Side = 0, Alternate = true, Speed = 2.8f },
						new Converge(shuriken, 120) { Count = 4, Speed = 3.4f, FirstAt = 50 })
					: new SideShots(shuriken, 26) { Side = 0, Alternate = true, Speed = 2.6f },
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
		// The whole worm (shortened), slithering, instead of just its head
		public override bool DrawWithTerraria => true;
		// Frozen mid fade-in, back segments were see-through
		public override bool ForceOpaque => true;
		// Its head clear of the bullet box (which ends at x 395) at the hero's scale, the body coming out of the screen's right edge
		public override bool LeadWithDrawNpc => true;
		public override float LeadScale => 1.2f;
		public override Vector2 DrawCenter => new(490f, 170f);
		public override bool SwayParts => false;
		public override IEnumerable<NPC> DrawParts() => BossKit.WormChain(DrawNpc, 10);
		public override void PoseForBattle(List<NPC> parts, NPC anchor, int time, float attacking) =>
			BossKit.PoseWorm(BossKit.WormChain(DrawNpc, 10), time, attacking);

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

		/// <summary>
		/// Hits eat it from the tail up. A random segment used to die each hit, and since a dead segment cuts the chain
		/// the battle draws, the worm shrank to a bare head by 90% HP; from the tail it stays whole-looking (ten
		/// segments shown) until it's nearly dead. Pieces already split off are finished first.
		/// </summary>
		public override NPC StrikeTarget()
		{
			NPC head = DrawNpc;
			var chain = BossKit.WormChain(head, int.MaxValue);
			var onChain = new HashSet<int>(chain.Select(n => n.whoAmI));
			NPC stray = Members().FirstOrDefault(m => m.life > 0 && !onChain.Contains(m.whoAmI));
			if (stray != null)
				return stray;
			return chain.Count > 1 ? chain[^1] : head ?? Npc;
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
			return Dialogue.Flavor(this, lines, Turn);
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
			// Its own Vile Spit, and the Worm Teeth it drops
			// Two shots to pop, and a charged shot only counts as one
			Bullet vile(Vector2 p, Vector2 v)
			{
				Bullet b = Shots.Npc(NPCID.VileSpit, p, v, 0.8f, 0.8f, new Vector2(12, 12), rotate: false).Spin(0.08f);
				b.Toughness = 2;
				b.MaxShotDamage = 1;
				return b;
			}
			Bullet tooth(Vector2 p, Vector2 v) => Shots.Item(ItemID.WormTooth, p, v, 0.9f, 0.7f, new Vector2(8, 8), rotate: true);
			Bullet soul(Vector2 p, Vector2 v) => Shots.Npc(NPCID.EaterofSouls, p, v, 0.7f, 0.8f, new Vector2(16, 16), rotate: false).FaceTravel();
			return Cycle(
				// Yellow SOUL: worms swim in; shoot a segment out and the worm splits, the back half coming for you
				() => new SplittingWorms(hard ? 90 : 110) { Segments = hard ? 10 : 9, Speed = hard ? 2f : 1.7f, SplitSpeed = hard ? 2.6f : 2.2f },
				// It bursts up out of the ground under the box and dives back in
				() => new Eruption(hard ? 64 : 80) { Segments = hard ? 10 : 9, Hunt = hard ? 0.085f : 0.07f },
				// Yellow SOUL: Vile Spit drifts in at you; Eaters of Souls hang back and spit more once it's hurt
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new VileDrift(vile, 22) { Speed = 1.5f },
						new Gunships(soul, vile, 90) { Toughness = 4, FireEvery = 50, ShotSpeed = 1.8f })
					: new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new VileDrift(vile, 24) { Speed = 1.4f },
						new Gunships(soul, vile, 130) { Toughness = 3, FireEvery = 64, ShotSpeed = 1.6f }),
				// Underground: pitch dark but for a little light round the SOUL; the worms' heads glow, and so do the
				// cracks they burst out of
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Underground { LightRadius = hard ? 34f : 40f },
					new Eruption(hard ? 66 : 82) { Segments = 9, Hunt = hard ? 0.07f : 0.055f, FirstAt = 40, DamageMult = 0.6f }),
				// It burrows under the box toward you, then bursts out in a spray of teeth
				() => new BurrowTrail(tooth, hard ? 64 : 80) { Speed = hard ? 1.8f : 1.5f, Shards = hard ? 9 : 7 },
				// It coils round the box, closing in, spitting from its head
				() => new Constrict { Segments = hard ? 17 : 15, EndRadius = hard ? 42f : 50f, Turn = hard ? 0.038f : 0.033f, Spit = vile, SpitEvery = hard ? 28 : 38 },
				// A worm comes in and splits in three in front of you, like it does when you cut it in Terraria
				() => new SplittingWorms(hard ? 85 : 105) { SplitsItself = true, Soul = SoulMode.Red, Segments = 12, Speed = 2f, SplitSpeed = 2.3f },
				// Teeth rain down from its gaping mouth while a worm leaps through
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new Eruption(hard ? 110 : 140) { Segments = 7, FirstAt = 30 },
					new Rain(tooth, hard ? 16 : 22) { SpeedMin = 1.6f, SpeedMax = 2.2f, Wobble = 0f }),
				// Phase 2: two worms erupt at once, one chasing the other
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks,
						new Eruption(80) { Segments = 8 },
						new SplittingWorms(140) { Segments = 8, FirstAt = 40 })
					: new Snake(Head, Body, 110) { Segments = 9, Speed = 2.3f });
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

		/// <summary>Its turns with several attacks at once last longer (grazing them all ended them in a moment).</summary>
		private const int ComboTicks = BattleConstants.DefaultEnemyTurnTicks * 8 / 5;

		/// <summary>When it attacks it opens up (its eye frames) and stays open a while, instead of a one-second blink.</summary>
		private int openUntil = -1;

		public override int? FrameOverride(int time, int frameCount)
		{
			if (frameCount < 8)
				return null;
			if (AttackEnergy > 0.18f)
				openUntil = time + 80;
			bool open = time < openUntil || !CreepersLeft;
			return (open ? 4 : 0) + time / 8 % 4;
		}

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
			return Dialogue.Flavor(this, lines, Turn);
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
			// Crimson drops for bullets: Ichor falling, Vertebrae thrown (no more plain red squares)
			Bullet ichor(Vector2 p, Vector2 v) => Shots.Item(ItemID.Ichor, p, v, 0.8f, 0.6f, new Vector2(8, 10)).Sparkly(new Color(255, 220, 80), 8);
			Bullet bone(Vector2 p, Vector2 v) => Shots.Item(ItemID.Vertebrae, p, v, 0.85f, 0.7f, new Vector2(10, 10));
			return Cycle(
				// Purple SOUL: caught in its mind, Creepers crawl the strings while Ichor drips down
				() => new Combo(ComboTicks,
					new StringRunners(creeper, hard ? 16 : 22) { Speed = hard ? 3.4f : 2.9f },
					new Rain(ichor, hard ? 20 : 28) { SpeedMin = 1.4f, SpeedMax = 1.9f }),
				// Confused: the arrow keys turn around while Creepers drift across
				() => new MindFlip(creeper, hard ? 24 : 30) { Speed = hard ? 1.8f : 1.5f },
				// Its Creepers circle the box, then ram you one at a time
				() => new CreeperCharge { Make = creeper, Count = hard ? 10 : 8, Every = hard ? 22 : 28, Speed = hard ? 5.4f : 4.8f },
				// Illusions: they flicker between real and false together; the false ones can be passed through
				() => new PhaseBullets(creeper, hard ? 7 : 9) { Speed = hard ? 1.9f : 1.6f, RealTicks = hard ? 60 : 52 },
				// Copies of the Brain fade in round the box and all charge: only the steady one is real
				() => new BrainIllusions(hard ? 58 : 70) { Copies = hard ? 5 : 4, Speed = hard ? 6.2f : 5.6f },
				// Neurons fire along the lines between them
				() => new NeuronWeb(hard ? 44 : 54) { Nodes = hard ? 5 : 4 },
				() => new Orbiters(creeper, hard ? 75 : 95) { Count = hard ? 9 : 7, AngularSpeed = 0.035f },
				// It charges across the box itself
				() => new LaneDash((p, d) => Shots.Npc(NPCID.BrainofCthulhu, p, Vector2.Zero, 0.4f, 1f, new Vector2(30, 26), rotate: false),
					hard ? 42 : 56) { AllowVertical = true, Speed = hard ? 8.6f : 7.2f, LaunchSound = SoundID.ForceRoar },
				// Vertebrae lobbed in arcs that come down on you
				() => new BoneLob(bone, hard ? 13 : 17),
				// A spiral of Ichor from the middle of the box (it fades in, so it can't hit you where it starts)
				() => new Sprinkler(ichor)
				{
					Origin = new Vector2(BattleConstants.BoxCenterX, BattleConstants.BoxCenterY),
					Spiral = true, Arms = 3, Every = hard ? 11 : 13, Speed = 1.9f, TurnSpeed = 0.045f, ArmTicks = 20,
				},
				() => new Combo(ComboTicks,
					new Orbiters(creeper, hard ? 110 : 130) { Count = hard ? 7 : 6 },
					new BoneLob(bone, hard ? 22 : 30)),
				// It reads your mind: Ichor lands where you're about to be
				() => new MindRead(hard ? 18 : 22) { Lead = hard ? 42 : 36, Double = true },
				// Your reflection mirrors you through the middle of the box while Ichor drips
				() => new Combo(ComboTicks,
					new MirrorSoul { Second = hard },
					new Rain(ichor, hard ? 16 : 22) { SpeedMin = 1.5f, SpeedMax = 2.1f }),
				// A memory test: spots flash in order, then burst in the same order
				() => new MemoryFlash(150) { Spots = hard ? 6 : 5, Step = hard ? 11 : 13, Radius = hard ? 26f : 24f },
				// Creepers bounce round the walls of its mind
				() => new Ricochet(creeper, hard ? 20 : 26) { Speed = hard ? 3f : 2.6f, Bounces = 3 });
		}
	}

	// ====================================================================== Queen Bee

	public class QueenBee : Encounter
	{
		public override string Name => "QUEEN BEE";
		public override string EncounterText => "* QUEEN BEE buzzes furiously!";
		// Her wings make her sprite tall and her body sits low in it; lifted so she isn't down by the box
		public override Vector2 DrawCenter => new(500, 150);

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* QUEEN BEE hums a calm little tune.";
			string[] lines = {
				"* QUEEN BEE inspects you for honey.",
				"* The buzzing is deafening.",
				"* Smells like honey and anger.",
			};
			return LifeRatio < 0.5f && Turn % 3 == 0 ? "* QUEEN BEE's wings are getting tired." : Dialogue.Flavor(this, lines, Turn);
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
				// The box turns into honeycomb: cells light up, then fill with honey
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks, new Honeycomb(72) { Fill = 0.62f }, new Homing(bee, 40) { Speed = 1.5f, FirstAt = 30 })
					: new Honeycomb(84),
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
		public override bool TargetableParts => true;
		// Hands sit at head - 120 * ai[0] across (see PoseForBattle): ai[0] = 1 is the left one
		public override string PartName(NPC part) => part.type == NPCID.SkeletronHand ? (part.ai[0] > 0f ? "LEFT HAND" : "RIGHT HAND") : "SKELETRON";

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
			return LifeRatio < 0.5f && Turn % 3 == 0 ? "* SKELETRON's curse is weakening." : Dialogue.Flavor(this, lines, Turn);
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
			Bullet head(Vector2 p, Vector2 d) => Shots.Npc(NPCID.SkeletronHead, p, Vector2.Zero, 0.5f, 1.2f, new Vector2(34, 34), rotate: false).Spin(0.35f);
			// With both hands broken, the head does the hands' work
			bool hands = Members().Any(m => m.type == NPCID.SkeletronHand && m.life > 0);
			Bullet hand(Vector2 p, Vector2 d) => hands
				? Shots.Npc(NPCID.SkeletronHand, p, Vector2.Zero, 0.9f, 1f, new Vector2(26, 26), rotate: false)
				: head(p, d);
			Bullet bone(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Bone, p, v, 1f, 0.6f, new Vector2(10, 10), spin: 0.25f);
			return Cycle(
				() => new LaneDash(hand, hard ? 45 : 60) { AllowVertical = true, Speed = hard ? 8f : 7f },
				// A tunnel of bones slides through; follow the gap as it winds
				() => new BoneTunnel(hard ? 9 : 11) { Speed = hard ? 2.6f : 2.2f, GapSize = hard ? 42f : 48f, WanderSpeed = hard ? 0.022f : 0.018f },
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
			return LifeRatio < 0.5f && Turn % 3 == 0 ? "* DEERCLOPS is breathing hard. Frost covers its fur." : Dialogue.Flavor(this, lines, Turn);
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
				// Its shadow hands creep in, freeze, then lunge at you
				() => new FreezeAndFire(hand, hard ? 11 : 15) { LungeSpeed = hard ? 4.2f : 3.6f, FreezeEvery = hard ? 70 : 84 },
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
			return LifeRatio < 0.5f && Turn % 3 == 0 ? "* The WALL OF FLESH is tearing apart." : Dialogue.Flavor(this, lines, Turn);
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
				// The wall itself pushes into the box while its eyes fire across what's left
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new FleshPush { Side = -1, MaxPush = hard ? 0.6f : 0.5f },
					new Beam(hard ? 60 : 80) { FixedAngle = 0f, Tilt = 0.1f, Width = 12f, Color = new Color(255, 80, 200), FireSound = SoundID.Item33, FirstAt = 70 }),
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
			if (LifeRatio < 0.3f && Turn % 2 == 0)
				return $"* {Name} is badly hurt.";
			string[] lines = {
				$"* {Name} towers over you.",
				$"* {Name} is sizing you up.",
				"* The air feels heavy.",
				$"* {Name} prepares its next attack.",
			};
			return string.Format(Dialogue.Flavor(this, lines, Turn), Name);
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
				// Lines of swords (the Roaring Knight's): they appear one by one, then fire across in order
				() => new SwordLines(hard ? 40 : 50) { Diagonals = hard, Color = _ => new Color(255, 120, 120) },
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
