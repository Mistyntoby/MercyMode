using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using MercyMode.Battle.Net;

namespace MercyMode.Battle
{
	/// <summary>
	/// The battle screen's side of multiplayer (<see cref="BattleNet"/>): waiting for the party before the enemies
	/// attack, MERCY and spares from the others, their hits, and drawing them.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>The server said everyone is ready: this player turn's enemy turn may start.</summary>
		private bool enemyTurnGranted;

		private static string WaitingText()
		{
			var names = BattleNet.WaitingOn.ToList();
			return names.Count == 0 ? "* ..." : $"* Waiting for {string.Join(" and ", names)}...";
		}

		private void UpdateWaiting()
		{
			text = WaitingText();
			textShown = text.Length;
			// Everyone else left: no one to wait for
			if (!BattleNet.Allies.Any())
				OnNetEnemyTurn();
		}

		/// <summary>Leaves the battle on this client only (multiplayer /mmbattle end).</summary>
		public void Leave()
		{
			if (Active)
				End();
		}

		/// <summary>The whole party has picked an action (or the wait ran out).</summary>
		internal void OnNetEnemyTurn()
		{
			if (phase != Phase.Waiting)
				return;
			enemyTurnGranted = true;
			StartEnemyTurn();
		}

		/// <summary>An enemy's MERCY as the server has it, after anyone's ACT.</summary>
		internal void OnNetMercy(NPC npc, float mercy)
		{
			foreach (BattleEnemy e in enemies)
				if (e.E.Npc == npc)
					e.E.SetMercyQuiet(mercy);
		}

		/// <summary>Another party member spared this enemy.</summary>
		internal void OnNetSpared(NPC npc)
		{
			BattleEnemy e = enemies.FirstOrDefault(x => !x.Out && (x.E.Npc == npc || x.E.Members().Contains(npc)));
			if (e == null)
				return;
			e.Out = true;
			WithEnemy(e, PlayEnemySpared);
		}

		/// <summary>Another party member's FIGHT hit landed: show it here too.</summary>
		internal void OnNetHit(NPC npc, int damage, bool crit)
		{
			BattleEnemy e = enemies.FirstOrDefault(x => x.E.Npc == npc || x.E.Members().Contains(npc));
			if (e == null || phase == Phase.None)
				return;
			WithEnemy(e, () =>
			{
				EnemyNumber(damage, crit ? HeroCritColor : HeroDamageColor, damage > 0 ? -1 : DamageNumber.MissFrame, at: PartSpot(npc));
				enemyShake = 18;
			});
			Sfx("damage");
		}

		// ---- drawing the party ----

		/// <summary>Party boxes sit side by side across the panel (one centred, three filling it), like Deltarune's.</summary>
		private static int PartyBoxX(int index) => (int)Math.Round(320f - BattleNet.PartySize * 213f / 2f + index * 213f + 0.5f);

		/// <summary>Where the other party members stand: one above-behind, one below-behind the player.</summary>
		private static readonly Vector2[] AllyFeet = { new(70, 146), new(70, 292) };

		private void DrawAllies(SpriteBatch sb, Matrix m)
		{
			if (!BattleNet.Online)
				return;
			float fly = FlyProgress();
			int slot = 0;
			foreach (Player p in BattleNet.Allies)
			{
				if (slot >= AllyFeet.Length)
					break;
				Vector2 feet = AllyFeet[slot++];
				if (p.dead)
					continue;
				float bob = (float)Math.Round(Math.Sin((time + slot * 25) / 20f));
				// They fade in and out with the glide, standing in their battle spot
				DrawPlayerPose(sb, m, p, feet + new Vector2(0f, bob), HeroScale, HeroPose.Idle, 0f, shadow: (1f - fly) * 0.9f, ally: true);
			}
		}

		private void DrawAllyBoxes(float top)
		{
			if (!BattleNet.Online || BattleNet.PartySize < 2)
				return;
			for (int i = 0; i < BattleNet.Party.Count; i++)
			{
				int who = BattleNet.Party[i];
				if (who == Main.myPlayer || who < 0 || who >= Main.maxPlayers)
					continue;
				Player p = Main.player[who];
				var r = new Rectangle(PartyBoxX(i), (int)top, 212, 34);
				DrDraw.Rect(r.X, r.Y, r.Width, 34f, Color.Black);
				DrDraw.Outline(r.X, r.Y - 2, r.Width, 36, new Color(70, 70, 90), 2);

				// The command they picked, or a still-thinking dot
				if (BattleNet.ReadyFaces.TryGetValue(who, out int face) && face > 0)
					DrDraw.Sprite("spr_headkris", face, r.X + 3, r.Y + 4, Color.White);
				else if (!BattleNet.ReadyFaces.ContainsKey(who))
					DrDraw.Text("...", r.X + 8, r.Y + 8, new Color(128, 128, 128), DrDraw.SmallFont);

				const float nameScale = 0.68f, nameRoom = 64f;
				string name = (p.active ? p.name : "?").ToUpperInvariant();
				float scale = MathHelper.Clamp(nameRoom / Math.Max(1f, DrDraw.Measure(name, DrDraw.BigFont)), 0.4f, nameScale);
				while (name.Length > 1 && DrDraw.Measure(name, DrDraw.BigFont) * scale > nameRoom)
					name = name.Substring(0, name.Length - 1);
				DrDraw.Text(name, r.X + 40, r.Y + 7, p.dead ? Color.Gray : Color.White, DrDraw.BigFont, scale);

				const int labelX = 112, barX = 130, barWidth = 28, barY = 10, barHeight = 12;
				float ratio = p.statLifeMax2 > 0 ? MathHelper.Clamp(p.statLife / (float)p.statLifeMax2, 0f, 1f) : 0f;
				DrDraw.Text("HP", r.X + labelX - 4, r.Y + barY - 3, Color.White, DrDraw.SmallFont);
				DrDraw.Rect(r.X + barX, r.Y + barY, barWidth, barHeight, new Color(128, 0, 0));
				DrDraw.Rect(r.X + barX, r.Y + barY, (float)Math.Ceiling(ratio * barWidth), barHeight, new Color(255, 160, 64));
				string hp = $"{Math.Max(0, p.statLife)}/{p.statLifeMax2}";
				float room = 208 - (barX + barWidth + 3);
				float numberScale = Math.Min(1f, room / Math.Max(1f, DrDraw.Measure(hp, DrDraw.SmallFont)));
				DrDraw.Text(hp, r.X + barX + barWidth + 3, r.Y + barY + barHeight / 2f - DrDraw.LineHeight(DrDraw.SmallFont) * numberScale / 2f,
					ratio <= 0.25f ? new Color(255, 255, 0) : Color.White, DrDraw.SmallFont, numberScale);
			}
		}
	}
}
