using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using MercyMode.Deltarune;

namespace MercyMode.Battle
{
	/// <summary>
	/// Drawing in Deltarune's 640x480 space. The sprite batch is begun with a matrix that maps it onto the
	/// screen, so every coordinate here is a Deltarune pixel.
	/// </summary>
	public static class DrDraw
	{
		public static SpriteBatch Sb;

		private static readonly Rectangle Pixel = new(0, 0, 1, 1);

		public static void Rect(float x, float y, float w, float h, Color color)
		{
			if (w <= 0 || h <= 0)
				return;
			Sb.Draw(TextureAssets.MagicPixel.Value, new Vector2(x, y), Pixel, color, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);
		}

		/// <summary>A straight line of some thickness from a to b (beams, telegraphs).</summary>
		public static void Line(Vector2 a, Vector2 b, float thickness, Color color)
		{
			Vector2 d = b - a;
			float length = d.Length();
			if (length <= 0f || thickness <= 0f)
				return;
			Sb.Draw(TextureAssets.MagicPixel.Value, a, Pixel, color, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f),
				new Vector2(length, thickness), SpriteEffects.None, 0f);
		}

		/// <summary>GameMaker's draw_rectangle(..., outline = true) with a 1 px line, or thicker.</summary>
		public static void Outline(float x, float y, float w, float h, Color color, float t = 1f)
		{
			Rect(x, y, w, t, color);
			Rect(x, y + h - t, w, t, color);
			Rect(x, y, t, h, color);
			Rect(x + w - t, y, t, h, color);
		}

		/// <summary>Draws a Deltarune sprite like draw_sprite_ext. Returns false if it isn't loaded.</summary>
		public static bool Sprite(string name, int frame, float x, float y, Color color, float scale = 1f, float rotation = 0f, float alpha = 1f)
		{
			DrSprite s = DeltaruneAssets.Sprite(name);
			if (s == null)
				return false;
			Sb.Draw(s.Frame(frame), new Vector2(x, y), null, color * alpha, rotation, s.Origin, scale, SpriteEffects.None, 0f);
			return true;
		}

		public static bool Sprite(string name, int frame, float x, float y, Color color, Vector2 scale, float rotation = 0f, float alpha = 1f)
		{
			DrSprite s = DeltaruneAssets.Sprite(name);
			if (s == null)
				return false;
			Sb.Draw(s.Frame(frame), new Vector2(x, y), null, color * alpha, rotation, s.Origin, scale, SpriteEffects.None, 0f);
			return true;
		}

		/// <summary>Draws part of a sprite frame (for bars that fill up).</summary>
		public static bool SpritePart(string name, int frame, float x, float y, Rectangle source, Color color)
		{
			DrSprite s = DeltaruneAssets.Sprite(name);
			if (s == null)
				return false;
			Sb.Draw(s.Frame(frame), new Vector2(x + source.X, y + source.Y) - s.Origin, source, color, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
			return true;
		}

		// 7x6 heart for when the real sprites aren't there
		private static readonly string[] HeartShape = {
			".XX.XX.",
			"XXXXXXX",
			"XXXXXXX",
			".XXXXX.",
			"..XXX..",
			"...X...",
		};

		/// <summary>Fallback heart, top-left at (x, y), size x size.</summary>
		public static void HeartShapeAt(float x, float y, float size, Color color)
		{
			float px = size / 7f;
			for (int row = 0; row < HeartShape.Length; row++)
				for (int col = 0; col < HeartShape[row].Length; col++)
					if (HeartShape[row][col] == 'X')
						Rect(x + col * px, y + row * px + px * 0.5f, px, px, color);
		}

		// ---- text ----

		public const string BigFont = "fnt_mainbig";
		public const string SmallFont = "fnt_main";

		/// <summary>Height of one line of text in a font, in Deltarune pixels.</summary>
		public static int LineHeight(string font)
		{
			DrFont f = DeltaruneAssets.Font(font);
			if (f != null)
				return f.LineHeight;
			return font == BigFont ? 32 : 16;
		}

		private static float FallbackScale(string font) => font == BigFont ? 1.05f : 0.55f;

		public static float Measure(string text, string font)
		{
			DrFont f = DeltaruneAssets.Font(font);
			if (f == null)
			{
				// No fonts on a dedicated server (the headless lab): a fixed width per character
				if (Main.dedServ)
					return text.Length * (font == BigFont ? 16f : 8f);
				return FontAssets.MouseText.Value.MeasureString(text).X * FallbackScale(font);
			}
			float w = 0;
			foreach (char c in text)
				if (f.Glyphs.TryGetValue(c, out var g))
					w += g.Shift;
			return w;
		}

		/// <summary>Draws text like draw_text with the given Deltarune font. '\n' starts a new line.</summary>
		public static void Text(string text, float x, float y, Color color, string font = BigFont, float scale = 1f)
		{
			DrFont f = DeltaruneAssets.Font(font);
			if (f == null)
			{
				float s = FallbackScale(font) * scale;
				int line = 0;
				foreach (string l in text.Split('\n'))
				{
					Sb.DrawString(FontAssets.MouseText.Value, l, new Vector2(x, y + line * LineHeight(font) * scale), color, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
					line++;
				}
				return;
			}

			float cx = x, cy = y;
			foreach (char c in text)
			{
				if (c == '\n')
				{
					cx = x;
					cy += f.LineHeight * scale;
					continue;
				}
				if (!f.Glyphs.TryGetValue(c, out var g) && !f.Glyphs.TryGetValue('?', out g))
					continue;
				if (g.Width > 0 && g.Height > 0)
					Sb.Draw(f.Texture, new Vector2(cx + g.Offset * scale, cy), new Rectangle(g.X, g.Y, g.Width, g.Height), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				cx += g.Shift * scale;
			}
		}

		/// <summary>Breaks text into lines no wider than maxWidth, keeping existing line breaks.</summary>
		public static string Wrap(string text, float maxWidth, string font = BigFont)
		{
			var result = new StringBuilder();
			foreach (string paragraph in text.Split('\n'))
			{
				if (result.Length > 0)
					result.Append('\n');
				// Continuation lines of a "* " line are indented like Deltarune's
				string indent = paragraph.StartsWith("* ") ? "  " : "";
				var line = new StringBuilder();
				foreach (string word in paragraph.Split(' '))
				{
					string candidate = line.Length == 0 ? word : line + " " + word;
					if (line.Length > 0 && Measure(candidate, font) > maxWidth)
					{
						result.Append(line).Append('\n');
						line.Clear().Append(indent).Append(word);
					}
					else
					{
						line.Clear().Append(candidate);
					}
				}
				result.Append(line);
			}
			return result.ToString();
		}

		/// <summary>Big damage numbers from spr_numbersfontbig (digits 0-9), or the font as a fallback.</summary>
		public static void Number(int value, float centerX, float y, Color color, float alpha = 1f)
		{
			string s = value.ToString();
			DrSprite digits = DeltaruneAssets.Sprite("spr_numbersfontbig");
			if (digits == null || digits.Frames.Length < 10)
			{
				Text(s, centerX - Measure(s, BigFont) / 2f, y, color * alpha);
				return;
			}
			float w = digits.Width * s.Length;
			float x = centerX - w / 2f;
			foreach (char c in s)
			{
				Sb.Draw(digits.Frame(c - '0'), new Vector2(x, y), null, color * alpha, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
				x += digits.Width;
			}
		}
	}
}
