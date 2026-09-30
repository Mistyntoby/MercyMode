using Terraria;
using Terraria.ModLoader;

namespace MercyMode.Deltarune
{
	public class AssetsCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;
		public override string Command => "drassets";
		public override string Usage => "/drassets [reload]";
		public override string Description => "Shows which Deltarune assets Mercy Mode loaded from your install";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (args.Length > 0 && args[0] == "reload")
			{
				DeltaruneAssets.State = DeltaruneAssets.LoadState.NotStarted;
				DeltaruneAssets.StartLoading();
				caller.Reply("* Reloading Deltarune assets...", MercyMode.Gray);
				return;
			}

			caller.Reply($"* Deltarune assets: {DeltaruneAssets.State}", MercyMode.MercyYellow);
			caller.Reply("* " + DeltaruneAssets.StatusMessage, MercyMode.TextWhite);
			foreach (var role in DeltaruneAssets.SoundRoles.Keys)
				caller.Reply($"   {role}: {(DeltaruneAssets.HasSound(role) ? "real" : "vanilla fallback")}", MercyMode.Gray);
			if (!string.IsNullOrEmpty(DeltaruneAssets.DumpPath))
				caller.Reply("* Full asset name list: " + DeltaruneAssets.DumpPath, MercyMode.Gray);
		}
	}
}
