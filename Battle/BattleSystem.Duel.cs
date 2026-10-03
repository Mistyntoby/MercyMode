using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using MercyMode.Battle.Net;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>The other player in a duel, as an enemy: their name, ACTs, and an attack built live by them.</summary>
	public sealed class DuelEncounter : Encounter
	{
		public int Opponent;
		/// <summary>Where they stand on the battle screen (mirroring the hero), body centre.</summary>
		public Vector2 Center;

		private Player P => Main.player[Opponent];

		public override string Name => P.name;
		public override string EncounterText => $"* {P.name} challenges you!";
		public override bool IsBoss => false;
		/// <summary>The bullets carry their own damage (from the weapon the piece came from).</summary>
		public override int Damage => 1;
		public override Vector2 DrawCenter => Center;

		private static readonly string[] Lines =
		{
			"* {0} is sizing you up.",
			"* {0} grips their weapon.",
			"* {0} circles you warily.",
			"* {0} is plotting something.",
			"* Smells like a rivalry.",
		};

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return $"* {P.name} looks ready to call it a draw.";
			if (LifeRatio < 0.35f)
				return $"* {P.name} is breathing hard.";
			return string.Format(Lines[Turn % Lines.Length], P.name);
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			new ActOption
			{
				Name = "Check",
				Description = "Useless\nanalysis",
				Run = b => new List<string> { $"* {P.name} - HP {Math.Max(0, Npc.life)}/{Npc.lifeMax} DF {P.statDefense}\n* Another adventurer. Definitely armed." },
			},
			MercyAct("Talk", "Talk it\nout", 40f, $"* You tried to talk {P.name} down."),
			MercyAct("Compliment", "Say something\nnice", 34f, $"* You complimented {P.name}'s gear."),
			MercyAct("Handshake", "Offer a\nhandshake", 50f, $"* You offered {P.name} a handshake."),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => battle.NewDuelAttack();
	}

	public partial class BattleSystem
	{
		/// <summary>The duel opponent (-1: not in a duel).</summary>
		private int duelWith = -1;
		/// <summary>Our turn to choose (the other one waits, then builds the attack we dodge).</summary>
		private bool duelMyTurn;
		/// <summary>Won, lost, spared or left: nothing more is sent or taken.</summary>
		private bool duelOver;
		private DuelEncounter duelEnc;
		private int duelHp = -1, duelHpMax = 100, duelDef;
		private Vector2 duelRemoteSoul;
		private bool duelRemoteSoulSet;
		private int duelSentHp = -1;
		/// <summary>The attack we're dodging, fed by the other player's pieces.</summary>
		private DuelAttack duelLive;

		public static bool InDuel => Active && Instance.duelWith >= 0;

		/// <summary>FIGHT against a player does this share of its damage (the pieces do half their weapon's too).</summary>
		private const float DuelFightScale = 0.5f;

		/// <summary>The proxy NPC slot: index 200 exists in Terraria's array but is never updated, drawn or synced.</summary>
		private static NPC DuelProxy => Main.npc[Main.maxNPCs];

		public static bool IsDuelProxy(NPC npc) => npc != null && npc.whoAmI >= Main.maxNPCs;

		/// <summary>The server paired us with this player. False if a battle can't start here.</summary>
		internal bool StartDuel(int opponent, bool meFirst)
		{
			Player o = Main.player[opponent];
			if (Active || Player.dead || !o.active || o.dead || ModContent.GetInstance<MercyConfig>()?.TurnBasedBattles == false)
				return false;
			NPC proxy = DuelProxy;
			proxy.SetDefaults(NPCID.TargetDummy);
			proxy.whoAmI = Main.maxNPCs;
			proxy.active = true;
			proxy.immortal = true;
			proxy.dontTakeDamage = false;
			proxy.friendly = false;
			proxy.realLife = -1;
			proxy.damage = 0;
			duelWith = opponent;
			duelMyTurn = meFirst;
			duelOver = false;
			duelHp = o.statLife;
			duelHpMax = o.statLifeMax2;
			duelDef = o.statDefense;
			duelRemoteSoulSet = false;
			duelSentHp = -1;
			duelLive = null;
			SyncDuelProxy();
			duelEnc = new DuelEncounter
			{
				Npc = proxy,
				Opponent = opponent,
				Center = new Vector2(ScreenWidth - HeroFeet.X, HeroFeet.Y - 21f * HeroScale),
			};
			Start(proxy, "duel", new List<NPC> { proxy });
			return true;
		}

		/// <summary>The proxy stands where the other player is, with their HP, so FIGHT, the HP bars and the glide just work.</summary>
		private void SyncDuelProxy()
		{
			if (duelWith < 0)
				return;
			Player o = Main.player[duelWith];
			NPC proxy = DuelProxy;
			proxy.active = true;
			proxy.width = o.width;
			proxy.height = o.height;
			proxy.position = o.position;
			proxy.lifeMax = Math.Max(1, duelHpMax);
			proxy.life = Math.Clamp(duelHp, 0, proxy.lifeMax);
			proxy.defense = duelDef;
			proxy.velocity = Vector2.Zero;
		}

		/// <summary>Every tick of a duel: the proxy, our HP and SOUL to the other player, and whether they're still here.</summary>
		private void UpdateDuel()
		{
			if (duelWith < 0)
				return;
			if (!Main.player[duelWith].active && !duelOver)
			{
				OnDuelEnd();
				return;
			}
			SyncDuelProxy();
			if (duelOver)
				return;
			int hp = Math.Max(0, battleLife);
			if (hp != duelSentHp || time % 30 == 0)
			{
				duelSentHp = hp;
				BattleNet.SendDuel(BattleNet.DuelKind.Hp, w =>
				{
					w.Write((short)hp);
					w.Write((short)Player.statLifeMax2);
					w.Write((short)Player.statDefense);
				});
			}
			if (phase is Phase.EnemyIntro or Phase.EnemyTurn or Phase.EnemyOutro && time % 2 == 0)
				BattleNet.SendDuel(BattleNet.DuelKind.Soul, w =>
				{
					w.Write((short)soul.X);
					w.Write((short)soul.Y);
				});
		}

		/// <summary>Our turn's over (our box closed): the other one chooses, and we wait.</summary>
		private void DuelBoxClosed()
		{
			inv = -1;
			duelMyTurn = false;
			duelLive = null;
			BattleNet.SendDuel(BattleNet.DuelKind.YourTurn);
			EnterDuelWait();
		}

		private void EnterDuelWait()
		{
			defending = false;
			faceAction = FaceNone;
			if (heroPose == HeroPose.Defend)
				SetHeroPose(HeroPose.Idle);
			SetText($"* {Main.player[duelWith].name} is deciding what to do...");
			SetPhase(Phase.DuelWait);
		}

		/// <summary>Lines of our own text box, for the other player to read while they wait.</summary>
		private void DuelShareText(string line)
		{
			if (duelWith < 0 || duelOver || !duelMyTurn || string.IsNullOrEmpty(line))
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Text, w => w.Write(line));
		}

		/// <summary>Our FIGHT (or summon) landed: the other player takes it.</summary>
		private void DuelSendHit(int damage, bool crit)
		{
			if (duelWith < 0 || duelOver)
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Hit, w =>
			{
				w.Write(damage);
				w.Write(crit);
			});
		}

		/// <summary>Their FIGHT landed on us: real damage (their side already took our defense off).</summary>
		private void TakeDuelHit(int damage)
		{
			if (Player.dead || phase == Phase.Death || damage <= 0)
				return;
			Player.immune = false;
			Player.immuneTime = 0;
			HurtingPlayer = true;
			double dealt;
			try
			{
				string by = Main.player[duelWith].name;
				dealt = Player.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{Player.name} was slain by {by}.")),
					damage, 0, knockback: 0f, scalingArmorPenetration: 1f);
			}
			finally
			{
				HurtingPlayer = false;
			}
			Player.immune = false;
			Player.immuneTime = 0;
			battleLife = Player.statLife;
			Sfx("hurt");
			hurtTimer = 0;
			shake = 4;
			HeroNumber((int)dealt, Color.White);
		}

		/// <summary>Our SOUL broke: the other player won.</summary>
		private void DuelOnDeath()
		{
			if (duelWith < 0 || duelOver)
				return;
			duelOver = true;
			BattleNet.SendDuel(BattleNet.DuelKind.Died);
		}

		/// <summary>SPARE with their MERCY full: the duel ends peacefully for both.</summary>
		private void DuelSpare()
		{
			faceAction = FaceSpare;
			string name = Main.player[duelWith].name;
			if (encounter.Mercy < 100f)
			{
				SetHeroPose(HeroPose.Act);
				ShowMessages(new[] { $"* You spared {name}!\n* But their name wasn't YELLOW..." }, StartEnemyTurn);
				return;
			}
			BattleNet.SendDuel(BattleNet.DuelKind.Spared);
			duelOver = true;
			targetEnemy.Out = true;
			battleOver = true;
			SetHeroPose(HeroPose.Victory);
			ShowMessages(new[] { $"* You spared {name}!\n* The duel ends peacefully." }, StartOutro);
		}

		/// <summary>Something from the other player.</summary>
		internal void OnDuel(BattleNet.DuelKind kind, BinaryReader r)
		{
			if (duelWith < 0 || !Active)
				return;
			switch (kind)
			{
				case BattleNet.DuelKind.Hp:
					duelHp = r.ReadInt16();
					duelHpMax = r.ReadInt16();
					duelDef = r.ReadInt16();
					break;
				case BattleNet.DuelKind.Soul:
					duelRemoteSoul = new Vector2(r.ReadInt16(), r.ReadInt16());
					if (!duelRemoteSoulSet)
						soul = duelRemoteSoul;
					duelRemoteSoulSet = true;
					break;
				case BattleNet.DuelKind.Hit:
				{
					int damage = r.ReadInt32();
					r.ReadBoolean();
					if (!duelOver)
						TakeDuelHit(damage);
					break;
				}
				case BattleNet.DuelKind.BoxOpen:
					if (!duelOver && phase == Phase.DuelWait)
						EnterBuild();
					break;
				case BattleNet.DuelKind.Place:
				{
					DuelPiece piece = DuelPiece.Read(r);
					if (duelLive != null && phase is Phase.EnemyIntro or Phase.EnemyTurn)
						duelLive.Incoming.Enqueue(piece);
					break;
				}
				case BattleNet.DuelKind.BuildDone:
					if (duelLive != null)
						duelLive.BuilderDone = true;
					break;
				case BattleNet.DuelKind.YourTurn:
					if (!duelOver && phase is Phase.Build or Phase.DuelWait)
					{
						Bullets.Clear();
						boxTimer = 0;
						duelMyTurn = true;
						BeginPlayerTurn();
					}
					break;
				case BattleNet.DuelKind.Text:
				{
					string line = r.ReadString();
					if (phase == Phase.DuelWait)
						SetText(line);
					break;
				}
				case BattleNet.DuelKind.Spared:
					if (duelOver)
						break;
					duelOver = true;
					targetEnemy.Out = true;
					battleOver = true;
					Bullets.Clear();
					SetHeroPose(HeroPose.Idle);
					ShowMessages(new[] { $"* {Main.player[duelWith].name} spared you.\n* The duel ends peacefully." }, StartOutro);
					break;
				case BattleNet.DuelKind.Died:
					if (duelOver)
						break;
					duelOver = true;
					duelHp = 0;
					SyncDuelProxy();
					// Our own FIGHT did it: the result screen says so when the bar is done
					if (phase is Phase.FightBar or Phase.FightResult)
						break;
					battleOver = true;
					targetEnemy.Out = true;
					Bullets.Clear();
					SetHeroPose(HeroPose.Victory);
					ShowMessages(new[] { $"* YOU WON!\n* {Main.player[duelWith].name} was defeated." }, StartOutro);
					break;
			}
		}

		/// <summary>The other player left the duel (or the game).</summary>
		internal void OnDuelEnd()
		{
			if (duelWith < 0 || !Active || duelOver || phase is Phase.Death or Phase.Outro)
				return;
			duelOver = true;
			targetEnemy.Out = true;
			battleOver = true;
			Bullets.Clear();
			ShowMessages(new[] { $"* {Main.player[duelWith].name} left the battle." }, StartOutro);
		}

		/// <summary>The battle is closing: tell the server we're out, and put the proxy away.</summary>
		private void EndDuel()
		{
			if (duelWith < 0)
				return;
			BattleNet.SendDuelEnd();
			duelWith = -1;
			duelEnc = null;
			duelLive = null;
			DuelProxy.active = false;
			DuelProxy.life = 0;
		}

		/// <summary>The other player in place of the enemy sprite: facing us, gliding in from where they stood.</summary>
		private void DrawDuelOpponent(SpriteBatch sb, Matrix m)
		{
			if (duelWith < 0)
				return;
			Player o = Main.player[duelWith];
			if (!o.active)
				return;
			float glide = FlyProgress();
			float scale = MathHelper.Lerp(WorldPixelScale(), HeroScale, glide);
			Vector2 center = EnemyPosNow;
			if (enemyShake > 0)
				center.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;
			Vector2 feet = center + new Vector2(0f, o.height / 2f * scale);
			float bob = (float)Math.Round(Math.Sin((time + 40) / 20f)) * glide;
			HeroLight = glide >= 1f ? Color.White : Color.Lerp(Lighting.GetColor(o.Center.ToTileCoordinates()), Color.White, glide);
			DrawPlayerPose(sb, m, o, feet + new Vector2(0f, bob), scale, HeroPose.Idle, 0f, 0f, ally: true, facing: -1);
			HeroLight = Color.White;
			DrawSlash(center);
		}

		// ================================================================== the attack we dodge

		/// <summary>An attack built live by the other player: their pieces arrive and turn into bullets.</summary>
		private sealed class DuelAttack : EnemyAttack
		{
			public readonly Queue<DuelPiece> Incoming = new();
			public bool BuilderDone;
			/// <summary>The builder's own copy: shows the pieces, can't hurt anyone.</summary>
			public bool Preview;
			private int doneFor;

			public DuelAttack()
			{
				Duration = DuelAttackTicks;
				Soul = SoulMode.Red;
			}

			public override void Update(BattleSystem b, int tick)
			{
				while (Incoming.Count > 0)
					b.SpawnPiece(Incoming.Dequeue());
				if (Preview || !BuilderDone)
					return;
				// Finished: once the last of it has flown by, the box closes
				if (b.Bullets.All(x => x.Dead || !x.Harmful))
				{
					if (++doneFor > 30)
						b.turnTimer = Math.Min(b.turnTimer, 1);
				}
				else
					doneFor = 0;
			}
		}

		/// <summary>The longest an attack can run (the builder gets <see cref="DuelBuildTicks"/>, plus time for the last pieces).</summary>
		private const int DuelAttackTicks = 22 * 60;

		internal EnemyAttack NewDuelAttack()
		{
			duelLive = new DuelAttack();
			return duelLive;
		}

		/// <summary>We're in the box: the other player starts building.</summary>
		private void DuelBoxOpened()
		{
			if (duelWith >= 0 && !duelOver)
				BattleNet.SendDuel(BattleNet.DuelKind.BoxOpen);
		}

		// ================================================================== pieces

		internal enum PieceKind : byte { Slash, Thrust, Arrow, Spray, Orb, Minion, Bounce, Shot }

		/// <summary>One placed attack piece: what, from which weapon, how hard, where and which way.</summary>
		internal struct DuelPiece
		{
			public PieceKind Kind;
			public int Item, Proj, Damage;
			public Vector2 At, Dir;

			public void Write(BinaryWriter w)
			{
				w.Write((byte)Kind);
				w.Write(Item);
				w.Write(Proj);
				w.Write((short)Damage);
				w.Write((short)At.X);
				w.Write((short)At.Y);
				w.Write(Dir.X);
				w.Write(Dir.Y);
			}

			public static DuelPiece Read(BinaryReader r) => new()
			{
				Kind = (PieceKind)r.ReadByte(),
				Item = r.ReadInt32(),
				Proj = r.ReadInt32(),
				Damage = r.ReadInt16(),
				At = new Vector2(r.ReadInt16(), r.ReadInt16()),
				Dir = new Vector2(r.ReadSingle(), r.ReadSingle()),
			};
		}

		/// <summary>Ticks a piece shows as a warning before its bullets come.</summary>
		private const int PieceWarnTicks = 36;

		private static (Texture2D tex, Rectangle src) ItemTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Item.Length)
				return (null, default);
			Main.instance.LoadItem(type);
			Texture2D tex = TextureAssets.Item[type].Value;
			Rectangle src = Main.itemAnimations[type] != null ? Main.itemAnimations[type].GetFrame(tex) : tex.Bounds;
			return (tex, src);
		}

		private static (Texture2D tex, Rectangle src) ProjTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Projectile.Length)
				return (null, default);
			Main.instance.LoadProjectile(type);
			Texture2D tex = TextureAssets.Projectile[type].Value;
			int frames = Math.Max(1, Main.projFrames[type]);
			return (tex, new Rectangle(0, 0, tex.Width, tex.Height / frames));
		}

		/// <summary>A bullet showing a texture about <paramref name="size"/> px across.</summary>
		private static Bullet TexBullet((Texture2D tex, Rectangle src) t, Vector2 at, Vector2 vel, float size, float hit)
		{
			var b = new Bullet { Position = at, Velocity = vel, HitSize = new Vector2(hit) };
			if (t.tex != null && !Main.dedServ)
			{
				b.Texture = t.tex;
				b.Source = t.src;
				b.Scale = size / Math.Max(1f, Math.Max(t.src.Width, t.src.Height));
			}
			return b;
		}

		/// <summary>A piece's bullets (the same on both screens), after a short warning where it'll come from.</summary>
		private void SpawnPiece(DuelPiece pc)
		{
			Vector2 dir = pc.Dir.LengthSquared() > 0.001f ? Vector2.Normalize(pc.Dir) : Vector2.UnitX;
			// The warning: a flashing ring with a line the way it'll go
			Spawn(new Bullet
			{
				Position = pc.At,
				Harmful = false,
				Lifetime = PieceWarnTicks,
				OnDraw = b =>
				{
					float a = b.Age / 4 % 2 == 0 ? 0.9f : 0.4f;
					DrDraw.Outline(b.Position.X - 9, b.Position.Y - 9, 18, 18, new Color(255, 80, 80) * a, 2);
					DrDraw.Line(b.Position, b.Position + dir * 40f, 2f, new Color(255, 80, 80) * a * 0.7f);
				},
			});
			var item = ItemTexture(pc.Item);
			var proj = pc.Proj > 0 ? ProjTexture(pc.Proj) : item;
			var bullets = new List<Bullet>();
			switch (pc.Kind)
			{
				case PieceKind.Slash:
				{
					Bullet b = TexBullet(item, pc.At, dir * 4.5f, 32f, 18f);
					b.OnUpdate = x => x.Rotation += 0.35f;
					b.Trail = 4;
					bullets.Add(b);
					break;
				}
				case PieceKind.Thrust:
				{
					Bullet b = TexBullet(item, pc.At, dir * 8.5f, 32f, 12f);
					b.RotateWithVelocity = true;
					b.RotationOffset = MathHelper.PiOver4;
					b.Trail = 5;
					bullets.Add(b);
					break;
				}
				case PieceKind.Arrow:
					foreach (float spread in new[] { -0.22f, 0f, 0.22f })
					{
						Bullet b = TexBullet(proj, pc.At, dir.RotatedBy(spread) * 6f, 22f, 8f);
						b.RotateWithVelocity = true;
						b.RotationOffset = MathHelper.PiOver2;
						bullets.Add(b);
					}
					break;
				case PieceKind.Spray:
					foreach (float spread in new[] { -0.36f, -0.18f, 0f, 0.18f, 0.36f })
						bullets.Add(new Bullet { Position = pc.At, Velocity = dir.RotatedBy(spread) * 7f, HitSize = new Vector2(6), Color = new Color(255, 230, 120) });
					break;
				case PieceKind.Orb:
				{
					Bullet b = TexBullet(proj, pc.At, dir * 2.6f, 24f, 12f);
					b.Lifetime = 320;
					b.OnUpdate = x =>
					{
						x.Rotation += 0.1f;
						if (x.Age < 200)
							x.Velocity = Vector2.Lerp(x.Velocity, (SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * 2.6f, 0.03f);
					};
					bullets.Add(b);
					break;
				}
				case PieceKind.Minion:
				{
					Bullet b = TexBullet(proj, pc.At, dir * 1.9f, 28f, 14f);
					b.Lifetime = 260;
					b.OnUpdate = x =>
					{
						x.Velocity = Vector2.Lerp(x.Velocity, (SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * 1.9f, 0.06f);
						x.FlipX = x.Velocity.X < 0f;
					};
					bullets.Add(b);
					break;
				}
				case PieceKind.Bounce:
				{
					Bullet b = TexBullet(item, pc.At, dir * 4.2f, 26f, 14f);
					b.Lifetime = 280;
					b.OnUpdate = x =>
					{
						x.Rotation += 0.25f;
						Rectangle box = Box;
						if (x.Position.X < box.Left + 6 && x.Velocity.X < 0 || x.Position.X > box.Right - 6 && x.Velocity.X > 0)
							x.Velocity.X = -x.Velocity.X;
						if (x.Position.Y < box.Top + 6 && x.Velocity.Y < 0 || x.Position.Y > box.Bottom - 6 && x.Velocity.Y > 0)
							x.Velocity.Y = -x.Velocity.Y;
					};
					bullets.Add(b);
					break;
				}
				default:
				{
					Bullet b = TexBullet(item, pc.At, dir * 6f, 24f, 10f);
					b.RotateWithVelocity = true;
					b.RotationOffset = MathHelper.PiOver4;
					bullets.Add(b);
					break;
				}
			}
			foreach (Bullet b in bullets)
			{
				b.StartDelay = PieceWarnTicks;
				b.DamageMult = Math.Max(1, pc.Damage);
				b.OffscreenMargin = 120f;
				Spawn(b);
			}
		}

		// ================================================================== building (the other player is in the box)

		/// <summary>An attack piece the builder can place: one per kind of weapon they carry, from their best one.</summary>
		private sealed class PieceOption
		{
			public PieceKind Kind;
			public Item Item;
			public int Proj, Damage, Cost;
			public string Label;
		}

		private readonly List<PieceOption> pieceOptions = new();
		private int piecePick;
		private float buildInk;
		private int buildTicks;
		private bool buildDone;
		private Vector2? dragFrom;
		private bool mouseWasDown;
		private DuelAttack buildPreview;

		private const int DuelBuildTicks = 15 * 60;
		private const float InkMax = 100f, InkStart = 45f, InkPerTick = 0.3f;

		private static readonly Dictionary<PieceKind, (string label, int cost)> PieceInfo = new()
		{
			[PieceKind.Slash] = ("SLASH", 25),
			[PieceKind.Thrust] = ("THRUST", 20),
			[PieceKind.Arrow] = ("ARROWS", 25),
			[PieceKind.Spray] = ("SPRAY", 30),
			[PieceKind.Orb] = ("ORB", 35),
			[PieceKind.Minion] = ("MINION", 40),
			[PieceKind.Bounce] = ("BOUNCE", 30),
			[PieceKind.Shot] = ("SHOT", 15),
		};

		private static PieceKind Classify(Item it)
		{
			if (it.shoot > ProjectileID.None && ProjectileID.Sets.IsAWhip[it.shoot])
				return PieceKind.Thrust;
			if (it.CountsAsClass(DamageClass.Summon))
				return PieceKind.Minion;
			if (it.useAmmo == AmmoID.Arrow)
				return PieceKind.Arrow;
			if (it.useAmmo == AmmoID.Bullet)
				return PieceKind.Spray;
			if (it.CountsAsClass(DamageClass.Magic))
				return PieceKind.Orb;
			if (it.shoot > ProjectileID.None && it.noUseGraphic)
				return ItemID.Sets.Spears[it.type] ? PieceKind.Thrust : PieceKind.Bounce;
			if (it.CountsAsClass(DamageClass.Melee))
				return PieceKind.Slash;
			return PieceKind.Shot;
		}

		/// <summary>The pieces this player's weapons give: the strongest weapon of each kind.</summary>
		private void RefreshPieces()
		{
			pieceOptions.Clear();
			var best = new Dictionary<PieceKind, Item>();
			for (int i = 0; i < 50; i++)
			{
				Item it = Player.inventory[i];
				if (it == null || it.IsAir || it.damage <= 0 || it.accessory || it.ammo > 0 || it.useStyle == ItemUseStyleID.None || it.createTile >= 0 && it.pick <= 0)
					continue;
				if (it.consumable && it.shoot <= ProjectileID.None)
					continue;
				PieceKind k = Classify(it);
				if (!best.TryGetValue(k, out Item have) || Player.GetWeaponDamage(it) > Player.GetWeaponDamage(have))
					best[k] = it;
			}
			// Six fit in the panel: the strongest kinds
			var keep = best.OrderByDescending(kv => Player.GetWeaponDamage(kv.Value)).Take(6).ToList();
			foreach (var (k, it) in keep.OrderBy(kv => (int)kv.Key))
			{
				int proj = it.shoot is > ProjectileID.None and not ProjectileID.AbigailCounter and not ProjectileID.StormTigerGem ? it.shoot : 0;
				if (k == PieceKind.Arrow)
					proj = it.shoot > ProjectileID.None && it.shoot != ProjectileID.WoodenArrowFriendly ? it.shoot : ProjectileID.WoodenArrowFriendly;
				pieceOptions.Add(new PieceOption
				{
					Kind = k,
					Item = it,
					Proj = proj,
					Damage = Math.Max(1, (int)(Player.GetWeaponDamage(it) * 0.5f)),
					Cost = PieceInfo[k].cost,
					Label = PieceInfo[k].label,
				});
			}
			// Nothing to fight with: fists
			if (pieceOptions.Count == 0)
				pieceOptions.Add(new PieceOption { Kind = PieceKind.Shot, Item = null, Proj = 0, Damage = 3, Cost = 15, Label = "PUNCH" });
			piecePick = Math.Clamp(piecePick, 0, pieceOptions.Count - 1);
		}

		private void EnterBuild()
		{
			Bullets.Clear();
			boxAfterimages.Clear();
			RefreshPieces();
			buildInk = InkStart;
			buildTicks = DuelBuildTicks;
			buildDone = false;
			dragFrom = null;
			mouseWasDown = Main.mouseLeft;
			buildPreview = new DuelAttack { Preview = true };
			boxTimer = 0;
			if (!duelRemoteSoulSet)
				soul = new Vector2(BoxCenterX - SoulSize / 2f, BoxCenterY - SoulSize / 2f);
			BeginSoulMode(SoulMode.Red);
			SetText("");
			SetPhase(Phase.Build);
			Sfx("boost");
		}

		/// <summary>The mouse on the battle screen.</summary>
		private Vector2 MouseBattle()
		{
			ComputeScreenTransform();
			// The raw window position (Main.mouseX can be in UI-scaled units, the battle is drawn unscaled)
			return new Vector2((Terraria.GameInput.PlayerInput.MouseX - drOx) / drScale, (Terraria.GameInput.PlayerInput.MouseY - drOy) / drScale);
		}

		private float PaletteTop => ScreenHeight - panel + 44f;

		private Rectangle PieceButton(int i) => new(16 + i * 86, (int)PaletteTop, 82, 60);
		private Rectangle DoneButton => new(540, (int)PaletteTop + 30, 84, 30);

		private void UpdateBuild()
		{
			boxTimer = Math.Min(BoxGrowTicks, boxTimer + 1);
			if (duelRemoteSoulSet)
				soul = Vector2.Lerp(soul, duelRemoteSoul, 0.5f);
			buildPreview?.Update(this, phaseTicks);
			// The preview's bullets move like the real ones, but nothing hits
			updatingBullets = true;
			foreach (Bullet b in Bullets)
				b.Update();
			updatingBullets = false;
			Bullets.AddRange(spawnedDuringUpdate);
			spawnedDuringUpdate.Clear();
			Bullets.RemoveAll(b => b.Dead);

			if (buildDone)
				return;
			buildInk = Math.Min(InkMax, buildInk + InkPerTick);
			if (--buildTicks <= 0)
			{
				FinishBuild();
				return;
			}
			// Number keys pick a piece too
			for (int i = 0; i < Math.Min(9, pieceOptions.Count); i++)
				if (Main.keyState.IsKeyDown(Keys.D1 + i) && !Main.oldKeyState.IsKeyDown(Keys.D1 + i))
				{
					piecePick = i;
					Sfx("menumove");
				}

			Player.mouseInterface = true;
			bool down = Main.mouseLeft;
			Vector2 mouse = MouseBattle();
			if (down && !mouseWasDown)
			{
				// The palette and the DONE button, or the start of a placement
				int hit = Enumerable.Range(0, pieceOptions.Count).FirstOrDefault(i => PieceButton(i).Contains(mouse.ToPoint()), -1);
				if (hit >= 0)
				{
					piecePick = hit;
					Sfx("menumove");
				}
				else if (DoneButton.Contains(mouse.ToPoint()))
					FinishBuild();
				else if (mouse.Y < PaletteTop - 10f)
					dragFrom = mouse;
			}
			if (!down && mouseWasDown && dragFrom is Vector2 from)
			{
				dragFrom = null;
				PlacePiece(from, mouse);
			}
			// Right click: let go of a placement
			if (Main.mouseRight)
				dragFrom = null;
			mouseWasDown = down;
		}

		private void PlacePiece(Vector2 at, Vector2 to)
		{
			if (pieceOptions.Count == 0)
				return;
			PieceOption o = pieceOptions[Math.Clamp(piecePick, 0, pieceOptions.Count - 1)];
			if (buildInk < o.Cost)
			{
				Sfx("cantselect");
				return;
			}
			// A click aims at the SOUL; a drag sets the direction
			Vector2 dir = to - at;
			if (dir.Length() < 8f)
				dir = SoulCenter - at;
			if (dir.LengthSquared() < 0.01f)
				dir = Vector2.UnitX;
			dir.Normalize();
			buildInk -= o.Cost;
			var piece = new DuelPiece { Kind = o.Kind, Item = o.Item?.type ?? 0, Proj = o.Proj, Damage = o.Damage, At = at, Dir = dir };
			BattleNet.SendDuel(BattleNet.DuelKind.Place, piece.Write);
			buildPreview?.Incoming.Enqueue(piece);
			Sfx("select");
		}

		private void FinishBuild()
		{
			if (buildDone)
				return;
			buildDone = true;
			dragFrom = null;
			BattleNet.SendDuel(BattleNet.DuelKind.BuildDone);
			Sfx("select");
		}

		/// <summary>Over the battle screen while building: the placement being dragged and the piece at the mouse.</summary>
		private void DrawBuildOverlay()
		{
			if (phase != Phase.Build || buildDone || pieceOptions.Count == 0)
				return;
			Vector2 mouse = MouseBattle();
			PieceOption o = pieceOptions[Math.Clamp(piecePick, 0, pieceOptions.Count - 1)];
			bool afford = buildInk >= o.Cost;
			Color c = afford ? Color.White : new Color(255, 80, 80);
			if (dragFrom is Vector2 from)
			{
				DrDraw.Line(from, mouse, 2f, c * 0.8f);
				DrDraw.Outline(from.X - 8, from.Y - 8, 16, 16, c, 2);
			}
			else if (mouse.Y < PaletteTop - 10f)
				DrawPieceIcon(o, mouse, 22f, 0.6f);
		}

		private void DrawPieceIcon(PieceOption o, Vector2 at, float size, float alpha)
		{
			var t = o.Proj > 0 && o.Kind is PieceKind.Arrow or PieceKind.Orb or PieceKind.Minion ? ProjTexture(o.Proj) : ItemTexture(o.Item?.type ?? 0);
			if (t.tex == null || Main.dedServ)
			{
				DrDraw.Rect(at.X - 4, at.Y - 4, 8, 8, Color.White * alpha);
				return;
			}
			float scale = size / Math.Max(1f, Math.Max(t.src.Width, t.src.Height));
			DrDraw.Sb.Draw(t.tex, at, t.src, Color.White * alpha, 0f, t.src.Size() / 2f, scale, SpriteEffects.None, 0f);
		}

		/// <summary>The bottom panel while building: the pieces, the ink, the time left and DONE.</summary>
		private void DrawBuildPalette()
		{
			float top = PaletteTop;
			if (buildDone)
			{
				DrDraw.Text("* Your attack is set.\n* Watch them dodge it!", 30, top, Color.White);
				return;
			}
			// The hint goes at the top of the screen (the nameplate sits just above the panel)
			const string hint = "BUILD YOUR ATTACK: click to place, drag to aim, 1-6 to pick";
			DrDraw.Text(hint, ScreenWidth / 2f - DrDraw.Measure(hint, DrDraw.SmallFont) * 0.4f, 10, new Color(255, 220, 64), DrDraw.SmallFont, 0.8f);
			for (int i = 0; i < pieceOptions.Count && i < 6; i++)
			{
				PieceOption o = pieceOptions[i];
				Rectangle r = PieceButton(i);
				bool sel = i == piecePick;
				bool afford = buildInk >= o.Cost;
				DrDraw.Rect(r.X, r.Y, r.Width, r.Height, (sel ? new Color(60, 40, 10) : new Color(20, 20, 20)));
				DrDraw.Outline(r.X, r.Y, r.Width, r.Height, sel ? new Color(255, 200, 40) : new Color(90, 90, 90), sel ? 2 : 1);
				DrawPieceIcon(o, new Vector2(r.Center.X, r.Y + 17), 24f, afford ? 1f : 0.4f);
				DrDraw.Text(o.Label, r.X + 4, r.Y + 32, afford ? Color.White : new Color(128, 128, 128), DrDraw.SmallFont, 0.7f);
				DrDraw.Text($"{o.Cost}", r.Right - 18, r.Y + 2, afford ? new Color(120, 200, 255) : new Color(255, 80, 80), DrDraw.SmallFont, 0.7f);
				DrDraw.Text($"{o.Damage}", r.X + 4, r.Y + 46, new Color(255, 200, 80), DrDraw.SmallFont, 0.6f);
			}
			// Ink and time
			float inkW = 84f * buildInk / InkMax;
			DrDraw.Text("INK", 540, top - 4, new Color(120, 200, 255), DrDraw.SmallFont, 0.7f);
			DrDraw.Rect(540, top + 10, 84, 8, new Color(30, 30, 60));
			DrDraw.Rect(540, top + 10, inkW, 8, new Color(120, 200, 255));
			DrDraw.Text($"{(buildTicks + 59) / 60}s", 590, top - 4, Color.White, DrDraw.SmallFont, 0.7f);
			Rectangle d = DoneButton;
			DrDraw.Rect(d.X, d.Y, d.Width, d.Height, new Color(20, 60, 20));
			DrDraw.Outline(d.X, d.Y, d.Width, d.Height, new Color(80, 255, 80), 2);
			DrDraw.Text("DONE", d.X + 20, d.Y + 6, Color.White, DrDraw.SmallFont, 0.9f);
		}
	}
}
