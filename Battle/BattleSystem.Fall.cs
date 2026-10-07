using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MercyMode.Battle
{
	/// <summary>
	/// The battle "camera" an attack can move: the box stretched past the edges of the screen, the hero and the enemy
	/// slid sideways, the SOUL held still while everything rushes by (Skeletron's sideways fall). All of it is reset
	/// as the enemy turn ends. Only the drawing depends on the screen; the box and the SOUL use fixed numbers, so every
	/// player in a party battle sees the same attack.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>The box this attack wants (eased into like a bigger box), or null for the normal one.</summary>
		public Rectangle? BoxOverride;
		/// <summary>Holds the SOUL at this x (its left edge): the camera is following it. It still moves up and down.</summary>
		public float? HoldSoulX;
		/// <summary>How far the hero (and allies) and the enemy are drawn from their spots.</summary>
		public Vector2 HeroShift, EnemyShift;
		/// <summary>The enemy's opacity (it teleports in and out).</summary>
		public float EnemyFade = 1f;
		/// <summary>How far the scene has scrolled (the background drifts by at half of it).</summary>
		public float SceneScroll;

		private void ResetCamera()
		{
			BoxOverride = null;
			HoldSoulX = null;
			HeroShift = EnemyShift = Vector2.Zero;
			EnemyFade = 1f;
			SceneScroll = 0f;
		}

		/// <summary>Where the enemy is drawn with its shift (for effects round it).</summary>
		public Vector2 EnemyScreenNow => (encounter?.ScreenCenter ?? Vector2.Zero) + EnemyShift;

		/// <summary>Draws something moved by <paramref name="shift"/>: the sprite batch restarts with the shifted matrix.</summary>
		private static void WithShift(SpriteBatch sb, Vector2 shift, Matrix m, Action<Matrix> draw)
		{
			if (shift == Vector2.Zero)
			{
				draw(m);
				return;
			}
			Matrix s = Matrix.CreateTranslation(shift.X, shift.Y, 0f) * m;
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, s);
			DrDraw.Transform = s;
			try
			{
				draw(s);
			}
			finally
			{
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
				DrDraw.Transform = m;
			}
		}
	}
}
