using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>
	/// What the bosses say as their turn starts (Deltarune's speech bubbles), with the same effect tags as the regular
	/// enemies' lines ([shake], [tremble], [wave]). Any boss not listed gets the generic lines.
	/// </summary>
	public static class BossBubbles
	{
		private static readonly Dictionary<int, string[]> Lines = new()
		{
			[NPCID.EyeofCthulhu] = new[] { "...", "[tremble]I see you.[/tremble]", "Don't blink.", "[shake]LOOK AT ME.[/shake]" },
			[NPCID.KingSlime] = new[] { "Bow before the crown!", "[wave]*royal jiggle*[/wave]", "Insolent!", "[shake]SQUISH![/shake]" },
			[NPCID.EaterofWorldsHead] = new[] { "[wave]Hungry...[/wave]", "[shake]CRUNCH.[/shake]", "More. MORE.", "The world tastes good." },
			[NPCID.BrainofCthulhu] = new[] { "[tremble]Which one is real?[/tremble]", "I know what you're thinking.", "[wave]Think harder.[/wave]", "[shake]GET OUT OF MY HEAD.[/shake]" },
			[NPCID.QueenBee] = new[] { "[shake]BZZZT![/shake]", "My hive!", "[wave]Sting sting sting~[/wave]", "You smell like honey." },
			[NPCID.SkeletronHead] = new[] { "[tremble]The curse holds.[/tremble]", "None shall pass.", "[shake]RATTLE.[/shake]", "Turn back." },
			[NPCID.Deerclops] = new[] { "[tremble]...[/tremble]", "[shake]GRRRRAH![/shake]", "Cold... so cold...", "[wave]Shadows watch...[/wave]" },
			[NPCID.WallofFlesh] = new[] { "[shake]GRAAAAH![/shake]", "[tremble]You can't run.[/tremble]", "The underworld is MINE.", "[wave]Closer...[/wave]" },
			[NPCID.QueenSlimeBoss] = new[] { "Bow to the queen!", "[wave]Sparkle~[/wave]", "How rude!", "[shake]Shatter![/shake]" },
			[NPCID.Retinazer] = new[] { "TARGET ACQUIRED.", "[shake]FIRE.[/shake]", "Brother, now!", "[tremble]Scanning...[/tremble]" },
			[NPCID.Spazmatism] = new[] { "[shake]BURN![/shake]", "Hahaha!", "Brother, now!", "[wave]Hot hot hot~[/wave]" },
			[NPCID.TheDestroyer] = new[] { "[tremble]DRILLING.[/tremble]", "PROBES DEPLOYED.", "[shake]DESTROY.[/shake]", "SEGMENTS: ALL GREEN." },
			[NPCID.SkeletronPrime] = new[] { "[shake]ARMS UP.[/shake]", "Efficient.", "[tremble]Spinning up...[/tremble]", "PRIME DIRECTIVE." },
			[NPCID.Plantera] = new[] { "[wave]Grow...[/wave]", "[shake]SNAP![/shake]", "The jungle fights back.", "Petals and thorns." },
			[NPCID.Golem] = new[] { "[tremble]...[/tremble]", "LIHZAHRD POWER.", "[shake]STOMP.[/shake]", "Stone does not tire." },
			[NPCID.DukeFishron] = new[] { "[shake]SPLASH![/shake]", "[wave]Waves...[/wave]", "You used TRUFFLE WORM?", "The sea remembers." },
			[NPCID.HallowBoss] = new[] { "[wave]Dance with me~[/wave]", "How lovely.", "[shake]Shine.[/shake]", "Day or night, I am light." },
			[NPCID.CultistBoss] = new[] { "[tremble]The ritual is broken![/tremble]", "Which of us is real?", "[wave]Ancient powers...[/wave]", "[shake]FOOL![/shake]" },
			[NPCID.MoonLordCore] = new[] { "[tremble]...[/tremble]", "[shake]KNEEL.[/shake]", "You woke me.", "[wave]The end approaches.[/wave]" },
		};

		private static readonly string[] Generic = { "...", "[shake]RAAAH![/shake]", "You dare?", "[tremble]...[/tremble]" };

		public static string Get(int type, int turn)
		{
			// Their own lines first, then the extras (Dialogue.cs)
			string[] own = Lines.TryGetValue(type, out string[] l) ? l : Generic;
			string[] extra = Dialogue.BossBubbles(type) ?? Dialogue.GenericBossBubbles;
			string[] lines = own.Concat(extra.Where(x => !own.Contains(x))).ToArray();
			return lines[((turn % lines.Length) + lines.Length) % lines.Length];
		}
	}
}
