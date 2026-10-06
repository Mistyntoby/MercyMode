using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace MercyMode.Battle.Net
{
	/// <summary>
	/// A talker's question in a party: everyone answers on their own screen, the server counts the votes once all of
	/// the party has answered (or after <see cref="VoteTimeoutTicks"/>), and the most votes win. A tie is decided at
	/// random between the tied answers. Everyone then sees the same result.
	/// </summary>
	public static partial class BattleNet
	{
		public const int VoteTimeoutTicks = 20 * 60;

		/// <summary>Client: our answer to the question asked this round.</summary>
		public static void SendVote(int round, int choice, int options)
		{
			if (!InParty)
				return;
			ModPacket p = Packet(Msg.Vote);
			p.Write(MyBattle);
			p.Write(round);
			p.Write((byte)choice);
			p.Write((byte)options);
			ToServer(p);
		}

		internal static void ServerVote(int from, int id, int round, int choice, int options)
		{
			NetBattle b = Find(id);
			if (b == null || !b.Players.Contains(from) || options < 1 || choice < 0 || choice >= options)
				return;
			// Answering a question that's already settled: they get the result again
			if (round == b.SettledRound)
			{
				SendVoteResult(b, b.SettledChoice, false, new int[options], only: from);
				return;
			}
			if (round != b.VoteRound)
			{
				b.VoteRound = round;
				b.VoteOptions = options;
				b.VoteTicks = 0;
				b.Votes.Clear();
			}
			b.Votes[from] = choice;
			if (b.Players.All(b.Votes.ContainsKey))
				SettleVote(b);
		}

		/// <summary>Counts the votes: the most wins; a tie goes to one of the tied answers at random.</summary>
		private static void SettleVote(NetBattle b)
		{
			if (b.VoteRound < 0)
				return;
			var counts = new int[b.VoteOptions];
			foreach (int c in b.Votes.Values)
				if (c >= 0 && c < counts.Length)
					counts[c]++;
			int most = counts.Max();
			List<int> top = Enumerable.Range(0, counts.Length).Where(i => counts[i] == most).ToList();
			bool tie = top.Count > 1;
			int choice = tie ? top[Main.rand.Next(top.Count)] : top[0];
			b.SettledRound = b.VoteRound;
			b.SettledChoice = choice;
			b.VoteRound = -1;
			b.Votes.Clear();
			SendVoteResult(b, choice, tie, counts);
		}

		private static void SendVoteResult(NetBattle b, int choice, bool tie, int[] counts, int only = -1)
		{
			int round = b.SettledRound;
			ModPacket Make()
			{
				ModPacket p = Packet(Msg.VoteResult);
				p.Write(round);
				p.Write((byte)choice);
				p.Write(tie);
				p.Write((byte)counts.Length);
				foreach (int c in counts)
					p.Write((byte)c);
				return p;
			}
			if (only >= 0)
				ToPlayer(Make(), only, Msg.VoteResult);
			else
				ToParty(b, Make, Msg.VoteResult);
		}

		/// <summary>Lab: the settled answer of battle <paramref name="id"/> (-1: none yet).</summary>
		internal static int LabSettled(int id) => Find(id)?.SettledRound >= 0 ? Find(id).SettledChoice : -1;
	}
}
