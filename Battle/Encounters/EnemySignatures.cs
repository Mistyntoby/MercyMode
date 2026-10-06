using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>
	/// Enemies with their own attack and lines, on top of their family's: a zombie's hands reach up from the floor, a
	/// harpy rains feathers, a skeleton throws bones at you Undertale-style. The signature attack comes every other turn.
	/// </summary>
	public abstract partial class EnemyEncounter
	{
		/// <summary>The NPC's internal name ("Zombie", "CaveBat", a modded one's own), for matching variants together.</summary>
		private string Id => Npc.ModNPC?.Name ?? NPCID.Search.GetName(Npc.type) ?? "";

		private bool Is(params string[] names)
		{
			string id = Id;
			foreach (string n in names)
				if (id.Contains(n, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		private bool IdIs(params string[] names) => Array.IndexOf(names, Id) >= 0;

		private enum Sig
		{
			None, Zombie, DemonEye, Bat, Hellbat, Skeleton, AngryBones, Harpy, Hornet, Eater, Crimson, Antlion, Snow,
			FireImp, LavaSlime, Sorcerer, DarkCaster, Piranha, Jellyfish, Shark, Armor, Wraith, Pixie, Unicorn, Mummy,
			Werewolf, CursedSkull, Meteor, Granite, BoneSerpent, Tim, Ghost, Demon, Crab, Vulture, Umbrella,
		}

		private Sig Signature()
		{
			if (Is("Skeletron"))
				return Sig.None;
			if (Is("Zombie")) return Sig.Zombie;
			if (Is("DemonEye", "WanderingEye")) return Sig.DemonEye;
			if (Is("Hellbat", "Lavabat")) return Sig.Hellbat;
			if (Is("Bat")) return Sig.Bat;
			if (Is("AngryBones")) return Sig.AngryBones;
			if (Is("Skeleton", "UndeadMiner", "UndeadViking")) return Sig.Skeleton;
			if (Is("Harpy")) return Sig.Harpy;
			if (Is("Hornet") || IdIs("Bee", "BeeSmall")) return Sig.Hornet;
			if (Is("EaterofSouls", "Corruptor", "DevourerHead")) return Sig.Eater;
			if (Is("Crimera", "FaceMonster", "BloodCrawler", "Herpling")) return Sig.Crimson;
			if (Is("Antlion")) return Sig.Antlion;
			if (Is("Flinx", "IceSlime", "IceBat", "IceElemental", "IceTortoise", "IcyMerman", "IceMimic", "IceGolem", "ArmoredViking")) return Sig.Snow;
			if (Is("FireImp")) return Sig.FireImp;
			if (Is("LavaSlime")) return Sig.LavaSlime;
			if (Is("GoblinSorcerer")) return Sig.Sorcerer;
			if (Is("DarkCaster")) return Sig.DarkCaster;
			if (Is("Piranha")) return Sig.Piranha;
			if (Is("Jellyfish")) return Sig.Jellyfish;
			if (Is("Shark")) return Sig.Shark;
			if (Is("PossessedArmor")) return Sig.Armor;
			if (Is("Wraith")) return Sig.Wraith;
			if (Is("Pixie")) return Sig.Pixie;
			if (Is("Unicorn")) return Sig.Unicorn;
			if (Is("Mummy")) return Sig.Mummy;
			if (Is("Werewolf")) return Sig.Werewolf;
			if (Is("CursedSkull")) return Sig.CursedSkull;
			if (Is("MeteorHead")) return Sig.Meteor;
			if (Is("Granite")) return Sig.Granite;
			if (Is("BoneSerpent")) return Sig.BoneSerpent;
			if (IdIs("Tim", "RuneWizard")) return Sig.Tim;
			if (Is("Ghost", "Poltergeist")) return Sig.Ghost;
			if (Is("Demon") && !Is("DemonEye")) return Sig.Demon;
			if (Is("Crab")) return Sig.Crab;
			if (Is("Vulture")) return Sig.Vulture;
			if (IdIs("UmbrellaSlime")) return Sig.Umbrella;
			return Sig.None;
		}

		/// <summary>Its voice as it talks: zombies and mummies moan; the rest make their own hit sound.</summary>
		public override Terraria.Audio.SoundStyle? Voice => Signature() switch
		{
			Sig.Zombie or Sig.Mummy => SoundID.ZombieMoan,
			Sig.Werewolf => SoundID.NPCHit6,
			_ => Npc?.HitSound,
		};

		/// <summary>Its own lines, if it has a signature (null: the family's).</summary>
		protected string[] SignatureBubbles() => Signature() switch
		{
			Sig.Zombie => new[] { "[shake]Braaains...[/shake]", "[shake]Hhhrrrgh.[/shake]", "Can... I borrow... a cup of brains?", "*shuffles*" },
			Sig.DemonEye => new[] { "...", "*stares*", "I see you.", "*blinks once*" },
			Sig.Bat or Sig.Hellbat => new[] { "Skree!", "*flap flap flap*", "[tremble]SKREEEE![/tremble]" },
			Sig.Skeleton => new[] { "Rattle rattle.", "I've got a bone to pick with you.", "Nyeh." },
			Sig.AngryBones => new[] { "[tremble]GRRR![/tremble]", "Get out of my dungeon!", "*angry rattling*" },
			Sig.Harpy => new[] { "[wave]Ah-ha-ha![/wave]", "Feathers for you!", "Up here, groundling!" },
			Sig.Hornet => new[] { "Bzzz.", "[tremble]BZZZZ![/tremble]", "*angry buzzing*" },
			Sig.Eater => new[] { "[shake]Hungry...[/shake]", "*chitters*", "You smell... edible." },
			Sig.Crimson => new[] { "Squelch.", "[shake]Meat... meat...[/shake]", "*wet noises*" },
			Sig.Antlion => new[] { "*clicks mandibles*", "The sand is MINE.", "Click click." },
			Sig.Snow => new[] { "[tremble]Brrr![/tremble]", "Snowball fight!", "[tremble]*shivers adorably*[/tremble]" },
			Sig.FireImp => new[] { "[shake]Burn![/shake]", "[wave]Hehehe.[/wave]", "Feel the heat!" },
			Sig.LavaSlime => new[] { "Blub. (hot)", "Sizzle.", "Don't touch. I'm hot." },
			Sig.Sorcerer => new[] { "[tremble]Chaos![/tremble]", "Watch this trick!", "Abra... kadabra!" },
			Sig.DarkCaster => new[] { "[wave]Water... magic...[/wave]", "Drown.", "[wave]The deep calls.[/wave]" },
			Sig.Piranha => new[] { "Chomp chomp chomp.", "*blood in the water*", "[tremble]CHOMP.[/tremble]" },
			Sig.Jellyfish => new[] { "[tremble]Bzzt.[/tremble]", "*pulses*", "[shake]Zap?[/shake]" },
			Sig.Shark => new[] { "...", "*circles*", "Dun dun." },
			Sig.Armor => new[] { "Clank.", "No one's in here.", "*hollow clanking*" },
			Sig.Wraith => new[] { "[wave]Ooooh...[/wave]", "[wave]Join us...[/wave]", "[tremble]Coooold...[/tremble]" },
			Sig.Pixie => new[] { "Hey! Listen!", "[wave]Teehee![/wave]", "[wave]Sparkle sparkle![/wave]" },
			Sig.Unicorn => new[] { "Neigh!", "Behold my horn!", "*majestic galloping*" },
			Sig.Mummy => new[] { "Mmmph.", "Mmmmh mmh!", "*unravels a little*" },
			Sig.Werewolf => new[] { "[tremble]AWOOO![/tremble]", "[shake]Grrr...[/shake]", "The moon is full!" },
			Sig.CursedSkull => new[] { "[wave]Hehehe...[/wave]", "Cursed!", "[shake]*cackles*[/shake]" },
			Sig.Meteor => new[] { "[tremble]FWOOSH.[/tremble]", "Incoming!", "*burns*" },
			Sig.Granite => new[] { "...", "[shake]*grinding*[/shake]", "Rock solid." },
			Sig.BoneSerpent => new[] { "[wave]Hssss...[/wave]", "*rattles through the lava*" },
			Sig.Tim => new[] { "There are some who call me...", "Behold my magic!", "Hah!" },
			Sig.Ghost => new[] { "[wave]Boo.[/wave]", "[wave]Boooo![/wave]", "[wave]*floats ominously*[/wave]" },
			Sig.Demon => new[] { "Kneel.", "[shake]Your soul is mine.[/shake]", "Pathetic." },
			Sig.Crab => new[] { "Snip snip.", "*sideways*", "Pinch!" },
			Sig.Vulture => new[] { "*circles*", "Waiting...", "Not dead yet?" },
			Sig.Umbrella => new[] { "[wave]Pitter patter~[/wave]", "Forecast: you, soaked.", "Stay under me!", "[wave]Drip drip drip.[/wave]" },
			_ => null,
		};

		private Bullet Ball(Vector2 p, Vector2 v, Color c, float dmg = 0.7f, float scale = 1f) => Shots.Ball(p, v, c, dmg, scale);
		private Bullet Proj(int type, Vector2 p, Vector2 v, float scale = 1f, float dmg = 0.8f, float hit = 10f, bool rotate = true, float offset = MathHelper.PiOver2, float spin = 0f)
			=> Shots.Proj(type, p, v, scale, dmg, new Vector2(hit), rotate, offset, spin);

		/// <summary>Its own attack (null: it doesn't have one, or not this turn).</summary>
		public EnemyAttack SignatureAttack(BattleSystem battle)
		{
			int t = TurnTicks;
			bool h = Hard;
			Color fire = new(255, 140, 40);
			EnemyAttack a = Signature() switch
			{
				// Hands reach up out of the ground where you stand
				Sig.Zombie => new FloorSpikes((p, d) => Self(p, d, 22f, 0.9f).Trailing(3), h ? 30 : 42) { Width = 24f, Speed = 6.5f, Warn = 32 },
				// Little eyes circle you while the big one stares a line through the box
				Sig.DemonEye => new Combo(t,
					new Orbiters((p, v) => Self(p, v, 14f, 0.7f), h ? 90 : 120) { Count = h ? 6 : 5, AngularSpeed = 0.03f },
					new Beam(h ? 60 : 80) { Width = 8f, Color = new Color(255, 70, 70), Warn = 40, Active = 16 }),
				// A colony: lots of small, fast bats in waves
				Sig.Bat => new Swoopers((p, v) => Self(p, v, 16f, 0.7f), h ? 9 : 13) { Speed = h ? 3.4f : 2.9f, Amplitude = 18f },
				// Swooping over rising lava
				Sig.Hellbat => new Combo(t,
					new LavaRise { Peak = h ? 0.35f : 0.28f, EmberEvery = h ? 24 : 32 },
					new Swoopers((p, v) => Self(p, v, 18f, 0.8f).Fiery(), h ? 15 : 20) { Speed = 3.1f, Amplitude = 24f }),
				// Bones: jump the short ones, stay under the long ones (blue SOUL)
				Sig.Skeleton => new BoneWalls(h ? 30 : 38) { Speed = h ? 3f : 2.6f },
				Sig.AngryBones => new BoneWalls(h ? 26 : 32) { Speed = 3.2f, Color = new Color(255, 200, 200) },
				// Feathers slicing down at an angle
				Sig.Harpy => new Rain((p, v) => Proj(ProjectileID.HarpyFeather, p, v + new Vector2(0.9f, 0), 0.9f, 0.8f, 8f), h ? 6 : 9)
					{ SpeedMin = 2.6f, SpeedMax = 3.4f, Wobble = 0.1f },
				// Stingers in quick volleys at you
				Sig.Hornet => new AimedBursts((p, v) => Proj(ProjectileID.Stinger, p, v, 1f, 0.8f, 8f), h ? 22 : 32) { Count = h ? 4 : 3, Speed = 3.6f },
				// Vile spit lobbed in arcs
				Sig.Eater => new Lobs((p, v) => Ball(p, v, new Color(140, 200, 60), 0.8f, 1.2f).Dripping(new Color(110, 160, 40)), h ? 16 : 22) { Volley = 2, Splash = 0 },
				// Blood drips from above
				Sig.Crimson => new Rain((p, v) => Ball(p, v, new Color(200, 20, 30), 0.7f).Dripping(new Color(160, 0, 20)), h ? 7 : 10) { Wobble = 0.05f, SpeedMin = 2f, SpeedMax = 3f },
				// Sand flung up from below while its jaws snap up out of the floor
				Sig.Antlion => new Combo(t,
					new Rain((p, v) => Ball(p, v, new Color(220, 190, 120), 0.6f), h ? 12 : 16) { FromBelow = true, Wobble = 0.3f },
					new FloorSpikes((p, d) => Self(p, d, 24f, 1f), h ? 50 : 70) { Width = 26f, Speed = 7f }),
				// Snowballs bouncing around the box
				// Frozen floor: the SOUL slides about while icicles drop and snowballs roll in
				Sig.Snow => new IceFloor { MakeSide = (p, v) => Proj(ProjectileID.SnowBallHostile, p, v, 1f, 0.7f, 10f, rotate: false, spin: 0.15f), IcicleEvery = h ? 16 : 22, FallSpeed = h ? 5.6f : 5f },
				// Fireballs that home in, trailing flame
				// Lava rises from the floor while it throws fireballs
				Sig.FireImp => new Combo(t,
					new LavaRise { Peak = h ? 0.5f : 0.42f, EmberEvery = h ? 14 : 20 },
					new Homing((p, v) => Ball(p, v, fire, 0.9f, 1.2f).Fiery(), h ? 40 : 54) { Speed = 1.7f }),
				// Molten blobs lobbed in, splashing on landing
				Sig.LavaSlime => new Combo(t,
					new LavaRise { Peak = h ? 0.45f : 0.36f, EmberEvery = 999 },
					new Lobs((p, v) => Ball(p, v, fire, 0.8f, 1.4f).Fiery(), h ? 34 : 44)
						{ MakeSplash = (p, v) => Ball(p, v, new Color(255, 190, 60), 0.6f, 0.8f), Splash = 2 }),
				// Teleports around casting chaos balls
				Sig.Sorcerer => new Blinker((p, v) => Self(p, v, 26f, 1f), (p, v) => Ball(p, v, new Color(230, 80, 255), 0.9f, 1.3f).Sparkly(new Color(255, 160, 255)), h ? 34 : 46) { Shots = h ? 4 : 3 },
				// Water spheres bouncing off the walls
				Sig.DarkCaster => new Bouncers((p, v) => Ball(p, v, new Color(80, 150, 255), 0.9f, 1.4f).Dripping(new Color(120, 180, 255)), h ? 22 : 30),
				// A school darting across in lanes, one after another
				Sig.Piranha => new LaneDash((p, d) => Self(p, d, 22f, 0.9f), h ? 14 : 20) { Speed = 9f, Warn = 26, LaneWidth = 22f, StopBeforeEnd = 70 },
				// Rings of static closing in (they only hurt while lit)
				Sig.Jellyfish => new ClosingRing((p, v) => Ball(p, v, new Color(120, 200, 255), 0.8f).Phasing(36), h ? 50 : 70) { Count = 14, Speed = 1.2f },
				// The jaws close: find the gap
				Sig.Shark => new Jaws((p, v) => Ball(p, v, new Color(230, 230, 230), 1f, 1.4f), h ? 60 : 80) { GapSize = h ? 34f : 40f },
				// Sword cuts crossing the box
				Sig.Armor => new Slashes(h ? 44 : 58) { PerBurst = 2, Width = 12f, Stagger = 10, FirstAt = 10, Color = new Color(200, 210, 230) },
				// Wraiths that fade in and out as they come for you
				Sig.Wraith => new Homing((p, v) => Self(p, v, 20f, 0.9f).Phasing(40), h ? 24 : 34) { Speed = 1.6f },
				// Fairy dust: sparkles drifting down
				Sig.Pixie => new Rain((p, v) => Ball(p, v, new Color(255, 140, 220), 0.7f).Sparkly(new Color(255, 200, 255)), h ? 6 : 9) { Wobble = 0.9f, SpeedMin = 1.2f, SpeedMax = 2f },
				// A rainbow charge, horn first
				Sig.Unicorn => new LaneDash((p, d) => Self(p, d, 34f, 1.1f).Sparkly(new Color(255, 180, 255)), h ? 34 : 46) { Speed = 10f, Warn = 26, LaneWidth = 32f, AllowVertical = true },
				// Bandage walls
				Sig.Mummy => new Walls((p, v) => Ball(p, v, new Color(230, 220, 190), 0.8f), h ? 55 : 70) { Side = 0, Speed = 2f, Spacing = 16f, GapSize = 46f },
				// A lunge and claws
				Sig.Werewolf => new Combo(t,
					new LaneDash((p, d) => Self(p, d, 30f, 1.1f), h ? 40 : 55) { Speed = 9.5f, Warn = 24 },
					new Slashes(h ? 60 : 80) { PerBurst = 3, Width = 8f, Stagger = 6, FirstAt = 30, Color = new Color(220, 220, 220) }),
				// Skulls circling, then closing in
				Sig.CursedSkull => new Orbiters((p, v) => Self(p, v, 18f, 0.8f), h ? 80 : 105) { Count = h ? 7 : 5, AngularSpeed = 0.04f },
				// It bursts into burning debris
				Sig.Meteor => new Fireworks((p, v) => Self(p, v, 22f, 1f).Fiery(), (p, v) => Ball(p, v, fire, 0.7f).Fiery(6), h ? 46 : 60) { Count = h ? 8 : 6 },
				// Rocks orbit in, then shards fly at you
				Sig.Granite => new Converge((p, v) => Ball(p, v, new Color(110, 120, 160), 0.8f, 1.2f), h ? 55 : 75) { Count = h ? 8 : 6, Speed = 3.8f },
				// A burning serpent snaking across
				Sig.BoneSerpent => new Snake((p, v) => Self(p, v, 22f, 1f), (p, v) => Ball(p, v, fire, 0.8f, 1.1f).Fiery(), h ? 60 : 85) { Segments = 9 },
				// Blinks about, flinging sparks
				Sig.Tim => new Blinker((p, v) => Self(p, v, 26f, 1f), (p, v) => Ball(p, v, new Color(200, 120, 255), 0.8f).Sparkly(new Color(220, 180, 255)), h ? 30 : 40) { Shots = h ? 5 : 4, Spread = 0.4f },
				// Ghosts drifting through the walls of the box
				Sig.Ghost => new Swoopers((p, v) => Self(p, v, 22f, 0.8f).Phasing(50), h ? 20 : 28) { Speed = 1.8f, Amplitude = 40f },
				// Scythes that circle in and strike
				Sig.Demon => new Converge((p, v) => Proj(ProjectileID.DemonSickle, p, v, 0.8f, 1f, 14f, rotate: false, spin: 0.35f), h ? 60 : 80) { Count = h ? 6 : 4, Speed = 3.4f },
				// Sideways scuttling along the floor (jump them)
				Sig.Crab => new Walkers((p, v) => Self(p, v, 22f, 0.9f), h ? 28 : 38) { Speed = h ? 2f : 1.6f }.WithSoul(SoulMode.Blue),
				// Circles high, then dives
				Sig.Vulture => new Diver((p, v) => Self(p, v, 26f, 1f), h ? 30 : 42) { DiveSpeed = h ? 9f : 7.5f },
				// A downpour over the whole box: the only dry spot is under its umbrella as it drifts along the top
				Sig.Umbrella => new UmbrellaRain((p, v) => Self(p, v, 30f, 0.9f), (p, v) => Proj(ProjectileID.RainNimbus, p, v, 1f, 0.6f, 6f))
					{ Drift = h ? 0.026f : 0.02f, Shelter = h ? 22f : 26f, FallSpeed = h ? 3.8f : 3.2f },
				_ => null,
			};
			return a?.Lasting(t);
		}
	}
}
