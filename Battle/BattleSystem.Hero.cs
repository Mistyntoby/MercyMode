using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// The player's side of the battle screen: the Terraria character standing where Kris stands, its
	/// Deltarune-style poses, and the small animations around it (damage numbers, heal sparkles, screen shake).
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>True while the battle draws the player, so their colours ignore world lighting.</summary>
		public static bool DrawingHero;

		private enum HeroPose { Idle, AttackReady, Attack, ActReady, Act, ItemReady, Item, Defend, Victory }

		// scr_encountersetup: a lone Kris stands at (80, 140), 2x sprites, mywidth 68 / myheight 74
		private const float HeroX = 80, HeroY = 140, HeroHeight = 74;
		/// <summary>Bottom-centre of the character on the battle screen.</summary>
		private static readonly Vector2 HeroFeet = new(116, 216);
		private const float HeroScale = 1.5f;
		/// <summary>scr_moveheart: the SOUL leaves from (kris.x + 10, kris.y + 40).</summary>
		private static readonly Vector2 HeroHeart = new(HeroX + 10, HeroY + 40);

		private HeroPose heroPose = HeroPose.Idle;
		private float heroTimer; // Deltarune frames into the current pose
		private int hurtTimer = -1; // hurttimer, frames
		private Vector2 heroWorldScreen; // where the player was on the battle screen's coordinates when it started
		private Vector2 enemyWorldScreen;
		private const int FlyTicks = 10 * TicksPerFrame; // the party flies in over 10 frames
		private int shake; // obj_shake: 4 px, flips each frame, decays by 1
		private int shakeSign = 1;
		private int pendingHealFx = -1;
		private int pendingHealAmount;
		private readonly List<BattleEffect> effects = new();
		private EnemySnapshot enemySnap;
		private BattleEffect enemyOverride; // spare / death animation replaces the enemy sprite

		private void SetHeroPose(HeroPose pose)
		{
			heroPose = pose;
			heroTimer = 0;
		}

		/// <summary>Deltarune-frame updates for the hero and the effects.</summary>
		private void UpdateHeroFrame()
		{
			heroTimer += 1f;
			if (hurtTimer >= 0 && ++hurtTimer > 15)
				hurtTimer = -1;
			if (shake > 0)
			{
				shake--;
				shakeSign = -shakeSign;
			}

			// ACT returns to idle after actreturnframes (10 at 0.5 per frame = 20 frames)
			if (heroPose == HeroPose.Act && heroTimer >= 20)
				SetHeroPose(HeroPose.Idle);
			if (heroPose == HeroPose.Item && heroTimer >= 24)
				SetHeroPose(HeroPose.Idle);
			if (heroPose == HeroPose.Attack && heroTimer >= 30 && phase != Phase.FightResult)
				SetHeroPose(HeroPose.Idle);

			foreach (var e in effects)
				e.Frame();
			effects.RemoveAll(e => e.Done);
			if (enemyOverride != null)
			{
				enemyOverride.Frame();
				if (enemyOverride.Done)
					enemyOverride = null;
			}

			if (pendingHealFx > 0 && --pendingHealFx == 0)
				PlayHealFx(pendingHealAmount);
		}

		/// <summary>The pose shown right now: menus show the "ready" pose for the chosen command.</summary>
		private HeroPose CurrentPose()
		{
			if (heroPose != HeroPose.Idle)
				return heroPose;
			if (phase == Phase.EnemySelect || phase == Phase.ActSelect)
				return pendingChoice switch { Choice.Fight => HeroPose.AttackReady, _ => HeroPose.ActReady };
			if (phase == Phase.ItemSelect)
				return HeroPose.ItemReady;
			if (phase == Phase.FightBar)
				return HeroPose.AttackReady;
			return HeroPose.Idle;
		}

		// ---- coordinates ----

		private float drScale = 1f, drOx, drOy;

		private void ComputeScreenTransform()
		{
			float raw = Math.Min(Main.screenWidth / (float)ScreenWidth, Main.screenHeight / (float)ScreenHeight);
			drScale = raw >= 2f ? (float)Math.Floor(raw) : raw;
			drOx = (Main.screenWidth - ScreenWidth * drScale) / 2f;
			drOy = (Main.screenHeight - ScreenHeight * drScale) / 2f;
		}

		/// <summary>A point in the world to battle-screen coordinates (through the game zoom).</summary>
		private Vector2 WorldToBattle(Vector2 world)
		{
			ComputeScreenTransform();
			Vector2 screen = Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
			return new Vector2((screen.X - drOx) / drScale, (screen.Y - drOy) / drScale);
		}

		/// <summary>0 at the world position, 1 at the battle position (fly-in at the start, fly-out at the end).</summary>
		private float FlyProgress()
		{
			if (phase == Phase.Intro)
				return MathHelper.Clamp(phaseTicks / (float)FlyTicks, 0f, 1f);
			if (phase == Phase.Outro)
				return 1f - MathHelper.Clamp(phaseTicks / (float)FlyTicks, 0f, 1f);
			return 1f;
		}

		private Vector2 HeroFeetNow => Vector2.Lerp(heroWorldScreen, HeroFeet, FlyProgress());

		// ---- drawing ----

		private void DrawHero(SpriteBatch sb, Matrix m)
		{
			Player p = Player;
			if (p.dead)
				return;
			HeroPose pose = CurrentPose();
			Vector2 feet = HeroFeetNow;

			// Poses use Terraria's own body frames: 0 stand, 1-4 use-item swing, 5 jump
			int bodyFrame = 0, legFrame = 0;
			float bob = 0f;
			float squashY = 1f;
			switch (pose)
			{
				case HeroPose.Idle:
					bob = (float)Math.Round(Math.Sin(time / 20f) * 1f);
					break;
				case HeroPose.AttackReady:
					bodyFrame = 3;
					break;
				case HeroPose.Attack:
				{
					// attackframes 6 at speed 0.5: the swing takes 12 frames
					float t = Math.Min(1f, heroTimer / 12f);
					bodyFrame = t < 0.25f ? 1 : t < 0.5f ? 2 : t < 0.75f ? 3 : 4;
					break;
				}
				case HeroPose.ActReady:
					bodyFrame = 2;
					break;
				case HeroPose.Act:
				{
					// A little hop while acting (actframes 7, then back)
					float t = Math.Min(1f, heroTimer / 14f);
					bodyFrame = legFrame = t < 1f ? 5 : 0;
					bob = -(float)Math.Sin(t * Math.PI) * 10f;
					break;
				}
				case HeroPose.ItemReady:
				case HeroPose.Item:
					bodyFrame = 1;
					break;
				case HeroPose.Defend:
					bodyFrame = 3;
					squashY = 0.92f;
					break;
				case HeroPose.Victory:
				{
					float t = Math.Min(1f, heroTimer / 27f); // victoryframes 9 at 0.334
					bodyFrame = legFrame = t < 0.7f ? 5 : 0;
					bob = -(float)Math.Abs(Math.Sin(t * Math.PI * 2)) * 8f;
					break;
				}
			}

			// Hurt: drawn at x - 20 + hurtindex * 10 (hurtindex = hurttimer / 2, max 2) for 15 frames
			float hurtShift = 0f;
			Color tintHurt = Color.White;
			if (hurtTimer >= 0)
			{
				hurtShift = -20 + Math.Min(2, hurtTimer / 2) * 10;
				if (pose != HeroPose.Defend)
					bodyFrame = legFrame = 5;
			}

			// Swap in the pose, draw, then put everything back
			Rectangle oldBody = p.bodyFrame, oldLeg = p.legFrame;
			int oldDir = p.direction;
			Item oldHeld = p.inventory[p.selectedItem];
			int oldAnim = p.itemAnimation;
			p.bodyFrame.Y = bodyFrame * p.bodyFrame.Height;
			p.legFrame.Y = legFrame * p.legFrame.Height;
			p.direction = 1;
			p.itemAnimation = 0;
			p.inventory[p.selectedItem] = new Item();

			sb.End();
			Matrix squash = Matrix.CreateTranslation(-feet.X, -feet.Y, 0) * Matrix.CreateScale(1f, squashY, 1f) * Matrix.CreateTranslation(feet.X, feet.Y, 0);
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, squash * m);
			DrawingHero = true;
			try
			{
				// DrawPlayer scales around the bottom-centre of the hitbox and draws at (position - screenPosition)
				Vector2 position = Main.screenPosition + feet + new Vector2(hurtShift, bob) - new Vector2(p.width / 2f, p.height);
				Main.PlayerRenderer.DrawPlayer(Main.Camera, p, position, 0f, Vector2.Zero, 0f, HeroScale);
			}
			finally
			{
				DrawingHero = false;
				p.bodyFrame = oldBody;
				p.legFrame = oldLeg;
				p.direction = oldDir;
				p.itemAnimation = oldAnim;
				p.inventory[p.selectedItem] = oldHeld;
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
			}

			DrawHeroWeapon(pose, feet + new Vector2(hurtShift, bob));
		}

		/// <summary>The held weapon (or used item) drawn in the hero's hand for the poses that show it.</summary>
		private void DrawHeroWeapon(HeroPose pose, Vector2 feet)
		{
			Item item;
			if (pose == HeroPose.Item || pose == HeroPose.ItemReady)
				item = usedItemType > 0 ? ContentSamples.ItemsByType[usedItemType] : null;
			else if (pose == HeroPose.Attack || pose == HeroPose.AttackReady || pose == HeroPose.Defend)
				item = WeaponForDisplay();
			else
				return;
			if (item == null || item.IsAir)
				return;
			if (pose == HeroPose.Item && heroTimer > 15)
				return; // the item is used up at 15 frames

			Main.instance.LoadItem(item.type);
			Texture2D tex = TextureAssets.Item[item.type].Value;
			Rectangle src = Main.itemAnimations[item.type] != null ? Main.itemAnimations[item.type].GetFrame(tex) : tex.Bounds;
			Vector2 shoulder = feet + new Vector2(2, -30 * HeroScale);
			float scale = HeroScale * 0.75f;

			if (pose == HeroPose.Item || pose == HeroPose.ItemReady)
			{
				// Held up above the head
				float rise = pose == HeroPose.Item ? Math.Min(heroTimer, 15) * 0.6f : 0f;
				DrDraw.Sb.Draw(tex, shoulder + new Vector2(4, -26 - rise), src, Color.White, 0f, src.Size() / 2f, scale, SpriteEffects.None, 0f);
				return;
			}

			bool swing = item.useStyle == ItemUseStyleID.Swing;
			if (item.useStyle == ItemUseStyleID.Rapier)
			{
				// Shortswords and rapiers: point forward (the sprite points up-right) and thrust
				float thrust = pose == HeroPose.Attack ? (float)Math.Sin(Math.Min(1f, heroTimer / 8f) * Math.PI) * 14f : 0f;
				float r = pose == HeroPose.Defend ? -1.2f : MathHelper.PiOver4;
				DrDraw.Sb.Draw(tex, shoulder + new Vector2(6 + thrust, 6), src, Color.White, r, new Vector2(0, src.Height), scale, SpriteEffects.None, 0f);
				return;
			}
			float rot;
			if (pose == HeroPose.Attack && swing)
			{
				// Swing from over the shoulder to in front, like a Terraria sword swing
				float t = Math.Min(1f, heroTimer / 12f);
				rot = MathHelper.Lerp(-2.4f, 0.5f, t);
			}
			else if (pose == HeroPose.Defend)
			{
				rot = -1.9f; // held up across the body
			}
			else
			{
				rot = swing ? -0.8f : 0f;
			}

			if (swing)
			{
				// Swords: rotate around the handle (bottom-left corner), blade pointing up-right at rotation -pi/4
				DrDraw.Sb.Draw(tex, shoulder, src, Color.White, rot + MathHelper.PiOver4, new Vector2(0, src.Height), scale, SpriteEffects.None, 0f);
			}
			else
			{
				// Bows, guns, staves: held out in front; kick back a little when attacking
				float recoil = pose == HeroPose.Attack ? Math.Max(0, 6 - heroTimer) : 0;
				DrDraw.Sb.Draw(tex, shoulder + new Vector2(10 - recoil, 4), src, Color.White, rot, new Vector2(src.Width * 0.3f, src.Height / 2f), scale, SpriteEffects.None, 0f);
			}
		}

		private Item WeaponForDisplay()
		{
			Item held = Player.HeldItem;
			if (!held.IsAir && held.damage > 0 && held.useStyle != ItemUseStyleID.None && !held.accessory)
				return held;
			Item best = null;
			for (int i = 0; i < 10; i++)
			{
				Item it = Player.inventory[i];
				if (!it.IsAir && it.damage > 0 && !it.accessory && it.ammo == AmmoID.None && (best == null || it.damage > best.damage))
					best = it;
			}
			return best;
		}

		private int usedItemType;

		private void DrawEffects()
		{
			foreach (var e in effects)
				e.Draw();
		}

		private Matrix ShakeMatrix => shake > 0 ? Matrix.CreateTranslation(shakeSign * shake, shakeSign * shake, 0) : Matrix.Identity;

		// ---- effects ----

		private void AddEffect(BattleEffect e) => effects.Add(e);

		/// <summary>scr_dmgwriter_selfchar: (x, y + myheight - 24) on the hero.</summary>
		private void HeroNumber(int amount, Color color, int message = -1)
		{
			Vector2 feet = HeroFeetNow;
			float x = feet.X - (HeroFeet.X - HeroX);
			float y = feet.Y - (HeroFeet.Y - HeroY) + HeroHeight - 24;
			AddEffect(new DamageNumber(x, y, amount, color, message));
		}

		/// <summary>The enemy's damage number: from its sprite, 8 frames after the hit.</summary>
		private void EnemyNumber(int amount, Color color, int message = -1)
		{
			Vector2 c = encounter.DrawCenter;
			AddEffect(new DamageNumber(c.X - 30, c.Y - 20, amount, color, message, delay: 8));
		}

		/// <summary>obj_healanim: green stars rise off the hero, then the healed amount (or MAX) in green.</summary>
		private void PlayHealFx(int healed)
		{
			Vector2 feet = HeroFeetNow;
			var area = new Rectangle((int)(feet.X - 34), (int)(feet.Y - 74), 68, 74);
			for (int i = 0; i < 10; i++)
			{
				var pos = new Vector2(Main.rand.NextFloat(area.Left, area.Right), Main.rand.NextFloat(area.Top, area.Bottom));
				var vel = new Vector2(2 - Main.rand.NextFloat(2f), -3 - Main.rand.NextFloat(2f));
				AddEffect(new StarParticle(pos, vel, Vector2.Zero, 0.2f, -10f, new Color(0, 255, 0), 5));
			}
			if (healed > 0 && Player.statLife < Player.statLifeMax2)
				HeroNumber(healed, new Color(0, 255, 0));
			else
				HeroNumber(0, new Color(0, 255, 0), DamageNumber.MaxFrame);
		}

		/// <summary>Plays the heal sparkles after the item/act animation reaches its use frame.</summary>
		private void QueueHealFx(int healed, int frames)
		{
			pendingHealAmount = healed;
			pendingHealFx = Math.Max(1, frames);
		}

		/// <summary>Starts the battle-screen enemy's farewell from the last frame it was drawn.</summary>
		private void PlayEnemySpared()
		{
			enemyOverride = new SpareAnimation(enemySnap);
		}

		private void PlayEnemyDeath()
		{
			enemyOverride = new DeathAnimation(enemySnap);
		}
	}
}
