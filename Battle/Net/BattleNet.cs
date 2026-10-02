using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode.Battle.Net
{
	/// <summary>
	/// Multiplayer battles. Each player runs the battle screen on their own client; the server runs the round:
	/// <list type="number">
	/// <item>Choosing: everyone picks an action (nothing happens yet; "* Waiting for ...").</item>
	/// <item>Acting: one player at a time, in party order, carries out their action while the others watch.</item>
	/// <item>Enemy turn: everyone enters the bullet box at once, with the same attack (same random seed) and each
	/// other's SOULs shown in their colours.</item>
	/// </list>
	/// The server also owns who is in which battle (a party of up to <see cref="MaxParty"/>), which NPCs are frozen
	/// (only the battle's), MERCY, spares (loot drops on the server) and broken boss parts. Players who press the join
	/// key near a battle watch until its next bullet box, then fight. Enemy HP needs nothing extra: FIGHT uses
	/// <see cref="NPC.SimpleStrikeNPC"/>, which Terraria syncs.
	/// </summary>
	public static class BattleNet
	{
		private enum Msg : byte
		{
			RequestBattle, // c→s: roots of the enemies this client wants to fight
			RequestJoin, // c→s: join the battle this NPC is in (join key)
			JoinBattle, // s→c: open the battle screen (id, spectating, roots + MERCY)
			Party, // s→party: players fighting and players waiting to jump in
			Frozen, // s→all: these NPCs belong to battle id
			Unfrozen, // s→all: battle id is over
			Ready, // c→s: picked an action (face icon)
			ReadyState, // s→party: a member is ready
			TurnOf, // s→party: this player carries out their action now
			ActionDone, // c→s: finished my action
			PartyText, // c→s→party: a line of the acting player's text box
			BeginEnemyTurn, // s→party: bullet box (seed, round)
			AddMercy, // c→s: MERCY changed by this much
			SetMercy, // s→all: an enemy's MERCY now
			RequestSpare, // c→s
			Spared, // s→party
			KillMembers, // c→s: a boss's core broke
			PartyHit, // c→s→all: a FIGHT hit landed (party: damage number; outsiders: the swing)
			SoulPos, // c→s→party: my SOUL in the box
			Left, // c→s
			BattleState, // s→all: a battle's stage and players (outsiders' view, join prompt)
			PlayerColor, // both ways: a player's party colour choice
			PartyHp, // c→s→party: my HP in the battle (Terraria's own sync drifts: other clients guess regen)
		}

		/// <summary>Over: every enemy is gone (won); nobody can join and it isn't shown as a battle any more.</summary>
		public enum Stage : byte { Choosing, Acting, EnemyTurn, Over }

		public const int MaxParty = 3;
		/// <summary>How close (pixels) players must be to the one starting the battle to be pulled in with them.</summary>
		public const float JoinRange = 7 * 16;
		/// <summary>How close to a battle's enemy or fighter the join prompt shows up.</summary>
		public const float PromptRange = 8 * 16;
		/// <summary>Picking an action / carrying it out: after this long the party goes on without them.</summary>
		public const int ChooseTimeoutTicks = 60 * 60, ActTimeoutTicks = 45 * 60;

		public static bool Online => Main.netMode == NetmodeID.MultiplayerClient;
		/// <summary>The server of a real multiplayer game (the headless lab runs as a server too, but singleplayer).</summary>
		public static bool IsServer => Main.netMode == NetmodeID.Server && !Lab.LabSystem.Enabled;

		// ================================================================== shared: frozen NPCs, battles as seen from outside

		/// <summary>NPC index → (battle id, type when frozen). Kept on the server and mirrored on every client.</summary>
		private static readonly Dictionary<int, (int Battle, int Type)> frozen = new();

		public static bool IsFrozen(NPC npc)
		{
			if (frozen.Count == 0)
				return false;
			if (frozen.TryGetValue(npc.whoAmI, out var f) && f.Type == npc.type)
				return true;
			NPC root = MercyMode.Root(npc);
			return root != npc && frozen.TryGetValue(root.whoAmI, out f) && f.Type == root.type;
		}

		public static int BattleOf(NPC npc) => frozen.TryGetValue(MercyMode.Root(npc).whoAmI, out var f) ? f.Battle
			: frozen.TryGetValue(npc.whoAmI, out f) ? f.Battle : -1;

		/// <summary>A battle as every client sees it: its stage and players (for the join prompt and the outside view).</summary>
		public sealed class WorldBattle
		{
			public Stage Stage;
			public List<int> Players = new();
			public uint StageTick;
		}

		public static readonly Dictionary<int, WorldBattle> WorldBattles = new();

		/// <summary>The NPCs of a battle that are still around.</summary>
		public static IEnumerable<NPC> NpcsOf(int battle) => frozen.Where(f => f.Value.Battle == battle)
			.Where(f => Main.npc[f.Key].active && Main.npc[f.Key].type == f.Value.Type).Select(f => Main.npc[f.Key]);

		// ================================================================== client state

		public static int MyBattle { get; private set; } = -1;
		/// <summary>The party, in order: fighting players, then those waiting to jump in.</summary>
		public static readonly List<int> Party = new();
		/// <summary>Party members still watching until the next bullet box.</summary>
		public static readonly HashSet<int> Joining = new();
		/// <summary>Party members who have picked their action this round, with the command's face icon.</summary>
		public static readonly Dictionary<int, int> ReadyFaces = new();
		/// <summary>The player carrying out their action right now (-1: none).</summary>
		public static readonly HashSet<int> ActingNow = new();
		/// <summary>Players fighting right now, in order (their FIGHT bars stack in this order).</summary>
		public static readonly List<int> FightingNow = new();
		private static uint lastRequestTick;

		public static IEnumerable<Player> Allies => Party.Where(i => i != Main.myPlayer && i >= 0 && i < Main.maxPlayers && Main.player[i].active).Select(i => Main.player[i]);
		public static int MyPartyIndex => Math.Max(0, Party.IndexOf(Main.myPlayer));
		public static int PartySize => Math.Max(1, Party.Count);
		/// <summary>Fighting players who haven't picked yet, by name.</summary>
		public static IEnumerable<string> WaitingOn => Allies.Where(p => !Joining.Contains(p.whoAmI) && !ReadyFaces.ContainsKey(p.whoAmI)).Select(p => p.name);
		public static bool InParty => Online && MyBattle >= 0;

		public static bool RequestPending => Main.GameUpdateCount - lastRequestTick < 30 && lastRequestTick != 0;

		/// <summary>Other party members' SOULs in the box: position, SOUL mode, when last heard.</summary>
		public static readonly Dictionary<int, (Vector2 Pos, byte Mode, uint Tick)> AllySouls = new();
		/// <summary>Each ally's HP as they last reported it from their battle.</summary>
		public static readonly Dictionary<int, (int Life, int Max)> AllyHp = new();

		/// <summary>When each ally last landed a FIGHT hit (their attack pose).</summary>
		public static readonly Dictionary<int, uint> AllyHitTick = new();

		// ================================================================== server state

		private sealed class NetBattle
		{
			public int Id;
			public readonly List<int> Roots = new();
			public readonly List<int> Players = new();
			public readonly List<int> Pending = new();
			public readonly Dictionary<int, int> Ready = new();
			public readonly Dictionary<int, Vector2> SavedVelocity = new();
			public Stage Stage;
			/// <summary>Who acts, step by step: each ACT/ITEM/SPARE/DEFEND alone, then every FIGHT together.</summary>
			public readonly List<List<int>> Steps = new();
			public int StepIndex;
			public readonly HashSet<int> Done = new();
			public int StageTicks;
			public int EmptyTicks;
			public int Round;
			public List<int> Step => StepIndex < Steps.Count ? Steps[StepIndex].Where(Players.Contains).ToList() : new List<int>();
			public int Current => Step.FirstOrDefault(p => !Done.Contains(p), -1);
		}

		private static readonly List<NetBattle> battles = new();
		private static int nextBattleId = 1;

		internal static readonly List<string> LabSent = new();
		internal static bool LabCapture { get; set; }

		public static bool InBattle(int player) => battles.Any(b => b.Players.Contains(player) || b.Pending.Contains(player));
		internal static int LabBattleCount => battles.Count;
		internal static List<int> LabPlayers(int id) => Find(id)?.Players.ToList() ?? new List<int>();
		internal static List<int> LabPending(int id) => Find(id)?.Pending.ToList() ?? new List<int>();
		internal static Stage LabStage(int id) => Find(id)?.Stage ?? Stage.Choosing;
		internal static int LabCurrent(int id) => Find(id)?.Current ?? -1;
		internal static List<int> LabStep(int id) => Find(id)?.Step ?? new List<int>();

		public static void Reset()
		{
			frozen.Clear();
			battles.Clear();
			WorldBattles.Clear();
			Party.Clear();
			Joining.Clear();
			ReadyFaces.Clear();
			AllySouls.Clear();
			AllyHitTick.Clear();
			AllyHp.Clear();
			MyBattle = -1;
			ActingNow.Clear();
			FightingNow.Clear();
			lastRequestTick = 0;
		}

		// ================================================================== packets

		private static ModPacket Packet(Msg msg)
		{
			ModPacket p = ModContent.GetInstance<MercyMode>().GetPacket();
			p.Write((byte)msg);
			return p;
		}

		private static void ToServer(ModPacket p) => p.Send();

		private static void ToPlayer(ModPacket p, int player, Msg msg)
		{
			if (LabCapture)
			{
				LabSent.Add($"{msg}>{player}");
				return;
			}
			if (player >= 0 && player < Main.maxPlayers && Netplay.Clients[player].IsConnected())
				p.Send(player);
		}

		private static void ToAll(ModPacket p, Msg msg, int except = -1)
		{
			if (LabCapture)
			{
				LabSent.Add($"{msg}>all");
				return;
			}
			p.Send(-1, except);
		}

		/// <summary>To everyone in the battle, watchers included.</summary>
		private static void ToParty(NetBattle b, Func<ModPacket> make, Msg msg, int except = -1)
		{
			foreach (int pl in b.Players.Concat(b.Pending).ToList())
				if (pl != except)
					ToPlayer(make(), pl, msg);
		}

		public static void Handle(BinaryReader r, int from)
		{
			var msg = (Msg)r.ReadByte();
			if (Main.netMode == NetmodeID.Server)
				HandleServer(msg, r, from);
			else
				HandleClient(msg, r);
		}

		// ================================================================== client → server

		public static void RequestBattle(NPC root, List<NPC> roots)
		{
			if (RequestPending || IsFrozen(root))
				return;
			lastRequestTick = Main.GameUpdateCount;
			roots = roots.Where(n => !IsFrozen(n)).ToList();
			ModPacket p = Packet(Msg.RequestBattle);
			p.Write((byte)roots.Count);
			foreach (NPC n in roots)
				p.Write((short)n.whoAmI);
			ToServer(p);
		}

		public static void RequestJoin(NPC npc)
		{
			if (RequestPending)
				return;
			lastRequestTick = Main.GameUpdateCount;
			ModPacket p = Packet(Msg.RequestJoin);
			p.Write((short)MercyMode.Root(npc).whoAmI);
			ToServer(p);
		}

		public static void SendReady(int face)
		{
			if (!InParty)
				return;
			ReadyFaces[Main.myPlayer] = face;
			ModPacket p = Packet(Msg.Ready);
			p.Write(MyBattle);
			p.Write((byte)face);
			ToServer(p);
		}

		public static void SendActionDone()
		{
			if (!InParty)
				return;
			ModPacket p = Packet(Msg.ActionDone);
			p.Write(MyBattle);
			ToServer(p);
		}

		public static void SendPartyText(string text)
		{
			if (!InParty || Party.Count < 2)
				return;
			ModPacket p = Packet(Msg.PartyText);
			p.Write(MyBattle);
			p.Write(text ?? "");
			ToServer(p);
		}

		public static void SendSoul(Vector2 soul, byte mode)
		{
			if (!InParty || Party.Count < 2)
				return;
			ModPacket p = Packet(Msg.SoulPos);
			p.Write(MyBattle);
			p.Write((short)soul.X);
			p.Write((short)soul.Y);
			p.Write(mode);
			ToServer(p);
		}

		public static void SendHp(int life, int max)
		{
			if (!InParty || Party.Count < 2)
				return;
			ModPacket p = Packet(Msg.PartyHp);
			p.Write(MyBattle);
			p.Write((short)life);
			p.Write((short)max);
			ToServer(p);
		}

		public static void SendAddMercy(NPC npc, float delta)
		{
			if (!Online || delta == 0f)
				return;
			ModPacket p = Packet(Msg.AddMercy);
			p.Write((short)npc.whoAmI);
			p.Write(delta);
			ToServer(p);
		}

		public static void SendSpare(NPC npc)
		{
			ModPacket p = Packet(Msg.RequestSpare);
			p.Write((short)npc.whoAmI);
			ToServer(p);
		}

		public static void SendKillMembers(IEnumerable<NPC> npcs)
		{
			var list = npcs.ToList();
			ModPacket p = Packet(Msg.KillMembers);
			p.Write((short)list.Count);
			foreach (NPC n in list)
				p.Write((short)n.whoAmI);
			ToServer(p);
		}

		public static void SendPartyHit(NPC npc, int damage, bool crit)
		{
			if (!InParty)
				return;
			ModPacket p = Packet(Msg.PartyHit);
			p.Write(MyBattle);
			p.Write((short)npc.whoAmI);
			p.Write(damage);
			p.Write(crit);
			ToServer(p);
		}

		/// <summary>A player's party colour choice: from a client to the server, or from the server on to others.</summary>
		public static void SendColor(int player, byte choice, int toClient = -1, int ignoreClient = -1)
		{
			if (Main.netMode == NetmodeID.SinglePlayer || LabCapture)
				return;
			ModPacket p = Packet(Msg.PlayerColor);
			p.Write((byte)player);
			p.Write(choice);
			p.Send(toClient, ignoreClient);
		}

		public static void LeaveBattle()
		{
			if (!InParty)
				return;
			ModPacket p = Packet(Msg.Left);
			p.Write(MyBattle);
			ToServer(p);
			MyBattle = -1;
			Party.Clear();
			Joining.Clear();
			ReadyFaces.Clear();
			AllySouls.Clear();
			ActingNow.Clear();
			FightingNow.Clear();
		}

		// ================================================================== client handling

		private static void HandleClient(Msg msg, BinaryReader r)
		{
			BattleSystem battle = BattleSystem.Instance;
			switch (msg)
			{
				case Msg.JoinBattle:
				{
					int id = r.ReadInt32();
					bool spectate = r.ReadBoolean();
					int count = r.ReadByte();
					var roots = new List<(NPC Npc, float Mercy)>();
					for (int i = 0; i < count; i++)
					{
						NPC n = Main.npc[r.ReadInt16()];
						float mercy = r.ReadSingle();
						if (n.active)
							roots.Add((n, mercy));
					}
					lastRequestTick = 0;
					if (BattleSystem.Active || roots.Count == 0)
					{
						ModPacket p = Packet(Msg.Left);
						p.Write(id);
						ToServer(p);
						return;
					}
					// Set before starting: the battle screen asks who it's with from the first frame
					MyBattle = id;
					ReadyFaces.Clear();
					AllySouls.Clear();
					ActingNow.Clear();
			FightingNow.Clear();
					if (!battle.StartNet(roots, spectate))
					{
						MyBattle = -1;
						ModPacket p = Packet(Msg.Left);
						p.Write(id);
						ToServer(p);
					}
					break;
				}
				case Msg.Party:
				{
					int id = r.ReadInt32();
					var players = ReadPlayers(r);
					var pending = ReadPlayers(r);
					if (id != MyBattle)
						return;
					// Whoever is gone leaves the battle screen (walking off, or with us if the battle is ending)
					foreach (int gone in Party.Where(k => k != Main.myPlayer && !players.Contains(k) && !pending.Contains(k)).ToList())
						battle.OnAllyLeft(gone);
					Party.Clear();
					Party.AddRange(players);
					Party.AddRange(pending);
					Joining.Clear();
					Joining.UnionWith(pending);
					foreach (int gone in ReadyFaces.Keys.Where(k => !Party.Contains(k)).ToList())
						ReadyFaces.Remove(gone);
					break;
				}
				case Msg.Frozen:
				{
					int id = r.ReadInt32();
					int count = r.ReadInt16();
					for (int i = 0; i < count; i++)
					{
						int n = r.ReadInt16();
						frozen[n] = (id, r.ReadInt32());
					}
					break;
				}
				case Msg.Unfrozen:
				{
					int id = r.ReadInt32();
					foreach (int k in frozen.Where(f => f.Value.Battle == id).Select(f => f.Key).ToList())
						frozen.Remove(k);
					WorldBattles.Remove(id);
					break;
				}
				case Msg.ReadyState:
				{
					int id = r.ReadInt32();
					int player = r.ReadByte();
					int face = r.ReadByte();
					if (id == MyBattle)
						ReadyFaces[player] = face;
					break;
				}
				case Msg.TurnOf:
				{
					int id = r.ReadInt32();
					var players = ReadPlayers(r);
					if (id != MyBattle)
						return;
					ActingNow.Clear();
					ActingNow.UnionWith(players);
					FightingNow.Clear();
					if (players.Count > 1 || players.Count == 1 && ReadyFaces.TryGetValue(players[0], out int f) && f == FightFace)
						FightingNow.AddRange(players);
					battle.OnNetTurnOf(players);
					break;
				}
				case Msg.PartyText:
				{
					int id = r.ReadInt32();
					int player = r.ReadByte();
					string text = r.ReadString();
					if (id == MyBattle && player != Main.myPlayer)
						battle.OnNetText(player, text);
					break;
				}
				case Msg.BeginEnemyTurn:
				{
					int id = r.ReadInt32();
					int seed = r.ReadInt32();
					int round = r.ReadInt32();
					if (id != MyBattle)
						return;
					ReadyFaces.Clear();
					ActingNow.Clear();
			FightingNow.Clear();
					Joining.Remove(Main.myPlayer);
					battle.OnNetEnemyTurn(seed, round);
					break;
				}
				case Msg.SetMercy:
				{
					NPC n = Main.npc[r.ReadInt16()];
					float mercy = r.ReadSingle();
					n.GetGlobalNPC<MercyGlobalNPC>().Mercy = mercy;
					battle.OnNetMercy(n, mercy);
					break;
				}
				case Msg.Spared:
				{
					int id = r.ReadInt32();
					NPC n = Main.npc[r.ReadInt16()];
					if (id == MyBattle)
						battle.OnNetSpared(n);
					break;
				}
				case Msg.PartyHit:
				{
					int id = r.ReadInt32();
					int player = r.ReadByte();
					NPC n = Main.npc[r.ReadInt16()];
					int damage = r.ReadInt32();
					bool crit = r.ReadBoolean();
					if (id == MyBattle)
					{
						AllyHitTick[player] = Main.GameUpdateCount;
						battle.OnNetHit(player, n, damage, crit);
					}
					else
					{
						// Outside the battle: the fighter swings in the world (only a picture, it hits nothing)
						WorldVisuals.Swing(Main.player[player], n);
					}
					break;
				}
				case Msg.SoulPos:
				{
					int id = r.ReadInt32();
					int player = r.ReadByte();
					var pos = new Vector2(r.ReadInt16(), r.ReadInt16());
					byte mode = r.ReadByte();
					if (id == MyBattle)
						AllySouls[player] = (pos, mode, Main.GameUpdateCount);
					break;
				}
				case Msg.BattleState:
				{
					int id = r.ReadInt32();
					var stage = (Stage)r.ReadByte();
					var players = ReadPlayers(r);
					if (!WorldBattles.TryGetValue(id, out WorldBattle wb))
						WorldBattles[id] = wb = new WorldBattle();
					if (wb.Stage != stage)
						wb.StageTick = Main.GameUpdateCount;
					wb.Stage = stage;
					wb.Players = players;
					break;
				}
				case Msg.PartyHp:
				{
					int id = r.ReadInt32();
					int player = r.ReadByte();
					int life = r.ReadInt16(), max = r.ReadInt16();
					if (id == MyBattle)
						AllyHp[player] = (life, max);
					break;
				}
				case Msg.PlayerColor:
				{
					int player = r.ReadByte();
					byte choice = r.ReadByte();
					// Kept in a plain array: it can arrive before that player's character is set up on this client
					if (player != Main.myPlayer && player < Main.maxPlayers)
						PartyColors.Remote[player] = choice;
					break;
				}
			}
		}

		private static List<int> ReadPlayers(BinaryReader r)
		{
			int count = r.ReadByte();
			var list = new List<int>();
			for (int i = 0; i < count; i++)
				list.Add(r.ReadByte());
			return list;
		}

		private static void WritePlayers(ModPacket p, List<int> players)
		{
			p.Write((byte)players.Count);
			foreach (int pl in players)
				p.Write((byte)pl);
		}

		// ================================================================== server handling

		private static void HandleServer(Msg msg, BinaryReader r, int from)
		{
			switch (msg)
			{
				case Msg.RequestBattle:
				{
					int count = r.ReadByte();
					var roots = new List<NPC>();
					for (int i = 0; i < count; i++)
						roots.Add(Main.npc[r.ReadInt16()]);
					ServerStartBattle(from, roots);
					break;
				}
				case Msg.RequestJoin:
					ServerJoin(from, Main.npc[r.ReadInt16()]);
					break;
				case Msg.Ready:
					ServerReady(from, r.ReadInt32(), r.ReadByte());
					break;
				case Msg.ActionDone:
					ServerActionDone(from, r.ReadInt32());
					break;
				case Msg.PartyText:
				{
					int id = r.ReadInt32();
					string text = r.ReadString();
					NetBattle b = Find(id);
					if (b != null && b.Players.Contains(from))
						ToParty(b, () =>
						{
							ModPacket p = Packet(Msg.PartyText);
							p.Write(id);
							p.Write((byte)from);
							p.Write(text);
							return p;
						}, Msg.PartyText, except: from);
					break;
				}
				case Msg.AddMercy:
					ServerAddMercy(Main.npc[r.ReadInt16()], r.ReadSingle());
					break;
				case Msg.RequestSpare:
					ServerSpare(from, Main.npc[r.ReadInt16()]);
					break;
				case Msg.KillMembers:
				{
					int count = r.ReadInt16();
					var list = new List<NPC>();
					for (int i = 0; i < count; i++)
						list.Add(Main.npc[r.ReadInt16()]);
					ServerKill(from, list);
					break;
				}
				case Msg.PartyHit:
				{
					int id = r.ReadInt32();
					short npc = r.ReadInt16();
					int damage = r.ReadInt32();
					bool crit = r.ReadBoolean();
					if (Find(id) == null)
						return;
					// Everyone: the party shows the number, the rest of the world sees the swing
					ModPacket p = Packet(Msg.PartyHit);
					p.Write(id);
					p.Write((byte)from);
					p.Write(npc);
					p.Write(damage);
					p.Write(crit);
					ToAll(p, Msg.PartyHit, except: from);
					break;
				}
				case Msg.SoulPos:
				{
					int id = r.ReadInt32();
					short x = r.ReadInt16(), y = r.ReadInt16();
					byte mode = r.ReadByte();
					NetBattle b = Find(id);
					if (b != null)
						ToParty(b, () =>
						{
							ModPacket p = Packet(Msg.SoulPos);
							p.Write(id);
							p.Write((byte)from);
							p.Write(x);
							p.Write(y);
							p.Write(mode);
							return p;
						}, Msg.SoulPos, except: from);
					break;
				}
				case Msg.PartyHp:
				{
					int id = r.ReadInt32();
					short life = r.ReadInt16(), max = r.ReadInt16();
					NetBattle b = Find(id);
					if (b != null)
						ToParty(b, () =>
						{
							ModPacket p = Packet(Msg.PartyHp);
							p.Write(id);
							p.Write((byte)from);
							p.Write(life);
							p.Write(max);
							return p;
						}, Msg.PartyHp, except: from);
					break;
				}
				case Msg.Left:
					ServerLeave(from, r.ReadInt32());
					break;
				case Msg.PlayerColor:
				{
					int player = r.ReadByte();
					byte choice = r.ReadByte();
					// Only for themselves
					if (player != from)
						return;
					PartyColors.Remote[player] = choice;
					SendColor(player, choice, -1, from);
					break;
				}
			}
		}

		private static NetBattle Find(int id) => battles.FirstOrDefault(x => x.Id == id);

		/// <summary>Server: a battle with these enemies. Pulls in players close to the one starting it.</summary>
		internal static int ServerStartBattle(int requester, List<NPC> roots)
		{
			roots = roots.Where(n => n.active && n.life > 0 && !IsFrozen(n)).Distinct().ToList();
			if (InBattle(requester) || roots.Count == 0)
				return -1;

			var b = new NetBattle { Id = nextBattleId++ };
			b.Roots.AddRange(roots.Select(n => n.whoAmI));
			b.Players.Add(requester);
			Player leader = Main.player[requester];
			bool Near(Player p) => p.DistanceSQ(leader.Center) <= JoinRange * JoinRange;
			foreach (Player p in Main.player.Where(p => p.active).OrderBy(p => p.DistanceSQ(leader.Center)).ToList())
			{
				if (b.Players.Count >= MaxParty)
					break;
				if (p.whoAmI == requester || p.dead || InBattle(p.whoAmI) || !Near(p))
					continue;
				b.Players.Add(p.whoAmI);
			}
			battles.Add(b);
			Freeze(b);
			foreach (int pl in b.Players)
				SendJoin(b, pl, spectate: false);
			SendParty(b);
			SendState(b);
			ModContent.GetInstance<MercyMode>().Logger.Info($"MP battle {b.Id}: {string.Join(", ", roots.Select(n => n.FullName))} vs {string.Join(", ", b.Players.Select(p => Main.player[p].name))}");
			return b.Id;
		}

		/// <summary>Server: a player pressed the join key near a battle. They watch until its next bullet box.</summary>
		internal static void ServerJoin(int player, NPC npc)
		{
			NetBattle b = Find(BattleOf(npc));
			if (b == null || b.Stage == Stage.Over || !NpcsOf(b.Id).Any() || InBattle(player) || b.Players.Count + b.Pending.Count >= MaxParty || Main.player[player].dead)
				return;
			b.Pending.Add(player);
			SendJoin(b, player, spectate: true);
			SendParty(b);
			SendState(b);
		}

		internal static void ServerReady(int player, int id, int face)
		{
			NetBattle b = Find(id);
			if (b == null || !b.Players.Contains(player) || b.Stage is Stage.Acting or Stage.Over)
				return;
			if (b.Stage == Stage.EnemyTurn)
				SetStage(b, Stage.Choosing);
			b.Ready[player] = face;
			ToParty(b, () =>
			{
				ModPacket p = Packet(Msg.ReadyState);
				p.Write(id);
				p.Write((byte)player);
				p.Write((byte)face);
				return p;
			}, Msg.ReadyState);
			CheckReady(b);
		}

		/// <summary>The FIGHT command's face icon (BattleSystem.FaceFight).</summary>
		private const int FightFace = 1;

		/// <summary>
		/// Everyone has picked (or the wait ran out). Like Deltarune: ACT, ITEM, SPARE and DEFEND go one at a time in
		/// party order (so a heal lands before the attacks), then everyone who picked FIGHT attacks at once.
		/// </summary>
		private static void CheckReady(NetBattle b, bool force = false)
		{
			if (b.Stage == Stage.Acting || b.Ready.Count == 0 || !force && !b.Players.All(b.Ready.ContainsKey))
				return;
			b.Steps.Clear();
			var ready = b.Players.Where(b.Ready.ContainsKey).ToList();
			foreach (int pl in ready.Where(pl => b.Ready[pl] != FightFace))
				b.Steps.Add(new List<int> { pl });
			var fighters = ready.Where(pl => b.Ready[pl] == FightFace).ToList();
			if (fighters.Count > 0)
				b.Steps.Add(fighters);
			b.Ready.Clear();
			b.StepIndex = 0;
			b.Done.Clear();
			SetStage(b, Stage.Acting);
			SendTurn(b);
		}

		internal static void ServerActionDone(int player, int id)
		{
			NetBattle b = Find(id);
			if (b == null || b.Stage != Stage.Acting || !b.Step.Contains(player))
				return;
			b.Done.Add(player);
			// The step's over once everyone in it is done (a FIGHT step has several players)
			if (b.Step.All(b.Done.Contains))
				NextStep(b);
		}

		private static void NextStep(NetBattle b)
		{
			b.StepIndex++;
			b.Done.Clear();
			SendTurn(b);
		}

		/// <summary>The next player in line acts; after the last one, the enemies attack.</summary>
		private static void SendTurn(NetBattle b)
		{
			while (b.StepIndex < b.Steps.Count && b.Step.Count == 0)
				b.StepIndex++;
			b.StageTicks = 0;
			// Every enemy gone (won by an earlier action): nobody else acts, no bullet box
			if (!NpcsOf(b.Id).Any())
			{
				SetStage(b, Stage.Over);
				return;
			}
			if (b.StepIndex >= b.Steps.Count)
			{
				BeginEnemyTurn(b);
				return;
			}
			var who = b.Step;
			ToParty(b, () =>
			{
				ModPacket p = Packet(Msg.TurnOf);
				p.Write(b.Id);
				WritePlayers(p, who);
				return p;
			}, Msg.TurnOf);
		}

		private static void BeginEnemyTurn(NetBattle b)
		{
			// Watchers jump in now: they'll be in this bullet box
			b.Players.AddRange(b.Pending.Where(p => !b.Players.Contains(p)));
			b.Pending.Clear();
			b.Round++;
			SetStage(b, Stage.EnemyTurn);
			SendParty(b);
			int seed = Main.rand.Next();
			ToParty(b, () =>
			{
				ModPacket p = Packet(Msg.BeginEnemyTurn);
				p.Write(b.Id);
				p.Write(seed);
				p.Write(b.Round);
				return p;
			}, Msg.BeginEnemyTurn);
		}

		private static void SetStage(NetBattle b, Stage s)
		{
			b.Stage = s;
			b.StageTicks = 0;
			SendState(b);
		}

		internal static void ServerAddMercy(NPC npc, float delta)
		{
			if (!npc.active)
				return;
			var g = npc.GetGlobalNPC<MercyGlobalNPC>();
			g.Mercy = MathHelper.Clamp(g.Mercy + delta, 0f, 100f);
			ModPacket p = Packet(Msg.SetMercy);
			p.Write((short)npc.whoAmI);
			p.Write(g.Mercy);
			ToAll(p, Msg.SetMercy);
		}

		internal static void ServerSpare(int player, NPC npc)
		{
			if (!npc.active)
				return;
			NetBattle b = Find(BattleOf(npc));
			if (b != null)
				ToParty(b, () =>
				{
					ModPacket p = Packet(Msg.Spared);
					p.Write(b.Id);
					p.Write((short)npc.whoAmI);
					return p;
				}, Msg.Spared, except: player);

			Encounter e = EncounterRegistry.Create(EncounterRegistry.ResolveRoot(npc));
			var members = e.Members().Append(npc).Where(m => m.active).Select(m => m.whoAmI).Distinct().ToList();
			// Boss bags drop for every player who took part: a party that only ACTed never hit it, so count them in
			foreach (int m in members)
				foreach (int pl in b != null ? b.Players.Concat(b.Pending) : new[] { player })
					if (pl >= 0 && pl < Main.maxPlayers)
						Main.npc[m].playerInteraction[pl] = true;
			e.Spare();
			SyncNpcs(members);
		}

		internal static void ServerKill(int player, List<NPC> npcs)
		{
			var changed = new List<int>();
			foreach (NPC n in npcs)
			{
				if (!n.active || !IsFrozen(n))
					continue;
				n.life = 0;
				n.active = false;
				changed.Add(n.whoAmI);
			}
			SyncNpcs(changed);
		}

		internal static void ServerLeave(int player, int id)
		{
			NetBattle b = Find(id);
			if (b == null)
				return;
			bool wasCurrent = b.Stage == Stage.Acting && b.Step.Contains(player);
			if (!b.Players.Remove(player) && !b.Pending.Remove(player))
				return;
			b.Ready.Remove(player);
			if (b.Players.Count == 0 && b.Pending.Count == 0)
			{
				EndBattle(b);
				return;
			}
			if (b.Players.Count == 0)
			{
				// Only watchers left: they jump in now
				BeginEnemyTurn(b);
				return;
			}
			SendParty(b);
			SendState(b);
			if (wasCurrent)
			{
				// Whoever's left in this step may all be done already
				if (b.Step.All(b.Done.Contains))
					NextStep(b);
			}
			else
				CheckReady(b);
		}

		public static void ServerDisconnect(int player)
		{
			foreach (NetBattle b in battles.Where(x => x.Players.Contains(player) || x.Pending.Contains(player)).ToList())
				ServerLeave(player, b.Id);
		}

		/// <summary>Server, every tick: timeouts, players who vanished, battles whose enemies are all gone.</summary>
		public static void ServerUpdate()
		{
			foreach (NetBattle b in battles.ToList())
			{
				foreach (int pl in b.Players.Concat(b.Pending).ToList())
					if (!Main.player[pl].active)
						ServerLeave(pl, b.Id);
				if (!battles.Contains(b))
					continue;

				b.StageTicks++;
				if (b.Stage != Stage.Acting && b.Ready.Count > 0 && b.StageTicks > ChooseTimeoutTicks)
					CheckReady(b, force: true);
				else if (b.Stage == Stage.Acting && b.StageTicks > ActTimeoutTicks)
					NextStep(b);

				bool anyAlive = NpcsOf(b.Id).Any();
				// Won: nobody joins a battle that's over, and outsiders stop seeing it as one
				if (!anyAlive && b.Stage != Stage.Over)
					SetStage(b, Stage.Over);
				b.EmptyTicks = anyAlive ? 0 : b.EmptyTicks + 1;
				if (b.EmptyTicks > 60 * 20)
					EndBattle(b);
			}
		}

		private static void Freeze(NetBattle b)
		{
			var ids = new HashSet<int>();
			foreach (int r in b.Roots)
			{
				ids.Add(r);
				foreach (NPC m in EncounterRegistry.Create(Main.npc[r]).Members())
					if (m.active)
						ids.Add(m.whoAmI);
			}
			foreach (int i in ids)
			{
				NPC n = Main.npc[i];
				frozen[i] = (b.Id, n.type);
				b.SavedVelocity[i] = n.velocity;
				n.velocity = Vector2.Zero;
				n.netUpdate = true;
			}
			ModPacket p = Packet(Msg.Frozen);
			p.Write(b.Id);
			p.Write((short)ids.Count);
			foreach (int i in ids)
			{
				p.Write((short)i);
				p.Write(Main.npc[i].type);
			}
			ToAll(p, Msg.Frozen);
		}

		private static void EndBattle(NetBattle b)
		{
			battles.Remove(b);
			foreach (var (i, (_, type)) in frozen.Where(f => f.Value.Battle == b.Id).ToList())
			{
				frozen.Remove(i);
				NPC n = Main.npc[i];
				if (n.active && n.type == type && b.SavedVelocity.TryGetValue(i, out Vector2 v))
				{
					n.velocity = v;
					n.netUpdate = true;
				}
			}
			WorldBattles.Remove(b.Id);
			ModPacket p = Packet(Msg.Unfrozen);
			p.Write(b.Id);
			ToAll(p, Msg.Unfrozen);
			ModContent.GetInstance<MercyMode>().Logger.Info($"MP battle {b.Id} over");
		}

		private static void SendJoin(NetBattle b, int player, bool spectate)
		{
			var roots = b.Roots.Select(i => Main.npc[i]).Where(n => n.active).ToList();
			ModPacket p = Packet(Msg.JoinBattle);
			p.Write(b.Id);
			p.Write(spectate);
			p.Write((byte)roots.Count);
			foreach (NPC n in roots)
			{
				p.Write((short)n.whoAmI);
				p.Write(n.GetGlobalNPC<MercyGlobalNPC>().Mercy);
			}
			ToPlayer(p, player, Msg.JoinBattle);
		}

		private static void SendParty(NetBattle b) => ToParty(b, () =>
		{
			ModPacket p = Packet(Msg.Party);
			p.Write(b.Id);
			WritePlayers(p, b.Players);
			WritePlayers(p, b.Pending);
			return p;
		}, Msg.Party);

		/// <summary>Everyone (outsiders too): this battle's stage and who is in it.</summary>
		private static void SendState(NetBattle b)
		{
			if (!WorldBattles.TryGetValue(b.Id, out WorldBattle wb))
				WorldBattles[b.Id] = wb = new WorldBattle();
			wb.Stage = b.Stage;
			wb.Players = b.Players.Concat(b.Pending).ToList();
			ModPacket p = Packet(Msg.BattleState);
			p.Write(b.Id);
			p.Write((byte)b.Stage);
			WritePlayers(p, wb.Players);
			ToAll(p, Msg.BattleState);
		}

		private static void SyncNpcs(IEnumerable<int> ids)
		{
			if (LabCapture)
				return;
			foreach (int i in ids)
				NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, i);
		}
	}
}
