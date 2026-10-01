namespace MercyMode.Battle
{
	/// <summary>
	/// Numbers measured from Deltarune's battle code (chapter 1). Deltarune runs at 30 FPS and Terraria at 60,
	/// so durations in frames double and per-frame speeds halve. Distances are in Deltarune's 640x480 pixels;
	/// the battle screen is drawn in that space and scaled up.
	/// </summary>
	public static class BattleConstants
	{
		/// <summary>Terraria ticks per Deltarune frame.</summary>
		public const int TicksPerFrame = 2;

		public const int ScreenWidth = 640;
		public const int ScreenHeight = 480;
		/// <summary>Scale used to draw the Terraria player on the battle screen.</summary>
		public const float BattleCharacterScale = 1.5f;

		// ---- SOUL (obj_heart) ----
		/// <summary>global.sp = 4 px per frame.</summary>
		public const float SoulSpeed = 4f / TicksPerFrame;
		/// <summary>Holding X halves it: ceil(4 * 0.5) = 2 px per frame.</summary>
		public const float SoulSlowSpeed = 2f / TicksPerFrame;
		/// <summary>spr_dodgeheart is 20x20, drawn from its top-left corner.</summary>
		public const int SoulSize = 20;
		/// <summary>spr_dodgeheartmask bbox L2..R17, T2..B17: a 16x16 hitbox inset 2 px.</summary>
		public const int SoulHitInset = 2;
		public const int SoulHitSize = 16;
		/// <summary>Box keeps the SOUL's top-left in [left + 5, right - 22] (obj_growtangle Step_2).</summary>
		public const int BoxClampLow = 5;
		public const int BoxClampHigh = 22;

		// ---- Bullet box (obj_growtangle, spr_battlebg_0 75x75 at scale 2) ----
		public const int BoxSize = 150;
		/// <summary>maxtimer = 15 frames to grow (or shrink), spinning 180 degrees on the way.</summary>
		public const int BoxGrowTicks = 15 * TicksPerFrame;
		public const float BoxCenterX = 320f;
		public const float BoxCenterY = 170f;

		// ---- Getting hit (scr_damage) ----
		/// <summary>global.inv = global.invc * 40 frames, invc = 1. Bullets only hurt while inv &lt; 0.</summary>
		public const int InvincibleTicks = 40 * TicksPerFrame;
		/// <summary>image_speed 0.25 while invincible: a new frame every 4 frames.</summary>
		public const int SoulBlinkTicks = 4 * TicksPerFrame;

		// ---- Graze (obj_grazebox, spr_grazemask 50x50 centred on the SOUL) ----
		public const int GrazeSize = 50;
		/// <summary>obj_regularbullet: grazepoints = 5, timepoints = 5.</summary>
		public const float DefaultGrazePoints = 5f;
		public const float DefaultTimePoints = 5f;
		/// <summary>While still touching: grazepoints / 20 and timepoints / 20 every frame.</summary>
		public const float GrazeHoldDivisor = 20f;
		/// <summary>Grazing only shortens the turn while turntimer >= 10 frames.</summary>
		public const int GrazeTurnCutMinTicks = 10 * TicksPerFrame;
		/// <summary>grazetimer = 10 frames of flash after the first touch.</summary>
		public const int GrazeFlashTicks = 10 * TicksPerFrame;

		// ---- TP (tension) ----
		/// <summary>global.maxtension = 250. Mercy Mode's TP is 0-100.</summary>
		public const float MaxTension = 250f;
		public const float TensionToTP = 100f / MaxTension;
		/// <summary>DEFEND: scr_tensionheal(40).</summary>
		public const float DefendTension = 40f;
		/// <summary>DEFEND: tdamage = ceil(2 * tdamage / 3).</summary>
		public const float DefendDamageMult = 2f / 3f;

		// ---- FIGHT bar (obj_attackpress) ----
		/// <summary>boltspeed = 8 px per frame.</summary>
		public const float BoltSpeed = 8f;
		/// <summary>First bolt: boltframe = 30 + boltxoff, and boltxoff starts at -1.</summary>
		public const int BoltStartFrame = 29;
		/// <summary>A press counts while close &lt; 15 and close &gt; -5 (frames).</summary>
		public const int BoltWindowEarly = 15;
		public const int BoltWindowLate = 5;
		/// <summary>The dark box behind the bolt is 15 * boltspeed wide.</summary>
		public const int FightBoxWidth = 15 * 8;
		/// <summary>After the attack resolves (timermax = 50 frames with an attacker).</summary>
		public const int FightPostTicks = 50 * TicksPerFrame;
		/// <summary>Fade out: alpha += 0.08 per frame.</summary>
		public const float FightFadePerTick = 0.08f / TicksPerFrame;

		/// <summary>scr_boltcheck_onebutton: points for a press that is |off| frames from perfect.</summary>
		public static int BoltPoints(int framesOff)
		{
			int p = framesOff < 0 ? -framesOff : framesOff;
			return p switch
			{
				0 => 150,
				1 => 120,
				2 => 110,
				_ => 100 - p * 2,
			};
		}

		/// <summary>Damage = AT * points / 20 (before defense). A hit adds round(points / 10) tension.</summary>
		public const float DamagePointsDivisor = 20f;
		public const float HitTensionDivisor = 10f;

		// ---- Turns ----
		/// <summary>Most common global.turntimer in chapter 1 is 120-180 frames. 150 frames.</summary>
		public const int DefaultEnemyTurnTicks = 150 * TicksPerFrame;

		// ---- HUD (obj_battlecontroller) ----
		/// <summary>bpy = 152: bottom panel height.</summary>
		public const int PanelHeight = 152;
		/// <summary>The panel slides 30 px per frame, easing by (target - bp) / 2.5 once it's within 40.</summary>
		public const int PanelSlidePerFrame = 30;
		/// <summary>Text writer: one character per frame.</summary>
		public const float TextCharsPerTick = 1f / TicksPerFrame;
	}
}
