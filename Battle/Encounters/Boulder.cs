using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle.Encounters
{
	/// <summary>
	/// A rolling boulder (a trap's projectile) as an enemy. It has no NPC, so the battle runs on the stand-in slot
	/// like a duel's opponent. It doesn't talk (it's a rock), but it asks questions anyway, has the ACTs of something
	/// that can't hear you, and its little scenes are mostly it rolling over things.
	/// </summary>
	public sealed class BoulderEncounter : Encounter
	{
		public int ProjType = ProjectileID.Boulder;
		/// <summary>What you named it (the Name act), or null.</summary>
		private string given;
		private int namings;

		private static readonly string[] Names = { "DWAYNE", "ROCKY", "KEVIN", "SIR ROLLS-A-LOT", "PEBBLES", "GREG" };

		public static readonly HashSet<int> Types = new()
		{
			ProjectileID.Boulder, ProjectileID.BouncyBoulder, ProjectileID.LifeCrystalBoulder, ProjectileID.MoonBoulder,
			ProjectileID.RollingCactus, ProjectileID.MiniBoulder,
		};

		private string Kind => ProjType switch
		{
			ProjectileID.BouncyBoulder => "BOUNCY BOULDER",
			ProjectileID.LifeCrystalBoulder => "LIFE CRYSTAL BOULDER",
			ProjectileID.MoonBoulder => "MOON BOULDER",
			ProjectileID.RollingCactus => "ROLLING CACTUS",
			ProjectileID.MiniBoulder => "PEBBLE",
			_ => "BOULDER",
		};

		public override string Name => given ?? Kind;
		public override string EncounterText => $"* A {Kind} rolls into your path!\n* It will not be apologizing.";
		public override bool IsBoss => false;
		public override int Damage => Main.hardMode ? 55 : 28;
		public override Terraria.Audio.SoundStyle? Voice => SoundID.Item70;

		public override bool CustomSprite(out Texture2D texture, out Rectangle frame)
		{
			Main.instance.LoadProjectile(ProjType);
			texture = TextureAssets.Projectile[ProjType].Value;
			int frames = Math.Max(1, Main.projFrames[ProjType]);
			frame = new Rectangle(0, 0, texture.Width, texture.Height / frames);
			return true;
		}

		// It's a boulder: it rolls, slowly, in place
		public override float DrawRotation(int time) => time * 0.03f;
		public override float DrawScale(Rectangle frame) => 96f / Math.Max(1, Math.Max(frame.Width, frame.Height));

		public override string Bubble(int turn) => null;

		private static readonly string[] Lines =
		{
			"* {0} is rolling in place. Menacingly.",
			"* {0} is thinking about rolling. It is always thinking about rolling.",
			"* {0} has no eyes, yet you feel watched.",
			"* {0} gathers no moss.",
			"* Smells like gravel and poor life choices.",
			"* {0} is perfectly round. It's very proud of that.",
			"* {0} wobbles. Was that a threat?",
			"* You hear a faint rumble. It's either {0} or your stomach.",
			"* {0} is 100% rock. 0% remorse.",
			"* Somewhere, a pressure plate feels guilty.",
		};

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return $"* {Name} seems ready to roll somewhere else.";
			if (LifeRatio < 0.35f)
				return $"* {Name} is covered in cracks. It's fine. It's fine.";
			return string.Format(Lines[Turn % Lines.Length], Name);
		}

		// ---- its questions (it doesn't talk, but it asks) ----

		private static TalkAnswer A(string option, string reply, float mercy = 0f, bool skip = false, float damage = 1f)
			=> new() { Option = option, Reply = reply.Split('|'), Mercy = mercy, Skip = skip, DamageMult = damage };

		public override TalkTurn Talk(int turn) => (turn % 6) switch
		{
			0 => new TalkTurn { Lines = new[] { "...", "(It's a boulder. It doesn't talk.)" } },
			1 => new TalkTurn
			{
				Lines = new[] { "..." },
				Question = new TalkQuestion
				{
					Prompt = $"* {Name} seems to be asking if you believe in gravity.",
					Answers = new[]
					{
						A("Yes", "(It nods. Somehow. It keeps rolling.)"),
						A("No", "(It starts to float, confused.)|(It forgets to attack.)", mercy: 20f, skip: true),
						A("What's gravity?", "(It's disappointed in your education.)", damage: 1.25f),
					},
				},
			},
			2 => new TalkTurn { Lines = new[] { "*rumble*", "(That might have been a pep talk. For itself.)" } },
			3 => new TalkTurn
			{
				Lines = new[] { "*grind*" },
				Question = new TalkQuestion
				{
					Prompt = $"* {Name} wants to know where it's going.",
					Answers = new[]
					{
						A("Downhill", "(Correct. It's always downhill.)|(It respects that you get it.)", mercy: 25f),
						A("Toward me", "(That was the wrong thing to tell a boulder.)", damage: 1.35f),
						A("Nowhere", "(It has an existential crisis.)|(It sits very still for a while.)", mercy: 20f, skip: true),
						A("Somewhere nice", "(It imagines a beach. Boulders like beaches.)", mercy: 30f),
					},
				},
			},
			4 => new TalkTurn { Lines = new[] { "...", "(The silence is very loud.)" } },
			_ => new TalkTurn
			{
				Lines = new[] { "*rumble rumble*" },
				Question = new TalkQuestion
				{
					Prompt = $"* {Name} challenges you to rock, paper, scissors.",
					Answers = new[]
					{
						A("Rock", "(A tie. It respects a fellow rock.)", mercy: 25f),
						A("Paper", "(You win. It's buried in paperwork.)|(It forgets to attack.)", mercy: 30f, skip: true),
						A("Scissors", "(Scissors versus a boulder. The scissors lose.)|(So do you.)", damage: 1.5f),
					},
				},
			},
		};

		// ---- ACTs, with little scenes ----

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			new ActOption
			{
				Name = "Check",
				Description = "Useless\nanalysis",
				Run = b => new List<string> { $"* {Name} - AT {Damage} DF 999\n* A rock. It rolls. That's the whole personality." },
			},
			new ActOption
			{
				Name = "Push",
				Description = "Push it\naway",
				Run = b =>
				{
					PlayPush(b);
					float gained = GainMercy("Push", 20f);
					var lines = new List<string>
					{
						$"* You push {Name} with all your might.",
						"* It does not move.\n* It does not care.",
					};
					if (gained > 0f)
						lines.Add("* ...but it seems flattered by the effort.");
					if (Mercy >= 100f)
						lines.Add(SpareableLine);
					return lines;
				},
			},
			new ActOption
			{
				Name = "Jump",
				Description = "Leap over\nit",
				Run = b =>
				{
					PlayJump(b);
					float gained = GainMercy("Jump", 35f);
					var lines = new List<string>
					{
						$"* {Name} rolls at you.\n* You leap over it like an action hero!",
						gained > 0f ? "* It's impressed. Rocks can be impressed." : "* It's seen that one before.",
					};
					if (Mercy >= 100f)
						lines.Add(SpareableLine);
					return lines;
				},
			},
			new ActOption
			{
				Name = "Name",
				Description = "Give it\na name",
				Run = b =>
				{
					string old = Name;
					given = Names[namings++ % Names.Length];
					float gained = GainMercy("Name", 30f);
					var lines = new List<string> { $"* You name {old} \"{given}\".", gained > 0f ? $"* {given} rolls a little happier." : $"* {given} has had a lot of names today." };
					if (Mercy >= 100f)
						lines.Add(SpareableLine);
					return lines;
				},
			},
			new ActOption
			{
				Name = "Pet",
				Description = "It's a\nrock",
				Run = b =>
				{
					PlayFlatten(b);
					var lines = new List<string>
					{
						$"* You reach out to pet {Name}.",
						"* It rolls over your hand.\n* And your arm.\n* And the rest of you.",
						"* ...you feel closer, somehow.",
					};
					GainMercy("Pet", 15f);
					if (Mercy >= 100f)
						lines.Add(SpareableLine);
					return lines;
				},
			},
			HealPrayerAct(),
		};

		/// <summary>The hero walks up, shoves, the boulder wobbles a bit, the hero slides back.</summary>
		private static void PlayPush(BattleSystem b)
		{
			float reach = b.EnemySpot.X - b.HeroSpot.X - 80f;
			b.PlayCutscene(new BattleSystem.Cutscene
			{
				Length = 130,
				Hero = t => t < 40 ? new Vector2(reach * Ease(t / 40f), 0f)
					: t < 85 ? new Vector2(reach + (float)Math.Sin(t * 0.9f) * 3f, 0f)
					: new Vector2(reach * (1f - Ease((t - 85) / 45f)), 0f),
				Enemy = t => t is >= 40 and < 85 ? new Vector2((float)Math.Sin(t * 0.9f) * 1.5f, 0f) : Vector2.Zero,
				OnTick = (bs, t) =>
				{
					if (t is 45 or 60 or 75)
					{
						AttackSfx.Vanilla(SoundID.Dig, 0.6f, -0.3f);
						bs.Dust(bs.HeroSpot + new Vector2(reach - 10f, 0f), new Color(150, 130, 110));
					}
				},
			});
		}

		/// <summary>It rolls at the hero, who jumps it; it rolls off one side of the screen and back in the other.</summary>
		private static void PlayJump(BattleSystem b)
		{
			float toHero = b.HeroSpot.X - b.EnemySpot.X;
			b.PlayCutscene(new BattleSystem.Cutscene
			{
				Length = 140,
				Enemy = t => t < 70 ? new Vector2((toHero - 300f) * (t / 70f), 0f)
					: new Vector2(500f * (1f - Ease((t - 70) / 70f)), 0f),
				EnemySpin = t => t < 70 ? -t * 0.25f : 0f,
				Hero = t =>
				{
					// Up as it arrives, down once it's passed
					float k = (t - 22) / 28f;
					return k is > 0f and < 1f ? new Vector2(0f, -(float)Math.Sin(k * Math.PI) * 80f) : Vector2.Zero;
				},
				OnTick = (bs, t) =>
				{
					if (t == 0)
						AttackSfx.Vanilla(SoundID.Item70, 0.7f);
					if (t == 22)
						AttackSfx.Vanilla(SoundID.DoubleJump, 0.8f);
				},
			});
		}

		/// <summary>It rolls straight over the hero, who's left pressed into the ground for a moment.</summary>
		private static void PlayFlatten(BattleSystem b)
		{
			float toHero = b.HeroSpot.X - b.EnemySpot.X;
			b.PlayCutscene(new BattleSystem.Cutscene
			{
				Length = 120,
				Enemy = t => t < 40 ? new Vector2(toHero * Ease(t / 40f), 0f)
					: t < 70 ? new Vector2(toHero, 0f)
					: new Vector2(toHero * (1f - Ease((t - 70) / 50f)), 0f),
				EnemySpin = t => t < 40 ? -t * 0.2f : t >= 70 ? (t - 70) * 0.2f : -8f,
				Hero = t => t is >= 38 and < 75 ? new Vector2(0f, 12f) : Vector2.Zero,
				OnTick = (bs, t) =>
				{
					if (t == 38)
					{
						AttackSfx.Vanilla(SoundID.NPCHit4, 0.6f, -0.4f);
						bs.ShakeScreen(4f);
						bs.Dust(bs.HeroSpot, new Color(170, 150, 120), 10);
					}
				},
			});
		}

		private static float Ease(float k)
		{
			k = MathHelper.Clamp(k, 0f, 1f);
			return k * k * (3f - 2f * k);
		}

		// ---- attacks ----

		private Bullet Rock(Vector2 p, Vector2 v, float size)
		{
			Bullet b = Shots.Proj(ProjType, p, v, 1f, 1f, new Vector2(size * 0.8f), rotate: false, spin: 0.12f * Math.Sign(v.X == 0 ? 1 : v.X));
			Main.instance.LoadProjectile(ProjType);
			Texture2D tex = TextureAssets.Projectile[ProjType].Value;
			b.Scale = size / Math.Max(1, tex.Width);
			b.OffscreenMargin = 200f;
			// A rock to the face hurts, however small: a bigger rock, a bigger share
			b.MinLifeShare = MathHelper.Clamp(size / 300f, 0.04f, 0.12f);
			return b;
		}

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f || Main.hardMode;
			bool bouncy = ProjType == ProjectileID.BouncyBoulder;
			bool moon = ProjType == ProjectileID.MoonBoulder;
			return Cycle(
				// Blue SOUL: boulders roll along the floor, jump them
				() => new FloorRollers((p, v) => Rock(p, v, 22f), hard ? 46 : 60) { Speed = hard ? 3f : 2.5f },
				// Rocks tumble down from above, bouncing once off the floor
				() => new Bouncers((p, v) => Rock(p, v, 16f), hard ? 22 : 30) { Speed = 1.3f, BounceSpeed = bouncy ? 4.6f : moon ? 2.6f : 3.4f, Gravity = moon ? 0.05f : 0.09f },
				// An avalanche of pebbles with a gap
				() => new GapRows((p, v) => Rock(p, v, 12f), hard ? 30 : 38) { Speed = hard ? 1.9f : 1.6f, GapSize = hard ? 42f : 50f, Spacing = 16f },
				// One big one, ricocheting around the box
				() => new Ricochet((p, v) => Rock(p, v, 34f), hard ? 70 : 90) { Speed = hard ? 2.6f : 2.2f, Bounces = bouncy ? 4 : 2 },
				// Blue SOUL: a huge one rolls across, with small ones bouncing after it
				() => new Combo(BattleConstants.DefaultEnemyTurnTicks,
					new FloorRollers((p, v) => Rock(p, v, 30f), hard ? 90 : 110) { Speed = 2.2f },
					new Bouncers((p, v) => Rock(p, v, 12f), hard ? 40 : 54) { Speed = 1.2f, BounceSpeed = 3.2f, FirstAt = 30 }).WithSoul(SoulMode.Blue));
		}

		// ---- the stand-in ----

		/// <summary>Spared rather than broken (breaking it leaves some stone behind).</summary>
		public bool SparedIt;

		public override void Spare()
		{
			// It rolls off somewhere else; nothing to loot
			SparedIt = true;
			Npc.life = 0;
			Npc.active = false;
		}
	}

	/// <summary>Blue SOUL: boulders roll along the floor of the box from one side or the other.</summary>
	public class FloorRollers : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 2.5f;

		public FloorRollers(Func<Vector2, Vector2, Bullet> make, int every = 60)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 90;
			Soul = SoulMode.Blue;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = index % 2 == 0;
			Bullet probe = Make(Vector2.Zero, Vector2.Zero);
			float r = probe.HitSize.Y / 2f;
			var pos = new Vector2(fromLeft ? box.Left - 30 : box.Right + 30, box.Bottom - 4 - r);
			Bullet b = Make(pos, new Vector2(fromLeft ? Speed : -Speed, 0f));
			b.Lifetime = 400;
			AttackSfx.Vanilla(SoundID.Item70, 0.4f);
			battle.Spawn(b);
		}
	}

	/// <summary>Boulders rolling into the player start a battle with them.</summary>
	public class BoulderBattles : GlobalProjectile
	{
		public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) => BoulderEncounter.Types.Contains(entity.type);

		public override bool CanHitPlayer(Projectile projectile, Player target)
		{
			if (target.whoAmI != Main.myPlayer || !projectile.hostile)
				return true;
			if (BattleSystem.Active)
				return false;
			return !BattleSystem.TryStartBoulder(projectile, target);
		}
	}
}
