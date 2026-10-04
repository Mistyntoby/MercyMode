using System;
using Microsoft.Xna.Framework;
using Terraria;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// Hitting an enemy to start the battle is a first strike: before the party panel comes up, the hero's intro swing
	/// lands as a FIGHT hit (one perfectly timed bolt of the current weapon, nothing spent) and "* You struck first!"
	/// shows along the bottom.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>This battle opens with the hero's hit.</summary>
		private bool firstStrike;
		/// <summary>Multiplayer: when we asked the server for a battle after hitting something (it answers a moment later).</summary>
		private static uint firstStrikeAskedAt;
		/// <summary>How much longer the intro runs for the strike (the hit, the number, the line).</summary>
		private const int FirstStrikeExtraTicks = 50;
		/// <summary>Ticks from the swing until the hit lands (obj_heroparent: 10 frames).</summary>
		private const int FirstStrikeHitTicks = 10 * TicksPerFrame;
		private const string FirstStrikeLine = "* You struck first!";

		private static bool IsFirstStrikeReason(string reason) => reason != null && reason.StartsWith("hit by", StringComparison.Ordinal);

		/// <summary>When the bottom panel starts up in this battle's intro.</summary>
		private int IntroPanelTick => IntroPanelAt + (firstStrike ? FirstStrikeExtraTicks : 0);

		/// <summary>Whether a battle starting now (for this reason) opens with the hero's strike.</summary>
		private bool OpensWithStrike(string reason)
		{
			if (duelWith >= 0 || spectating)
				return false;
			if (IsFirstStrikeReason(reason))
				return true;
			// Multiplayer: the server started it for us shortly after our hit
			return reason == "multiplayer" && firstStrikeAskedAt != 0 && Main.GameUpdateCount - firstStrikeAskedAt < 2 * 60;
		}

		/// <summary>The intro swing is a real hit: the weapon's effect now, the damage when it lands.</summary>
		private void FirstStrikeUpdate()
		{
			if (!firstStrike)
				return;
			if (phaseTicks == IntroSwingAt)
			{
				fightWeapon = CurrentWeapon();
				SetHeroPose(HeroPose.Attack);
				if (fightWeapon.Shoots)
					FireShot(fightWeapon, fightWeapon.Projectile);
				else
				{
					Sfx("slash");
					MeleeEffect(fightWeapon);
				}
			}
			if (phaseTicks == IntroSwingAt + FirstStrikeHitTicks && fightWeapon != null && encounter.Alive)
			{
				ResolveHit(new PendingHit
				{
					Points = 150,
					Damage = Math.Max(1, fightWeapon.ShotDamage),
					Crit = Main.rand.Next(100) < fightWeapon.Crit,
					Ranged = fightWeapon.Shoots,
				});
				// The summon waits for a real FIGHT
				summonPendingTicks = -1;
			}
			if (phaseTicks == IntroSwingAt + FirstStrikeHitTicks + 2 * TicksPerFrame)
				SetHeroPose(HeroPose.Idle);
		}

		/// <summary>The line along the bottom while the strike plays (the panel is still down).</summary>
		private void DrawFirstStrikeLine()
		{
			if (!firstStrike || phase != Phase.Intro || phaseTicks < IntroSwingAt || panel > 0)
				return;
			float a = Math.Min(1f, (phaseTicks - IntroSwingAt) / 10f);
			int shown = Math.Min(FirstStrikeLine.Length, (phaseTicks - IntroSwingAt) / 2);
			string line = FirstStrikeLine.Substring(0, shown);
			float y = ScreenHeight - 70;
			DrDraw.Rect(0, y - 14, ScreenWidth, 56, Color.Black * (0.75f * a));
			DrDraw.Text(line, 32, y + 2, Color.Black * a);
			DrDraw.Text(line, 30, y, Color.White * a);
		}
	}
}
