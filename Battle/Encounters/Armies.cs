using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	public enum ArmyKind { None, Goblins, Pirates, FrostLegion, Martians, PumpkinMoon, FrostMoon, OldOnesArmy, Eclipse }

	/// <summary>Which event army an enemy marches with.</summary>
	public static class Armies
	{
		private static readonly Dictionary<int, ArmyKind> Members = new();

		private static void Add(ArmyKind kind, params int[] types)
		{
			foreach (int t in types)
				Members[t] = kind;
		}

		static Armies()
		{
			Add(ArmyKind.Goblins, NPCID.GoblinPeon, NPCID.GoblinThief, NPCID.GoblinWarrior, NPCID.GoblinSorcerer,
				NPCID.GoblinArcher, NPCID.GoblinSummoner, NPCID.ShadowFlameApparition);
			Add(ArmyKind.Pirates, NPCID.PirateDeckhand, NPCID.PirateCorsair, NPCID.PirateDeadeye, NPCID.PirateCrossbower,
				NPCID.PirateCaptain, NPCID.Parrot);
			Add(ArmyKind.FrostLegion, NPCID.MisterStabby, NPCID.SnowmanGangsta, NPCID.SnowBalla);
			Add(ArmyKind.Martians, NPCID.BrainScrambler, NPCID.RayGunner, NPCID.MartianOfficer, NPCID.GrayGrunt,
				NPCID.MartianEngineer, NPCID.GigaZapper, NPCID.ScutlixRider, NPCID.Scutlix, NPCID.MartianDrone, NPCID.MartianWalker);
			Add(ArmyKind.PumpkinMoon, NPCID.Scarecrow1, NPCID.Scarecrow2, NPCID.Scarecrow3, NPCID.Scarecrow4, NPCID.Scarecrow5,
				NPCID.Scarecrow6, NPCID.Scarecrow7, NPCID.Scarecrow8, NPCID.Scarecrow9, NPCID.Scarecrow10, NPCID.Splinterling,
				NPCID.Hellhound, NPCID.Poltergeist, NPCID.HeadlessHorseman);
			Add(ArmyKind.FrostMoon, NPCID.ZombieElf, NPCID.ZombieElfBeard, NPCID.ZombieElfGirl, NPCID.GingerbreadMan,
				NPCID.ElfArcher, NPCID.Nutcracker, NPCID.NutcrackerSpinning, NPCID.Yeti, NPCID.Krampus, NPCID.Flocko,
				NPCID.PresentMimic, NPCID.ElfCopter);
			Add(ArmyKind.OldOnesArmy, NPCID.DD2GoblinT1, NPCID.DD2GoblinT2, NPCID.DD2GoblinT3, NPCID.DD2GoblinBomberT1,
				NPCID.DD2GoblinBomberT2, NPCID.DD2GoblinBomberT3, NPCID.DD2JavelinstT1, NPCID.DD2JavelinstT2, NPCID.DD2JavelinstT3,
				NPCID.DD2WyvernT1, NPCID.DD2WyvernT2, NPCID.DD2WyvernT3, NPCID.DD2KoboldWalkerT2, NPCID.DD2KoboldWalkerT3,
				NPCID.DD2KoboldFlyerT2, NPCID.DD2KoboldFlyerT3, NPCID.DD2SkeletonT1, NPCID.DD2SkeletonT3, NPCID.DD2DrakinT2,
				NPCID.DD2DrakinT3, NPCID.DD2WitherBeastT2, NPCID.DD2WitherBeastT3, NPCID.DD2LightningBugT3);
			Add(ArmyKind.Eclipse, NPCID.Eyezor, NPCID.Frankenstein, NPCID.SwampThing, NPCID.Vampire, NPCID.VampireBat,
				NPCID.CreatureFromTheDeep, NPCID.Fritz, NPCID.ThePossessed, NPCID.Reaper, NPCID.Butcher, NPCID.DeadlySphere,
				NPCID.DrManFly, NPCID.Nailhead, NPCID.Psycho);
		}

		public static ArmyKind ArmyOf(NPC npc) => npc != null && Members.TryGetValue(npc.type, out ArmyKind k) ? k : ArmyKind.None;

		/// <summary>NPCs that belong to an event but must never start a battle (the crystal you defend, portals).</summary>
		public static bool NeverBattle(int type) => type is NPCID.DD2EterniaCrystal or NPCID.DD2LanePortal;
	}

	/// <summary>
	/// A soldier of an event army. Squads fight together (BattleSystem gathers them): sparing one makes the rest
	/// lose heart (MERCY up), defeating one makes the rest furious (they attack as if Hard).
	/// </summary>
	public class ArmyEnemy : EnemyEncounter
	{
		/// <summary>MERCY the rest of the squad gains when one of them is spared.</summary>
		public const float MoraleOnSpare = 30f;

		public ArmyKind Kind;

		public ArmyEnemy(ArmyKind kind) => Kind = kind;

		private string Army => Kind switch
		{
			ArmyKind.Goblins => "GOBLIN ARMY",
			ArmyKind.Pirates => "PIRATE CREW",
			ArmyKind.FrostLegion => "FROST LEGION",
			ArmyKind.Martians => "MARTIAN INVASION",
			ArmyKind.PumpkinMoon => "PUMPKIN MOON",
			ArmyKind.FrostMoon => "FROST MOON",
			ArmyKind.OldOnesArmy => "OLD ONE'S ARMY",
			_ => "ECLIPSE",
		};

		public override string EncounterText => $"* A soldier of the {Army} blocks the way!";
		public override string GroupEncounterText(int others) => $"* A squad of the {Army} surrounds you!";
		public string SquadSparedLine => Kind switch
		{
			ArmyKind.Goblins => "* The other goblins look at each other. Maybe the war isn't worth it.",
			ArmyKind.Pirates => "* The rest of the crew mutters about mutiny.",
			ArmyKind.FrostLegion => "* The other snowmen start to melt a little. Emotionally.",
			ArmyKind.Martians => "* The other martians file a report: 'Earthlings are... nice?'",
			ArmyKind.PumpkinMoon or ArmyKind.FrostMoon => "* The others hesitate. The night feels a little softer.",
			ArmyKind.OldOnesArmy => "* The rest of the army wavers. The Old One isn't watching.",
			_ => "* The others hesitate.",
		};

		protected override string CheckText => Kind switch
		{
			ArmyKind.Goblins => "* Part of the Goblin Army. Just following orders. Bad orders.",
			ArmyKind.Pirates => "* A pirate. Here for your gold. Mostly the gold.",
			ArmyKind.FrostLegion => "* A snowman with a grudge. Built for violence.",
			ArmyKind.Martians => "* From another world. Studying you. With lasers.",
			ArmyKind.PumpkinMoon => "* A creature of the Pumpkin Moon. Wants your candy.",
			ArmyKind.FrostMoon => "* A creature of the Frost Moon. Naughty list, permanently.",
			ArmyKind.OldOnesArmy => "* Etherian soldier. After the crystal. Not personally after you.",
			_ => "* A monster of the eclipse. It's only out when the sun isn't.",
		};

		protected override string[] Lines => Kind switch
		{
			ArmyKind.Goblins => new[] { "* {0} sharpens its blade.", "* War drums echo in the distance.", "* {0} checks its orders. They say 'attack'." },
			ArmyKind.Pirates => new[] { "* {0} says 'arr'.", "* Smells like salt and gunpowder.", "* {0} is counting your coins with its eyes." },
			ArmyKind.FrostLegion => new[] { "* {0} glares coldly.", "* Snow crunches underfoot.", "* {0} adjusts its tiny hat." },
			ArmyKind.Martians => new[] { "* {0} beeps something.", "* A tractor beam hums overhead.", "* {0} is taking notes." },
			ArmyKind.PumpkinMoon => new[] { "* {0} rattles in the wind.", "* The moon glows orange.", "* Smells like pie and fear." },
			ArmyKind.FrostMoon => new[] { "* {0} jingles menacingly.", "* Somewhere, a bell rings.", "* Smells like cocoa and malice." },
			ArmyKind.OldOnesArmy => new[] { "* {0} marches toward the crystal.", "* The portal crackles.", "* {0} waits for the next wave." },
			_ => new[] { "* {0} lurches out of the dark.", "* The sun is a black ring.", "* {0} doesn't like the light." },
		};

		protected override (string, string, string)[] ActList => Kind switch
		{
			ArmyKind.Goblins => new[] {
				("Treaty", "Offer a\ntreaty", "* You offer {0} a peace treaty. It reads it upside down, but nods."),
				("Tinker", "Mention the\nTinkerer", "* You mention the Goblin Tinkerer is doing well. {0} looks homesick."),
				("Retreat", "Sound the\nretreat", "* You yell 'RETREAT!' {0} almost obeys."),
			},
			ArmyKind.Pirates => new[] {
				("Parley", "Call for\nparley", "* You call for parley. {0} respects the code."),
				("Coin", "Toss a\ncoin", "* You toss {0} a coin. It bites it. Satisfied."),
				("Shanty", "Sing a\nshanty", "* You sing a sea shanty. {0} joins in on the chorus."),
			},
			ArmyKind.FrostLegion => new[] {
				("Hat", "Fix its\nhat", "* You straighten {0}'s hat. It's touched."),
				("Carrot", "Offer a\ncarrot", "* You offer {0} a new carrot nose. It accepts, emotional."),
				("Cold", "Say it's\nchilly", "* You say it's chilly. {0} agrees. Finally, someone gets it."),
			},
			ArmyKind.Martians => new[] {
				("Greet", "Take me to\nyour leader", "* You say 'take me to your leader.' {0} is impressed you know the line."),
				("Probe", "Decline\nthe probe", "* You politely decline the probe. {0} notes it down."),
				("Wave", "Wave\nhello", "* You wave. {0} waves back with three hands."),
			},
			ArmyKind.PumpkinMoon => new[] {
				("Candy", "Offer\ncandy", "* You offer {0} some candy. It's appeased. For now."),
				("Carve", "Praise the\ncarving", "* You praise {0}'s carving. Very spooky. It's flattered."),
				("Boo", "Say\n'boo'", "* You say 'boo.' {0} jumps. Then laughs."),
			},
			ArmyKind.FrostMoon => new[] {
				("Gift", "Give a\ngift", "* You give {0} a present. It's never gotten one before."),
				("Carol", "Sing a\ncarol", "* You sing a carol. {0} hums along despite itself."),
				("Nice", "Say it's\non the nice list", "* You tell {0} it's on the nice list. It doesn't believe you. But it wants to."),
			},
			ArmyKind.OldOnesArmy => new[] {
				("Crystal", "Shield the\ncrystal", "* You stand between {0} and the crystal. It hesitates."),
				("Home", "Ask about\nEtheria", "* You ask about Etheria. {0} gets a faraway look."),
				("Tavern", "Mention the\nTavernkeep", "* You mention the Tavernkeep. {0} owes him money."),
			},
			_ => new[] {
				("Light", "Hold up\na light", "* You hold up a torch. {0} squints and backs off."),
				("Wait", "Wait for\nthe sun", "* You remind {0} the eclipse won't last. It looks worried."),
				("Scream", "Scream\nback", "* You scream back. {0} is startled. Good."),
			},
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			int t = TurnTicks;
			Bullet self(Vector2 p, Vector2 v) => Self(p, v, 26f, 1f);
			Bullet small(Vector2 p, Vector2 v) => Self(p, v, 18f, 0.8f);
			Bullet ball(Vector2 p, Vector2 v, Color c) => Shots.Ball(p, v, c, 0.7f);
			// Each soldier opens the cycle somewhere different, so a squad doesn't repeat itself
			int offset = Npc.type % 3;
			EnemyAttack Pick(params Func<EnemyAttack>[] attacks) => attacks[(Turn + offset) % attacks.Length]();

			switch (Kind)
			{
				case ArmyKind.Goblins:
				{
					Bullet arrow(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.WoodenArrowHostile, p, v, 1f, 0.8f, new Vector2(8, 8), rotationOffset: MathHelper.PiOver2);
					Bullet chaos(Vector2 p, Vector2 v) => Shots.Ball(p, v, new Color(190, 90, 255), 0.8f, 1.4f);
					if (Npc.type == NPCID.GoblinArcher)
						return new SideShots(arrow, Hard ? 12 : 18) { Side = 0, Speed = 3.6f }.Lasting(t);
					if (Npc.type == NPCID.GoblinSorcerer)
						return new Homing(chaos, Hard ? 26 : 36) { Speed = 1.7f }.Lasting(t);
					return Pick(
						() => new LaneDash(self, Hard ? 44 : 58) { Speed = 7.5f, LaunchSound = SoundID.Item1 }.Lasting(t),
						() => new Converge((p, v) => ball(p, v, new Color(160, 160, 160)), Hard ? 60 : 78) { Count = 6, Speed = 3.6f }.Lasting(t),
						() => new Walls(small, Hard ? 60 : 75) { Side = 0, Speed = 2f, Spacing = 22f, GapSize = 46f }.Lasting(t));
				}
				case ArmyKind.Pirates:
				{
					Bullet cannonball(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.CannonballHostile, p, v, 1f, 1f, new Vector2(14, 14), rotate: false);
					Bullet shot(Vector2 p, Vector2 v) => ball(p, v, new Color(255, 220, 140));
					return Pick(
						() => new Bouncers(cannonball, Hard ? 28 : 36).Lasting(t),
						() => new SideShots(shot, Hard ? 10 : 15) { Side = 0, Speed = 4f }.Lasting(t),
						() => new LaneDash(self, Hard ? 44 : 58) { AllowVertical = true, Speed = 8f }.Lasting(t),
						() => new Swoopers((p, v) => Shots.Npc(NPCID.Parrot, p, v, 0.9f, 0.7f, new Vector2(12, 12), rotate: false).FaceTravel(), Hard ? 18 : 26).Lasting(t));
				}
				case ArmyKind.FrostLegion:
				{
					Bullet snowball(Vector2 p, Vector2 v) => Shots.Proj(ProjectileID.SnowBallHostile, p, v, 1f, 0.7f, new Vector2(10, 10), rotate: false);
					return Pick(
						() => new Rain(snowball, Hard ? 9 : 13) { Wobble = 0.3f }.Lasting(t),
						() => new AimedBursts(snowball, Hard ? 28 : 40) { Count = 3, Speed = 2.6f }.Lasting(t),
						() => new LaneDash(self, Hard ? 44 : 58) { Speed = 8f }.Lasting(t));
				}
				case ArmyKind.Martians:
				{
					var green = new Color(120, 255, 140);
					return Pick(
						() => new Beam(Hard ? 44 : 58) { Width = 9f, Warn = 34, Active = 12, Color = green, FireSound = SoundID.Item12 }.Lasting(t),
						() => new AimedBursts((p, v) => ball(p, v, green), Hard ? 26 : 38) { Count = 3, Speed = 3f }.Lasting(t),
						() => new Homing((p, v) => ball(p, v, new Color(200, 120, 255)), Hard ? 30 : 42) { Speed = 1.8f }.Lasting(t),
						() => new Slam(self, (p, v) => ball(p, v, green), Hard ? 60 : 76) { Width = 40f, Shards = 2 }.Lasting(t));
				}
				case ArmyKind.PumpkinMoon:
				{
					var orange = new Color(255, 150, 40);
					return Pick(
						() => new Rain((p, v) => ball(p, v, orange), Hard ? 8 : 12) { Wobble = 0.4f }.Lasting(t),
						() => new LaneDash(self, Hard ? 40 : 54) { AllowVertical = true, Speed = 8.5f }.Lasting(t),
						() => new Homing(small, Hard ? 26 : 36) { Speed = 1.8f }.Lasting(t),
						() => new GapRows((p, v) => ball(p, v, orange), Hard ? 30 : 40) { Speed = 1.7f, GapSize = 48f }.Lasting(t));
				}
				case ArmyKind.FrostMoon:
				{
					var ice = new Color(170, 230, 255);
					return Pick(
						() => new Sprinkler((p, v) => ball(p, v, ice)) { Every = Hard ? 6 : 9, Speed = 2.4f, TurnSpeed = 0.06f }.Lasting(t),
						() => new SideShots((p, v) => ball(p, v, new Color(255, 80, 80)), Hard ? 11 : 16) { Side = 0, Speed = 3.6f }.Lasting(t),
						() => new Slam(self, (p, v) => ball(p, v, ice), Hard ? 58 : 74) { Width = 40f, Shards = 2 }.Lasting(t),
						() => new Bouncers(small, Hard ? 26 : 34).Lasting(t));
				}
				case ArmyKind.OldOnesArmy:
				{
					var ether = new Color(255, 120, 220);
					return Pick(
						() => new Fireworks((p, v) => Shots.Ball(p, v, Color.White, 0.9f, 1.6f), (p, v) => ball(p, v, ether), Hard ? 44 : 58) { Count = 7 }.Lasting(t),
						() => new SideShots((p, v) => ball(p, v, new Color(255, 200, 120)), Hard ? 11 : 16) { Side = 0, Speed = 4f }.Lasting(t),
						() => new LaneDash(self, Hard ? 42 : 56) { Speed = 8f }.Lasting(t),
						() => new Beam(Hard ? 46 : 60) { Width = 10f, Color = new Color(255, 140, 60), FireSound = SoundID.Item34 }.Lasting(t));
				}
				default:
				{
					var blood = new Color(220, 40, 60);
					return Pick(
						() => new LaneDash(self, Hard ? 40 : 54) { AllowVertical = true, Speed = 8.5f, LaunchSound = SoundID.Roar }.Lasting(t),
						() => new Converge((p, v) => ball(p, v, blood), Hard ? 56 : 72) { Count = 8, Speed = 4f }.Lasting(t),
						() => new Swoopers(small, Hard ? 18 : 26) { Speed = 2.6f }.Lasting(t),
						() => new Slam(self, (p, v) => ball(p, v, blood), Hard ? 58 : 74) { Width = 40f, Shards = 2 }.Lasting(t));
				}
			}
		}
	}
}
