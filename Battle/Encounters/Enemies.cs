using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>
	/// Regular enemies are grouped by how they behave (their AI style), so one family covers every slime, every
	/// zombie-like walker and so on, including modded enemies that reuse vanilla AI.
	/// </summary>
	public static class EnemyFamilies
	{
		public static Encounter For(NPC npc) => npc.aiStyle switch
		{
			NPCAIStyleID.Slime => new SlimeEnemy(),
			NPCAIStyleID.Fighter => new FighterEnemy(),
			NPCAIStyleID.DemonEye or NPCAIStyleID.Bat or NPCAIStyleID.Flying or NPCAIStyleID.HoveringFighter
				or NPCAIStyleID.Vulture or NPCAIStyleID.FlyingFish => new FlierEnemy(),
			NPCAIStyleID.Caster => new CasterEnemy(),
			NPCAIStyleID.Worm => new WormEnemy(),
			_ => new GenericEnemy(),
		};
	}

	/// <summary>Shared rules for regular enemies: shorter turns, quicker MERCY, simple text.</summary>
	public abstract class EnemyEncounter : Encounter
	{
		/// <summary>Most chapter 1 enemy turns are 120 frames.</summary>
		protected const int TurnTicks = 120 * BattleConstants.TicksPerFrame;

		public override bool IsBoss => false;

		/// <summary>Stronger enemies (and anything in Hardmode) attack harder.</summary>
		protected bool Hard => Npc.damage >= 40 || Main.hardMode;

		protected abstract string[] Lines { get; }
		protected abstract (string name, string desc, string line)[] ActList { get; }
		protected abstract string CheckText { get; }

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return $"* {Name} seems ready to leave.";
			if (LifeRatio < 0.35f)
				return $"* {Name} looks hurt.";
			return string.Format(Lines[Turn % Lines.Length], Name);
		}

		public override List<ActOption> Acts(BattleSystem battle)
		{
			var acts = new List<ActOption> { CheckAct(CheckText) };
			float[] amounts = { 60f, 50f, 40f };
			for (int i = 0; i < ActList.Length; i++)
				acts.Add(MercyAct(ActList[i].name, ActList[i].desc, amounts[Math.Min(i, 2)], string.Format(ActList[i].line, Name)));
			acts.Add(HealPrayerAct());
			return acts;
		}

		/// <summary>The enemy's own sprite as a bullet, about <paramref name="size"/> px across.</summary>
		protected Bullet Self(Vector2 p, Vector2 v, float size = 26f, float damage = 1f)
		{
			int type = Npc.type;
			Main.instance.LoadNPC(type);
			var tex = TextureAssets.Npc[type].Value;
			float frameH = tex.Height / (float)Math.Max(1, Main.npcFrameCount[type]);
			float scale = MathHelper.Clamp(size / Math.Max(frameH, tex.Width), 0.2f, 1.5f);
			Bullet b = Shots.Npc(type, p, v, scale, damage, new Vector2(size * 0.6f), rotate: false).FaceTravel();
			Color c = DrawColor(Npc);
			b.Color = c;
			return b;
		}
	}

	public class SlimeEnemy : EnemyEncounter
	{
		protected override string CheckText => "* Bouncy. Sticky. Surprisingly polite.";
		protected override string[] Lines => new[] {
			"* {0} jiggles expectantly.",
			"* {0} is trying to look intimidating. It's a slime.",
			"* Smells like gel.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Jiggle", "Jiggle\nat it", "* You jiggle at {0}. It jiggles back, delighted."),
			("Pet", "Pet\nit", "* You pet {0}. Your hand is sticky now. It seems happy."),
			("Compliment", "Admire\nits gel", "* You admire {0}'s gel. It glistens proudly."),
		};

		private Color Gel => new(DrawColor(Npc).R, DrawColor(Npc).G, DrawColor(Npc).B);

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new Bouncers((p, v) => Shots.Ball(p, v, Gel, 0.7f, 1.2f), Hard ? 20 : 28).Lasting(TurnTicks),
			// It hops up and lands on you, splashing gel along the floor
			() => new Slam((p, d) => Self(p, d, 34f, 1f), (p, v) => Shots.Ball(p, v, Gel, 0.6f), Hard ? 60 : 75)
				{ Width = 40f, Shards = 1, Debris = 2 }.Lasting(TurnTicks),
			() => new ClosingRing((p, v) => Shots.Ball(p, v, Gel, 0.6f), Hard ? 55 : 75) { Count = 12, Speed = 1f }.Lasting(TurnTicks));
	}

	public class FighterEnemy : EnemyEncounter
	{
		protected override string CheckText => "* It just keeps walking toward you. Always has.";
		protected override string[] Lines => new[] {
			"* {0} shuffles closer.",
			"* {0} groans something you can't make out.",
			"* {0} is blocking the way.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Shuffle", "Shuffle\nalong", "* You shuffle in place. {0} shuffles too. It's almost a dance."),
			("Groan", "Groan\nback", "* You groan at {0}. It feels understood."),
			("Bandage", "Offer a\nbandage", "* You offer {0} a bandage. It doesn't know what to do with it, but keeps it."),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new Walls((p, v) => Self(p, v, 18f, 0.8f), Hard ? 55 : 75) { Spacing = 22f, GapSize = 44f, Speed = Hard ? 2f : 1.5f }.Lasting(TurnTicks),
			() => new LaneDash((p, d) => Self(p, d, 34f, 1f), Hard ? 45 : 60) { Speed = 6.5f }.Lasting(TurnTicks),
			() => new SideShots((p, v) => Shots.Ball(p, v, Color.LightGray, 0.7f), Hard ? 12 : 18) { Side = 0, Speed = 3f }.Lasting(TurnTicks));
	}

	public class FlierEnemy : EnemyEncounter
	{
		protected override string CheckText => "* Flaps around a lot. Has never once landed.";
		protected override string[] Lines => new[] {
			"* {0} circles overhead.",
			"* {0} flutters impatiently.",
			"* You hear wings everywhere.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Wave", "Wave\nhello", "* You wave at {0}. It flutters closer, curious."),
			("Still", "Stand\nvery still", "* You stand perfectly still. {0} stops circling."),
			("Sky", "Point at\nthe sky", "* You point at the sky. {0} looks up, then back at you."),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new Swoopers((p, v) => Self(p, v, 24f, 0.8f), Hard ? 18 : 26) { Speed = Hard ? 2.6f : 2.1f }.Lasting(TurnTicks),
			() => new AimedBursts((p, v) => Shots.Ball(p, v, Shots.Red, 0.7f), Hard ? 30 : 42) { Count = Hard ? 4 : 3 }.Lasting(TurnTicks),
			() => new Converge((p, v) => Self(p, v, 18f, 0.7f), Hard ? 70 : 90) { Count = Hard ? 6 : 5, Speed = 3.6f, Radius = 70f }.Lasting(TurnTicks));
	}

	public class CasterEnemy : EnemyEncounter
	{
		protected override string CheckText => "* Knows some magic. Very proud of it.";
		protected override string[] Lines => new[] {
			"* {0} mutters an incantation.",
			"* {0} teleports a few feet to the left. Then back.",
			"* The air crackles with magic.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Applaud", "Applaud\nthe magic", "* You applaud {0}'s last spell. It takes a bow."),
			("Ask", "Ask how\nit works", "* You ask {0} how the magic works. It starts a very long explanation."),
			("Duck", "Duck\ndramatically", "* You duck dramatically. {0} appreciates the theatrics."),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new Homing((p, v) => Shots.Ball(p, v, Shots.Purple, 0.8f), Hard ? 22 : 32) { Speed = 1.7f }.Lasting(TurnTicks),
			() => new ClosingRing((p, v) => Shots.Ball(p, v, Shots.Purple, 0.7f), Hard ? 50 : 70) { Count = 14 }.Lasting(TurnTicks),
			// Magic orbs gather around you, then strike
			() => new Converge((p, v) => Shots.Ball(p, v, Shots.Purple, 0.7f), Hard ? 60 : 80) { Count = Hard ? 8 : 6, Speed = 3.6f }.Lasting(TurnTicks),
			() => new Beam(Hard ? 70 : 90) { Width = 12f, Color = Shots.Purple, FireSound = SoundID.Item8 }.Lasting(TurnTicks));
	}

	public class WormEnemy : EnemyEncounter
	{
		protected override string CheckText => "* Lives underground. Eats dirt. Living the dream.";
		protected override string[] Lines => new[] {
			"* {0} wriggles menacingly.",
			"* The ground rumbles.",
			"* {0} coils up, then uncoils.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Stomp", "Stomp the\nground", "* You stomp the ground. {0} rumbles back in greeting."),
			("Tunnel", "Admire its\ntunnels", "* You compliment {0}'s tunnels. They are very good tunnels."),
			("Dirt", "Offer\nsome dirt", "* You offer {0} some dirt. Apparently it's delicious."),
		};

		public override float DrawRotation(int time) => -BossKit.WormRotation;

		private Bullet Head(Vector2 p, Vector2 v)
		{
			Main.instance.LoadNPC(Npc.type);
			var tex = TextureAssets.Npc[Npc.type].Value;
			float scale = MathHelper.Clamp(22f / Math.Max(tex.Width, tex.Height / (float)Math.Max(1, Main.npcFrameCount[Npc.type])), 0.2f, 1.5f);
			return Shots.Npc(Npc.type, p, v, scale, 1f, new Vector2(14, 14), rotationOffset: BossKit.WormRotation);
		}

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new Snake(Head, (p, v) => Shots.Ball(p, v, Shots.Brown, 0.8f, 1.2f), Hard ? 70 : 95) { Segments = 7 }.Lasting(TurnTicks),
			() => new Rain((p, v) => Shots.Ball(p, v, Shots.Brown, 0.6f), Hard ? 10 : 14) { Wobble = 0.2f }.Lasting(TurnTicks),
			// Dirt falls from the ceiling in rows; follow the gap
			() => new GapRows((p, v) => Shots.Ball(p, v, Shots.Brown, 0.6f), Hard ? 34 : 42) { Speed = 1.5f, GapSize = 50f }.Lasting(TurnTicks));
	}

	public class GenericEnemy : EnemyEncounter
	{
		protected override string CheckText => "* It doesn't look friendly. But it doesn't look evil either.";
		protected override string[] Lines => new[] {
			"* {0} is waiting for you to make a move.",
			"* {0} looks at you suspiciously.",
			"* {0} blocks the way.",
		};
		protected override (string, string, string)[] ActList => new[] {
			("Talk", "Try\ntalking", "* You try to talk to {0}. It listens. Kind of."),
			("Compliment", "Say\nsomething nice", "* You compliment {0}. It doesn't know how to react."),
			("Joke", "Tell a\njoke", "* You tell {0} a joke. It almost laughs."),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => Cycle(
			() => new AimedBursts((p, v) => Shots.Ball(p, v, Shots.Red, 0.7f), Hard ? 32 : 45) { Count = 3 }.Lasting(TurnTicks),
			() => new Rain((p, v) => Shots.Ball(p, v, Color.White, 0.6f), Hard ? 9 : 13).Lasting(TurnTicks),
			() => new SideShots((p, v) => Shots.Ball(p, v, Shots.Red, 0.7f), Hard ? 12 : 18) { Side = 0 }.Lasting(TurnTicks),
			() => new Fireworks((p, v) => Shots.Ball(p, v, Color.White, 0.8f, 1.6f), (p, v) => Shots.Ball(p, v, Shots.Red, 0.6f), Hard ? 50 : 65)
				{ Count = Hard ? 8 : 6 }.Lasting(TurnTicks));
	}
}
