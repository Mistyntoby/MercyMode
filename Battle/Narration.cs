using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MercyMode.Battle
{
	/// <summary>
	/// Battle lines are written to the player ("* You tell it a joke."). Deltarune names the character instead ("* Kris
	/// told a joke"), and in a party everyone reads them: "* You toss it a coin." becomes "* ALEX tosses it a coin."
	/// </summary>
	public static class Narration
	{
		private static readonly Dictionary<string, string> Irregular = new()
		{
			["are"] = "is", ["have"] = "has", ["do"] = "does", ["go"] = "goes", ["don't"] = "doesn't", ["aren't"] = "isn't",
			["haven't"] = "hasn't", ["were"] = "was", ["can"] = "can", ["can't"] = "can't", ["could"] = "could",
			["will"] = "will", ["won't"] = "won't", ["would"] = "would", ["should"] = "should", ["might"] = "might",
			["must"] = "must", ["may"] = "may", ["also"] = "also", ["just"] = "just", ["still"] = "still", ["really"] = "really",
		};

		/// <summary>"tell" → "tells", "toss" → "tosses", "try" → "tries"; past tense and the like stay.</summary>
		private static string Conjugate(string verb)
		{
			string lower = verb.ToLowerInvariant();
			if (Irregular.TryGetValue(lower, out string v))
				return v;
			if (lower.EndsWith("ed") || lower.EndsWith("s") && !lower.EndsWith("ss"))
				return verb;
			if (Regex.IsMatch(lower, "(ss|sh|ch|x|z|o)$"))
				return verb + "es";
			if (Regex.IsMatch(lower, "[^aeiou]y$"))
				return verb.Substring(0, verb.Length - 1) + "ies";
			return verb + "s";
		}

		public static string ThirdPerson(string line, string name)
		{
			if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(name) || !Regex.IsMatch(line, @"\b[Yy]ou"))
				return line;
			string s = line;
			s = Regex.Replace(s, @"\b[Yy]ou're\b", name + " is");
			s = Regex.Replace(s, @"\b[Yy]ou've\b", name + " has");
			s = Regex.Replace(s, @"\b[Yy]ou'll\b", name + " will");
			s = Regex.Replace(s, @"\b[Yy]ou'd\b", name + " would");
			s = Regex.Replace(s, @"\b[Yy]ourself\b", "themself");
			s = Regex.Replace(s, @"\b[Yy]ours\b", name + "'s");
			s = Regex.Replace(s, @"\b[Yy]our\b", name + "'s");
			// "You" doing something: the verb right after it agrees with the name
			s = Regex.Replace(s, @"\bYou (\w+(?:'t)?)", m => name + " " + Conjugate(m.Groups[1].Value));
			// Anywhere else (an object: "it waves at you")
			s = Regex.Replace(s, @"\b[Yy]ou\b", name);
			return s;
		}
	}
}
