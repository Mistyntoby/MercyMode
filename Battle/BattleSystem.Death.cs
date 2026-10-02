using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using MercyMode.Deltarune;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// Dying in a battle: like Deltarune's game over, the screen goes black around the SOUL, it cracks
	/// (snd_break1), then shatters into falling shards (snd_break2). Only after that does Terraria kill the player,
	/// back in the world.
	/// </summary>
	public partial class BattleSystem
	{
		// ---- timeline (Deltarune frames into the Death phase) ----
		/// <summary>The screen cuts to black at once; the SOUL sits alone for a second, then cracks.</summary>
		private const int SoulCrackFrame = 30;
		/// <summary>The cracked SOUL holds for another second, then breaks apart.</summary>
		private const int SoulShatterFrame = SoulCrackFrame + 30;
		/// <summary>The shards fall away; then the player dies for real.</summary>
		private const int SoulDeathEndFrame = SoulShatterFrame + 45;

		private bool deathPending;
		private PlayerDeathReason deathReason;
		private double deathDamage;

		private struct SoulShard
		{
			public Vector2 Position, Velocity;
			public float Frame;
		}
		private readonly List<SoulShard> soulShards = new();

		/// <summary>
		/// Called from <see cref="BattlePlayer.PreKill"/> when a hit in the battle would kill the player. The death is
		/// held back and replayed once the SOUL has shattered.
		/// </summary>
		public void RequestSoulDeath(PlayerDeathReason reason, double damage)
		{
			if (phase == Phase.Death || deathPending)
				return;
			// Multiplayer: down instead of dead while a partner is still standing
			if (TryGoDown())
				return;
			deathReason = reason;
			deathDamage = damage;
			// The bullet loop may be running right now; the death starts once it's done
			deathPending = true;
		}

		private void BeginSoulDeath()
		{
			deathPending = false;
			battleLife = 0;
			Bullets.Clear();
			boxAfterimages.Clear();
			soulShards.Clear();
			panelDir = 0;
			// Deltarune cuts the music the moment the SOUL is lost
			music?.Stop();
			musicStarted = false;
			SetPhase(Phase.Death);
		}

		private void UpdateSoulDeath()
		{
			if (phaseTicks == SoulCrackFrame * TicksPerFrame)
				DeltaruneAssets.Play("soulcrack", SoundID.Item27);
			if (phaseTicks == SoulShatterFrame * TicksPerFrame)
			{
				DeltaruneAssets.Play("soulshatter", SoundID.Shatter);
				Vector2 center = SoulCenter;
				for (int i = 0; i < 6; i++)
				{
					soulShards.Add(new SoulShard
					{
						Position = center + Main.rand.NextVector2Circular(4f, 4f),
						// spread out sideways, popped upward, then gravity takes them
						Velocity = new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-5f, -1.5f)),
						Frame = Main.rand.Next(4),
					});
				}
			}

			// Shards fall every tick (velocities are per Deltarune frame)
			for (int i = 0; i < soulShards.Count; i++)
			{
				SoulShard s = soulShards[i];
				s.Velocity.Y += 0.35f * FrameStep;
				s.Position += s.Velocity * FrameStep;
				s.Frame += 0.2f * FrameStep;
				soulShards[i] = s;
			}

			if (phaseTicks >= SoulDeathEndFrame * TicksPerFrame)
				End(killPlayer: true);
		}

		/// <summary>Black over everything, with the SOUL (whole, cracked, or in pieces) on top.</summary>
		private void DrawSoulDeath(float left, float top, float width, float height)
		{
			if (phase != Phase.Death)
				return;

			// A hard cut to black, no fade
			DrDraw.Rect(left, top, width, height, Color.Black);

			int frame = phaseTicks / TicksPerFrame;
			if (frame < SoulCrackFrame)
			{
				if (!DrDraw.Sprite("spr_dodgeheart", 0, soul.X, soul.Y, Color.White))
					DrDraw.HeartShapeAt(soul.X + 2, soul.Y + 2, 16, Color.Red);
			}
			else if (frame < SoulShatterFrame)
			{
				if (!DrDraw.Sprite("spr_heartbreak", 0, soul.X, soul.Y, Color.White))
				{
					// Fallback crack: the heart's two halves pulled a little apart
					DrDraw.HeartShapeAt(soul.X + 1, soul.Y + 2, 16, Color.Red);
					DrDraw.Rect(soul.X + 9, soul.Y + 2, 2, 16, Color.Black);
				}
			}
			else
			{
				foreach (SoulShard s in soulShards)
				{
					if (!DrDraw.Sprite("spr_heartshards", (int)s.Frame, s.Position.X, s.Position.Y, Color.White))
						DrDraw.Rect(s.Position.X - 2f, s.Position.Y - 2f, 4f, 4f, Color.Red);
				}
			}
		}
	}
}
