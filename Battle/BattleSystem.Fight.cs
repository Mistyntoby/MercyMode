using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using MercyMode.Deltarune;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>One weapon the player can FIGHT with, and what it does in a battle.</summary>
	public sealed class WeaponOption
	{
		/// <summary>Inventory slot, or -1 for bare hands.</summary>
		public int Slot = -1;
		/// <summary>Null for bare hands.</summary>
		public Item Item;
		/// <summary>Terraria's damage for one shot: the weapon's damage with every modifier, plus the ammo's.</summary>
		public int ShotDamage;
		/// <summary>Bolts on the FIGHT bar: faster weapons get more.</summary>
		public int Bolts = 1;
		/// <summary>How many Terraria hits each bolt is worth (see <see cref="BattleSystem.TurnSeconds"/>).</summary>
		public float HitShare = 1f;
		/// <summary>Crit chance in percent.</summary>
		public int Crit;
		/// <summary>Mana per bolt, 0 for none.</summary>
		public int ManaCost;
		/// <summary>Ammo (or throwables) left, -1 when the weapon doesn't use any.</summary>
		public int Ammo = -1;
		public bool UsesAmmo, Throwable;
		/// <summary>What it shoots (for the projectile on screen), 0 for none.</summary>
		public int Projectile;
		/// <summary>Why it can't be used right now (no ammo, no mana), or null.</summary>
		public string Problem;

		public bool Usable => Problem == null;
		/// <summary>Fires something at the enemy (gun, bow, spell, thrown weapon) instead of swinging at it.</summary>
		public bool Shoots => Item != null && (Throwable || (UsesAmmo || ManaCost > 0 || Item.DamageType.CountsAsClass(DamageClass.Ranged)
			|| Item.DamageType.CountsAsClass(DamageClass.Magic)) && Item.useStyle != ItemUseStyleID.Swing);
		public string Name => Item?.Name ?? "Bare hands";
		/// <summary>Damage of a perfect turn before defense and crits: what the menu compares.</summary>
		public int PerfectTurn => (int)Math.Round(ShotDamage * HitShare * Bolts * BattleSystem.DamageScale);
	}

	/// <summary>
	/// FIGHT: choose a weapon (with its stats), choose the enemy, then time a bolt for each hit. Damage follows the
	/// weapon's real Terraria numbers: ammo is added and used up, magic costs mana, and Terraria's crits roll on top
	/// of the timing.
	/// </summary>
	public partial class BattleSystem
	{
		// ---- balance ----
		/// <summary>A perfect FIGHT turn deals what this many seconds of the weapon's Terraria DPS would.</summary>
		public const float TurnSeconds = 8f;
		/// <summary>One bolt per this many ticks of use time (so ~45-tick weapons get one, fast ones up to four).</summary>
		private const float BoltTierTicks = 45f;
		private const int MaxBolts = 4;
		/// <summary>
		/// The config's damage multiplier (1 = the balance above), times the square root of the difficulty's enemy
		/// health multiplier: Expert (2x health) fights last ~1.4x as long instead of 2x, Master (3x) ~1.7x.
		/// </summary>
		public static float DamageScale =>
			(ModContent.GetInstance<MercyConfig>()?.FightDamageMultiplier ?? 1f) * (float)Math.Sqrt(Math.Max(1f, Main.GameModeInfo.EnemyMaxLifeMultiplier));

		/// <summary>The weapon chosen in this battle's FIGHT menu (inventory slot and type), or -1 for the held one.</summary>
		private int fightWeaponSlot = -1, fightWeaponType;
		private List<WeaponOption> weaponOptions = new();

		/// <summary>Every weapon in the inventory, best first; bare hands if there are none.</summary>
		public List<WeaponOption> Weapons()
		{
			var list = new List<WeaponOption>();
			var seen = new HashSet<int>();
			for (int i = 0; i < 50; i++)
			{
				Item item = Player.inventory[i];
				if (!IsWeapon(item) || !seen.Add(item.type))
					continue;
				list.Add(Describe(item, i));
			}
			if (list.Count == 0)
				list.Add(Describe(null, -1));
			// Usable first, then strongest; tools after real weapons
			return list.OrderBy(w => w.Usable ? 0 : 1)
				.ThenBy(w => w.Item != null && (w.Item.pick > 0 || w.Item.axe > 0 || w.Item.hammer > 0) ? 1 : 0)
				.ThenByDescending(w => w.PerfectTurn).ToList();
		}

		private static bool IsWeapon(Item item)
		{
			if (item == null || item.IsAir || item.damage <= 0 || item.accessory || item.ammo != AmmoID.None || item.useStyle == ItemUseStyleID.None)
				return false;
			// Summon staves make minions; whips are fine
			if (item.DamageType.CountsAsClass(DamageClass.Summon) && !(item.shoot > ProjectileID.None && ProjectileID.Sets.IsAWhip[item.shoot]))
				return false;
			return true;
		}

		/// <summary>Works out a weapon's battle stats from its Terraria stats.</summary>
		private WeaponOption Describe(Item item, int slot)
		{
			var w = new WeaponOption { Slot = slot, Item = item };
			if (item == null)
			{
				w.ShotDamage = 5;
				Speed(w, 20);
				return w;
			}

			w.ShotDamage = Math.Max(1, Player.GetWeaponDamage(item));
			w.Crit = Player.GetWeaponCrit(item);
			w.Projectile = item.shoot;
			if (item.useAmmo > 0)
			{
				w.UsesAmmo = true;
				Item ammo = Player.ChooseAmmo(item);
				w.Ammo = ammo == null ? 0 : Player.inventory.Where(i => !i.IsAir && i.type == ammo.type).Sum(i => i.stack);
				if (ammo != null && Player.PickAmmo(item, out int proj, out _, out int damage, out _, out _, dontConsume: true))
				{
					w.ShotDamage = Math.Max(1, damage);
					w.Projectile = proj;
				}
				else
				{
					w.Problem = "No ammo";
				}
			}
			else if (item.consumable)
			{
				w.Throwable = true;
				w.Ammo = Player.inventory.Where(i => !i.IsAir && i.type == item.type).Sum(i => i.stack);
			}
			if (item.mana > 0)
			{
				w.ManaCost = Player.GetManaCost(item);
				if (Player.statMana < w.ManaCost && w.Problem == null)
					w.Problem = "No mana";
			}
			Speed(w, Math.Max(1, item.useAnimation));
			return w;
		}

		/// <summary>Bolts and the share of Terraria hits each one stands for, from the use time in ticks.</summary>
		private static void Speed(WeaponOption w, int useTicks)
		{
			float hits = TurnSeconds * 60f / useTicks;
			w.Bolts = Math.Clamp((int)Math.Round(BoltTierTicks / useTicks), 1, MaxBolts);
			w.HitShare = hits / w.Bolts;
		}

		/// <summary>The weapon FIGHT uses now: the one chosen this battle, else the held weapon, else the best one.</summary>
		private WeaponOption CurrentWeapon()
		{
			List<WeaponOption> all = Weapons();
			if (fightWeaponSlot >= 0)
			{
				WeaponOption chosen = all.FirstOrDefault(w => w.Slot == fightWeaponSlot && w.Item?.type == fightWeaponType)
					?? all.FirstOrDefault(w => w.Item?.type == fightWeaponType);
				if (chosen != null)
					return chosen;
			}
			return all.FirstOrDefault(w => w.Item != null && w.Slot == Player.selectedItem) ?? all[0];
		}

		/// <summary>What the hero holds on the battle screen: the highlighted weapon while choosing, else the current one.</summary>
		private Item WeaponForDisplay()
		{
			if (phase == Phase.WeaponSelect && listIndex < weaponOptions.Count)
				return weaponOptions[listIndex].Item;
			return fightWeapon?.Item ?? CurrentWeapon().Item;
		}

		// ================================================================== weapon menu

		private void OpenWeaponSelect()
		{
			weaponOptions = Weapons();
			WeaponOption current = CurrentWeapon();
			listIndex = Math.Max(0, weaponOptions.FindIndex(w => w.Slot == current.Slot && w.Item?.type == current.Item?.type));
			SetPhase(Phase.WeaponSelect);
		}

		private void UpdateWeaponSelect()
		{
			int before = listIndex;
			if (Pressed(Microsoft.Xna.Framework.Input.Keys.Down) && listIndex + 1 < weaponOptions.Count)
				listIndex++;
			if (Pressed(Microsoft.Xna.Framework.Input.Keys.Up) && listIndex > 0)
				listIndex--;
			if (listIndex != before)
				Sfx("menumove");
			if (Cancel)
			{
				SetPhase(Phase.Menu);
				return;
			}
			if (!Confirm)
				return;

			WeaponOption pick = weaponOptions[listIndex];
			if (!pick.Usable)
			{
				Sfx("cantselect");
				return;
			}
			Sfx("select");
			fightWeaponSlot = pick.Slot;
			fightWeaponType = pick.Item?.type ?? 0;
			// Weapons on the hotbar get equipped for real
			if (pick.Slot >= 0 && pick.Slot < 10)
				Player.selectedItem = pick.Slot;
			OpenEnemySelect();
		}

		private void DrawWeaponSelect(float y)
		{
			if (weaponOptions.Count == 0)
				return;
			WeaponOption equipped = CurrentWeapon();
			const int rows = 3;
			int first = Math.Clamp(listIndex - rows + 1, 0, Math.Max(0, weaponOptions.Count - rows));
			for (int i = first; i < Math.Min(weaponOptions.Count, first + rows); i++)
			{
				WeaponOption w = weaponOptions[i];
				float ey = y + (i - first) * 30;
				string name = w.Name;
				float scale = Math.Min(1f, 270f / Math.Max(1f, DrDraw.Measure(name, DrDraw.BigFont)));
				DrDraw.Text(name, 80, ey + (1f - scale) * 10f, w.Usable ? Color.White : new Color(128, 128, 128), DrDraw.BigFont, scale);
				if (i == listIndex)
					DrawHeartCursor(55, ey + 10);

				// Better or worse than what's equipped
				bool isEquipped = w.Slot == equipped.Slot && w.Item?.type == equipped.Item?.type;
				if (isEquipped)
					DrDraw.Text("E", 360, ey + 6, new Color(128, 128, 128), DrDraw.SmallFont);
				else if (w.PerfectTurn > equipped.PerfectTurn)
					StatArrow(362, ey + 8, true);
				else if (w.PerfectTurn < equipped.PerfectTurn)
					StatArrow(362, ey + 8, false);
			}
			if (first > 0)
				DrDraw.Text("^", 380, y - 4, Color.White, DrDraw.SmallFont);
			if (first + rows < weaponOptions.Count)
				DrDraw.Text("v", 380, y + 70, Color.White, DrDraw.SmallFont);

			// Stats of the highlighted weapon, on the right like ITEM's description
			WeaponOption sel = weaponOptions[Math.Clamp(listIndex, 0, weaponOptions.Count - 1)];
			Color gray = new(160, 160, 160);
			float sx = 410, sy = y - 6;
			DrDraw.Text($"ATK {sel.PerfectTurn}", sx, sy, Color.White, DrDraw.SmallFont);
			if (sel.PerfectTurn != equipped.PerfectTurn)
				StatArrow(sx + DrDraw.Measure($"ATK {sel.PerfectTurn}", DrDraw.SmallFont) + 8, sy + 3, sel.PerfectTurn > equipped.PerfectTurn);
			DrDraw.Text($"{sel.Bolts} HIT{(sel.Bolts > 1 ? "S" : "")}  CRIT {sel.Crit}%", sx, sy + 20, gray, DrDraw.SmallFont);
			string cost = sel.Problem != null ? sel.Problem.ToUpperInvariant()
				: sel.ManaCost > 0 ? $"MANA {sel.ManaCost}/HIT ({Player.statMana})"
				: sel.Ammo >= 0 ? $"{(sel.Throwable ? "LEFT" : "AMMO")} {sel.Ammo}"
				: "";
			if (cost.Length > 0)
				DrDraw.Text(cost, sx, sy + 40, sel.Problem != null ? new Color(255, 80, 80) : gray, DrDraw.SmallFont);
			DrDraw.Text($"DMG {sel.ShotDamage}", sx, sy + 60, gray, DrDraw.SmallFont);
		}

		/// <summary>A small pixel arrow: green pointing up for an upgrade, red pointing down for a downgrade.</summary>
		private static void StatArrow(float x, float y, bool up)
		{
			Color c = up ? new Color(0, 255, 0) : new Color(255, 60, 60);
			for (int row = 0; row < 5; row++)
			{
				int w = 1 + row * 2;
				float ry = up ? y + row * 2 : y + (4 - row) * 2;
				DrDraw.Rect(x + 4 - row, ry, w, 2, c);
			}
			DrDraw.Rect(x + 3, up ? y + 10 : y - 4, 3, 4, c);
		}

		// ================================================================== the FIGHT bar

		private sealed class FightBolt
		{
			/// <summary>boltframe: the frame it reaches the target line.</summary>
			public float Frame;
			public bool Alive = true;
		}

		private sealed class PendingHit
		{
			public int Points;
			public bool Crit;
			public int Damage;
			public float Ticks;
			public bool Ranged;
		}

		private sealed class BoltBurst
		{
			public Vector2 Position;
			public int Timer = 20;
			public bool Perfect;
		}

		private WeaponOption fightWeapon;
		private readonly List<FightBolt> bolts = new();
		private readonly List<PendingHit> pendingHits = new();
		private readonly List<BoltBurst> boltBursts = new();
		private int hitsTried, hitsLanded;
		/// <summary>Best press this turn (for the size of the slash).</summary>
		private int bestPoints;

		private void StartFightBar()
		{
			fightWeapon = CurrentWeapon();
			boltX = 0;
			fightFade = 0;
			hitsTried = hitsLanded = 0;
			bestPoints = 0;
			bolts.Clear();
			pendingHits.Clear();
			boltBursts.Clear();
			// obj_attackpress: the first bolt reaches the line at frame 29, the next ones 12 or 18 frames apart
			float frame = BoltStartFrame;
			for (int i = 0; i < fightWeapon.Bolts; i++)
			{
				bolts.Add(new FightBolt { Frame = frame });
				frame += Main.rand.NextBool() ? 12f : 18f;
			}
			SetPhase(Phase.FightBar);
		}

		private void UpdateFightBar()
		{
			boltX += 1f / TicksPerFrame;
			// Deltarune checks presses once per frame, so score on the frame this tick belongs to
			int now = (int)Math.Floor(boltX);

			if (Confirm)
			{
				// scr_boltcheck_onebutton: the alive bolt in the window that's closest to (or furthest past) the line
				FightBolt best = null;
				int bestClose = int.MaxValue;
				foreach (FightBolt b in bolts)
				{
					if (!b.Alive)
						continue;
					int close = (int)b.Frame - now;
					if (close < BoltWindowEarly && close > -BoltWindowLate && close < bestClose)
					{
						best = b;
						bestClose = close;
					}
				}
				if (best != null)
					PressBolt(best, bestClose);
			}

			foreach (FightBolt b in bolts)
				// Past the line, or the enemy already went down to an earlier bolt
				if (b.Alive && (b.Frame - boltX < -BoltWindowLate || !encounter.Alive))
					b.Alive = false;

			for (int i = pendingHits.Count - 1; i >= 0; i--)
			{
				PendingHit h = pendingHits[i];
				h.Ticks--;
				if (h.Ticks <= 0)
				{
					pendingHits.RemoveAt(i);
					ResolveHit(h);
				}
			}
			for (int i = boltBursts.Count - 1; i >= 0; i--)
				if (--boltBursts[i].Timer <= 0)
					boltBursts.RemoveAt(i);

			if (bolts.All(b => !b.Alive) && pendingHits.Count == 0)
			{
				if (hitsLanded == 0)
					EnemyNumber(0, HeroDamageColor, DamageNumber.MissFrame);
				SetPhase(Phase.FightResult);
			}
		}

		/// <summary>A bolt pressed in time: pays for the shot, swings or fires, and queues the hit.</summary>
		private void PressBolt(FightBolt bolt, int close)
		{
			bolt.Alive = false;
			int points = BoltPoints(close);
			bestPoints = Math.Max(bestPoints, points);
			boltBursts.Add(new BoltBurst
			{
				Position = new Vector2(FightBarX + 80 + (bolt.Frame - boltX) * BoltSpeed, FightBarY),
				Perfect = points == 150,
			});

			// Each bolt is one shot: it needs its ammo, mana or throwable
			WeaponOption w = fightWeapon;
			int damage = w.ShotDamage;
			int projectile = w.Projectile;
			if (w.Item != null)
			{
				if (w.UsesAmmo)
				{
					if (!Player.PickAmmo(w.Item, out projectile, out _, out damage, out _, out _, dontConsume: false))
					{
						Fizzle("* Out of ammo!");
						return;
					}
				}
				else if (w.Throwable)
				{
					Item stack = Player.inventory.FirstOrDefault(i => !i.IsAir && i.type == w.Item.type);
					if (stack == null)
					{
						Fizzle("* Nothing left to throw!");
						return;
					}
					if (ItemLoader.ConsumeItem(stack, Player) && --stack.stack <= 0)
						stack.TurnToAir();
				}
				if (w.ManaCost > 0 && !Player.CheckMana(w.Item, pay: true))
				{
					Fizzle("* Not enough mana!");
					return;
				}
			}

			hitsTried++;
			SetHeroPose(HeroPose.Attack);
			bool ranged = w.Shoots;
			if (ranged)
				FireShot(w, projectile);
			else
				Sfx("slash");
			if (points == 150)
			{
				Sfx("crit");
				for (int i = 0; i < 3; i++)
					AddEffect(new CritSparkle(new Vector2(HeroX + 68 + Main.rand.NextFloat(50f), HeroY + 30 + Main.rand.NextFloat(30f))));
			}

			pendingHits.Add(new PendingHit
			{
				Points = points,
				Damage = Math.Max(1, damage),
				// Terraria's crit roll, on top of the timing
				Crit = Main.rand.Next(100) < w.Crit,
				// obj_heroparent: the hit lands 10 frames after the swing (and a shot takes about as long to fly)
				Ticks = 10 * TicksPerFrame,
				Ranged = ranged,
			});
		}

		private void Fizzle(string why)
		{
			Sfx("cantselect");
			hitsTried++;
			AddEffect(new DamageNumber(HeroX + 40, HeroY + 10, 0, new Color(160, 160, 160), DamageNumber.MissFrame));
			Mod.Logger.Debug("FIGHT bolt fizzled: " + why);
		}

		/// <summary>The colour of damage the hero deals: merge_color(c_aqua, c_white, 0.5). Crits are gold.</summary>
		private static readonly Color HeroDamageColor = new(128, 255, 255);
		private static readonly Color HeroCritColor = new(255, 220, 64);

		private void ResolveHit(PendingHit hit)
		{
			// An earlier bolt may already have won the fight
			if (!encounter.Alive)
				return;

			float timing = hit.Points / 150f;
			int raw = Math.Max(1, (int)Math.Round(hit.Damage * fightWeapon.HitShare * timing * DamageScale));
			// A breakable boss: the part picked in the enemy list (or the next one, if an earlier hit broke it)
			NPC chosen = encounter.ChosenPart;
			NPC core = encounter.TargetableParts ? encounter.CorePart : null;
			NPC target = encounter.TargetableParts && Encounter.CanHit(chosen) ? chosen : encounter.StrikeTarget();
			Vector2 spot = PartSpot(target);
			slashPart = encounter.TargetableParts ? target.whoAmI : -1;
			int dealt = target.SimpleStrikeNPC(raw, Player.direction, crit: hit.Crit, knockBack: 0f,
				damageType: fightWeapon.Item?.DamageType ?? DamageClass.Melee);

			Sfx("damage");
			if (!hit.Ranged)
				slashTimer = 0;
			else
				AddEffect(new ShotImpact(spot + Main.rand.NextVector2Circular(14f, 14f)));
			enemyShake = 18;
			if (hit.Crit)
				Sfx("crit");
			// Several hits stack their numbers upward
			EnemyNumber(dealt > 0 ? dealt : 0, hit.Crit ? HeroCritColor : HeroDamageColor, dealt > 0 ? -1 : DamageNumber.MissFrame,
				yOffset: -18f * hitsLanded, at: spot);
			hitsLanded++;

			if (encounter.TargetableParts && (!target.active || target.life <= 0 || !encounter.Members().Contains(target)))
				BreakPart(target, core, spot);

			// A killing blow: the enemy breaks apart right away (obj_deathanim), from how it looked a moment ago
			if (!encounter.Alive && enemyOverride == null)
			{
				PlayEnemyDeath();
				targetEnemy.Out = true;
				OnEnemyDefeated(targetEnemy);
				// The rest of a multi-hit attack carries on into the next enemy
				RetargetIfNeeded();
			}

			if (dealt > 0)
			{
				// round(points / 10) tension per hit in Deltarune; shared out over the bolts so fast weapons don't get more
				var mp = Player.GetModPlayer<MercyPlayer>();
				mp.TP = Math.Min(100f, mp.TP + (float)Math.Round(hit.Points / HitTensionDivisor) / fightWeapon.Bolts * TensionToTP);
			}
		}

		/// <summary>
		/// A part of a breakable boss was destroyed: it bursts, and the rest of a multi-hit attack moves to the next
		/// part. Losing its core takes the rest of the boss with it.
		/// </summary>
		private void BreakPart(NPC part, NPC core, Vector2 spot)
		{
			AttackSfx.Explosion();
			ShakeScreen(4);
			AddEffect(new Shockwave(spot, Color.White, 46f));
			Sparks.Burst(this, spot, 14, new Color(255, 230, 180), 3.2f, 0.06f);
			if (core != null && part == core)
			{
				foreach (NPC m in encounter.Members().ToList())
				{
					if (m == core)
						continue;
					m.life = 0;
					m.active = false;
				}
			}
			encounter.ChosenPart = encounter.TargetParts().FirstOrDefault(Encounter.CanHit);
		}

		/// <summary>Where a part of the enemy is on the battle screen (as last drawn), or the enemy's spot.</summary>
		private Vector2 PartSpot(NPC part) =>
			part != null && encounter.TargetableParts && partScreen.TryGetValue(part.whoAmI, out Vector2 at) ? at : encounter.ScreenCenter;

		private void UpdateFightResult()
		{
			for (int i = boltBursts.Count - 1; i >= 0; i--)
				if (--boltBursts[i].Timer <= 0)
					boltBursts.RemoveAt(i);
			if (phaseTicks > FightPostTicks)
				fightFade += FightFadePerTick;
			if (fightFade < 1f)
				return;

			if (!encounter.Alive)
			{
				if (LivingEnemies.Count == 0)
				{
					battleOver = true;
					SetHeroPose(HeroPose.Victory);
					string won = enemies.Count == 1 ? $"* {encounter.Name} was defeated." : "* Every enemy was defeated.";
					ShowMessages(new[] { "* YOU WON!\n" + won }, StartOutro);
					return;
				}
				// Others are still fighting: straight on to their turn, like Deltarune
				StartEnemyTurn();
				return;
			}
			StartEnemyTurn();
		}

		// ================================================================== shooting

		/// <summary>Where shots leave the weapon on the battle screen.</summary>
		private Vector2 Muzzle()
		{
			Vector2 feet = HeroFeetNow;
			float reach = 18f;
			Item item = fightWeapon?.Item;
			if (item != null)
			{
				Main.instance.LoadItem(item.type);
				reach = TextureAssets.Item[item.type].Value.Width * HeroScaleNow * 0.8f;
			}
			return feet + new Vector2(10f + reach, -36f * HeroScaleNow / BattleCharacterScale);
		}

		/// <summary>Gun, bow or spell: its own sound, a kick back, a flash at the muzzle and the projectile flying over.</summary>
		private void FireShot(WeaponOption w, int projectile)
		{
			if (w.Item?.UseSound is Terraria.Audio.SoundStyle use)
				AttackSfx.Vanilla(use);
			else
				Sfx("attack");
			heroRecoil = RecoilFrames;
			Vector2 from = Muzzle();
			NPC aim = Encounter.CanHit(encounter.ChosenPart) ? encounter.ChosenPart : null;
			Vector2 to = PartSpot(aim) + Main.rand.NextVector2Circular(14f, 14f);
			AddEffect(new MuzzleFlash(from));
			AddEffect(new ShotProjectile(projectile, from, to, 10f));
		}
	}

	/// <summary>A projectile flying from the hero to the enemy, with a short fading trail.</summary>
	public sealed class ShotProjectile : BattleEffect
	{
		private readonly int type;
		private readonly Vector2 from, to;
		private readonly float frames;
		private float t;

		public ShotProjectile(int type, Vector2 from, Vector2 to, float frames)
		{
			this.type = type;
			this.from = from;
			this.to = to;
			this.frames = frames;
		}

		public override void Step(float dt)
		{
			t += dt;
			if (t >= frames)
				Done = true;
		}

		private Vector2 At(float time) => Vector2.Lerp(from, to, MathHelper.Clamp(time / frames, 0f, 1f));

		public override void Draw()
		{
			Vector2 pos = At(t);
			Vector2 dir = to - from;
			float angle = (float)Math.Atan2(dir.Y, dir.X);
			// A light streak behind it, so even shots with no sprite (some magic) are visible
			DrDraw.Line(At(t - 2.5f), pos, 3f, Color.White * 0.55f);
			if (type <= 0 || type >= TextureAssets.Projectile.Length)
				return;
			Main.instance.LoadProjectile(type);
			Texture2D tex = TextureAssets.Projectile[type].Value;
			int frameCount = Math.Max(1, Main.projFrames[type]);
			var src = new Rectangle(0, 0, tex.Width, tex.Height / frameCount);
			// Most projectile sprites point up: turn them to the direction of travel
			for (int k = 2; k >= 0; k--)
			{
				float a = k == 0 ? 1f : 0.35f / k;
				DrDraw.Sb.Draw(tex, At(t - k * 0.8f), src, Color.White * a, angle + MathHelper.PiOver2, src.Size() / 2f,
					BattleCharacterScale * 0.8f, SpriteEffects.None, 0f);
			}
		}
	}

	/// <summary>A quick star-shaped flash at a gun's muzzle.</summary>
	public sealed class MuzzleFlash : BattleEffect
	{
		private readonly Vector2 pos;
		private float t;

		public MuzzleFlash(Vector2 pos) => this.pos = pos;

		public override void Step(float dt)
		{
			t += dt;
			if (t >= 4f)
				Done = true;
		}

		public override void Draw()
		{
			float a = 1f - t / 4f;
			float s = 6f + t * 4f;
			Color c = new Color(255, 240, 160) * a;
			DrDraw.Rect(pos.X - s, pos.Y - 1.5f, s * 2f, 3f, c);
			DrDraw.Rect(pos.X - 1.5f, pos.Y - s * 0.6f, 3f, s * 1.2f, c);
			DrDraw.Rect(pos.X - 3f, pos.Y - 3f, 6f, 6f, Color.White * a);
		}
	}

	/// <summary>Sparks where a shot hits the enemy (melee hits get Deltarune's slash instead).</summary>
	public sealed class ShotImpact : BattleEffect
	{
		private readonly Vector2 pos;
		private float t;

		public ShotImpact(Vector2 pos) => this.pos = pos;

		public override void Step(float dt)
		{
			t += dt;
			if (t >= 8f)
				Done = true;
		}

		public override void Draw()
		{
			float a = 1f - t / 8f;
			float r = 4f + t * 3f;
			for (int i = 0; i < 6; i++)
			{
				Vector2 d = (MathHelper.TwoPi * i / 6f + 0.3f).ToRotationVector2();
				DrDraw.Line(pos + d * r * 0.4f, pos + d * r, 2f, Color.White * a);
			}
		}
	}
}
