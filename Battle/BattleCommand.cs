using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>Testing helper: spawns enemies next to you and opens battles right away.</summary>
	public class BattleCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;
		public override string Command => "mmbattle";
		public override string Usage => "/mmbattle [eye | npc <id|name> | group <n> <name> | group <name>, <name>, ... | spawn [dx dy] | spawnnpc <id|name> | loadout <starter|melee|spear|ranged|magic|thrown|endgame|summon|mixed> | clearinv confirm | join | end | clear | heal | hp <n> | mercy <n> | kit | night | tp <0-100> | bosshp <n> | turn <n>]";
		public override string Description => "Mercy Mode test commands (type /mmbattle for the list)";

		/// <summary>Weapon sets for demonstrating FIGHT with each kind of weapon (item, stack).</summary>
		private static readonly Dictionary<string, (int, int)[]> Loadouts = new()
		{
			["starter"] = new (int, int)[] { (ItemID.CopperHelmet, 1), (ItemID.CopperChainmail, 1), (ItemID.CopperGreaves, 1), (ItemID.CopperShortsword, 1), (ItemID.CopperBroadsword, 1), (ItemID.WoodenBow, 1), (ItemID.WoodenArrow, 200), (ItemID.WandofSparking, 1) },
			["melee"] = new (int, int)[] { (ItemID.MoltenHelmet, 1), (ItemID.MoltenBreastplate, 1), (ItemID.MoltenGreaves, 1), (ItemID.Gladius, 1), (ItemID.Muramasa, 1), (ItemID.NightsEdge, 1), (ItemID.TerraBlade, 1) },
			["spear"] = new (int, int)[] { (ItemID.HallowedHelmet, 1), (ItemID.HallowedPlateMail, 1), (ItemID.HallowedGreaves, 1), (ItemID.Spear, 1), (ItemID.Trident, 1), (ItemID.Gungnir, 1) },
			["ranged"] = new (int, int)[] { (ItemID.NecroHelmet, 1), (ItemID.NecroBreastplate, 1), (ItemID.NecroGreaves, 1), (ItemID.WoodenBow, 1), (ItemID.WoodenArrow, 300), (ItemID.Minishark, 1), (ItemID.Megashark, 1), (ItemID.MusketBall, 999) },
			["magic"] = new (int, int)[] { (ItemID.JungleHat, 1), (ItemID.JungleShirt, 1), (ItemID.JunglePants, 1), (ItemID.WandofSparking, 1), (ItemID.WaterBolt, 1), (ItemID.MagicMissile, 1), (ItemID.LastPrism, 1) },
			["thrown"] = new (int, int)[] { (ItemID.NinjaHood, 1), (ItemID.NinjaShirt, 1), (ItemID.NinjaPants, 1), (ItemID.Shuriken, 200), (ItemID.ThrowingKnife, 200), (ItemID.BoneDagger, 200), (ItemID.Javelin, 200) },
			["endgame"] = new (int, int)[] { (ItemID.SolarFlareHelmet, 1), (ItemID.SolarFlareBreastplate, 1), (ItemID.SolarFlareLeggings, 1), (ItemID.Zenith, 1), (ItemID.SDMG, 1), (ItemID.ChlorophyteBullet, 999), (ItemID.LastPrism, 1), (ItemID.DayBreak, 1) },
			["summon"] = new (int, int)[] { (ItemID.BeeHeadgear, 1), (ItemID.BeeBreastplate, 1), (ItemID.BeeGreaves, 1), (ItemID.AbigailsFlower, 1), (ItemID.ImpStaff, 1), (ItemID.StardustDragonStaff, 1), (ItemID.BlandWhip, 1) },
			["mixed"] = new (int, int)[] { (ItemID.PlatinumHelmet, 1), (ItemID.PlatinumChainmail, 1), (ItemID.PlatinumGreaves, 1), (ItemID.NightsEdge, 1), (ItemID.Minishark, 1), (ItemID.MusketBall, 999), (ItemID.MagicMissile, 1), (ItemID.Shuriken, 200) },
		};

		private static void ShowHelp(CommandCaller caller)
		{
			Color h = MercyMode.MercyYellow, t = MercyMode.TextWhite, g = MercyMode.Gray;
			caller.Reply("* Mercy Mode commands:", h);
			caller.Reply("Battles (singleplayer):", h);
			caller.Reply("  /mmbattle eye  - fight the Eye of Cthulhu", t);
			caller.Reply("  /mmbattle npc <id|name>  - spawn an enemy or boss and fight it", t);
			caller.Reply("  /mmbattle group <n> <name>  or  group <a>, <b>, <c>  - fight a squad", t);
			caller.Reply("  /mmbattle spawn [dx dy]  - spawn the Eye without starting  |  spawnnpc <id|name>", t);
			caller.Reply("Gear:", h);
			caller.Reply("  /mmbattle loadout <starter|melee|spear|ranged|magic|thrown|endgame|summon|mixed>", t);
			caller.Reply("  /mmbattle kit  - potions and one of each weapon kind", t);
			caller.Reply("  /mmbattle clearinv confirm  - DELETES your main inventory (not armor)", t);
			caller.Reply("In a battle:", h);
			caller.Reply("  /mmbattle end  |  heal  |  hp <n>  |  mercy <n>  |  tp <0-100>  |  bosshp <n>  |  turn <n>", t);
			caller.Reply("Other:", h);
			caller.Reply("  /mmbattle join  - join a battle nearby (multiplayer; same as the Join Battle key)", t);
			caller.Reply("  /mmbattle night  |  clear", t);
			caller.Reply("  /drassets  - which Deltarune assets loaded", g);
		}

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			Player player = caller.Player;
			string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
			// Nothing (or help): the list of commands
			if (cmd is "" or "help" or "?")
			{
				ShowHelp(caller);
				return;
			}

			switch (cmd)
			{
				case "tp" when args.Length == 2 && float.TryParse(args[1], out float tp):
					player.GetModPlayer<MercyPlayer>().TP = MathHelper.Clamp(tp, 0f, 100f);
					caller.Reply($"* TP set to {tp}%", MercyMode.TPOrange);
					return;
				case "mercy" when args.Length == 2 && float.TryParse(args[1], out float mercy) && BattleSystem.Active:
					BattleSystem.Instance.Encounter.Mercy = mercy;
					caller.Reply($"* MERCY set to {mercy}%.", MercyMode.MercyYellow);
					return;
				case "clear":
					foreach (NPC n in Main.ActiveNPCs)
						if (!n.friendly && !n.townNPC)
							n.active = false;
					caller.Reply("* Cleared nearby enemies.", MercyMode.Gray);
					return;
				case "heal":
					player.statLife = player.statLifeMax2;
					if (BattleSystem.Active)
						BattleSystem.Instance.SetBattleLife(player.statLife);
					caller.Reply("* HP restored.", MercyMode.TextWhite);
					return;
				case "hp" when args.Length == 2 && int.TryParse(args[1], out int life):
					// e.g. "/mmbattle hp 1" then take a hit to see the SOUL break
					player.statLife = Math.Clamp(life, 1, player.statLifeMax2);
					if (BattleSystem.Active)
						BattleSystem.Instance.SetBattleLife(player.statLife);
					caller.Reply($"* HP set to {player.statLife}.", MercyMode.TextWhite);
					return;
				case "turn" when args.Length == 2 && int.TryParse(args[1], out int turn) && BattleSystem.Active:
					// Attacks are picked by turn number, so this chooses the next enemy attack
					BattleSystem.Instance.Encounter.Turn = Math.Max(0, turn);
					caller.Reply($"* The next enemy turn uses attack {turn}.", MercyMode.TextWhite);
					return;
				case "night":
					Main.dayTime = false;
					Main.time = 0;
					caller.Reply("* It's night now.", MercyMode.TextWhite);
					return;
				case "kit":
					var src = player.GetSource_FromThis();
					player.QuickSpawnItem(src, ItemID.LesserHealingPotion, 5);
					player.QuickSpawnItem(src, ItemID.HealingPotion, 3);
					player.QuickSpawnItem(src, ItemID.Mushroom, 2);
					player.QuickSpawnItem(src, ItemID.WoodenBow, 1);
					player.QuickSpawnItem(src, ItemID.WoodenArrow, 100);
					player.QuickSpawnItem(src, ItemID.CopperBroadsword, 1);
					// One of each kind of weapon for testing FIGHT: a gun with bullets, a wand, throwables
					player.QuickSpawnItem(src, ItemID.FlintlockPistol, 1);
					player.QuickSpawnItem(src, ItemID.MusketBall, 100);
					player.QuickSpawnItem(src, ItemID.WandofSparking, 1);
					player.QuickSpawnItem(src, ItemID.Shuriken, 50);
					caller.Reply("* Got healing items and one of each kind of weapon.", MercyMode.TextWhite);
					return;
				case "loadout":
				{
					// Weapon sets for showing off FIGHT (video clips): /mmbattle loadout <name>
					string which = args.Length > 1 ? args[1].ToLowerInvariant() : "";
					if (!Loadouts.TryGetValue(which, out var items))
					{
						caller.Reply($"* Loadouts: {string.Join(", ", Loadouts.Keys)}. Use /mmbattle clearinv confirm first for a clean weapon list.", MercyMode.Gray);
						return;
					}
					var lsrc = player.GetSource_FromThis();
					foreach (var (type, stack) in items)
						player.QuickSpawnItem(lsrc, type, stack);
					player.QuickSpawnItem(lsrc, ItemID.HealingPotion, 5);
					// Magic needs the mana to show more than one hit
					if (which is "magic" or "endgame" or "mixed")
					{
						player.statManaMax = Math.Max(player.statManaMax, 200);
						player.statMana = player.statManaMax2;
					}
					caller.Reply($"* Got the {which} loadout. Armor goes in your inventory: equip it yourself.", MercyMode.TextWhite);
					return;
				}
				case "clearinv":
					// Empties the 50 main inventory slots (not armor, accessories, coins or ammo slots) so the weapon list
					// shows only a loadout. Destroys those items, so it has to be confirmed
					if (args.Length < 2 || args[1] != "confirm")
					{
						caller.Reply("* This DELETES everything in your main inventory (not armor/accessories). Type /mmbattle clearinv confirm to do it.", new Color(255, 80, 80));
						return;
					}
					for (int i = 0; i < 50; i++)
						player.inventory[i].TurnToAir();
					caller.Reply("* Main inventory cleared.", MercyMode.Gray);
					return;
				case "bosshp" when args.Length == 2 && int.TryParse(args[1], out int hp):
					if (BattleSystem.Active)
					{
						foreach (NPC m in BattleSystem.Instance.Encounter.Members())
							m.life = Math.Clamp(hp, 1, m.lifeMax);
					}
					else
					{
						foreach (NPC n in Main.ActiveNPCs)
							if (n.type == NPCID.EyeofCthulhu)
								n.life = Math.Clamp(hp, 1, n.lifeMax);
					}
					caller.Reply($"* Enemy HP set to {hp}.", MercyMode.TextWhite);
					return;
				case "join":
					// Multiplayer: join the battle going on nearby (same as the Join Battle key)
					if (!Net.BattleNetSystem.TryJoin())
						caller.Reply("* There's no battle with room close enough to join.", MercyMode.Gray);
					return;
				case "end":
					// Multiplayer: just leave (the enemies belong to the server and the rest of the party)
					if (BattleSystem.Active && Net.BattleNet.Online)
						BattleSystem.Instance.Leave();
					else if (BattleSystem.Active)
						foreach (Encounter e in BattleSystem.Instance.Encounters.ToList())
							foreach (NPC m in e.Members())
								m.active = false;
					caller.Reply("* Battle ended.", MercyMode.Gray);
					return;
			}

			if (BattleSystem.Active)
			{
				caller.Reply("* A battle is already going.", MercyMode.Gray);
				return;
			}
			if (!MercyMode.IsSingleplayer)
			{
				caller.Reply("* Singleplayer only.", MercyMode.Gray);
				return;
			}

			if (cmd == "group" && args.Length >= 2)
			{
				// "/mmbattle group 3 zombie" or a mixed squad: "/mmbattle group goblin peon, goblin archer, goblin sorcerer"
				var types = new List<int>();
				if (int.TryParse(args[1], out int count) && args.Length >= 3)
				{
					int t = ParseNpc(string.Join(" ", args, 2, args.Length - 2));
					for (int i = 0; i < Math.Clamp(count, 1, 3); i++)
						types.Add(t);
				}
				else
				{
					foreach (string name in string.Join(" ", args, 1, args.Length - 1).Split(','))
						types.Add(ParseNpc(name.Trim()));
				}
				if (types.Count == 0 || types.Any(t => t <= 0))
				{
					caller.Reply("* No NPC with that id or name.", MercyMode.Gray);
					return;
				}
				NPC first = null;
				for (int i = 0; i < types.Count && i < 3; i++)
				{
					NPC n = SpawnNear(player, types[i], 160 + i * 70, -40 - (i % 2) * 50);
					first ??= n;
				}
				BattleSystem.QueueStart(first, 20);
				caller.Reply($"* Spawned {types.Count} enemies, starting the battle...", MercyMode.TextWhite);
				return;
			}

			if ((cmd == "npc" || cmd == "spawnnpc") && args.Length >= 2)
			{
				int type = ParseNpc(string.Join(" ", args, 1, args.Length - 1));
				if (type <= 0)
				{
					caller.Reply("* No NPC with that id or name.", MercyMode.Gray);
					return;
				}
				NPC spawned = SpawnNear(player, type, 220, -60);
				if (cmd == "npc")
				{
					// Worms, the Brain and others build their parts on their first AI ticks; start a moment later
					BattleSystem.QueueStart(spawned, 20);
					caller.Reply($"* Spawned {spawned.FullName}, starting the battle...", MercyMode.TextWhite);
				}
				else
					caller.Reply($"* Spawned {spawned.FullName}.", MercyMode.TextWhite);
				return;
			}

			// Anything not known: the list, rather than a surprise Eye of Cthulhu
			if (cmd != "eye" && cmd != "spawn")
			{
				caller.Reply($"* Unknown command \"{cmd}\".", MercyMode.Gray);
				ShowHelp(caller);
				return;
			}

			NPC eye = null;
			foreach (NPC n in Main.ActiveNPCs)
				if (n.type == NPCID.EyeofCthulhu)
					eye = n;
			if (eye == null)
			{
				int dx = args.Length >= 3 && int.TryParse(args[1], out int x) ? x : 300;
				int dy = args.Length >= 3 && int.TryParse(args[2], out int y) ? y : -200;
				eye = SpawnNear(player, NPCID.EyeofCthulhu, dx, dy);
			}
			if (cmd == "spawn")
			{
				caller.Reply("* The Eye of Cthulhu is here. Touch or hit it to start the battle.", MercyMode.TextWhite);
				return;
			}
			Begin(caller, eye, player);
		}

		private static void Begin(CommandCaller caller, NPC npc, Player player)
		{
			BattleSystem.TryStart(npc, player, "command");
			if (!BattleSystem.Active)
				caller.Reply($"* Couldn't start a battle with {npc.FullName} (turned off in the config, or not eligible).", MercyMode.Gray);
		}

		private static NPC SpawnNear(Player player, int type, int dx, int dy)
		{
			// Worms and multi-part bosses spawn their own pieces; Wall of Flesh needs the special spawner
			if (type == NPCID.WallofFlesh)
			{
				NPC.SpawnWOF(player.Center + new Vector2(dx, 0));
				if (Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active)
					return Main.npc[Main.wofNPCIndex];
				// SpawnWOF only works in the Underworld; for testing elsewhere, spawn the mouth directly
			}
			int i = NPC.NewNPC(player.GetSource_FromThis(), (int)player.Center.X + dx, (int)player.Center.Y + dy, type);
			// The Twins come as a pair (the Mechanical Eye summons both)
			int twin = type == NPCID.Retinazer ? NPCID.Spazmatism : type == NPCID.Spazmatism ? NPCID.Retinazer : 0;
			if (twin != 0 && !NPC.AnyNPCs(twin))
				NPC.NewNPC(player.GetSource_FromThis(), (int)player.Center.X + dx, (int)player.Center.Y + dy + 120, twin);
			return Main.npc[i];
		}

		private static int ParseNpc(string text)
		{
			if (int.TryParse(text, out int id))
				return id > 0 && id < NPCLoader.NPCCount ? id : 0;
			string want = text.Replace(" ", "").Replace("'", "").ToLowerInvariant();
			// Internal names, spaces ignored: "moon lord core" -> MoonLordCore
			for (int t = 1; t < NPCID.Count; t++)
				if (NPCID.Search.GetName(t).ToLowerInvariant() == want)
					return t;
			// Display names; several parts can share one ("Moon Lord"), so prefer the one a battle is about
			int first = 0;
			for (int t = 1; t < NPCLoader.NPCCount; t++)
			{
				string name = Lang.GetNPCNameValue(t).Replace(" ", "").Replace("'", "").ToLowerInvariant();
				if (name != want)
					continue;
				bool isBoss = ContentSamples.NpcsByNetId.TryGetValue(t, out NPC sample) && sample.boss;
				if (EncounterRegistry.HasCustom(t) || isBoss)
					return t;
				if (first == 0)
					first = t;
			}
			return first;
		}
	}
}
