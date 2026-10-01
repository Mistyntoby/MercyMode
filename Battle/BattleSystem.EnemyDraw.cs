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
			NPC anchorNpc = encounter.Npc.active ? encounter.Npc : encounter.DrawNpc;
			if (anchorNpc == null || !anchorNpc.active)
				return;
			List<NPC> parts = encounter.DrawParts().Where(n => n.active).Distinct().OrderBy(n => n.whoAmI).ToList();
			if (!parts.Contains(anchorNpc))
				parts.Insert(0, anchorNpc);

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
			float fit = Math.Min(BattleCharacterScale, Math.Min(area.X / Math.Max(1, bounds.Width), area.Y / Math.Max(1, bounds.Height)));
			float s = MathHelper.Lerp(WorldPixelScale(), fit, glide);

			// Where the anchor NPC lands; once it has arrived the whole group is centred on the enemy's spot
			Vector2 anchor = anchorNpc.Center;
			Vector2 centring = (bounds.Center.ToVector2() - anchor) * s * glide;
			float attackMotion = phase == Phase.EnemyTurn ? MathHelper.Clamp(enemyAttackEnergy, 0f, 1f) : 0f;
			Vector2 p = EnemyPosNow - centring + new Vector2(0, (float)Math.Sin(time / 20f) * 4f * glide) - enemyAttackDirection * (attackMotion * 3f);
			if (enemyShake > 0)
				p.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;

			// World pixels around the anchor -> battle pixels around p, scaled by s
			Matrix local = Matrix.CreateTranslation(-p.X, -p.Y, 0f) * Matrix.CreateScale(s, s, 1f) * Matrix.CreateTranslation(p.X, p.Y, 0f) * baseMatrix;
			Vector2 savedScreen = Main.screenPosition;
			bool savedMenu = Main.gameMenu;
			int[] savedAlpha = parts.Select(n => n.alpha).ToArray();
			EnemyLight = WorldLightTint(anchor);

			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, local);
			DrawingEnemy = true;
			try
			{
				// Some draw code reads Main.screenPosition rather than the argument; and with gameMenu set,
				// Lighting.GetColor returns white, so chains and arms are full-bright like the rest of the battle
				Main.screenPosition = anchor - p;
				Main.gameMenu = true;
				for (int i = 0; i < parts.Count; i++)
				{
					if (encounter.ForceOpaque)
						parts[i].alpha = 0;
					Main.instance.DrawNPCDirect(sb, parts[i], parts[i].behindTiles, Main.screenPosition);
				}
			}
			finally
			{
				Main.gameMenu = savedMenu;
				Main.screenPosition = savedScreen;
				for (int i = 0; i < parts.Count; i++)
					parts[i].alpha = savedAlpha[i];
				DrawingEnemy = false;
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
			DrawSlash(p + centring);
		}
	}
}
