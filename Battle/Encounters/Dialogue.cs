using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	/// <summary>
	/// More to say: extra turn lines (the "* ..." text at the start of each turn) and speech bubbles for every boss,
	/// enemy family and event army, on top of the ones each encounter already has. Family and army lines can use {0}
	/// for the enemy's name. Bubbles can use the effect tags ([shake], [tremble], [wave]).
	/// </summary>
	public static class Dialogue
	{
		// ================================================================== bosses: turn lines

		private static readonly Dictionary<int, string[]> BossFlavor = new()
		{
			[NPCID.KingSlime] = new[] {
				"* KING SLIME adjusts his crown. It slides right back down.",
				"* Something shiny is suspended in the gel. Possibly a ninja.",
				"* KING SLIME wobbles regally.",
				"* The ground squelches with every bounce.",
				"* KING SLIME is having trouble seeing past his own crown.",
				"* A smaller slime bows in the background. Loyal subject.",
				"* KING SLIME is 90% gel and 10% ego.",
				"* It smells like the inside of a lemon drop.",
				"* KING SLIME demands you address him as 'Your Jiggliness'.",
				"* The ninja inside gives you a small thumbs up.",
			},
			[NPCID.EyeofCthulhu] = new[] {
				"* EYE OF CTHULHU hasn't blinked once. It can't.",
				"* Its veins pulse in time with your heartbeat.",
				"* The moon seems to be watching too.",
				"* EYE OF CTHULHU is looking at you. Specifically you.",
				"* Servants circle overhead like flies.",
				"* You feel very, very perceived.",
				"* EYE OF CTHULHU's pupil narrows.",
				"* Somewhere, an optometrist shudders.",
				"* It smells like copper and night air.",
				"* EYE OF CTHULHU rolls back, then fixes on you again.",
			},
			[NPCID.EaterofWorldsHead] = new[] {
				"* Ebonstone crumbles where EATER OF WORLDS passes.",
				"* EATER OF WORLDS' segments argue about who's in front.",
				"* A low rumble comes from somewhere beneath you.",
				"* The Corruption seems to lean toward the worm.",
				"* Vile spit sizzles on the ground.",
				"* EATER OF WORLDS is chewing. On what, you'd rather not know.",
				"* Its many plates clack together like teeth.",
				"* It smells like rot and wet stone.",
				"* The tail end is still catching up.",
				"* EATER OF WORLDS is, technically, still hungry.",
			},
			[NPCID.BrainofCthulhu] = new[] {
				"* You hear your own thoughts in someone else's voice.",
				"* BRAIN OF CTHULHU pulses. Your head throbs with it.",
				"* A Creeper stares at you without blinking.",
				"* For a moment, you forget which way is left.",
				"* The Crimson's heartbeat is very loud here.",
				"* BRAIN OF CTHULHU already knows what you'll pick.",
				"* You're not sure you're the one who's thinking.",
				"* It smells like iron and wet meat.",
				"* BRAIN OF CTHULHU flickers. Was that two of them?",
				"* Ichor drips from somewhere above.",
			},
			[NPCID.QueenBee] = new[] {
				"* The buzzing is coming from inside your ears now.",
				"* Honey drips slowly from the hive walls.",
				"* QUEEN BEE hovers protectively over the larva.",
				"* A worker bee files a complaint about you.",
				"* The air is sticky and sweet.",
				"* QUEEN BEE's stinger glints.",
				"* Every hexagon in the hive is perfect. She checked.",
				"* Smells like flowers and anger.",
				"* QUEEN BEE does a little aerial loop. Showing off.",
				"* You wonder how many bees are in this hive. Too many.",
			},
			[NPCID.SkeletronHead] = new[] {
				"* SKELETRON's hands drum on the dungeon walls.",
				"* The old man's curse hangs heavy in the air.",
				"* Bones rattle in the dark.",
				"* SKELETRON's jaw creaks open. Nothing comes out.",
				"* The dungeon's bricks seem to lean in to watch.",
				"* Somewhere, a water bolt sits in a chest, unopened.",
				"* SKELETRON spins once, for intimidation.",
				"* It smells like dust and old books.",
				"* SKELETRON's eye sockets glow a little brighter.",
				"* You hear the Clothier faintly apologising.",
			},
			[NPCID.Deerclops] = new[] {
				"* Snow swirls around DEERCLOPS' feet.",
				"* Its single eye never leaves you.",
				"* The shadows at the edge of the screen get closer.",
				"* DEERCLOPS sniffs the air. It smells your fear. And your sandwich.",
				"* Icicles crack somewhere above.",
				"* It's getting dark. Very dark.",
				"* DEERCLOPS stomps. The ground remembers.",
				"* Smells like wet fur and frost.",
				"* Hands reach out of the darkness, then think better of it.",
				"* DEERCLOPS' antlers scrape the sky.",
			},
			[NPCID.WallofFlesh] = new[] {
				"* WALL OF FLESH keeps moving. It never stops moving.",
				"* The Hungry gnash their teeth.",
				"* Lava bubbles far below.",
				"* WALL OF FLESH's eyes roll in different directions.",
				"* You can hear the Underworld screaming.",
				"* WALL OF FLESH is very, very wide.",
				"* Something wet slaps against WALL OF FLESH.",
				"* It smells like brimstone and meat.",
				"* The world feels like it's about to change.",
				"* You remember the Guide. You feel bad.",
			},
			[NPCID.QueenSlimeBoss] = new[] {
				"* QUEEN SLIME's crystals catch the light.",
				"* Pink gel glitters on the ground.",
				"* QUEEN SLIME is posing. For whom?",
				"* A little crystal slime bows to its queen.",
				"* QUEEN SLIME's wings flutter. They're mostly decorative.",
				"* It smells like candy and gemstones.",
			},
			[NPCID.Retinazer] = new[] {
				"* RETINAZER's lens refocuses with a click.",
				"* A red dot dances across your chest.",
				"* The Twins share a look. Literally.",
				"* RETINAZER hums with charging energy.",
				"* It smells like ozone.",
				"* The Twins circle each other, then you.",
			},
			[NPCID.Spazmatism] = new[] {
				"* SPAZMATISM can't keep still.",
				"* Green fire licks the air.",
				"* The Twins share a look. Literally.",
				"* SPAZMATISM grins with a mouth it shouldn't have.",
				"* It smells like cursed smoke.",
				"* The Twins circle each other, then you.",
			},
			[NPCID.TheDestroyer] = new[] {
				"* The ground vibrates as THE DESTROYER passes.",
				"* Red lights blink down its whole length.",
				"* A probe whirs past your ear.",
				"* You can't see where THE DESTROYER ends.",
				"* It smells like oil and hot metal.",
				"* THE DESTROYER's segments clank in sequence.",
			},
			[NPCID.SkeletronPrime] = new[] {
				"* SKELETRON PRIME's saw revs.",
				"* The vice snaps open and shut.",
				"* The cannon swivels toward you.",
				"* SKELETRON PRIME is very proud of its arms.",
				"* It smells like grease and gunpowder.",
				"* A bolt falls off. PRIME pretends not to notice.",
			},
			[NPCID.Plantera] = new[] {
				"* Vines creep across the floor.",
				"* PLANTERA's petals snap open.",
				"* The jungle holds its breath.",
				"* Spores drift in the humid air.",
				"* It smells like soil and blossoms.",
				"* PLANTERA's hooks dig into the walls.",
			},
			[NPCID.Golem] = new[] {
				"* GOLEM's eyes glow like the sun.",
				"* Ancient gears grind inside the stone.",
				"* Dust falls from the temple ceiling.",
				"* GOLEM's fists are bigger than you.",
				"* It smells like old stone and heat.",
				"* The Lihzahrd carvings watch silently.",
			},
			[NPCID.DukeFishron] = new[] {
				"* DUKE FISHRON's mustache twitches.",
				"* Seawater pours off his scales.",
				"* A Sharkron circles hungrily.",
				"* The ocean roars behind him.",
				"* It smells very, very fishy.",
				"* DUKE FISHRON snorts a jet of bubbles.",
			},
			[NPCID.HallowBoss] = new[] {
				"* The EMPRESS OF LIGHT spins slowly.",
				"* Prismatic light paints the ground.",
				"* Her wings shimmer through every colour.",
				"* The Hallow sings softly.",
				"* It smells like rain and rainbows.",
				"* The EMPRESS OF LIGHT hums a tune only butterflies know.",
			},
			[NPCID.CultistBoss] = new[] {
				"* The LUNATIC CULTIST mutters ancient words.",
				"* Ice, fire and lightning swirl around him.",
				"* His copies flicker at the edge of your vision.",
				"* The moon seems closer than usual.",
				"* It smells like incense and ozone.",
				"* The tablet behind him glows.",
			},
			[NPCID.MoonLordCore] = new[] {
				"* The MOON LORD's eyes open, one after another.",
				"* Reality feels thin here.",
				"* The sky is the wrong colour.",
				"* The MOON LORD's hands reach for the stars.",
				"* It smells like nothing you have a name for.",
				"* Something vast breathes above you.",
			},
		};

		// ================================================================== bosses: speech bubbles

		private static readonly Dictionary<int, string[]> BossBubble = new()
		{
			[NPCID.EyeofCthulhu] = new[] { "[wave]Watching...[/wave]", "You look tired.", "Night is MINE.", "[tremble]Closer.[/tremble]", "I never sleep.", "Eye see you.", "[shake]BLINK AND DIE.[/shake]", "Pretty light you have." },
			[NPCID.KingSlime] = new[] { "Kneel, peasant!", "[wave]Boing~[/wave]", "My crown! My CROWN!", "I am ROYALTY.", "[shake]BOUNCE![/shake]", "Off with your head!", "Gel is forever.", "[wave]Wibble wobble~[/wave]" },
			[NPCID.EaterofWorldsHead] = new[] { "[tremble]Rumble...[/tremble]", "Down here.", "[shake]CHOMP![/shake]", "We are many.", "[wave]Slither...[/wave]", "Taste the rot.", "Which of us? All.", "[shake]FEED.[/shake]" },
			[NPCID.BrainofCthulhu] = new[] { "Left is right.", "[wave]Shhh...[/wave]", "You're thinking it too.", "[tremble]Forget.[/tremble]", "I'm in here now.", "[shake]OBEY.[/shake]", "Look closer...", "We're the same, you and I." },
			[NPCID.QueenBee] = new[] { "[wave]Bzz bzz~[/wave]", "My babies!", "[shake]STING![/shake]", "Leave my honey!", "Swarm!", "[tremble]Bzzzzz...[/tremble]", "Sweet, aren't I?", "You're not welcome here." },
			[NPCID.SkeletronHead] = new[] { "[tremble]Old man...[/tremble]", "[shake]CLACK.[/shake]", "The dungeon is mine.", "Bones never forget.", "[wave]Spin...[/wave]", "Hands up.", "You'll stay forever.", "[tremble]Cursed...[/tremble]" },
			[NPCID.Deerclops] = new[] { "[shake]ROAAAR![/shake]", "[wave]...hungry...[/wave]", "[tremble]Dark...[/tremble]", "Mine.", "[shake]STOMP![/shake]", "[wave]Shadows... hands...[/wave]", "...cold.", "[tremble]Grrrr...[/tremble]" },
			[NPCID.WallofFlesh] = new[] { "[shake]FEED ME.[/shake]", "[tremble]The world shifts...[/tremble]", "Nowhere to go.", "[wave]Burn...[/wave]", "[shake]HUNGRY![/shake]", "You did this.", "The Guide was a fool.", "[tremble]Hardmode awaits.[/tremble]" },
			[NPCID.QueenSlimeBoss] = new[] { "How dare you!", "[wave]Shimmer~[/wave]", "Crystals, attack!", "Simply DIVINE.", "[shake]Crash![/shake]", "Mind the sparkles.", "My minions adore me.", "[wave]La la la~[/wave]" },
			[NPCID.Retinazer] = new[] { "LOCKED ON.", "[tremble]RECALIBRATING.[/tremble]", "PRECISION.", "[shake]LASER.[/shake]", "TARGET: YOU.", "OPTICS NOMINAL.", "[wave]Scan... scan...[/wave]", "Brother. Focus." },
			[NPCID.Spazmatism] = new[] { "[shake]CHOMP CHOMP![/shake]", "Fire fire fire!", "[wave]Wheee~[/wave]", "Toasty!", "Brother's boring.", "[shake]RAAAH![/shake]", "Cursed flames!", "Catch!" },
			[NPCID.TheDestroyer] = new[] { "[tremble]WHIRRR...[/tremble]", "LENGTH: EXCESSIVE.", "[shake]CRUSH.[/shake]", "PROBES: OUT.", "ALL SEGMENTS: FIRE.", "[wave]Burrowing...[/wave]", "YOU ARE IN THE WAY.", "LASERS: ARMED." },
			[NPCID.SkeletronPrime] = new[] { "[shake]SAW.[/shake]", "Vice grip.", "[tremble]Cannon ready.[/tremble]", "Laser online.", "UPGRADED.", "[shake]SPIN.[/shake]", "Four arms, one goal.", "You lack arms." },
			[NPCID.Plantera] = new[] { "[wave]Bloom...[/wave]", "[shake]CHOMP![/shake]", "Roots run deep.", "The bulb was MINE.", "[tremble]Thorns...[/tremble]", "Feed the jungle.", "[wave]Spores~[/wave]", "Photosynthesis!" },
			[NPCID.Golem] = new[] { "[shake]CRUSH.[/shake]", "[tremble]Rumble.[/tremble]", "Fists. Ready.", "Ancient. Eternal.", "SUN. POWER.", "[shake]PUNCH.[/shake]", "The temple stands.", "[tremble]...[/tremble]" },
			[NPCID.DukeFishron] = new[] { "[wave]Glub glub~[/wave]", "[shake]TYPHOON![/shake]", "Sharknado!", "The ocean is angry!", "Mind the bubbles.", "[shake]ROAR![/shake]", "Fish... and chips?", "[wave]Tides turn...[/wave]" },
			[NPCID.HallowBoss] = new[] { "[wave]Twirl~[/wave]", "Ethereal lances!", "[shake]Radiance.[/shake]", "So bright, so bright.", "[wave]Rainbows~[/wave]", "Shall we dance?", "Pretty, isn't it?", "[tremble]Daylight...[/tremble]" },
			[NPCID.CultistBoss] = new[] { "[tremble]Ph'nglui...[/tremble]", "The moon rises!", "[shake]IMPUDENCE![/shake]", "Lunar flames!", "Mirror, mirror...", "[wave]Chant with me...[/wave]", "The Old One stirs.", "You can't stop it now." },
			[NPCID.MoonLordCore] = new[] { "[shake]INSIGNIFICANT.[/shake]", "[tremble]Behold.[/tremble]", "I see everything.", "[wave]Phantasmal...[/wave]", "Bow, mortal.", "[shake]END.[/shake]", "Your world is mine.", "[tremble]Eyes everywhere.[/tremble]" },
		};

		private static readonly string[] GenericBossBubble = { "Begone!", "[wave]...[/wave]", "You're persistent.", "[shake]HAH![/shake]", "Not bad.", "[tremble]Rrrgh...[/tremble]", "Is that all?", "Come on, then!" };

		// ================================================================== enemy families: turn lines

		private static readonly Dictionary<string, string[]> FamilyFlavor = new()
		{
			["SlimeEnemy"] = new[] {
				"* {0} is trying very hard to look scary.",
				"* {0} jiggles in a threatening manner.",
				"* {0} has a coin stuck in it. Tempting.",
				"* {0} is thinking about hugging you. Aggressively.",
				"* {0} leaves a little slime trail when it moves.",
				"* {0} bounces in place. It's warming up.",
				"* {0} reflects your face, slightly distorted.",
				"* Smells like gel and pond water.",
				"* {0} is unsure if it has a front side.",
				"* {0} gurgles happily.",
			},
			["FighterEnemy"] = new[] {
				"* {0} lumbers closer.",
				"* {0} groans something that might be words.",
				"* {0} is missing a shoe. It hasn't noticed.",
				"* {0} swings its arms. Mostly at air.",
				"* {0} has really committed to this.",
				"* Smells like old dirt.",
				"* {0} stares at you with dead eyes. Rude.",
				"* {0} trips over a rock, recovers, pretends it didn't.",
				"* {0} sniffs. Brains? No, just you.",
				"* {0} looks like it skipped breakfast. And lunch. Forever.",
			},
			["FlierEnemy"] = new[] {
				"* {0} circles overhead.",
				"* {0} flaps lazily.",
				"* {0} dives a little, then thinks better of it.",
				"* The wind carries {0} back and forth.",
				"* {0} is very proud of being up there.",
				"* {0} screeches, mostly for effect.",
				"* {0} looks down on you. Literally.",
				"* A feather (or something) drifts down.",
				"* {0} does a loop.",
				"* {0} hovers just out of reach.",
			},
			["CasterEnemy"] = new[] {
				"* {0} mutters an incantation.",
				"* Magic crackles at {0}'s fingertips.",
				"* {0} flips through a spellbook, looking for the good one.",
				"* {0} is teleporting in place, nervously.",
				"* The air smells like burnt mana.",
				"* {0} raises a hand dramatically. Nothing happens. Yet.",
				"* {0}'s robes billow without any wind.",
				"* {0} is very proud of its hat.",
				"* {0} draws a rune in the air.",
				"* {0} hums an old spell under its breath.",
			},
			["WormEnemy"] = new[] {
				"* {0} burrows a little deeper, then pops back up.",
				"* The ground shifts beneath you.",
				"* {0} is mostly underground. Mostly.",
				"* Dirt rains down as {0} wriggles.",
				"* {0}'s segments ripple.",
				"* {0} tastes the soil. Good vintage.",
				"* You feel a rumble through your feet.",
				"* {0} curls into a knot, then undoes it.",
				"* Smells like worms. Obviously.",
				"* {0} seems to be counting its own segments.",
			},
			["WaterEnemy"] = new[] {
				"* {0} flops around, looking for water.",
				"* {0} blows a bubble.",
				"* Drips of water fall from {0}.",
				"* {0} looks at you like you're bait.",
				"* {0} gulps air dramatically.",
				"* It smells like the ocean. And fish. Mostly fish.",
				"* {0}'s scales shimmer.",
				"* {0} is out of its depth.",
				"* {0} swims in a circle, even on land.",
				"* {0} glubs something insulting.",
			},
			["SpiderEnemy"] = new[] {
				"* {0} taps its many legs impatiently.",
				"* Webs cling to everything.",
				"* {0} spins a little more web, just in case.",
				"* {0} has eight eyes. All of them are on you.",
				"* Something skitters in the shadows.",
				"* {0} rubs its fangs together.",
				"* {0} is hanging upside down. It looks comfortable.",
				"* Smells like dust and silk.",
				"* {0} waits. Patiently. Very patiently.",
				"* A fly is stuck in the web nearby. It waves at you.",
			},
			["MimicEnemy"] = new[] {
				"* {0}'s lid flaps open and shut.",
				"* {0} is pretending to be a chest again. You're not falling for it.",
				"* Coins jingle inside {0}.",
				"* {0} has a very long tongue.",
				"* {0} hops, rattling its loot.",
				"* {0} looks valuable. That's the trick.",
				"* Something glints inside {0}'s mouth.",
				"* {0} snaps its lid. CLACK.",
				"* {0} seems to want you to open it.",
				"* Smells like old wood and greed.",
			},
			["ChargerEnemy"] = new[] {
				"* {0} paws at the ground.",
				"* {0} snorts steam.",
				"* {0} lowers its head.",
				"* {0} is building up speed in its mind.",
				"* {0} has one setting: forward.",
				"* The ground shakes as {0} stamps.",
				"* {0} sizes you up. You look like a target.",
				"* {0} hasn't learned to turn yet.",
				"* {0}'s muscles tense.",
				"* {0} revs up. You can almost hear an engine.",
			},
			["SpiritEnemy"] = new[] {
				"* {0} drifts through a wall, then back.",
				"* The air goes cold around {0}.",
				"* {0} wails softly.",
				"* You can see through {0}. It's a bit embarrassing for both of you.",
				"* {0} remembers something sad.",
				"* Faint whispers come from everywhere.",
				"* {0} flickers in and out of sight.",
				"* {0} looks lonely.",
				"* Smells like old candles.",
				"* {0} hums a song from long ago.",
			},
			["BladeEnemy"] = new[] {
				"* {0} spins in place, humming.",
				"* {0}'s edge catches the light.",
				"* {0} slices the air, for practice.",
				"* {0} is very sharp. It wants you to know.",
				"* {0} rattles in its own invisible sheath.",
				"* {0} points at you. With its whole body.",
				"* Steel sings as {0} moves.",
				"* {0} is polishing itself.",
				"* {0} has a nick in its blade. It's self-conscious about it.",
				"* {0} hovers, perfectly balanced.",
			},
			["SnapperEnemy"] = new[] {
				"* {0}'s jaws click.",
				"* {0} snaps at a passing fly.",
				"* {0} is mostly mouth.",
				"* {0} smiles. Too many teeth.",
				"* {0} drools a little.",
				"* {0} is waiting for you to get closer.",
				"* {0} sways back and forth.",
				"* {0} gnaws on a vine.",
				"* Smells like jungle and teeth.",
				"* {0} chomps, just to stay in practice.",
			},
			["GenericBoss"] = new[] {
				"* {0} looms over the battlefield.",
				"* The ground trembles.",
				"* {0} lets out a roar.",
				"* {0} is not holding back.",
				"* This is a real fight.",
				"* {0}'s shadow covers you.",
				"* The music gets louder.",
				"* {0} is studying your moves.",
			},
			["GenericEnemy"] = new[] {
				"* {0} blocks the way.",
				"* {0} seems determined.",
				"* {0} watches you carefully.",
				"* {0} shifts its weight.",
				"* {0} is waiting for you to make a move.",
				"* {0} looks around, then back at you.",
				"* {0} makes a noise. You can't tell what kind.",
				"* {0} doesn't look like it'll back down.",
				"* {0} is having a weird day too.",
				"* {0} fidgets.",
			},
			// Event armies
			["Army.Goblins"] = new[] {
				"* {0} polishes a spiky ball.", "* {0} yells a goblin war cry. It cracks halfway.", "* {0} checks the map. It's upside down.",
				"* More goblins arrive in the distance.", "* {0} smells like burnt wood.", "* {0} is wearing a helmet three sizes too big.",
			},
			["Army.Pirates"] = new[] {
				"* {0} squints with its good eye.", "* A cannon booms somewhere offshore.", "* {0} is humming a shanty.",
				"* {0} has a parrot. The parrot is judging you.", "* {0} buries a treasure chest. Badly.", "* {0} swigs from a bottle.",
			},
			["Army.FrostLegion"] = new[] {
				"* {0} rolls a snowball the size of your head.", "* {0}'s carrot nose twitches.", "* {0} is melting a bit, but staying strong.",
				"* A cold wind blows.", "* {0} chuckles. It sounds like crunching ice.", "* {0} straightens its scarf.",
			},
			["Army.Martians"] = new[] {
				"* {0} probes the air with a strange device.", "* A saucer hums overhead.", "* {0} writes 'primitive' on a clipboard.",
				"* {0}'s helmet fogs up.", "* {0} chirps in a language of beeps.", "* {0} is far from home and not happy about it.",
			},
			["Army.PumpkinMoon"] = new[] {
				"* {0} grins a carved grin.", "* Leaves skitter across the ground.", "* The Pumpkin Moon hangs heavy and orange.",
				"* {0} smells like pie spice.", "* A crow caws somewhere.", "* {0} creaks like an old scarecrow.",
			},
			["Army.FrostMoon"] = new[] {
				"* {0} jingles with every step.", "* Snowflakes fall in the moonlight.", "* {0} has your name on a list.",
				"* {0} hums a carol, off-key.", "* A present lies on the ground. It's ticking.", "* {0} smells like peppermint.",
			},
			["Army.OldOnesArmy"] = new[] {
				"* The Eternia Crystal glows behind you.", "* {0} marches in step.", "* The Etherian portal crackles.",
				"* {0} checks on the crystal. Still there. Good.", "* {0} grunts an order.", "* Mana hums in the air.",
			},
			["Army.Eclipse"] = new[] {
				"* The sky is dark at noon.", "* {0} growls in the gloom.", "* Something screams in the distance.",
				"* {0} looks like it came out of a movie.", "* The black sun burns overhead.", "* {0} drools.",
			},
		};

		// ================================================================== enemy families: speech bubbles

		private static readonly Dictionary<string, string[]> FamilyBubble = new()
		{
			["SlimeEnemy"] = new[] { "Gloop!", "[wave]Wobble~[/wave]", "Squishy squad!", "[shake]SPLAT![/shake]", "I'm sticky.", "Bouncy bouncy!", "Gel me tender.", "[wave]Bloop bloop~[/wave]" },
			["FighterEnemy"] = new[] { "[shake]RAAAGH.[/shake]", "Brrrains...", "Get... over here...", "[tremble]Mmmhh...[/tremble]", "*thud* *thud*", "You smell alive.", "[wave]Uuuughhh...[/wave]", "Night is ours." },
			["FlierEnemy"] = new[] { "Swoop!", "[wave]Weee~[/wave]", "Can't reach me!", "[shake]SCREECH![/shake]", "Too slow!", "Bombs away!", "Sky's mine.", "*flap flap*" },
			["CasterEnemy"] = new[] { "[wave]Hocus pocus~[/wave]", "Feel the arcane!", "[shake]KABOOM![/shake]", "Blink!", "My spells never miss.", "[tremble]Chaos...[/tremble]", "Bolt!", "Now you see me..." },
			["WormEnemy"] = new[] { "[tremble]*rumble rumble*[/tremble]", "Down below!", "Dirt is delicious.", "[shake]BURST![/shake]", "Tunnel time.", "Wriggle!", "Up you go!", "Mmm, minerals." },
			["WaterEnemy"] = new[] { "Glub!", "[wave]Swish~[/wave]", "Wet you!", "Snap snap!", "Fish fight!", "[shake]SPLOOSH![/shake]", "Bubbles!", "Water's fine!" },
			["SpiderEnemy"] = new[] { "[tremble]Skitter...[/tremble]", "Web time.", "You're stuck now.", "[shake]BITE![/shake]", "Eight eyes on you.", "Come into my parlour.", "*click click*", "Tangled!" },
			["MimicEnemy"] = new[] { "Open me!", "[shake]CHOMP![/shake]", "Ha! Fooled you!", "Free loot! (lie)", "*rattle rattle*", "Treasure tastes you.", "[wave]Hop hop~[/wave]", "Lid's open!" },
			["ChargerEnemy"] = new[] { "[shake]RAMMING SPEED![/shake]", "Move!", "*stomp stomp*", "Here I go!", "Brakes? What brakes?", "[tremble]Rrrrr...[/tremble]", "Head-first!", "Can't stop!" },
			["SpiritEnemy"] = new[] { "[wave]Boo...[/wave]", "[tremble]Who's there?[/tremble]", "Join us...", "I was like you once.", "[wave]Float...[/wave]", "So lonely...", "[shake]LEAVE![/shake]", "Do you remember?" },
			["BladeEnemy"] = new[] { "[shake]SLASH![/shake]", "Cut!", "Edge lord.", "Parry this!", "*shiiing*", "Sharp wit too.", "[wave]Whirl~[/wave]", "Steel yourself." },
			["SnapperEnemy"] = new[] { "Nom!", "[shake]SNAP SNAP![/shake]", "Lunch!", "Teeth teeth teeth.", "Open wide!", "[tremble]Grrr...[/tremble]", "Dinner time.", "*gnash*" },
			["GenericEnemy"] = new[] { "Hey!", "Watch it.", "[shake]HAH![/shake]", "Over here!", "Try me.", "[wave]...[/wave]", "My turn!", "Back off!" },
			["Army.Goblins"] = new[] { "For the horde!", "[shake]WAAAGH![/shake]", "Shinies!", "Stab stab!", "Charge!", "[tremble]Hehehe...[/tremble]" },
			["Army.Pirates"] = new[] { "Arrr!", "[wave]Yo ho ho~[/wave]", "Shiver me timbers!", "[shake]FIRE THE CANNONS![/shake]", "Gold!", "Walk the plank!" },
			["Army.FrostLegion"] = new[] { "Snow way!", "[shake]FREEZE![/shake]", "Chill out.", "Ice to meet you.", "[wave]Brrr~[/wave]", "Cold as ice." },
			["Army.Martians"] = new[] { "[tremble]Bzzt.[/tremble]", "Specimen located.", "Beam it up.", "[shake]ZAP![/shake]", "Primitive!", "Probe initiated." },
			["Army.PumpkinMoon"] = new[] { "[wave]Ooooo~[/wave]", "Trick or treat!", "[shake]HAHAHA![/shake]", "Carve you up!", "Spooky!", "[tremble]Hollow...[/tremble]" },
			["Army.FrostMoon"] = new[] { "Ho ho HO!", "[shake]NAUGHTY![/shake]", "Jingle!", "[wave]Fa la la~[/wave]", "Coal for you!", "Presents!" },
			["Army.OldOnesArmy"] = new[] { "The crystal!", "[shake]FOR ETHERIA![/shake]", "March!", "Next wave!", "Hold the line!", "[tremble]Portal opens...[/tremble]" },
			["Army.Eclipse"] = new[] { "[shake]RAAAAH![/shake]", "[tremble]Darkness...[/tremble]", "No sun!", "Scream!", "[wave]Eclipse~[/wave]", "You're in a horror movie." },
		};

		// ================================================================== lookup

		private static string Key(Encounter e) => e switch
		{
			ArmyEnemy a => "Army." + a.Kind,
			_ => e.GetType().Name,
		};

		private static string[] Extra(Encounter e, Dictionary<int, string[]> byType, Dictionary<string, string[]> byFamily)
		{
			if (byType != null && e.Npc != null && byType.TryGetValue(e.Npc.type, out string[] t))
				return t;
			if (byFamily != null && byFamily.TryGetValue(Key(e), out string[] f))
				return f;
			return null;
		}

		/// <summary>
		/// A turn line: the encounter's own lines, then the extras, all the way through before any repeats. The own
		/// lines come first so the opening turns read the same as before.
		/// </summary>
		public static string Flavor(Encounter e, string[] own, int turn)
		{
			string[] extra = Extra(e, BossFlavor, FamilyFlavor);
			string[] all = Merge(own, extra);
			return all.Length == 0 ? "" : all[Index(turn, all.Length)];
		}

		/// <summary>A speech bubble: the encounter's own lines and the extras.</summary>
		public static string Bubble(Encounter e, string[] own, int turn)
		{
			string[] extra = Extra(e, BossBubble, FamilyBubble);
			string[] all = Merge(own, extra);
			return all.Length == 0 ? null : all[Index(turn, all.Length)];
		}

		/// <summary>A boss with no bubbles of its own: the generic boss ones plus the general extras.</summary>
		public static string[] GenericBossBubbles => GenericBossBubble;

		public static string[] BossBubbles(int type) => BossBubble.TryGetValue(type, out string[] b) ? b : null;

		private static string[] Merge(string[] own, string[] extra)
		{
			if (extra == null || extra.Length == 0)
				return own ?? Array.Empty<string>();
			if (own == null || own.Length == 0)
				return extra;
			return own.Concat(extra.Where(x => !own.Contains(x))).ToArray();
		}

		private static int Index(int turn, int length) => ((turn % length) + length) % length;
	}
}
