using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>Testing helper: spawns enemies next to you and opens battles right away.</summary>
	public class BattleCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;
		public override string Command => "mmbattle";
		public override string Usage => "/mmbattle [npc <id|name> | spawn [dx dy] | spawnnpc <id|name> | end | clear | heal | hp <n> | mercy <n> | kit | night | tp <0-100> | bosshp <n> | turn <n>]";
		public override string Description => "Start Mercy Mode battles for testing (no arguments: Eye of Cthulhu)";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			Player player = caller.Player;
			string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";

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
					caller.Reply("* Got some healing items.", MercyMode.TextWhite);
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
				case "end":
					if (BattleSystem.Active)
						foreach (NPC m in BattleSystem.Instance.Encounter.Members())
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
			return Main.npc[i];
		}

		private static int ParseNpc(string text)
		{
			if (int.TryParse(text, out int id))
				return id > 0 && id < NPCLoader.NPCCount ? id : 0;
			string want = text.Replace(" ", "").ToLowerInvariant();
			for (int t = 1; t < NPCLoader.NPCCount; t++)
			{
				string name = Lang.GetNPCNameValue(t).Replace(" ", "").ToLowerInvariant();
				if (name == want)
					return t;
			}
			if (NPCID.Search.TryGetId(text, out int byInternal))
				return byInternal;
			return 0;
		}
	}
}
