using System.Collections.Generic;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== talkers and their questions
	// Some enemies don't use speech bubbles: like Rouxls Kaard, they hold forth in the text box at the bottom (with
	// their face beside it) as their turn starts, and now and then they ask you something. Alone you answer; in a
	// party everyone votes and the most votes win (a tie is decided at random).

	/// <summary>One answer: what it's called, what the talker says back, and what it does.</summary>
	public sealed class TalkAnswer
	{
		public string Option;
		/// <summary>The talker's reply, one text box each.</summary>
		public string[] Reply = System.Array.Empty<string>();
		/// <summary>MERCY gained (or lost, if negative).</summary>
		public float Mercy;
		/// <summary>It gets so caught up in your answer it forgets to attack this turn.</summary>
		public bool Skip;
		/// <summary>This turn's bullets hit this much harder (or softer).</summary>
		public float DamageMult = 1f;
		/// <summary>HP restored to everyone who heard it.</summary>
		public int Heal;
	}

	public sealed class TalkQuestion
	{
		public string Prompt;
		/// <summary>Two to four answers.</summary>
		public TalkAnswer[] Answers;
	}

	/// <summary>What a talker says as its turn starts, and the question it may end on.</summary>
	public sealed class TalkTurn
	{
		public string[] Lines = System.Array.Empty<string>();
		public TalkQuestion Question;
	}

	/// <summary>An enemy turn that does nothing (the talker forgot to attack).</summary>
	public sealed class QuietAttack : EnemyAttack
	{
		public QuietAttack() => Duration = 40;
		public override void Update(BattleSystem battle, int tick) { }
	}

	/// <summary>The talkers' scripts, by NPC type. Each turn takes the next entry, round and round.</summary>
	public static class Talkers
	{
		private static TalkAnswer A(string option, string reply, float mercy = 0f, bool skip = false, float damage = 1f, int heal = 0)
			=> new() { Option = option, Reply = reply.Split('|'), Mercy = mercy, Skip = skip, DamageMult = damage, Heal = heal };

		private static TalkTurn Say(params string[] lines) => new() { Lines = lines };

		private static TalkTurn Ask(string[] lines, string prompt, params TalkAnswer[] answers)
			=> new() { Lines = lines, Question = new TalkQuestion { Prompt = prompt, Answers = answers } };

		private static readonly Dictionary<int, TalkTurn[]> Scripts = new()
		{
			// Tim: a wizard whose confidence outpaces his magic
			[NPCID.Tim] = new[]
			{
				Say("Behold! It is I, TIM!", "Wizard of the Caverns! Master of the Arcane! Owner of a VERY good hat!"),
				Ask(new[] { "Before I smite thee, a question.", "Thou mayest answer honestly. I will know if thou liest. Probably." },
					"* Is TIM's hat the best hat you've ever seen?",
					A("Yes", "HAHA! Correct! Thou hast taste!|For that, I shall smite thee... gently.", mercy: 30f, damage: 0.7f),
					A("No", "...|Thou hast wounded TIM. TIM will now wound thee BACK.", damage: 1.4f),
					A("What hat?", "THE HAT ON MY HEAD, FOOL!|It's... it's right there... why does no one notice the hat...", mercy: 15f, skip: true)),
				Say("Feel my magic! Feast thine eyes on these bolts!", "Each one hand-crafted. With my hands. That I have. Because I'm a wizard."),
				Ask(new[] { "Riddle me this, adventurer!" },
					"* What has roots nobody sees, is taller than trees, up up it goes, yet never grows?",
					A("A mountain", "WHAT. How did thou know that.|I have been working on that riddle for SIX YEARS.", mercy: 35f),
					A("Tim", "Ha! Flattery! I am indeed very tall. In spirit.", mercy: 20f),
					A("A tree", "It literally says TALLER THAN TREES in the riddle!", damage: 1.25f),
					A("Pass", "Coward! ...Wise coward. Fine.", mercy: 5f)),
				Say("Thou art still standing? Most irregular.", "Usually people faint at the sight of TIM. Or at least sigh."),
			},
			// The Clown: everything's a bit, the bombs are not
			[NPCID.Clown] = new[]
			{
				Say("HONK HONK!", "Welcome to the show, kiddo! You're the audience AND the target!"),
				Ask(new[] { "Ok, ok, crowd work time.", "Knock knock!" },
					"* (The clown is waiting.)",
					A("Who's there?", "Boom.|...Boom who?|BOOM! HAHAHAHA!", damage: 1.2f),
					A("Not again", "Tough crowd! TOUGH CROWD!|Alright, I'll go easy. Like my material.", mercy: 15f, damage: 0.8f),
					A("Laugh anyway", "Oh, you're GOOD. A pity laugh! My favourite kind!|I'm gonna remember this, kid.", mercy: 35f)),
				Say("You ever notice how bombs are just... loud presents?", "Here, have some presents!"),
				Ask(new[] { "Quick question for my career." },
					"* Is the clown funny?",
					A("Very", "AW SHUCKS. I'm putting you in my will. I don't have a will. I have bombs.", mercy: 30f, skip: true),
					A("No", "The clown is going to cry now. The clown cries bombs.", damage: 1.4f),
					A("Kind of", "Kind of! That's the nicest thing anyone's said since clown college!", mercy: 20f)),
			},
			// The Pirate Captain: theatrical, by the book (the book is the Pirate Code)
			[NPCID.PirateCaptain] = new[]
			{
				Say("AVAST! Ye stand before Captain of the Fleet!", "Hand over yer doubloons, or I'll have ye swabbin' the decks of Davy Jones!"),
				Ask(new[] { "But the Code says I must give ye a choice." },
					"* What will you give the Captain?",
					A("Gold", "Arr! A sensible landlubber!|...this be a COPPER coin.|I'll allow it.", mercy: 30f),
					A("A song", "A SHANTY? Me favourite!|*The Captain sings along, badly*", mercy: 25f, skip: true, heal: 8),
					A("Nothing", "Then I'll take it with CANNONBALLS!", damage: 1.4f),
					A("Parley", "Parley, eh? The Code demands I listen.|...I've listened. Still attackin'. Gently.", mercy: 15f, damage: 0.8f)),
				Say("Ye fight well for someone without a ship!", "Ye ever considered a career in piracy? Benefits include: scurvy."),
				Ask(new[] { "Settle a debate among me crew." },
					"* Which is the best part of a ship?",
					A("The cannons", "AYE! Ye've a fine eye for destruction!", mercy: 20f, damage: 1.15f),
					A("The parrot", "Polly IS the best part. Don't tell the cannons.", mercy: 30f),
					A("The plank", "...ye've been on the wrong ships, mate.", mercy: 10f)),
			},
			// The Goblin Sorcerer: a self-important academic of the goblin army
			[NPCID.GoblinSorcerer] = new[]
			{
				Say("Ah. A test subject.", "I am the army's foremost sorcerer. My thesis on Chaos Balls was very well received."),
				Ask(new[] { "Let us test your intellect." },
					"* What is the correct term for a ball of chaos?",
					A("Chaos ball", "Correct! ...wait, that's just the name. Hmph. Partial marks.", mercy: 15f),
					A("Spicy marble", "SPICY... MARBLE?|...I'm writing that down. That's going in the paper.", mercy: 35f, skip: true),
					A("Rock", "A ROCK. You think I studied for EIGHT YEARS to throw ROCKS?", damage: 1.35f)),
				Say("Teleportation is simple, really.", "You simply stop being here and start being there. Peasants make it look hard."),
			},
			// The Nymph (the Lost Girl, unmasked): sweet words, sharp teeth
			[NPCID.Nymph] = new[]
			{
				Say("Oh, you came back for me...", "That's so sweet. Hold still."),
				Ask(new[] { "Tell me something, traveller." },
					"* Do you think I'm pretty?",
					A("Yes", "Oh, you flatterer...|That won't save you. But it was nice.", mercy: 20f, damage: 0.85f),
					A("Terrifying", "Thank you! I've been practising.", mercy: 30f),
					A("Run", "Run? From little old me? Rude.", damage: 1.3f)),
			},
		};

		/// <summary>This enemy's script for this turn, or null if it's not a talker.</summary>
		public static TalkTurn For(int npcType, int turn)
		{
			if (!Scripts.TryGetValue(npcType, out TalkTurn[] turns) || turns.Length == 0)
				return null;
			return turns[((turn % turns.Length) + turns.Length) % turns.Length];
		}

		public static bool IsTalker(int npcType) => Scripts.ContainsKey(npcType);
	}
}
