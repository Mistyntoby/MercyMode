using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle.Net
{
	/// <summary>
	/// Player against player. A PvP hit opens a challenge for both players; when both press the challenge key the
	/// server starts a duel. From then on the server only passes messages between the two (each client runs its own
	/// battle screen against the other player) and tells one when the other is gone.
	/// </summary>
	public static partial class BattleNet
	{
		/// <summary>Everything the two duelists tell each other.</summary>
		public enum DuelKind : byte
		{
			Hp, // my HP, max HP, defense
			Hit, // my FIGHT landed on you: damage, crit
			Soul, // my SOUL in the box (for the one building the attack)
			BoxOpen, // I'm in the box now: build your attack
			Place, // an attack piece placed
			BuildDone, // the builder is finished
			YourTurn, // my box closed: your turn to choose
			Text, // a line of my text box
			Spared, // I spared you
			Died, // my SOUL broke
			Hurt, // a bullet hit my SOUL for this much (the builder sees the number and the flinch)
			MercyAsk, // I used a MERCY act on you: accept or refuse (its text)
			MercyAnswer, // my answer to your MERCY act
			Pose, // how I stand and what I hold (so you see me pick and ready a weapon)
			Fire, // I swung or shot: item, projectile, where at (x -1: at you)
			ForceSoul, // the builder changed the dodger's SOUL mode
			BeamState, // my held beam in FIGHT: on, charge
		}

		/// <summary>How long a challenge stays open after the hit (refreshed by more hits).</summary>
		public const int ChallengeTicks = 10 * 60;
		/// <summary>Both players have to be this close for a challenge to count.</summary>
		public const float DuelRange = 50 * 16;

		// ================================================================== server

		private sealed class Challenge
		{
			/// <summary>Each challenge's own number: a client's "I pressed" belongs to one, never to the next.</summary>
			public int Id;
			public int A, B;
			public bool PressedA, PressedB;
			public uint Expires;
			public bool Has(int p) => A == p || B == p;
			public int Count => (PressedA ? 1 : 0) + (PressedB ? 1 : 0);
		}

		private static readonly List<Challenge> challenges = new();
		private static int nextChallengeId = 1;
		/// <summary>Server: who is dueling whom (both directions).</summary>
		private static readonly Dictionary<int, int> duelPartner = new();

		public static bool InDuelServer(int player) => duelPartner.ContainsKey(player);

		private static bool CanDuel(int p) => p >= 0 && p < Main.maxPlayers && Main.player[p].active && !Main.player[p].dead
			&& !InBattle(p) && !duelPartner.ContainsKey(p);

		private static void HandleDuelServer(Msg msg, BinaryReader r, int from)
		{
			switch (msg)
			{
				case Msg.DuelOffer:
				{
					int attacker = r.ReadByte();
					ServerOffer(attacker, from);
					break;
				}
				case Msg.DuelPress:
				{
					int other = r.ReadByte();
					ServerPress(from, other);
					break;
				}
				case Msg.DuelRelay:
				{
					byte kind = r.ReadByte();
					int len = r.ReadUInt16();
					byte[] data = r.ReadBytes(len);
					if (!duelPartner.TryGetValue(from, out int to))
						break;
					ModPacket p = Packet(Msg.DuelRelay);
					p.Write(kind);
					p.Write((ushort)data.Length);
					p.Write(data);
					ToPlayer(p, to, Msg.DuelRelay);
					break;
				}
				case Msg.DuelEnd:
					ServerEndDuel(from);
					break;
			}
		}

		private static void ServerOffer(int attacker, int victim)
		{
			if (attacker == victim || !CanDuel(attacker) || !CanDuel(victim)
				|| Main.player[attacker].DistanceSQ(Main.player[victim].Center) > DuelRange * DuelRange)
				return;
			Challenge c = challenges.FirstOrDefault(x => x.Has(attacker) && x.Has(victim));
			if (c == null)
			{
				// One challenge per player at a time: a newer one replaces theirs
				challenges.RemoveAll(x => x.Has(attacker) || x.Has(victim));
				c = new Challenge { Id = nextChallengeId++, A = attacker, B = victim };
				challenges.Add(c);
			}
			c.Expires = Main.GameUpdateCount + ChallengeTicks;
			SendChallenge(c);
		}

		private static void ServerPress(int player, int other)
		{
			Challenge c = challenges.FirstOrDefault(x => x.Has(player) && x.Has(other) && x.Expires > Main.GameUpdateCount);
			if (c == null)
				return;
			if (c.A == player)
				c.PressedA = true;
			else
				c.PressedB = true;
			// Pressing keeps it open a little longer for the other one
			c.Expires = Math.Max(c.Expires, Main.GameUpdateCount + 5 * 60);
			if (c.Count < 2)
			{
				SendChallenge(c);
				return;
			}
			challenges.Remove(c);
			// Both pressed the key from outside any battle, so a battle or duel the server still lists them in is
			// left over (one ended without the server hearing): clear it rather than fail without a word
			foreach (int p in new[] { c.A, c.B })
			{
				DropStale(p);
				if (duelPartner.ContainsKey(p))
					ServerEndDuel(p);
			}
			if (!CanDuel(c.A) || !CanDuel(c.B))
			{
				// Still can't (one died, left...): close both prompts
				c.Expires = Main.GameUpdateCount;
				SendChallenge(c);
				return;
			}
			duelPartner[c.A] = c.B;
			duelPartner[c.B] = c.A;
			// The one who was hit goes first
			SendDuelStart(c.A, c.B, first: false);
			SendDuelStart(c.B, c.A, first: true);
			ModContent.GetInstance<MercyMode>().Logger.Info($"Duel: {Main.player[c.A].name} vs {Main.player[c.B].name}");
		}

		private static void SendChallenge(Challenge c)
		{
			foreach (var (me, other, pressed) in new[] { (c.A, c.B, c.PressedA), (c.B, c.A, c.PressedB) })
			{
				ModPacket p = Packet(Msg.ChallengeState);
				p.Write(c.Id);
				p.Write((byte)other);
				p.Write((byte)c.Count);
				p.Write(pressed);
				p.Write((int)Math.Max(0, c.Expires - Main.GameUpdateCount));
				ToPlayer(p, me, Msg.ChallengeState);
			}
		}

		private static void SendDuelStart(int to, int opponent, bool first)
		{
			ModPacket p = Packet(Msg.DuelStart);
			p.Write((byte)opponent);
			p.Write(first);
			ToPlayer(p, to, Msg.DuelStart);
		}

		private static void ServerEndDuel(int player)
		{
			if (!duelPartner.TryGetValue(player, out int other))
				return;
			duelPartner.Remove(player);
			if (duelPartner.TryGetValue(other, out int back) && back == player)
				duelPartner.Remove(other);
			ModPacket p = Packet(Msg.DuelEnd);
			ToPlayer(p, other, Msg.DuelEnd);
		}

		private static void ServerDuelDisconnect(int player)
		{
			challenges.RemoveAll(c => c.Has(player));
			ServerEndDuel(player);
		}

		private static void ServerDuelUpdate()
		{
			challenges.RemoveAll(c => c.Expires <= Main.GameUpdateCount || !Main.player[c.A].active || !Main.player[c.B].active);
			// A duelist who vanished without a disconnect (shouldn't happen, but never leave the other stuck)
			foreach (int p in duelPartner.Keys.ToList())
				if (!Main.player[p].active)
					ServerEndDuel(p);
		}

		// ================================================================== client

		/// <summary>The open challenge: with whom, how many pressed, whether we did, until when (-1: none).</summary>
		public static int ChallengeWith = -1;
		public static int ChallengeId;
		public static int ChallengeCount;
		public static bool ChallengePressed;
		public static uint ChallengeUntil;

		public static bool ChallengeOpen => Online && ChallengeWith >= 0 && Main.GameUpdateCount < ChallengeUntil
			&& Main.player[ChallengeWith].active;

		/// <summary>The other player hit us with PvP on: open a challenge for both.</summary>
		public static void SendDuelOffer(int attacker)
		{
			if (!Online || attacker < 0 || attacker >= Main.maxPlayers)
				return;
			ModPacket p = Packet(Msg.DuelOffer);
			p.Write((byte)attacker);
			ToServer(p);
		}

		public static void SendDuelPress()
		{
			if (!ChallengeOpen || ChallengePressed)
				return;
			ChallengePressed = true;
			ChallengeCount = Math.Min(2, ChallengeCount + 1);
			ModPacket p = Packet(Msg.DuelPress);
			p.Write((byte)ChallengeWith);
			ToServer(p);
		}

		public static void SendDuelEnd()
		{
			if (!Online)
				return;
			ToServer(Packet(Msg.DuelEnd));
		}

		/// <summary>Sends something to the duel opponent (through the server).</summary>
		public static void SendDuel(DuelKind kind, Action<BinaryWriter> write = null)
		{
			if (!Online)
				return;
			using var ms = new MemoryStream();
			using (var w = new BinaryWriter(ms))
				write?.Invoke(w);
			byte[] data = ms.ToArray();
			ModPacket p = Packet(Msg.DuelRelay);
			p.Write((byte)kind);
			p.Write((ushort)data.Length);
			p.Write(data);
			ToServer(p);
		}

		private static void HandleDuelClient(Msg msg, BinaryReader r)
		{
			BattleSystem battle = BattleSystem.Instance;
			switch (msg)
			{
				case Msg.ChallengeState:
				{
					int id = r.ReadInt32();
					int other = r.ReadByte();
					int count = r.ReadByte();
					bool pressed = r.ReadBoolean();
					int ticks = r.ReadInt32();
					if (BattleSystem.Active)
						break;
					// A new challenge (even with the same player): nothing pressed yet, whatever the last one had.
					// The same one: the server's word, plus a press of ours it may not have heard yet
					bool same = id == ChallengeId;
					ChallengePressed = pressed || same && ChallengePressed;
					ChallengeId = id;
					ChallengeWith = other;
					ChallengeCount = Math.Max(count, ChallengePressed && !pressed ? Math.Min(2, count + 1) : count);
					ChallengeUntil = Main.GameUpdateCount + (uint)ticks;
					break;
				}
				case Msg.DuelStart:
				{
					int opponent = r.ReadByte();
					bool first = r.ReadBoolean();
					ChallengeWith = -1;
					ChallengePressed = false;
					if (battle == null || !battle.StartDuel(opponent, first))
						SendDuelEnd();
					break;
				}
				case Msg.DuelRelay:
				{
					var kind = (DuelKind)r.ReadByte();
					int len = r.ReadUInt16();
					byte[] data = r.ReadBytes(len);
					using var br = new BinaryReader(new MemoryStream(data));
					battle?.OnDuel(kind, br);
					break;
				}
				case Msg.DuelEnd:
					battle?.OnDuelEnd();
					break;
			}
		}
	}
}
