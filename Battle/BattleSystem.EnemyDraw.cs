using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// Bosses made of several NPCs (Golem's head and fists, Prime's arms, the Moon Lord's hands) or drawn by special
	/// code (the Empress's wings, Deerclops's legs) are drawn with Terraria's own NPC renderer on the battle screen,
	/// every part where it is relative to the others, scaled to fit.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>True while the battle draws enemy NPCs, so they show (not hidden) and ignore world lighting.</summary>
		public static bool DrawingEnemy;
		/// <summary>Multiplied into the enemy's colour while drawing (world light while gliding in or out).</summary>
		public static Color EnemyLight = Color.White;
		/// <summary>While picking a part to FIGHT, the part under the cursor (the others dim); -1 otherwise.</summary>
		public static int FlashPart = -1;
		/// <summary>Where each part of a breakable boss was last drawn on the battle screen (hit effects land there).</summary>
		private readonly Dictionary<int, Vector2> partScreen = new();
		/// <summary>The part the last melee hit struck, for the slash; -1 for the whole enemy.</summary>
		private int slashPart = -1;

		/// <summary>The world area the parts cover, using their sprite frames as well as their hitboxes.</summary>
		private static Rectangle PartBounds(List<NPC> parts)
		{
			Rectangle bounds = Rectangle.Empty;
			foreach (NPC n in parts)
			{
				int w = n.width, h = n.height;
				if (n.frame.Width > 0 && n.frame.Height > 0)
				{
					w = Math.Max(w, (int)(n.frame.Width * n.scale));
					h = Math.Max(h, (int)(n.frame.Height * n.scale));
				}
				var r = new Rectangle((int)n.Center.X - w / 2, (int)n.Center.Y - h / 2, w, h);
				bounds = bounds == Rectangle.Empty ? r : Rectangle.Union(bounds, r);
			}
			return bounds;
		}

		private void DrawEnemyComposite(SpriteBatch sb, Matrix baseMatrix)
		{
			List<NPC> parts = encounter.DrawParts().Where(n => n.active).Distinct().ToList();
			// The NPC the battle started with, unless it isn't one of the drawn parts (a worm touched mid-body: its head leads)
			NPC anchorNpc = !encounter.LeadWithDrawNpc && encounter.Npc.active && (parts.Count == 0 || parts.Contains(encounter.Npc)) ? encounter.Npc : encounter.DrawNpc;
			if (anchorNpc == null || !anchorNpc.active)
				return;
			if (!parts.Contains(anchorNpc))
				parts.Add(anchorNpc);
			// Terraria's order (Main.DrawNPCs): behind-tiles NPCs first, then each pass from slot 199 down to 0,
			// so lower slots end up on top (Golem's head over its body)
			parts = parts.OrderByDescending(n => n.behindTiles).ThenByDescending(n => n.whoAmI).ToList();

			// Pose first (restored after the draw), so the size and centring below use the posed layout
			Vector2[] savedPosition = parts.Select(n => n.position).ToArray();
			Rectangle[] savedFrame = parts.Select(n => n.frame).ToArray();
			float[] savedRotation = parts.Select(n => n.rotation).ToArray();
			int[] savedDirection = parts.Select(n => n.spriteDirection).ToArray();
			encounter.PoseForBattle(parts, anchorNpc, time, phase == Phase.EnemyTurn ? MathHelper.Clamp(enemyAttackEnergy, 0f, 1f) : 0f);

			Rectangle bounds = PartBounds(parts);
			// Never smaller than the boss's known size (its drawing can be far bigger than its hitboxes)
			Vector2 min = encounter.CompositeSize;
			if (bounds.Width < min.X)
				bounds.Inflate((int)((min.X - bounds.Width) / 2f), 0);
			if (bounds.Height < min.Y)
				bounds.Inflate(0, (int)((min.Y - bounds.Height) / 2f));
			float glide = FlyProgress();
			// Fits the encounter's area on the battle screen, no bigger than the hero's scale
			Vector2 area = encounter.CompositeArea;
			float fit = encounter.LeadWithDrawNpc ? BattleCharacterScale
				: Math.Min(BattleCharacterScale, Math.Min(area.X / Math.Max(1, bounds.Width), area.Y / Math.Max(1, bounds.Height)));
			float s = MathHelper.Lerp(WorldPixelScale(), fit, glide);

			// Where the anchor NPC lands; once it has arrived the whole group is centred on the enemy's spot
			Vector2 anchor = anchorNpc.Center;
			Vector2 centring = encounter.LeadWithDrawNpc ? Vector2.Zero : (bounds.Center.ToVector2() - anchor) * s * glide;
			float attackMotion = phase == Phase.EnemyTurn ? MathHelper.Clamp(enemyAttackEnergy, 0f, 1f) : 0f;
			Vector2 p = EnemyPosNow - centring + new Vector2(0, (float)Math.Sin(time / 20f) * 4f * glide) - enemyAttackDirection * (attackMotion * 3f);
			if (enemyShake > 0)
				p.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;

			// Idle animation (the battle freezes the boss's AI): the whole body breathes, and every part other than
			// the anchor floats on its own rhythm. Arms and chains follow, since Terraria draws them between the parts.
			float breathe = 1f + (float)Math.Sin(time / 45f) * 0.015f;
			float sway = Math.Max(bounds.Width, bounds.Height) * 0.018f;
			Vector2 PartSway(int i) => new(
				(float)Math.Sin(time / 37f + i * 1.9f) * sway,
				(float)Math.Cos(time / 29f + i * 2.7f) * sway * 0.8f);

			// World pixels around the anchor -> battle pixels around p, scaled by s
			Matrix local = Matrix.CreateTranslation(-p.X, -p.Y, 0f) * Matrix.CreateScale(s * breathe, s * breathe, 1f) * Matrix.CreateTranslation(p.X, p.Y, 0f) * baseMatrix;
			Vector2 savedScreen = Main.screenPosition;
			bool savedMenu = Main.gameMenu;
			int[] savedAlpha = parts.Select(n => n.alpha).ToArray();
			EnemyLight = WorldLightTint(anchor);

			// Immediate, like the Bestiary: some bosses apply shaders mid-draw (the Empress's wings)
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, local);
			bool[] savedDummy = parts.Select(n => n.IsABestiaryIconDummy).ToArray();
			DrawingEnemy = true;
			try
			{
				// Some draw code reads Main.screenPosition rather than the argument; and with gameMenu set,
				// Lighting.GetColor returns white, so chains and arms are full-bright like the rest of the battle
				Main.screenPosition = anchor - p;
				Main.gameMenu = true;
				// Move every part first: a part's drawing can reach for another's position (hands draw their arms
				// to the core)
				for (int i = 0; i < parts.Count; i++)
				{
					// As a Bestiary icon, NPC drawing never restarts the sprite batch with the world camera
					// (the Empress does that for her wings, which threw her into the corner of the screen)
					parts[i].IsABestiaryIconDummy = true;
					if (encounter.ForceOpaque)
						parts[i].alpha = 0;
					if (parts[i] != anchorNpc && encounter.SwayParts)
						parts[i].position += PartSway(i);
				}
				// Remember where each part lands on the battle screen, for hits on that part
				foreach (NPC n in parts)
					partScreen[n.whoAmI] = p + (n.Center - anchor) * s * breathe;
				FlashPart = phase == Phase.EnemySelect && pendingChoice == Choice.Fight && focus == targetEnemy
					&& encounter.TargetableParts && encounter.ChosenPart != null ? encounter.ChosenPart.whoAmI : -1;
				for (int i = 0; i < parts.Count; i++)
					Main.instance.DrawNPCDirect(sb, parts[i], parts[i].behindTiles, Main.screenPosition);
			}
			finally
			{
				Main.gameMenu = savedMenu;
				Main.screenPosition = savedScreen;
				for (int i = 0; i < parts.Count; i++)
				{
					parts[i].alpha = savedAlpha[i];
					parts[i].position = savedPosition[i];
					parts[i].frame = savedFrame[i];
					parts[i].rotation = savedRotation[i];
					parts[i].spriteDirection = savedDirection[i];
					parts[i].IsABestiaryIconDummy = savedDummy[i];
				}
				DrawingEnemy = false;
				FlashPart = -1;
				EnemyLight = Color.White;
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, baseMatrix);
			}

			// The spare and death animations work from one sprite: use the main part's
			NPC main = encounter.DrawNpc ?? anchorNpc;
			Main.instance.LoadNPC(main.type);
			Texture2D tex = TextureAssets.Npc[main.type].Value;
			Rectangle frame = main.frame.Width > 0 && main.frame.Height > 0
				? main.frame
				: new Rectangle(0, 0, tex.Width, tex.Height / Math.Max(1, Main.npcFrameCount[main.type]));
			enemySnap = new EnemySnapshot
			{
				Texture = tex, Frame = frame, Position = p + (main.Center - anchor) * s, Rotation = main.rotation,
				Scale = s * main.scale, Color = Color.White, Valid = true,
			};
			DrawSlash(slashPart >= 0 && partScreen.TryGetValue(slashPart, out Vector2 hitAt) ? hitAt : p + centring);
		}
	}
}
