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

		/// <summary>
		/// Faces the way it's going at any angle, like Terraria turns the Hungry: a left-facing sprite flipped while it
		/// heads right, and tilted to its heading (only flipping, it looked backwards coming in from above or below).
		/// </summary>
		public static Bullet FaceAlong(this Bullet b)
		{
			b.OnUpdate += x =>
			{
				x.RotateWithVelocity = false;
				if (x.Velocity.LengthSquared() < 0.01f)
					return;
				float a = x.Velocity.ToRotation();
				x.FlipX = x.Velocity.X > 0f;
				x.Rotation = x.FlipX ? a : a + MathHelper.Pi;
			};
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
		// Lots of segments and spit on screen at once: each hit a little softer than other bosses'
		public override float DamageFactor => 0.75f;
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
			// Its sprite faces down: turned to face the SOUL (it hangs back and spits at you)
			Bullet soul(Vector2 p, Vector2 v)
			{
				Bullet b = Shots.Npc(NPCID.EaterofSouls, p, v, 0.7f, 0.8f, new Vector2(16, 16), rotate: false);
				b.OnUpdate += x => x.Rotation = (battle.SoulCenter - x.Position).ToRotation() - MathHelper.PiOver2;
				return b;
			}
			return Cycle(
				// Yellow SOUL: worms swim in; shoot a segment out and the worm splits, the back half coming for you
				() => new SplittingWorms(hard ? 90 : 110) { Segments = hard ? 10 : 9, Speed = hard ? 2f : 1.7f, SplitSpeed = hard ? 2.6f : 2.2f },
				// It bursts up out of the ground under the box and dives back in
				() => new Eruption(hard ? 70 : 86) { Segments = hard ? 10 : 9, Hunt = hard ? 0.05f : 0.04f, MaxDive = hard ? 4.4f : 4f },
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
				// (The box grows for it: more room to get out of the way in the dark)
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks * 3 / 2,
					new Underground { LightRadius = hard ? 40f : 46f },
					new Eruption(hard ? 64 : 78) { Segments = 9, Hunt = hard ? 0.045f : 0.035f, MaxDive = 3.6f, Warn = 60, FirstAt = 50, DamageMult = 0.6f })
					{ Grow = 1.45f },
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

		/// <summary>Tougher than she looks: she takes a bit over half of every hit (she went down in a few turns).</summary>
		public override float PartDamageScale(NPC part) => 0.55f;
		// Her turns are dense: each sting takes off less
		public override float DamageFactor => 0.65f;

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			int turn = BattleConstants.DefaultEnemyTurnTicks;
			Bullet bee(Vector2 p, Vector2 v) => Shots.Npc(NPCID.Bee, p, v, 1f, 0.5f, new Vector2(10, 10), rotate: false).FaceTravel();
			Bullet stinger(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.Stinger, p, v, 1f, 0.6f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
			Bullet queen(Vector2 p, Vector2 v) => Shots.Npc(NPCID.QueenBee, p, v, 0.45f, 1.2f, new Vector2(40, 30), rotate: false);
			Bullet honeyBall(Vector2 p, Vector2 v) => Shots.Ball(p, v, Honey.Amber, 0.6f, 1.3f);
			return Cycle(
				() => new Homing(bee, hard ? 12 : 16) { Speed = hard ? 2.4f : 2f, Turn = 0.035f, SteerTicks = 75 },
				// Honey puddles over the box: in them you wade at half speed, while bees home in
				() => new Combo(turn,
					new HoneyPuddles { Count = hard ? 4 : 3, Drift = hard ? 0.6f : 0f },
					new Homing(bee, hard ? 22 : 28) { Speed = 1.8f, FirstAt = 30 }),
				// The box turns into honeycomb: cells light up, then fill with honey
				() => hard
					? new Combo(turn, new Honeycomb(64) { Fill = 0.66f }, new Homing(bee, 34) { Speed = 1.6f, FirstAt = 30 })
					: new Combo(turn, new Honeycomb(76), new Homing(bee, 60) { Speed = 1.4f, FirstAt = 60 }),
				// The swarm: a cloud of bees drifting after you, bursting wide every so often
				() => new Swarm { Make = bee, Count = hard ? 20 : 16, Speed = hard ? 0.75f : 0.6f, BurstEvery = hard ? 75 : 90 },
				// Green SOUL: stingers from every side; turn the shield to block them (the turn lasts until the last one)
				() => new ShieldSpears(stinger, hard ? 14 : 19) { Speed = hard ? 3f : 2.5f },
				// The Queen charges across your row, leaving stingers hanging behind her that fire up and down
				() => new RoyalDash(queen, stinger, hard ? 62 : 78) { Speed = hard ? 9f : 8f, FireSpeed = hard ? 3.4f : 3f },
				// She hovers above and sprays stingers in a sweeping fan
				() => new Sprinkler(stinger)
				{
					Every = hard ? 5 : 7, Arms = hard ? 2 : 1, Speed = 3.2f, TurnSpeed = 0.065f, FanSpread = hard ? 1f : 0.85f,
				},
				// Honey eggs pulse, then hatch into bees that come for you
				() => new LarvaHatch(bee, hard ? 36 : 46) { Bees = hard ? 4 : 3, BeeSpeed = hard ? 2.2f : 1.9f },
				() => new LaneDash((p, d) => Shots.Npc(NPCID.QueenBee, p, Vector2.Zero, 0.45f, 1.2f, new Vector2(40, 30), rotate: false).FaceTravel(),
					hard ? 44 : 58) { Speed = hard ? 9.5f : 8f, LaunchSound = SoundID.Roar },
				// The waggle dance: one bee shows the path, then the swarm flies it (backwards below half HP)
				() => new WaggleDance { Make = bee, Reverse = hard, Stream = hard ? 32 : 26 },
				() => new Combo(turn,
					new SideShots(stinger, hard ? 8 : 11) { Side = 0, Speed = 3.8f },
					new Homing(bee, hard ? 40 : 55) { Speed = 1.6f, FirstAt = 40 }),
				// Honey floods up the box (wade through it slowly) while stingers rain down
				() => new Combo(turn,
					new HoneyFlood { Fill = hard ? 0.6f : 0.5f },
					new Rain(stinger, hard ? 9 : 12) { SpeedMin = 2.2f, SpeedMax = 3f }),
				// Stingers hang in the air around you, then dive
				() => new Converge(stinger, hard ? 46 : 60) { Count = hard ? 11 : 8, Speed = hard ? 5f : 4.3f, RotationOffset = MathHelper.PiOver2 },
				// Bees close in from all round; find the gap
				() => new Combo(turn,
					new ClosingRing(bee, hard ? 60 : 75) { Count = 18, GapSize = hard ? 2 : 3, Speed = hard ? 1.3f : 1.1f },
					new Rain(stinger, hard ? 26 : 34)),
				// Green SOUL, harder: tricksters every third stinger
				() => new ShieldSpears(stinger, hard ? 16 : 20) { Speed = hard ? 2.8f : 2.4f, TricksterEvery = 3 },
				// Honey bombs float in and burst into rings of stingers
				() => new Fireworks(honeyBall, stinger, hard ? 40 : 52) { Count = hard ? 10 : 8, ShardSpeed = 2f },
				// Rows of stingers with a gap to thread, bees chasing you through them
				() => new Combo(turn,
					new GapRows(stinger, hard ? 30 : 36) { Speed = 1.7f, GapSize = hard ? 40f : 46f },
					new Homing(bee, hard ? 45 : 60) { Speed = 1.5f, FirstAt = 50 }),
				() => new Combo(turn,
					new Homing(bee, hard ? 22 : 28) { Speed = 1.8f },
					new SideShots(stinger, hard ? 14 : 18) { Side = 0 },
					new HoneyPuddles { Count = 2 }));
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
		// Its turns are busy (two patterns at once): each hit stings a bit less to make up for it
		public override float DamageFactor => 0.8f;

		/// <summary>
		/// Its hands are sturdy (they took a hit or two), and its head shrugs off most of a hit while its hands guard it:
		/// a fifth with both up, half with one, all of it once they're broken.
		/// </summary>
		public override float PartDamageScale(NPC part)
		{
			if (part == null)
				return 1f;
			if (part.type == NPCID.SkeletronHand)
				return 0.16f;
			int hands = Members().Count(m => m.type == NPCID.SkeletronHand && m.active && m.life > 0);
			return hands >= 2 ? 0.08f : hands == 1 ? 0.25f : 0.6f;
		}
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
			Bullet skull(Vector2 p, Vector2 v) => Shots.Npc(NPCID.SkeletronHead, p, v, 0.55f, 1.2f, new Vector2(30, 30), rotate: false);
			Bullet water(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.WaterBolt, p, v, 1f, 0.7f, new Vector2(12, 12));
			// The big finish, once, as it gets low: the SOUL falls sideways past Skeletron after Skeletron
			if (LifeRatio < 0.4f && !fell)
			{
				fell = true;
				return Fall(hard, skull);
			}
			// Every turn has more than one thing going on (one pattern at a time was easy, just long)
			int turn = BattleConstants.DefaultEnemyTurnTicks;
			return Cycle(
				// Slammed into the floor, bones burst up along it, and a skull blaster now and then: jump, and keep jumping
				() => new Combo(turn,
					new FloorBoneWave(hard ? 56 : 70) { Height = hard ? 32f : 28f },
					new SkullBlasters(skull, hard ? 70 : 90) { Charge = 34, FirstAt = 60 }),
				// Undertale's platforms: hop from platform to platform over a floor full of bones
				() => new BonePlatforms { LowSpeed = hard ? 1.4f : 1.2f, SkimEvery = hard ? 44 : 55, Duration = turn * 3 / 2 },
				// Skulls slide in round the box, charge, and fire beams through the SOUL, while water bolts bounce about
				() => new Combo(turn,
					new SkullBlasters(skull, hard ? 36 : 48) { Charge = hard ? 28 : 34 },
					new Ricochet(water, hard ? 60 : 80) { Bounces = 3, Speed = 2.2f, FirstAt = 30 }),
				// Gravity goes every which way (an arrow shows where), bones along whichever side is down
				() => new GravityFlip { FlipEvery = hard ? 80 : 96, Speed = hard ? 3f : 2.6f, AllWays = true, Duration = turn * 4 / 3 },
				// Blue SOUL: blue bones (stand still) and orange bones (keep moving) sweep across, white ones to hop
				() => new ColoredBones(hard ? 30 : 38) { Speed = hard ? 3f : 2.6f },
				// The hands clap together on the SOUL's row while bones rain down
				() => new Combo(turn,
					new HandClap(hand, hard ? 48 : 60),
					new Rain(bone, hard ? 18 : 24)),
				// A tunnel of bones slides through; follow the gap as it winds, skulls flying in along it
				() => new Combo(turn,
					new BoneTunnel(hard ? 9 : 11) { Speed = hard ? 2.6f : 2.2f, GapSize = hard ? 42f : 48f, WanderSpeed = hard ? 0.022f : 0.018f },
					new LaneDash(skull, hard ? 80 : 100) { Speed = 6f, FirstAt = 60 }),
				// The Dungeon Guardian drifts after you while bones fall: keep moving, don't get cornered
				() => new Combo(turn,
					new GuardianChase { MakeGuardian = (p, v) => Shots.Npc(NPCID.DungeonGuardian, p, v, 0.6f, 1.5f, new Vector2(30, 30), rotate: false), Speed = hard ? 1.25f : 1.1f },
					new Rain(bone, hard ? 20 : 26)),
				// A hand slams the floor and scatters bones along it (blue SOUL: jump them), skulls swooping low
				() => new Combo(turn,
					new Slam(hand, bone, hard ? 56 : 72) { Width = 40f, Shards = hard ? 3 : 2, ShardSpeed = 2.6f }.WithSoul(SoulMode.Blue),
					new SkullBlasters(skull, hard ? 80 : 100) { Charge = 36, FirstAt = 70 }),
				// Two long bones turn round the middle like propeller blades, water bolts bouncing between them
				() => new Combo(turn,
					new BoneWheel { Arms = hard ? 3 : 2, Turn = hard ? 0.017f : 0.014f },
					new Ricochet(water, hard ? 70 : 90) { Bounces = 3, Speed = 2f, FirstAt = 50 }),
				// Blue SOUL: walls of bones to jump over and duck under, with blue ones mixed in
				() => new Combo(turn,
					new BoneWalls(hard ? 30 : 38) { Speed = hard ? 3.2f : 2.6f, Color = new Color(235, 225, 200) },
					new ColoredBones(hard ? 60 : 80) { Speed = 2.4f, FirstAt = 40 }),
				// Cursed skulls circle the box, then dive at the SOUL one by one; spikes from the walls between dives
				() => new Combo(turn,
					new CreeperCharge { Make = (p, v) => Shots.Npc(NPCID.CursedSkull, p, v, 0.8f, 1f, new Vector2(22, 22), rotate: false), Count = hard ? 7 : 6, Every = hard ? 32 : 38 },
					new SpikeWalls(hard ? 40 : 52) { FirstAt = 30 }),
				// The box squeezes in while rows of bones drift through with a gap to thread
				() => new GapRows(bone, hard ? 30 : 36) { Speed = 1.5f, GapSize = hard ? 30f : 34f, Drift = 16f, Grow = 0.65f },
				// Four walls of bones close in, one side open, and a skull blaster waits outside
				() => new Combo(turn,
					new BoneCage(hard ? 100 : 120),
					new SkullBlasters(skull, hard ? 80 : 100) { Charge = 36, FirstAt = 50 }),
				// Its spinning head ricochets round a bigger box, bursting into bones at the walls
				() => new BouncingSkull((p, v) => Shots.Npc(NPCID.SkeletronHead, p, v, 0.7f, 1.3f, new Vector2(44, 44), rotate: false).Spin(0.3f), bone)
					{ Speed = hard ? 3.2f : 2.8f, Bones = hard ? 7 : 5, Grow = 1.3f },
				// A ring of skulls round the box, firing in turn like a clock hand, over sweeping coloured bones
				() => new Combo(turn,
					new SkullBlasters(skull, hard ? 26 : 32) { Charge = hard ? 26 : 30, Sweep = MathHelper.TwoPi / 6f },
					new ColoredBones(hard ? 70 : 90) { Speed = 2.6f, FirstAt = 50 }) { Grow = 1.2f },
				// Blasters while a hand sweeps through
				() => new Combo(turn,
					new SkullBlasters(skull, hard ? 54 : 70) { Charge = 36 },
					new LaneDash(hand, hard ? 70 : 90) { Speed = 7f, FirstAt = 40 }),
				// The fall (also its finish as it gets low)
				() => Fall(hard, skull));
		}

		private bool fell;

		private static EnemyAttack Fall(bool hard, Func<Vector2, Vector2, Bullet> skull) =>
			new SideFall { MakeSkull = skull, WallEvery = hard ? 48 : 56, SkullEvery = hard ? 66 : 80, Gap = hard ? 44f : 48f, TunnelGap = hard ? 46f : 52f };
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
			// Shadow hands glow in the dark: their eyes are all you see of them
			Bullet shadowHand(Vector2 p, Vector2 v)
			{
				Bullet h = hand(p, v);
				h.Light = 18f;
				return h;
			}
			int turn = BattleConstants.DefaultEnemyTurnTicks;
			return Cycle(
				() => new FloorSpikes(spike, hard ? 26 : 34) { Warn = hard ? 26 : 30 }.WithSoul(SoulMode.Blue),
				// Its shadow hands creep in, freeze, then lunge at you (fewer and slower than they were: a crowd of them
				// all lunging at once couldn't be dodged)
				() => new FreezeAndFire(hand, hard ? 22 : 28) { LungeSpeed = hard ? 3.4f : 3f, FreezeEvery = hard ? 80 : 92, CreepSpeed = 0.6f },
				// Rubble tears up in a line across the box, from the floor or the ceiling
				() => new RubbleLine(hard ? 58 : 72) { Reach = hard ? 0.6f : 0.55f, StepEvery = hard ? 5 : 6 },
				// A boulder crashes down and breaks into rubble along the floor
				() => new Slam(boulder, rock, hard ? 58 : 76) { Width = 44f, Shards = hard ? 3 : 2, Debris = 4 }.WithSoul(SoulMode.Blue),
				// A blizzard shoves you about while snowballs ride the wind
				() => new Blizzard { Make = iceShard, Push = hard ? 0.8f : 0.65f, SnowEvery = hard ? 8 : 11, Speed = hard ? 3.3f : 2.8f },
				// Frost gathers around you, then shatters inward
				() => new Converge(iceShard, hard ? 52 : 68) { Count = hard ? 10 : 8, Speed = hard ? 4.4f : 3.8f },
				// Its stare sweeps across: keep still while it passes, dodge the rocks between
				() => new Combo(turn,
					new FrostStare(hard ? 60 : 72) { Speed = hard ? 2.6f : 2.3f },
					new Rain(rock, hard ? 16 : 22) { SpeedMin = 2f, SpeedMax = 2.6f, Wobble = 0f }),
				// It stomps and ice rolls along the floor: jump it (sometimes from both sides)
				() => new StompWaves(hard ? 42 : 52) { Speed = hard ? 3.2f : 2.8f },
				// Shadow hands grab along your row or your column, from both ends
				() => new ShadowGrab(hand, hard ? 52 : 64) { Speed = hard ? 6.5f : 6f },
				() => new Rain(rock, hard ? 10 : 15) { SpeedMin = 2.2f, SpeedMax = 3f, Wobble = 0f },
				// The floor freezes over (you slide) while icicles drop
				() => new IceFloor { IcicleEvery = hard ? 18 : 24 },
				// An avalanche: walls of snow sweep through with a gap
				() => new Tsunami(hard ? 80 : 100) { Water = new Color(225, 240, 255), Speed = hard ? 1.7f : 1.5f, GapSize = hard ? 42f : 48f },
				// Boulders lobbed in arcs, bursting into rubble where they land
				() => new Lobs(boulder, hard ? 34 : 44) { Splash = 3 },
				// An ice prison closes in round you; find the gap
				() => new Combo(turn,
					new ClosingRing(iceShard, hard ? 64 : 80) { Count = 18, GapSize = 3, Speed = hard ? 1.25f : 1.05f },
					new RubbleLine(hard ? 110 : 140) { FirstAt = 50 }),
				// Stomps send rings of frost out across the box
				() => new ShockwaveRings(hard ? 60 : 75) { Color = Frost.Deep, GrowSpeed = hard ? 1.7f : 1.5f },
				() => new Combo(turn,
					new FloorSpikes(spike, 45),
					new Rain(rock, 22) { Wobble = 0f }),
				// Phase 2, full screen: the lights go out. In the dark you only see the shadow hands' eyes as they creep
				// in, freeze and lunge, and the cracks where rubble is about to tear up
				() => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Underground { LightRadius = 70f, Sound = SoundID.DeerclopsScream },
						new FreezeAndFire(shadowHand, 26) { LungeSpeed = 3.2f, FreezeEvery = 96, CreepSpeed = 0.55f },
						new RubbleLine(120) { FirstAt = 90, Reach = 0.4f }) { FullScreen = true }
					: new ShadowGrab(hand, 58));
		}
	}

	// ====================================================================== Wall of Flesh

	public class WallOfFlesh : Encounter
	{
		public override string Name => "WALL OF FLESH";
		public override string EncounterText => "* The WALL OF FLESH closes in!";
		// Its own track, held for the whole battle (Terraria only plays it while it notices the wall near the screen)
		public override int BattleMusic => 12;
		// Every turn piles on (two or three things at once), so each hit is a bit lighter
		public override float DamageFactor => 0.75f;
		public override Vector2 DrawCenter => new(540f, 165f);

		public override IEnumerable<NPC> Members()
		{
			foreach (NPC n in base.Members())
				yield return n;
			foreach (NPC n in BossKit.OfTypes(NPCID.TheHungry, NPCID.TheHungryII, NPCID.LeechHead, NPCID.LeechBody, NPCID.LeechTail))
				if (n.realLife != Npc.whoAmI)
					yield return n;
		}

		// ---- three heads: the mouth and both eyes, each with its own HP ----

		private NPC Mouth => Npc.active && Npc.type == NPCID.WallofFlesh ? Npc
			: Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active ? Main.npc[Main.wofNPCIndex] : Npc;

		/// <summary>The eyes still in the wall, the upper one first.</summary>
		public List<NPC> Eyes => Members().Where(m => m.active && m.type == NPCID.WallofFleshEye).OrderBy(m => m.Center.Y).ToList();

		private readonly Dictionary<int, int> eyeHp = new();
		/// <summary>An eye's own HP: an eighth of the wall's. Every hit on it hurts the wall too (they share its life).</summary>
		private int EyeMax => Math.Max(1, (int)(Mouth.lifeMax * 0.125f));

		public override bool TargetableParts => true;
		public override NPC CorePart => Mouth.active ? Mouth : null;
		public override IEnumerable<NPC> PartPool() => Eyes.Prepend(Mouth).Where(m => m.active);
		public override string PartName(NPC part)
		{
			if (part.type != NPCID.WallofFleshEye)
				return "MOUTH";
			List<NPC> eyes = Eyes;
			return eyes.Count > 1 && part == eyes[0] ? "UPPER EYE" : eyes.Count > 1 ? "LOWER EYE" : part.Center.Y < Mouth.Center.Y ? "UPPER EYE" : "LOWER EYE";
		}

		public override bool PartHit(NPC part, int damage)
		{
			if (part.type != NPCID.WallofFleshEye)
				return false;
			int left = (eyeHp.TryGetValue(part.whoAmI, out int hp) ? hp : EyeMax) - damage;
			eyeHp[part.whoAmI] = left;
			return left <= 0;
		}

		/// <summary>Lab: sets an eye's own HP.</summary>
		internal void LabSetEyeHp(NPC eye, int hp) => eyeHp[eye.whoAmI] = hp;

		public override float PartLifeRatio(NPC part) => part.type == NPCID.WallofFleshEye
			? (eyeHp.TryGetValue(part.whoAmI, out int hp) ? hp : EyeMax) / (float)EyeMax
			: Mouth.life / (float)Math.Max(1, Mouth.lifeMax);

		/// <summary>The eyes guard the mouth: it takes under half a hit while both watch, all of it once they're gone.</summary>
		public override float PartDamageScale(NPC part)
		{
			if (part != null && part.type == NPCID.WallofFleshEye)
				return 1f;
			int eyes = Eyes.Count;
			return eyes >= 2 ? 0.45f : eyes == 1 ? 0.7f : 1f;
		}

		// ---- drawing: the whole wall, not just its mouth ----

		public override bool DrawsSelf => true;
		// It slides in from the right over a second before the battle gets going
		public override int IntroHold => 60;

		/// <summary>Where the eyes sit on the wall, above and below the mouth.</summary>
		private const float EyeOffset = 104f;

		public override EnemySnapshot DrawSelf(Vector2 at, int time, NPC flash, Dictionary<NPC, Vector2> partSpots, BattleSystem battle)
		{
			var sb = DrDraw.Sb;
			NPC mouth = Mouth;
			// Its entrance: sliding in from off the right edge, fading up as it comes
			float enter = battle.EnemyEntrance;
			float eased = 1f - (float)Math.Pow(1f - enter, 3);
			at.X += (1f - eased) * 320f;
			float alpha = MathHelper.Clamp(enter * 1.4f, 0f, 1f);
			// Idle: the whole wall heaves slowly, and the mouth and eyes each bob on their own beat
			float heave = (float)Math.Sin(time / 45f);
			at.X += heave * 3f;
			Main.instance.LoadNPC(NPCID.WallofFlesh);
			Main.instance.LoadNPC(NPCID.WallofFleshEye);
			Texture2D mouthTex = TextureAssets.Npc[NPCID.WallofFlesh].Value, eyeTex = TextureAssets.Npc[NPCID.WallofFleshEye].Value;
			int mouthFrames = Math.Max(1, Main.npcFrameCount[NPCID.WallofFlesh]), eyeFrames = Math.Max(1, Main.npcFrameCount[NPCID.WallofFleshEye]);
			Rectangle mouthFrame = mouthTex.Frame(1, mouthFrames, 0, time / 8 % mouthFrames);
			float s = 84f / Math.Max(1, mouthFrame.Height);
			bool flashOn = time / 6 % 2 == 0;

			// The flesh: Terraria's wall strip (three frames, stacked), tiled up and down the screen behind the mouth (one
			// strip, as in the game: a second one beside it showed as a stray band of membrane)
			Texture2D wall = TextureAssets.Wof.Value;
			int fh = wall.Height / 3;
			int wf = time / 6 % 3;
			float ws = s, left = at.X - mouthFrame.Width * s * 0.35f;
			Color shade = Color.White * alpha;
			for (float y = at.Y - 300f; y < at.Y + 260f; y += fh * ws)
			{
				// It ripples as it heaves, a little behind the mouth
				float ripple = (float)Math.Sin(time / 45f - y * 0.01f) * 2f;
				sb.Draw(wall, new Vector2(left + ripple, y), new Rectangle(0, wf * fh, wall.Width, fh), shade, 0f, Vector2.Zero, ws, SpriteEffects.None, 0f);
			}

			// The eyes (or the torn holes where they were), turning to watch the SOUL in the box or you outside it
			Vector2 look = battle.SoulCenter;
			List<NPC> eyes = Eyes;
			foreach (int side in new[] { -1, 1 })
			{
				Vector2 pos = at + new Vector2(6f, side * EyeOffset + (float)Math.Sin(time / 28f + side * 1.7f) * 4f);
				NPC eye = eyes.FirstOrDefault(e => Math.Sign(e.Center.Y - mouth.Center.Y) == side) ?? (eyes.Count == 2 ? eyes[side < 0 ? 0 : 1] : null);
				if (eye == null)
				{
					DrDraw.Ball(pos, 14f, new Color(60, 5, 15) * alpha);
					DrDraw.Ball(pos + new Vector2(-3, 2), 9f, new Color(25, 0, 5) * alpha);
					continue;
				}
				partSpots[eye] = pos;
				Rectangle ef = eyeTex.Frame(1, eyeFrames, 0, time / 8 % eyeFrames);
				float rot = (look - pos).ToRotation() - MathHelper.Pi;
				Color c = (eye == flash && flashOn ? Color.Lerp(Color.White, Color.Yellow, 0.5f) : Color.White) * alpha;
				float blink = 1f + (float)Math.Sin(time / 20f + side) * 0.03f;
				sb.Draw(eyeTex, pos, ef, c, rot, ef.Size() / 2f, s * blink, SpriteEffects.None, 0f);
			}

			// The mouth, in the middle, breathing
			Vector2 mouthAt = at + new Vector2(0f, (float)Math.Sin(time / 32f) * 3f);
			partSpots[mouth] = mouthAt;
			Color mc = (mouth == flash && flashOn ? Color.Lerp(Color.White, Color.Yellow, 0.5f) : Color.White) * alpha;
			float breathe = 1f + (float)Math.Sin(time / 32f) * 0.04f;
			sb.Draw(mouthTex, mouthAt, mouthFrame, mc, 0f, mouthFrame.Size() / 2f, s * breathe, SpriteEffects.None, 0f);
			return new EnemySnapshot { Texture = mouthTex, Frame = mouthFrame, Position = at, Scale = s, Color = Color.White, Valid = true };
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

		/// <summary>How far the wall has eaten into the box (a share of its width): it's always coming.</summary>
		private float Advance => Math.Min(0.3f, 0.06f + Turn * 0.025f);

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			int turn = BattleConstants.DefaultEnemyTurnTicks;
			Bullet laser(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.EyeLaser, p, v, 1f, 0.7f, new Vector2(14, 6), rotationOffset: MathHelper.PiOver2);
			Bullet hungry(Vector2 p, Vector2 v) => Shots.Npc(NPCID.TheHungry, p, v, 0.55f, 0.8f, new Vector2(12, 12), rotate: false).FaceAlong();
			Bullet leechHead(Vector2 p, Vector2 v) => Shots.Npc(NPCID.LeechHead, p, v, 0.8f, 0.8f, new Vector2(12, 12), rotationOffset: BossKit.WormRotation);
			Bullet leechBody(Vector2 p, Vector2 v) => Shots.Npc(NPCID.LeechBody, p, v, 0.8f, 0.7f, new Vector2(10, 10), rotationOffset: BossKit.WormRotation);
			Bullet scythe(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.DemonSickle, p, v, 0.8f, 0.8f, new Vector2(16, 16), rotate: false);
			Bullet ember(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.ImpFireball, p, v, 1f, 0.6f, new Vector2(10, 10));
			// Its eyes fire from where they are on the screen; a broken eye fires nothing
			List<Vector2> eyeSpots() => Eyes.Select(e => battle.PartScreen(e)).ToList();
			Vector2 mouthSpot() => battle.PartScreen(Mouth);
			bool eyesLeft = Eyes.Count > 0;
			// With both eyes gone it's all mouth (and angrier)
			float rage = eyesLeft ? 1f : 1.2f;

			EnemyAttack With(EnemyAttack main, params EnemyAttack[] more) =>
				new Combo(main.Duration, new EnemyAttack[] { new FleshEdge { Share = Advance } }.Concat(new[] { main }).Concat(more).ToArray())
				{ Grow = main.Grow };
			EnemyAttack eyesOr(Func<EnemyAttack> withEyes, Func<EnemyAttack> without) => eyesLeft ? withEyes() : without();

			return Cycle(
				// Its eyes take turns firing bursts at you while the Hungry lunge on their tethers
				() => With(eyesOr(
					() => new EyeLasers(eyeSpots, laser, hard ? 30 : 38) { Burst = hard ? 4 : 3, Speed = 4.6f },
					() => new HungryTethers(hungry, 26)),
					new HungryTethers(hungry, hard ? 52 : 66) { FirstAt = 30 }),
				// The eyes aim, then sweep beams through you, crossing
				() => With(eyesOr(
					() => new EyeBeams(eyeSpots, hard ? 140 : 160) { Width = hard ? 16f : 14f, Sweep = hard ? 0.34f : 0.3f },
					() => new Jaws((p, v) => Shots.Ball(p, v, new Color(255, 220, 220), 1f, 1.5f), 60) { GapSize = 36f })),
				// Its tongue latches on and drags you toward the wall while lasers come
				() => With(new Tongue { Mouth = mouthSpot, Pull = (hard ? 0.62f : 0.5f) * rage },
					eyesOr(() => new EyeLasers(eyeSpots, laser, hard ? 44 : 56) { Burst = 2 }, () => new Rain((p, v) => Shots.Blood(p, v), 14))),
				// Walls of the Hungry sweep through with a gap
				() => With(new Walls(hungry, hard ? 55 : 70) { Side = -1, Speed = (hard ? 2f : 1.6f) * rage, Spacing = 18f, GapSize = 42f }),
				// Demon scythes fade in round the box, crawl, then speed through it
				() => With(new Scythes(scythe, hard ? 38 : 48) { Count = hard ? 4 : 3 }),
				// Leeches wriggle out of the wall
				() => With(new Snake(leechHead, leechBody, hard ? 70 : 95) { Side = -1, Segments = 6, Speed = 2.6f * rage },
					new Scythes(scythe, hard ? 90 : 120) { FirstAt = 40, Count = 2 }),
				// Its mouth: teeth snap shut over the box, leaving one gap
				() => With(new Jaws((p, v) => Shots.Ball(p, v, new Color(255, 220, 220), 1f, 1.5f), hard ? 62 : 80) { GapSize = hard ? 34f : 40f }),
				// The Underworld rises: lava climbs the box while fire imps lob fireballs
				() => With(new LavaRise { MakeSide = ember, Peak = hard ? 0.45f : 0.38f }),
				// Green SOUL: the Hungry come from every side; block them
				() => new ShieldSpears(hungry, hard ? 17 : 21) { Speed = (hard ? 2.6f : 2.3f) * rage, TricksterEvery = 4 },
				// Blood rains from above while the Hungry lunge
				() => With(new Rain((p, v) => Shots.Blood(p, v), hard ? 9 : 12) { SpeedMin = 2.2f, SpeedMax = 3f },
					new HungryTethers(hungry, hard ? 44 : 56)),
				// The wall shoves into the box while its eyes fire across what's left
				() => With(new FleshPush { Side = -1, MaxPush = hard ? 0.55f : 0.45f },
					eyesOr(() => new EyeBeams(eyeSpots, 200) { FirstAt = 70 }, () => new Scythes(scythe, 60) { FirstAt = 60 })),
				// Everything it has: beams, tethers and scythes in a bigger box, the lava coming up
				() => hard
					? With(new LavaRise { MakeSide = ember, Peak = 0.3f, RiseShare = 0.6f, Duration = turn * 3 / 2 },
						eyesOr(() => new EyeBeams(eyeSpots, 180) { FirstAt = 40 }, () => new Jaws((p, v) => Shots.Ball(p, v, new Color(255, 220, 220), 1f, 1.5f), 90)),
						new HungryTethers(hungry, 48),
						new Scythes(scythe, 80) { FirstAt = 60 }).WithGrow(1.25f)
					: With(new HungryTethers(hungry, 34), new Scythes(scythe, 70) { FirstAt = 40 }));
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
