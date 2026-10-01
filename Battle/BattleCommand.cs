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
		public override string Usage => "/mmbattle [tp <0-100>]";
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
				int i = NPC.NewNPC(player.GetSource_FromThis(), (int)player.Center.X + 300, (int)player.Center.Y - 200, NPCID.EyeofCthulhu);
				eye = Main.npc[i];
			}
			BattleSystem.TryStart(eye, player);
			if (!BattleSystem.Active)
				caller.Reply("* Couldn't start the battle (turned off in the config?).", MercyMode.Gray);
		}
	}
}
