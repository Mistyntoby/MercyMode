using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>Testing helper: spawns the Eye of Cthulhu next to you and opens the battle right away.</summary>
	public class BattleCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;
		public override string Command => "mmbattle";
		public override string Usage => "/mmbattle [spawn [dx dy] | kit | night | tp <0-100> | bosshp <n>]";
		public override string Description => "Spawn the Eye of Cthulhu and start a Mercy Mode battle (for testing)";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			Player player = caller.Player;
			if (args.Length == 2 && args[0] == "tp" && float.TryParse(args[1], out float tp))
			{
				player.GetModPlayer<MercyPlayer>().TP = MathHelper.Clamp(tp, 0f, 100f);
				caller.Reply($"* TP set to {tp}%", MercyMode.TPOrange);
				return;
			}
			if (args.Length == 1 && args[0] == "night")
			{
				Main.dayTime = false;
				Main.time = 0;
				caller.Reply("* It's night now.", MercyMode.TextWhite);
				return;
			}
			if (args.Length == 1 && args[0] == "kit")
			{
				player.QuickSpawnItem(player.GetSource_FromThis(), ItemID.LesserHealingPotion, 5);
				player.QuickSpawnItem(player.GetSource_FromThis(), ItemID.HealingPotion, 3);
				player.QuickSpawnItem(player.GetSource_FromThis(), ItemID.Mushroom, 2);
				player.QuickSpawnItem(player.GetSource_FromThis(), ItemID.WoodenBow, 1);
				player.QuickSpawnItem(player.GetSource_FromThis(), ItemID.WoodenArrow, 100);
				caller.Reply("* Got some healing items.", MercyMode.TextWhite);
				return;
			}
			if (args.Length == 2 && args[0] == "bosshp" && int.TryParse(args[1], out int hp))
			{
				foreach (NPC n in Main.ActiveNPCs)
					if (n.type == NPCID.EyeofCthulhu)
						n.life = System.Math.Clamp(hp, 1, n.lifeMax);
				caller.Reply($"* Eye of Cthulhu HP set to {hp}.", MercyMode.TextWhite);
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

			NPC eye = null;
			foreach (NPC n in Main.ActiveNPCs)
				if (n.type == NPCID.EyeofCthulhu)
					eye = n;
			if (eye == null)
			{
				int dx = args.Length >= 3 && int.TryParse(args[1], out int x) ? x : 300;
				int dy = args.Length >= 3 && int.TryParse(args[2], out int y) ? y : -200;
				int i = NPC.NewNPC(player.GetSource_FromThis(), (int)player.Center.X + dx, (int)player.Center.Y + dy, NPCID.EyeofCthulhu);
				eye = Main.npc[i];
			}
			if (args.Length >= 1 && args[0] == "spawn")
			{
				caller.Reply("* The Eye of Cthulhu is here. Touch or hit it to start the battle.", MercyMode.TextWhite);
				return;
			}
			BattleSystem.TryStart(eye, player, "command");
			if (!BattleSystem.Active)
				caller.Reply("* Couldn't start the battle (turned off in the config?).", MercyMode.Gray);
		}
	}
}
