using System.Collections.Generic;
using Terraria.ID;

namespace MercyMode
{
	public static class ActLines
	{
		private static readonly Dictionary<int, string[]> Lines = new()
		{
			[NPCID.KingSlime] = new[] {
				"* You compliment the crown. King Slime jiggles proudly.",
				"* You bow. King Slime bounces a little less aggressively.",
				"* You offer to hold the ninja. It's still in there. You try anyway.",
			},
			[NPCID.EyeofCthulhu] = new[] {
				"* You stare back. Eye of Cthulhu blinks first.",
				"* You offer it eye drops. It seems tempted.",
				"* You tell it the night is almost over. It looks at the horizon.",
			},
			[NPCID.EaterofWorldsHead] = new[] {
				"* You point out there's plenty of corruption to go around.",
				"* You pat a segment. The other segments get jealous.",
				"* You suggest a snack that isn't you.",
			},
			[NPCID.BrainofCthulhu] = new[] {
				"* You think calm thoughts. The Brain reads them.",
				"* You ask what it's thinking about. It's thinking about you. Uncomfortable.",
				"* You agree that you are, in fact, very small.",
			},
			[NPCID.QueenBee] = new[] {
				"* You promise not to touch the larva again. She's skeptical.",
				"* You compliment the hive architecture.",
				"* You hum. Queen Bee hums back. It's kind of nice.",
			},
			[NPCID.SkeletronHead] = new[] {
				"* You tell Skeletron the curse isn't his fault.",
				"* You offer to talk to the Clothier for him.",
				"* You rattle your bones in solidarity. You don't have visible bones.",
			},
			[NPCID.Deerclops] = new[] {
				"* You lend Deerclops a scarf. It's too small. It appreciates the gesture.",
				"* You compliment the antlers.",
				"* You light a campfire. Deerclops warms its hands.",
			},
			[NPCID.WallofFlesh] = new[] {
				"* You try to reason with the wall. The wall is a wall.",
				"* You compliment its many teeth.",
				"* You remind it the Guide was a nice guy, actually.",
			},
			[NPCID.QueenSlimeBoss] = new[] {
				"* You call Queen Slime pretty. The crystals sparkle.",
				"* You mention she's way more elegant than King Slime.",
				"* You offer a gelatin tiara. She accepts.",
			},
			[NPCID.Retinazer] = new[] {
				"* You ask Retinazer to stop lasering for one second. It does not.",
				"* You compliment Retinazer's focus.",
				"* You tell Retinazer it's clearly the smart twin.",
			},
			[NPCID.Spazmatism] = new[] {
				"* You tell Spazmatism to breathe. It breathes fire.",
				"* You compliment Spazmatism's energy.",
				"* You tell Spazmatism it's clearly the fun twin.",
			},
			[NPCID.TheDestroyer] = new[] {
				"* You ask the Destroyer to maybe destroy a little less.",
				"* You count the segments out loud. It takes a while. It's flattered.",
				"* You compliment the paint job.",
			},
			[NPCID.SkeletronPrime] = new[] {
				"* You ask Skeletron Prime which arm is its favorite. It spins.",
				"* You oil a squeaky joint. Prime relaxes slightly.",
				"* You tell Prime the upgrade looks good.",
			},
			[NPCID.Plantera] = new[] {
				"* You water Plantera. It seems surprised anyone bothered.",
				"* You promise to stop breaking bulbs.",
				"* You tell Plantera about photosynthesis. It already knew.",
			},
			[NPCID.Golem] = new[] {
				"* You compliment the stonework.",
				"* You offer Golem a Lihzahrd power cell. Oh wait.",
				"* You tell Golem it can take a break. It's been guarding for centuries.",
			},
			[NPCID.DukeFishron] = new[] {
				"* You apologize for using a truffle worm.",
				"* You tell the Duke he has a very dignified mustache.",
				"* You throw a fish back into the ocean. The Duke notices.",
			},
			[NPCID.HallowBoss] = new[] {
				"* You admire the Empress's wings.",
				"* You stop to take in the light show.",
				"* You bow politely. The Empress dims her lances a little.",
			},
			[NPCID.CultistBoss] = new[] {
				"* You question the cult's life choices.",
				"* You offer the Cultist a hobby that isn't summoning gods.",
				"* You ask how the meetings are going.",
			},
			[NPCID.MoonLordCore] = new[] {
				"* You look the Moon Lord in all of its eyes.",
				"* You tell the Moon Lord the world is doing fine without him.",
				"* You tell the Moon Lord his hands are very expressive.",
			},
		};

		private static readonly string[] Generic = {
			"* You tell it a joke. It almost laughs.",
			"* You try to talk it down.",
			"* You show it you mean no harm.",
		};

		public static string Get(int npcType, int actCount)
		{
			string[] set = Lines.TryGetValue(npcType, out var l) ? l : Generic;
			return set[actCount % set.Length];
		}
	}
}
