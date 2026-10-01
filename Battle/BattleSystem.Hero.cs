using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// The player's side of the battle screen: the Terraria character standing where Kris stands, its
	/// Deltarune-style poses, the intro glide, and the small animations around it (damage numbers, heal sparkles,
	/// screen shake).
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>True while the battle draws the player, so their colours ignore world lighting.</summary>
		public static bool DrawingHero;
		/// <summary>
		/// Multiplied into the hero's colours while drawing. White on the battle screen; the world's light at the
		/// player's spot while gliding in or out, so the battle sprite turns into exactly what the world shows.
		/// </summary>
		public static Color HeroLight = Color.White;

		/// <summary>True for the NPC the battle screen draws; the world copy is hidden so it never shows twice.</summary>
		public static bool IsBattleSprite(NPC npc) => Active && Instance.encounter?.DrawNpc == npc;

		private enum HeroPose { Idle, AttackReady, Attack, ActReady, Act, ItemReady, Item, Defend, Victory }

		// scr_encountersetup: a lone Kris stands at (80, 140), 2x sprites, mywidth 68 / myheight 74
		private const float HeroX = 80, HeroY = 140, HeroHeight = 74;
		/// <summary>Bottom-centre of the character on the battle screen.</summary>
		private static readonly Vector2 HeroFeet = new(116, 216);
		private const float HeroScale = BattleCharacterScale;
		/// <summary>scr_moveheart: the SOUL leaves from (kris.x + 10, kris.y + 40).</summary>
		private static readonly Vector2 HeroHeart = new(HeroX + 10, HeroY + 40);

		// ---- intro timeline (ticks into the Intro phase) ----
		/// <summary>Hero and enemy glide from the world to their battle spots (and sizes) while the background fades in.</summary>
		private const int GlideTicks = 15 * TicksPerFrame;
		/// <summary>Then the hero swings their weapon (the weapon-draw sound plays)...</summary>
		private const int IntroSwingAt = GlideTicks + 3 * TicksPerFrame;
		private const int SwingFrames = 12; // attackframes 6 at speed 0.5
		/// <summary>...and once the swing is over the bottom UI glides up.</summary>
		private const int IntroPanelAt = IntroSwingAt + SwingFrames * TicksPerFrame;

		// ---- healing (Deltarune frames) ----
		/// <summary>The potion is raised and used this many frames into the ITEM pose; the heal lands then.</summary>
		private const int ItemUseFrame = 8;
		/// <summary>How long the potion takes to rise to its overhead spot.</summary>
		private const float ItemRiseFrames = 4f;
		/// <summary>The ITEM pose returns to idle shortly after the use frame.</summary>
		private const int ItemPoseFrames = ItemUseFrame + 8;
		/// <summary>Heal Prayer (an ACT) heals near the top of the ACT hop.</summary>
		private const int ActHealFrame = 4;

		private HeroPose heroPose = HeroPose.Idle;
		private float heroTimer; // Deltarune frames into the current pose
		private int hurtTimer = -1; // hurttimer, frames
		private Vector2 heroWorldScreen; // the player's feet in the world, in battle-screen coordinates
		private float heroWorldScale;
		private Vector2 enemyWorldScreen;
		private float enemyWorldScale;
		private int shake; // obj_shake: 4 px, flips each frame, decays by 1
		private int shakeSign = 1;
		private int pendingHealFx = -1;
		private int pendingHealAmount;
		private readonly List<BattleEffect> effects = new();
		private EnemySnapshot enemySnap;
		private BattleEffect enemyOverride; // spare / death animation replaces the enemy sprite
		private int usedItemType;

		/// <summary>Afterimages left behind while gliding (obj_afterimage).</summary>
		private struct TrailPoint
		{
			public Vector2 HeroFeet;
			public float HeroScale;
			public Vector2 EnemyPos;
			public float EnemyScale;
			public int Age;
		}
		private readonly List<TrailPoint> trail = new();
		private const int TrailLife = 8; // frames

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
			enemyAttackEnergy = Math.Max(0f, enemyAttackEnergy - 0.055f);
			enemyAttackDirection = Vector2.Lerp(enemyAttackDirection, Vector2.Zero, 0.12f);

			// ACT returns to idle after actreturnframes (10 at 0.5 per frame = 20 frames)
			if (heroPose == HeroPose.Act && heroTimer >= 20)
				SetHeroPose(HeroPose.Idle);
			if (heroPose == HeroPose.Item && heroTimer >= ItemPoseFrames)
				SetHeroPose(HeroPose.Idle);
			if (heroPose == HeroPose.Attack && heroTimer >= SwingFrames + 6 && phase != Phase.FightResult)
				SetHeroPose(HeroPose.Idle);

			// Afterimages: drop one every frame while gliding, let the old ones fade out
			for (int i = trail.Count - 1; i >= 0; i--)
			{
				var t = trail[i];
				t.Age++;
				if (t.Age > TrailLife)
					trail.RemoveAt(i);
				else
					trail[i] = t;
			}
			if (Gliding)
			{
				trail.Add(new TrailPoint
				{
					HeroFeet = HeroFeetNow,
					HeroScale = HeroScaleNow,
					EnemyPos = EnemyPosNow,
					EnemyScale = EnemyScaleNow(out _, out _),
				});
			}

			foreach (var e in effects)
				e.Frame();
			effects.RemoveAll(e => e.Done);
			for (int i = boxAfterimages.Count - 1; i >= 0; i--)
			{
				BoxAfterimage image = boxAfterimages[i];
				image.Age++;
				if (image.Age > 18)
					boxAfterimages.RemoveAt(i);
				else
					boxAfterimages[i] = image;
			}
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

		// ---- coordinates and the glide ----

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

		/// <summary>How big one world pixel is on the battle screen: the size things start the glide at.</summary>
		private float WorldPixelScale()
		{
			ComputeScreenTransform();
			return Main.GameViewMatrix.Zoom.X / drScale;
		}

		private bool Gliding => phase == Phase.Intro && phaseTicks <= GlideTicks || phase == Phase.Outro && phaseTicks <= GlideTicks;

		/// <summary>0 at the world position, 1 at the battle position. Eases out going in, eases in coming back.</summary>
		private float FlyProgress()
		{
			float t;
			if (phase == Phase.Intro)
			{
				t = MathHelper.Clamp(phaseTicks / (float)GlideTicks, 0f, 1f);
				return 1f - (float)Math.Pow(1f - t, 3); // ease-out cubic
			}
			if (phase == Phase.Outro)
			{
				t = MathHelper.Clamp(phaseTicks / (float)GlideTicks, 0f, 1f);
				return 1f - t * t * t; // ease-in cubic on the way back
			}
			return 1f;
		}

		private Vector2 HeroFeetNow => Vector2.Lerp(heroWorldScreen, HeroFeet, FlyProgress());
		private float HeroScaleNow => MathHelper.Lerp(heroWorldScale, HeroScale, FlyProgress());
		private Vector2 EnemyPosNow => encounter == null ? Vector2.Zero : Vector2.Lerp(enemyWorldScreen, encounter.DrawCenter, FlyProgress());

		/// <summary>The enemy's sprite frame and its size right now (world size at the start of the glide).</summary>
		private float EnemyScaleNow(out Texture2D tex, out Rectangle frame)
		{
			tex = null;
			frame = default;
			NPC npc = encounter?.DrawNpc;
			if (npc == null || !npc.active)
				return 1f;
			Main.instance.LoadNPC(npc.type);
			tex = TextureAssets.Npc[npc.type].Value;
			frame = npc.frame.Width > 0 && npc.frame.Height > 0 ? npc.frame : new Rectangle(0, 0, tex.Width, tex.Height / Math.Max(1, Main.npcFrameCount[npc.type]));
			return MathHelper.Lerp(enemyWorldScale, encounter.DrawScale(frame), FlyProgress());
		}

		/// <summary>Called when the battle starts: remembers where and how big things were in the world.</summary>
		private void CaptureWorldPositions(NPC root)
		{
			heroWorldScreen = WorldToBattle(Player.Bottom);
			heroWorldScale = WorldPixelScale();
			enemyWorldScreen = WorldToBattle(root.Center);
			enemyWorldScale = WorldPixelScale() * root.scale;
			trail.Clear();
		}

		// ---- drawing the hero ----

		private void DrawHero(SpriteBatch sb, Matrix m)
		{
			Player p = Player;
			if (p.dead)
				return;

			HeroLight = WorldLightTint(p.Center);

			// Fading afterimages first (Terraria's own "shadow" draw makes them see-through)
			foreach (var t in trail)
				DrawPlayerPose(sb, m, p, t.HeroFeet, t.HeroScale, HeroPose.Idle, 0f, shadow: 0.35f + 0.6f * t.Age / TrailLife);

			HeroPose pose = CurrentPose();
			float bob = 0f, hurtShift = 0f;
			if (pose == HeroPose.Idle)
				bob = (float)Math.Round(Math.Sin(time / 20f) * 1f);
			else if (pose == HeroPose.Act)
				bob = -(float)Math.Sin(Math.Min(1f, heroTimer / 14f) * Math.PI) * 10f;
			else if (pose == HeroPose.Victory)
				bob = -(float)Math.Abs(Math.Sin(Math.Min(1f, heroTimer / 27f) * Math.PI * 2)) * 8f;
			// Hurt: drawn at x - 20 + hurtindex * 10 (hurtindex = hurttimer / 2, max 2) for 15 frames
			if (hurtTimer >= 0)
				hurtShift = -20 + Math.Min(2, hurtTimer / 2) * 10;

			// Stays solid on the way back: the world lighting (HeroLight) takes over instead of fading out,
			// and it lands exactly on the real character, which is hidden until the battle ends
			DrawPlayerPose(sb, m, p, HeroFeetNow + new Vector2(hurtShift, bob), HeroScaleNow, pose, heroTimer, 0f);
			HeroLight = Color.White;
		}

		/// <summary>
		/// Battle-screen colours are full bright; the world's are lit. Blends toward the light at a world spot as the
		/// glide approaches the world (start of the intro, end of the outro).
		/// </summary>
		private Color WorldLightTint(Vector2 worldPosition)
		{
			float blend = 1f - FlyProgress();
			if (blend <= 0f)
				return Color.White;
			Color light = Lighting.GetColor(worldPosition.ToTileCoordinates());
			return Color.Lerp(Color.White, light, blend);
		}

		/// <summary>Multiplies a colour's RGB by a tint, keeping its alpha.</summary>
		public static Color Tint(Color c, Color tint) =>
			new(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, c.A);

		/// <summary>How far through a weapon swing a pose is (0 = start, 1 = end), or -1 for no weapon.</summary>
		private static float SwingProgress(HeroPose pose, float timer) => pose switch
		{
			HeroPose.AttackReady => 0.5f,
			HeroPose.Attack => Math.Min(1f, timer / SwingFrames),
			HeroPose.Defend => 0.12f,
			_ => -1f,
		};

		/// <summary>
		/// Draws the player in a pose. Weapons are posed with Terraria's own use-style code so they sit exactly in
		/// the hand; items that Terraria doesn't draw while used (shortswords, spears...) and potions are drawn at
		/// the hand position of a posed arm.
		/// </summary>
		private void DrawPlayerPose(SpriteBatch sb, Matrix m, Player p, Vector2 feet, float scale, HeroPose pose, float timer, float shadow)
		{
			// Save everything we touch
			Rectangle oldBody = p.bodyFrame, oldLeg = p.legFrame;
			int oldDir = p.direction;
			int oldSlot = p.selectedItem;
			Item oldHeld = p.inventory[oldSlot];
			int oldAnim = p.itemAnimation, oldAnimMax = p.itemAnimationMax, oldTime = p.itemTime;
			float oldRot = p.itemRotation;
			Vector2 oldLoc = p.itemLocation;
			Player.CompositeArmData oldFront = p.compositeFrontArm, oldBack = p.compositeBackArm;

			p.direction = 1;
			p.itemAnimation = 0;
			p.compositeFrontArm = default;
			p.compositeBackArm = default;
			int bodyFrame = 0, legFrame = 0;

			Item weapon = WeaponForDisplay();
			float swing = SwingProgress(pose, timer);
			Item manualItem = null; // drawn by us at the hand
			float manualRotation = 0f;
			Vector2 manualHand = Vector2.Zero;
			Vector2 manualOrigin = Vector2.Zero;
			float manualThrust = 0f;

			if (swing >= 0f && weapon != null && shadow < 0.95f)
			{
				p.inventory[oldSlot] = weapon;
				p.itemAnimationMax = 30;
				p.itemAnimation = Math.Max(1, (int)Math.Round(30 * (1f - swing)));
				p.itemTime = p.itemAnimation;
				p.itemRotation = 0f; // guns and bows point straight ahead
				if (!weapon.noUseGraphic)
				{
					Main.instance.LoadItem(weapon.type);
					p.ItemCheck_ApplyUseStyle(p.mount.PlayerOffsetHitbox, weapon, Item.GetDrawHitbox(weapon.type, p));
					bodyFrame = VanillaUseBodyFrame(p, weapon);
				}
				else
				{
					// Shortswords and spears: arm straight out, item in the hand, a short thrust when attacking
					float armRot = pose == HeroPose.Defend ? -MathHelper.Pi * 0.85f : -MathHelper.PiOver2;
					p.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
					manualItem = weapon;
					manualHand = p.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRot);
					manualRotation = armRot + MathHelper.PiOver2; // along the arm
					if (pose == HeroPose.Attack)
						manualThrust = (float)Math.Sin(Math.Min(1f, timer / 8f) * Math.PI) * 10f;
					p.itemAnimation = 0;
				}
			}
			else
			{
				p.inventory[oldSlot] = new Item(); // nothing in hand
				switch (pose)
				{
					case HeroPose.ActReady:
						bodyFrame = 2;
						break;
					case HeroPose.Act:
						bodyFrame = legFrame = Math.Min(1f, timer / 14f) < 1f ? 5 : 0;
						break;
					case HeroPose.ItemReady:
					case HeroPose.Item:
						if (usedItemType > 0 && (pose == HeroPose.ItemReady || timer <= ItemUseFrame) && shadow < 0.95f)
						{
							// Arm raised, holding the item up; it's used up at ItemUseFrame
							float armRot = MathHelper.Pi;
							p.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
							manualItem = ContentSamples.ItemsByType[usedItemType];
							manualHand = p.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRot);
							// Raise the potion briskly, then hold it overhead until the use pose ends.
							float rise = 22f * (1f - (float)Math.Pow(1f - Math.Min(timer, ItemRiseFrames) / ItemRiseFrames, 2f));
							manualThrust = pose == HeroPose.Item ? rise : 0f;
						}
						else
						{
							bodyFrame = 1;
						}
						break;
					case HeroPose.Victory:
						bodyFrame = legFrame = Math.Min(1f, timer / 27f) < 0.7f ? 5 : 0;
						break;
				}
			}
			if (hurtTimer >= 0 && shadow == 0f && pose != HeroPose.Defend)
				bodyFrame = legFrame = 5;

			p.bodyFrame.Y = bodyFrame * p.bodyFrame.Height;
			p.legFrame.Y = legFrame * p.legFrame.Height;

			// Where the player's hitbox bottom-centre lands on the battle screen; world offsets scale around it
			Vector2 anchorWorld = p.position + new Vector2(p.width / 2f, p.height);
			Vector2 ToBattle(Vector2 world) => feet + (world - anchorWorld) * scale;

			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
			DrawingHero = true;
			try
			{
				// DrawPlayer draws at (position - screenPosition) and scales around the hitbox's bottom-centre
				Vector2 position = Main.screenPosition + feet - new Vector2(p.width / 2f, p.height);
				Main.PlayerRenderer.DrawPlayer(Main.Camera, p, position, 0f, Vector2.Zero, shadow, scale);
			}
			finally
			{
				DrawingHero = false;
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);

				if (manualItem != null && !manualItem.IsAir)
				{
					Main.instance.LoadItem(manualItem.type);
					Texture2D tex = TextureAssets.Item[manualItem.type].Value;
					Rectangle src = Main.itemAnimations[manualItem.type] != null ? Main.itemAnimations[manualItem.type].GetFrame(tex) : tex.Bounds;
					Vector2 hand = ToBattle(manualHand);
					if (pose == HeroPose.Item || pose == HeroPose.ItemReady)
					{
						// Potion held up by its bottom, rising as it's used
						DrDraw.Sb.Draw(tex, hand - new Vector2(0, manualThrust), src, Color.White * (1f - shadow), 0f, new Vector2(src.Width / 2f, src.Height), scale * 0.75f, SpriteEffects.None, 0f);
					}
					else
					{
						// Blade sprites point up-right (-45 degrees); turn them to follow the arm, handle in the hand
						Vector2 along = manualRotation.ToRotationVector2();
						DrDraw.Sb.Draw(tex, hand + along * manualThrust, src, Color.White * (1f - shadow), manualRotation + MathHelper.PiOver4,
							new Vector2(0, src.Height), scale * 0.85f, SpriteEffects.None, 0f);
					}
				}

				p.bodyFrame = oldBody;
				p.legFrame = oldLeg;
				p.direction = oldDir;
				p.inventory[oldSlot] = oldHeld;
				p.itemAnimation = oldAnim;
				p.itemAnimationMax = oldAnimMax;
				p.itemTime = oldTime;
				p.itemRotation = oldRot;
				p.itemLocation = oldLoc;
				p.compositeFrontArm = oldFront;
				p.compositeBackArm = oldBack;
			}
		}

		/// <summary>Player.PlayerFrame's body frame while an item is in use, by use style.</summary>
		private static int VanillaUseBodyFrame(Player p, Item item)
		{
			float a = p.itemAnimation, max = p.itemAnimationMax;
			switch (item.useStyle)
			{
				case ItemUseStyleID.Swing:
				case ItemUseStyleID.Rapier:
					return a < max * 0.333f ? 3 : a < max * 0.666f ? 2 : 1;
				case ItemUseStyleID.Shoot:
				{
					float r = p.itemRotation * p.direction;
					return r < -0.75f ? 2 : r > 0.6f ? 4 : 3;
				}
				case ItemUseStyleID.HoldUp:
				case ItemUseStyleID.EatFood:
					return 2;
				case ItemUseStyleID.DrinkLiquid:
					return a > max * 0.5f ? 3 : 2;
				default:
					return 3;
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

		private void DrawEffects()
		{
			foreach (var e in effects)
				e.Draw();
		}

		private Matrix ShakeMatrix => shake > 0 ? Matrix.CreateTranslation(shakeSign * shake, shakeSign * shake, 0) : Matrix.Identity;

		// ---- effects ----

		private void AddEffect(BattleEffect e) => effects.Add(e);

		/// <summary>Shakes the battle screen (obj_shake), for slams and explosions in attack patterns.</summary>
		public void ShakeScreen(int amount) => shake = Math.Max(shake, amount);

		public void ShowMercyGain(float amount)
		{
			if (amount <= 0f || encounter == null)
				return;

			AddEffect(new MercyGainPopup(encounter.DrawCenter + new Vector2(0f, -55f), amount));
			Sfx("mercyadd");
		}

		/// <summary>scr_dmgwriter_selfchar: (x, y + myheight - 24) on the hero.</summary>
		private void HeroNumber(int amount, Color color, int message = -1, int delay = 2)
		{
			Vector2 feet = HeroFeetNow;
			float x = feet.X - (HeroFeet.X - HeroX);
			float y = feet.Y - (HeroFeet.Y - HeroY) + HeroHeight - 24;
			AddEffect(new DamageNumber(x, y, amount, color, message, delay));
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
			// Sound, sparkles and number all land on the same frame
			Sfx("heal");
			Vector2 feet = HeroFeetNow;
			var area = new Rectangle((int)(feet.X - 34), (int)(feet.Y - 74), 68, 74);
			for (int i = 0; i < 10; i++)
			{
				var pos = new Vector2(Main.rand.NextFloat(area.Left, area.Right), Main.rand.NextFloat(area.Top, area.Bottom));
				var vel = new Vector2(2 - Main.rand.NextFloat(2f), -3 - Main.rand.NextFloat(2f));
				AddEffect(new StarParticle(pos, vel, Vector2.Zero, 0.2f, -10f, new Color(0, 255, 0), 5));
			}
			if (healed > 0 && Player.statLife < Player.statLifeMax2)
				HeroNumber(healed, new Color(0, 255, 0), delay: 1);
			else
				HeroNumber(0, new Color(0, 255, 0), DamageNumber.MaxFrame, delay: 1);
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
