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
	/// Multiplayer battles. Each player runs the battle screen on their own client (menus, the FIGHT bar, their own
	/// SOUL in their own box); the server only keeps the bookkeeping everyone has to agree on:
	/// <list type="bullet">
	/// <item>who is in which battle (a party of up to <see cref="MaxParty"/>: whoever starts it plus players nearby,
	/// and anyone who walks into the frozen enemies later),</item>
	/// <item>which NPCs are frozen (only the battle's; the rest of the world keeps running),</item>
	/// <item>the turn barrier: the enemies attack once every party member has picked an action,</item>
	/// <item>MERCY, spares (loot drops on the server) and broken boss parts.</item>
	/// </list>
	/// Enemy HP needs nothing extra: FIGHT uses <see cref="NPC.SimpleStrikeNPC"/>, which Terraria syncs.
	/// </summary>
	public static class BattleNet
	{
		private enum Msg : byte
		{
			RequestBattle, // client → server: roots of the enemies this client wants to fight
			RequestJoin, // client → server: touched an enemy of a battle already going
			JoinBattle, // server → client: start the battle screen (id, roots, their MERCY)
			Party, // server → party: the battle's players, in order
			Frozen, // server → all: these NPCs belong to battle id
			Unfrozen, // server → all: battle id is over (or these NPCs left it)
			Ready, // client → server: picked an action (face icon)
			ReadyState, // server → party: a member is ready (face icon)
			BeginEnemyTurn, // server → party: everyone is ready
			AddMercy, // client → server: MERCY changed by this much
			SetMercy, // server → all: an enemy's MERCY now
			RequestSpare, // client → server: spare this enemy
			Spared, // server → party: an enemy was spared by someone else
			KillMembers, // client → server: a boss's core broke, these parts go with it
			PartyHit, // client → server → party: a FIGHT hit landed (for the others' damage numbers)
			Left, // client → server: left the battle (won, fled, died, ended)
		}

		public const int MaxParty = 3;
		/// <summary>How close (pixels) other players must be to be pulled into a battle.</summary>
		public const float JoinRange = 60 * 16;
		/// <summary>An enemy turn starts anyway after this long, so one AFK player can't hold the others forever.</summary>
		public const int ReadyTimeoutTicks = 60 * 60;

		/// <summary>This is a multiplayer client: battles go through the server.</summary>
		public static bool Online => Main.netMode == NetmodeID.MultiplayerClient;
		/// <summary>This is the server of a real multiplayer game (the headless lab runs as a server too, but singleplayer).</summary>
		public static bool IsServer => Main.netMode == NetmodeID.Server && !Lab.LabSystem.Enabled;

		// ================================================================== shared: which NPCs are frozen

		/// <summary>NPC index → (battle id, type when frozen). Kept on the server and mirrored on every client.</summary>
		private static readonly Dictionary<int, (int Battle, int Type)> frozen = new();

		public static bool IsFrozen(NPC npc)
		{
			if (frozen.Count == 0)
				return false;
			if (frozen.TryGetValue(npc.whoAmI, out var f) && f.Type == npc.type)
				return true;
			// Worm segments and the like follow their head
			NPC root = MercyMode.Root(npc);
			return root != npc && frozen.TryGetValue(root.whoAmI, out f) && f.Type == root.type;
		}

		public static int BattleOf(NPC npc) => frozen.TryGetValue(MercyMode.Root(npc).whoAmI, out var f) ? f.Battle
			: frozen.TryGetValue(npc.whoAmI, out f) ? f.Battle : -1;

		// ================================================================== client state

		/// <summary>The battle this client is in (-1: none).</summary>
		public static int MyBattle { get; private set; } = -1;
		/// <summary>The party, in order (player indices, this player included).</summary>
		public static readonly List<int> Party = new();
		/// <summary>Party members who have picked their action this turn, with the command's face icon.</summary>
		public static readonly Dictionary<int, int> ReadyFaces = new();
		private static uint lastRequestTick;

		/// <summary>The other players in this client's battle.</summary>
		public static IEnumerable<Player> Allies => Party.Where(i => i != Main.myPlayer && i >= 0 && i < Main.maxPlayers && Main.player[i].active).Select(i => Main.player[i]);
		public static int MyPartyIndex => Math.Max(0, Party.IndexOf(Main.myPlayer));
		public static int PartySize => Math.Max(1, Party.Count);
		/// <summary>Who the party is still waiting on, by name.</summary>
		public static IEnumerable<string> WaitingOn => Allies.Where(p => !ReadyFaces.ContainsKey(p.whoAmI)).Select(p => p.name);

		/// <summary>A request is on its way to the server: don't send another, and don't take contact damage meanwhile.</summary>
		public static bool RequestPending => Main.GameUpdateCount - lastRequestTick < 30 && lastRequestTick != 0;

		// ================================================================== server state

		private sealed class NetBattle
		{
			public int Id;
			public List<int> Roots = new();
			public readonly List<int> Players = new();
			public readonly Dictionary<int, int> Ready = new();
			public readonly Dictionary<int, Vector2> SavedVelocity = new();
			public int WaitTicks;
			public int EmptyTicks;
		}

		private static readonly List<NetBattle> battles = new();
		private static int nextBattleId = 1;

		/// <summary>Messages the server would have sent (the lab has no clients to send them to).</summary>
		internal static readonly List<string> LabSent = new();
		internal static bool LabCapture { get; set; }

		public static bool InBattle(int player) => battles.Any(b => b.Players.Contains(player));
		internal static int LabBattleCount => battles.Count;
		internal static List<int> LabPlayers(int id) => battles.FirstOrDefault(b => b.Id == id)?.Players.ToList() ?? new List<int>();
		internal static int LabLastBattleId => nextBattleId - 1;

		public static void Reset()
		{
			frozen.Clear();
			battles.Clear();
			Party.Clear();
			ReadyFaces.Clear();
			MyBattle = -1;
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

		/// <summary>Server: to one player, if they're still connected.</summary>
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

		private static void ToAll(ModPacket p, Msg msg)
		{
			if (LabCapture)
			{
				LabSent.Add($"{msg}>all");
				return;
			}
			p.Send();
		}

		private static void ToParty(NetBattle b, Func<ModPacket> make, Msg msg, int except = -1)
		{
			foreach (int pl in b.Players)
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

		/// <summary>Asks the server for a battle with these enemies (or to join the one they're already in).</summary>
		public static void RequestBattle(NPC root, List<NPC> roots)
		{
			if (RequestPending)
				return;
			lastRequestTick = Main.GameUpdateCount;
			if (IsFrozen(root))
			{
				ModPacket j = Packet(Msg.RequestJoin);
				j.Write((short)MercyMode.Root(root).whoAmI);
				ToServer(j);
				return;
			}
			ModPacket p = Packet(Msg.RequestBattle);
			roots = roots.Where(n => !IsFrozen(n)).ToList();
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
			if (!Online || MyBattle < 0)
				return;
			ReadyFaces[Main.myPlayer] = face;
			ModPacket p = Packet(Msg.Ready);
			p.Write(MyBattle);
			p.Write((byte)face);
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
			if (!Online || MyBattle < 0 || Party.Count < 2)
				return;
			ModPacket p = Packet(Msg.PartyHit);
			p.Write(MyBattle);
			p.Write((short)npc.whoAmI);
			p.Write(damage);
			p.Write(crit);
			ToServer(p);
		}

		/// <summary>This client's battle screen closed.</summary>
		public static void LeaveBattle()
		{
			if (!Online || MyBattle < 0)
				return;
			ModPacket p = Packet(Msg.Left);
			p.Write(MyBattle);
			ToServer(p);
			MyBattle = -1;
			Party.Clear();
			ReadyFaces.Clear();
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
					if (BattleSystem.Active || roots.Count == 0 || !battle.StartNet(roots))
					{
						// Can't (already fighting, dead, battles turned off): let the server know straight away
						ModPacket p = Packet(Msg.Left);
						p.Write(id);
						ToServer(p);
						return;
					}
					MyBattle = id;
					ReadyFaces.Clear();
					break;
				}
				case Msg.Party:
				{
					int id = r.ReadInt32();
					int count = r.ReadByte();
					var players = new List<int>();
					for (int i = 0; i < count; i++)
						players.Add(r.ReadByte());
					if (id != MyBattle)
						return;
					Party.Clear();
					Party.AddRange(players);
					foreach (int gone in ReadyFaces.Keys.Where(k => !players.Contains(k)).ToList())
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
						int type = r.ReadInt32();
						frozen[n] = (id, type);
					}
					break;
				}
				case Msg.Unfrozen:
				{
					int id = r.ReadInt32();
					foreach (int k in frozen.Where(f => f.Value.Battle == id).Select(f => f.Key).ToList())
						frozen.Remove(k);
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
				case Msg.BeginEnemyTurn:
				{
					int id = r.ReadInt32();
					if (id != MyBattle)
						return;
					ReadyFaces.Clear();
					battle.OnNetEnemyTurn();
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
					NPC n = Main.npc[r.ReadInt16()];
					int damage = r.ReadInt32();
					bool crit = r.ReadBoolean();
					if (id == MyBattle)
						battle.OnNetHit(n, damage, crit);
					break;
				}
			}
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
					NetBattle b = battles.FirstOrDefault(x => x.Id == id);
					if (b != null)
						ToParty(b, () =>
						{
							ModPacket p = Packet(Msg.PartyHit);
							p.Write(id);
							p.Write(npc);
							p.Write(damage);
							p.Write(crit);
							return p;
						}, Msg.PartyHit, except: from);
					break;
				}
				case Msg.Left:
					ServerLeave(from, r.ReadInt32());
					break;
			}
		}

		/// <summary>Server: a player asked for a battle with these enemies. Pulls nearby players in and freezes the enemies.</summary>
		internal static int ServerStartBattle(int requester, List<NPC> roots)
		{
			roots = roots.Where(n => n.active && n.life > 0 && !IsFrozen(n)).Distinct().ToList();
			if (InBattle(requester))
				return -1;
			// Every enemy already taken: join that battle instead
			if (roots.Count == 0)
				return -1;

			var b = new NetBattle { Id = nextBattleId++ };
			b.Roots.AddRange(roots.Select(n => n.whoAmI));
			b.Players.Add(requester);
			Player leader = Main.player[requester];
			foreach (Player p in Main.ActivePlayers)
			{
				if (b.Players.Count >= MaxParty)
					break;
				if (p.whoAmI == requester || p.dead || InBattle(p.whoAmI) || p.DistanceSQ(leader.Center) > JoinRange * JoinRange)
					continue;
				b.Players.Add(p.whoAmI);
			}
			battles.Add(b);
			Freeze(b);
			foreach (int pl in b.Players)
				SendJoin(b, pl);
			SendParty(b);
			ModContent.GetInstance<MercyMode>().Logger.Info($"MP battle {b.Id}: {string.Join(", ", roots.Select(n => n.FullName))} vs {string.Join(", ", b.Players.Select(p => Main.player[p].name))}");
			return b.Id;
		}

		/// <summary>Server: a player walked into an enemy of a battle that's already going.</summary>
		internal static void ServerJoin(int player, NPC npc)
		{
			int id = BattleOf(npc);
			NetBattle b = battles.FirstOrDefault(x => x.Id == id);
			if (b == null || b.Players.Contains(player) || InBattle(player) || b.Players.Count >= MaxParty)
				return;
			b.Players.Add(player);
			SendJoin(b, player);
			SendParty(b);
		}

		internal static void ServerReady(int player, int id, int face)
		{
			NetBattle b = battles.FirstOrDefault(x => x.Id == id);
			if (b == null || !b.Players.Contains(player))
				return;
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

		/// <summary>Everyone has picked an action (or the wait ran out): the enemies attack.</summary>
		private static void CheckReady(NetBattle b, bool force = false)
		{
			if (b.Ready.Count == 0 || !force && !b.Players.All(b.Ready.ContainsKey))
				return;
			b.Ready.Clear();
			b.WaitTicks = 0;
			ToParty(b, () =>
			{
				ModPacket p = Packet(Msg.BeginEnemyTurn);
				p.Write(b.Id);
				return p;
			}, Msg.BeginEnemyTurn);
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

		/// <summary>Server: spare an enemy for real (loot, downed flags, event progress) and tell the rest of the party.</summary>
		internal static void ServerSpare(int player, NPC npc)
		{
			if (!npc.active)
				return;
			int id = BattleOf(npc);
			NetBattle b = battles.FirstOrDefault(x => x.Id == id);
			// The others see it leave first, so the NPC going away isn't taken for a death
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
				foreach (int pl in b?.Players ?? new List<int> { player })
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
				// Only a battle's own enemies
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
			NetBattle b = battles.FirstOrDefault(x => x.Id == id);
			if (b == null || !b.Players.Remove(player))
				return;
			b.Ready.Remove(player);
			if (b.Players.Count == 0)
			{
				EndBattle(b);
				return;
			}
			SendParty(b);
			CheckReady(b);
		}

		/// <summary>Server: a player left the game.</summary>
		public static void ServerDisconnect(int player)
		{
			foreach (NetBattle b in battles.Where(x => x.Players.Contains(player)).ToList())
				ServerLeave(player, b.Id);
		}

		/// <summary>Server, every tick: wait timeouts, players who vanished, battles whose enemies are all gone.</summary>
		public static void ServerUpdate()
		{
			foreach (NetBattle b in battles.ToList())
			{
				foreach (int pl in b.Players.ToList())
					if (!Main.player[pl].active)
						ServerLeave(pl, b.Id);
				if (!battles.Contains(b))
					continue;

				if (b.Ready.Count > 0 && ++b.WaitTicks > ReadyTimeoutTicks)
					CheckReady(b, force: true);

				// Every enemy gone and nobody left the battle (a client crashed mid-outro): close it after a while
				bool anyAlive = frozen.Any(f => f.Value.Battle == b.Id && Main.npc[f.Key].active && Main.npc[f.Key].type == f.Value.Type);
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
				NPC root = Main.npc[r];
				ids.Add(r);
				foreach (NPC m in EncounterRegistry.Create(root).Members())
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
			foreach (var (i, (battle, type)) in frozen.Where(f => f.Value.Battle == b.Id).ToList())
			{
				frozen.Remove(i);
				NPC n = Main.npc[i];
				if (n.active && n.type == type && b.SavedVelocity.TryGetValue(i, out Vector2 v))
				{
					n.velocity = v;
					n.netUpdate = true;
				}
			}
			ModPacket p = Packet(Msg.Unfrozen);
			p.Write(b.Id);
			ToAll(p, Msg.Unfrozen);
			ModContent.GetInstance<MercyMode>().Logger.Info($"MP battle {b.Id} over");
		}

		private static void SendJoin(NetBattle b, int player)
		{
			var roots = b.Roots.Select(i => Main.npc[i]).Where(n => n.active).ToList();
			ModPacket p = Packet(Msg.JoinBattle);
			p.Write(b.Id);
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
			p.Write((byte)b.Players.Count);
			foreach (int pl in b.Players)
				p.Write((byte)pl);
			return p;
		}, Msg.Party);

		private static void SyncNpcs(IEnumerable<int> ids)
		{
			if (LabCapture)
				return;
			foreach (int i in ids)
				NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, i);
		}
	}

	/// <summary>Runs the server's bookkeeping and cleans up between worlds.</summary>
	public class BattleNetSystem : ModSystem
	{
		public override void PostUpdateEverything()
		{
			if (BattleNet.IsServer)
				BattleNet.ServerUpdate();
		}

		public override void OnWorldLoad() => BattleNet.Reset();

		public override void OnWorldUnload() => BattleNet.Reset();
	}

	public class BattleNetPlayer : ModPlayer
	{
		public override void PlayerDisconnect()
		{
			if (Main.netMode == NetmodeID.Server)
				BattleNet.ServerDisconnect(Player.whoAmI);
		}
	}
}
