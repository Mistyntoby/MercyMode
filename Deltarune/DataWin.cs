// Minimal GameMaker data.win reader for Mercy Mode.
// The file layout and GameMaker's QOI variant are based on UndertaleModTool
// (https://github.com/UnderminersTeam/UndertaleModTool, GPL-3.0), so this file is GPL-3.0 too.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace MercyMode.Deltarune
{
	/// <summary>Decoded sprite frame as raw RGBA, ready to become a Texture2D on the main thread.</summary>
	public sealed class RawFrame
	{
		public int Width, Height;
		public byte[] Rgba;
	}

	public sealed class RawSprite
	{
		public string Name;
		public int Width, Height, OriginX, OriginY;
		public List<RawFrame> Frames = new();
	}

	/// <summary>Raw sound bytes. Either a WAV (RIFF) or OGG file.</summary>
	public sealed class RawSound
	{
		public string Name;
		public float Volume = 1f, Pitch = 1f;
		public byte[] Data;
	}

	public sealed class DataWin : IDisposable
	{
		public readonly string Path;
		private readonly FileStream fs;
		private readonly BinaryReader br;
		private readonly Dictionary<string, long> chunks = new();

		/// <summary>Sprite name -> SPRT entry address.</summary>
		public readonly Dictionary<string, long> Sprites = new();

		/// <summary>Sound name -> SOND entry address.</summary>
		public readonly Dictionary<string, long> Sounds = new();

		private List<uint> txtrEntries;
		private List<uint> audoEntries;
		private readonly Dictionary<int, (int w, int h, byte[] rgba)> pageCache = new();

		public DataWin(string path)
		{
			Path = path;
			fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
			br = new BinaryReader(fs);

			if (ReadMagic(0) != "FORM")
				throw new InvalidDataException($"{path} is not a GameMaker data file");

			long pos = 8;
			long end = 8 + U32(4);
			while (pos + 8 <= end && pos + 8 <= fs.Length)
			{
				string name = ReadMagic(pos);
				uint size = U32(pos + 4);
				chunks[name] = pos + 8; // start of chunk contents
				pos += 8 + size;
			}

			IndexNames("SPRT", Sprites);
			IndexNames("SOND", Sounds);
		}

		public void Dispose()
		{
			br.Dispose();
			fs.Dispose();
			pageCache.Clear();
		}

		/// <summary>Frees decoded texture pages. Call after you're done pulling sprites.</summary>
		public void ClearPageCache() => pageCache.Clear();

		// ---------- low level ----------

		private uint U32(long addr) { fs.Position = addr; return br.ReadUInt32(); }
		private int I32(long addr) { fs.Position = addr; return br.ReadInt32(); }
		private float F32(long addr) { fs.Position = addr; return br.ReadSingle(); }
		private ushort U16(long addr) { fs.Position = addr; return br.ReadUInt16(); }
		private short I16(long addr) { fs.Position = addr; return br.ReadInt16(); }
		private byte[] Bytes(long addr, int count) { fs.Position = addr; return br.ReadBytes(count); }
		private string ReadMagic(long addr) => Encoding.ASCII.GetString(Bytes(addr, 4));

		/// <summary>GameMaker string pointers point at the characters; the length sits 4 bytes before.</summary>
		private string Str(uint addr)
		{
			if (addr == 0 || addr < 4 || addr >= fs.Length)
				return null;
			int len = I32(addr - 4);
			if (len < 0 || addr + len > fs.Length)
				return null;
			return Encoding.UTF8.GetString(Bytes(addr, len));
		}

		private List<uint> PointerList(string chunk)
		{
			var list = new List<uint>();
			if (!chunks.TryGetValue(chunk, out long start))
				return list;
			uint count = U32(start);
			fs.Position = start + 4;
			for (int i = 0; i < count; i++)
				list.Add(br.ReadUInt32());
			return list;
		}

		private void IndexNames(string chunk, Dictionary<string, long> into)
		{
			foreach (uint entry in PointerList(chunk))
			{
				if (entry == 0)
					continue;
				string name = Str(U32(entry));
				if (name != null)
					into[name] = entry;
			}
		}

		// ---------- sprites ----------

		public RawSprite ReadSprite(string name)
		{
			if (!Sprites.TryGetValue(name, out long a))
				return null;

			var sprite = new RawSprite
			{
				Name = name,
				Width = (int)U32(a + 4),
				Height = (int)U32(a + 8),
				OriginX = I32(a + 48),
				OriginY = I32(a + 52),
			};

			// Offset 56 is either -1 (GMS2 "special" header follows) or the frame count directly
			long pos = a + 56;
			if (I32(pos) == -1)
			{
				uint version = U32(a + 60);
				uint type = U32(a + 64);
				if (type != 0)
					return null; // SWF / Spine sprites aren't supported
				pos = a + 76; // skip playback speed + speed type
				if (version >= 2) pos += 4; // sequence offset
				if (version >= 3) pos += 4; // nine-slice offset
			}

			int frameCount = I32(pos);
			var tpags = new List<uint>();
			fs.Position = pos + 4;
			for (int i = 0; i < frameCount; i++)
				tpags.Add(br.ReadUInt32());

			foreach (uint tpag in tpags)
				sprite.Frames.Add(ReadFrame(tpag));
			return sprite;
		}

		private RawFrame ReadFrame(uint tpag)
		{
			int srcX = U16(tpag), srcY = U16(tpag + 2), srcW = U16(tpag + 4), srcH = U16(tpag + 6);
			int tgtX = U16(tpag + 8), tgtY = U16(tpag + 10), tgtW = U16(tpag + 12), tgtH = U16(tpag + 14);
			int boundW = U16(tpag + 16), boundH = U16(tpag + 18);
			int page = I16(tpag + 20);

			var frame = new RawFrame { Width = Math.Max(1, boundW), Height = Math.Max(1, boundH) };
			frame.Rgba = new byte[frame.Width * frame.Height * 4];

			var (pw, ph, pix) = GetPage(page);
			for (int y = 0; y < tgtH; y++)
			{
				int dy = tgtY + y;
				if (dy < 0 || dy >= frame.Height) continue;
				// Nearest-neighbor in case the page stored the frame at a different size
				int sy = srcY + (srcH == tgtH ? y : y * srcH / Math.Max(1, tgtH));
				if (sy < 0 || sy >= ph) continue;
				for (int x = 0; x < tgtW; x++)
				{
					int dx = tgtX + x;
					if (dx < 0 || dx >= frame.Width) continue;
					int sx = srcX + (srcW == tgtW ? x : x * srcW / Math.Max(1, tgtW));
					if (sx < 0 || sx >= pw) continue;
					Buffer.BlockCopy(pix, (sy * pw + sx) * 4, frame.Rgba, (dy * frame.Width + dx) * 4, 4);
				}
			}
			return frame;
		}

		// ---------- texture pages ----------

		private (int w, int h, byte[] rgba) GetPage(int index)
		{
			if (pageCache.TryGetValue(index, out var cached))
				return cached;

			txtrEntries ??= PointerList("TXTR");
			if (index < 0 || index >= txtrEntries.Count)
				throw new InvalidDataException($"Texture page {index} out of range");

			long blob = FindTextureBlob(txtrEntries[index]);
			if (blob < 0)
				throw new InvalidDataException($"Texture page {index} has no embedded image (external textures aren't supported)");

			var decoded = DecodeImage(blob);
			pageCache[index] = decoded;
			return decoded;
		}

		/// <summary>
		/// TXTR entries grew extra fields across GameMaker versions (mips, block size, width/height...).
		/// Instead of guessing the version, find the field that points at a known image header.
		/// </summary>
		private long FindTextureBlob(uint entry)
		{
			for (int off = 4; off <= 28; off += 4)
			{
				uint p = U32(entry + off);
				if (p < 8 || p + 8 > fs.Length)
					continue;
				byte[] m = Bytes(p, 4);
				if (m[0] == 0x89 && m[1] == 'P' && m[2] == 'N' && m[3] == 'G') return p;
				if (m[0] == 'f' && m[1] == 'i' && m[2] == 'o' && m[3] == 'q') return p;
				if (m[0] == '2' && m[1] == 'z' && m[2] == 'o' && m[3] == 'q') return p;
			}
			return -1;
		}

		private (int w, int h, byte[] rgba) DecodeImage(long addr)
		{
			byte[] magic = Bytes(addr, 4);

			if (magic[0] == 0x89) // PNG: walk chunks to find IEND, then let FNA decode it
			{
				long p = addr + 8;
				while (true)
				{
					byte[] lenBytes = Bytes(p, 4);
					uint len = (uint)(lenBytes[0] << 24 | lenBytes[1] << 16 | lenBytes[2] << 8 | lenBytes[3]);
					string type = ReadMagic(p + 4);
					p += 12 + len;
					if (type == "IEND") break;
				}
				using var ms = new MemoryStream(Bytes(addr, (int)(p - addr)));
				Texture2D.TextureDataFromStreamEXT(ms, out int w, out int h, out byte[] pixels);
				return (w, h, pixels);
			}

			if (magic[0] == 'f') // QOI
			{
				int len = I32(addr + 8);
				return Qoi.Decode(Bytes(addr, 12 + len));
			}

			// BZip2 + QOI. GameMaker 2022.5+ inserts a 4 byte uncompressed size before the BZ2 stream.
			byte[] after = Bytes(addr + 8, 3);
			long bzStart = after[0] == 'B' && after[1] == 'Z' && after[2] == 'h' ? addr + 8 : addr + 12;
			fs.Position = bzStart;
			using var output = new MemoryStream();
			using (var bz = new Ionic.BZip2.BZip2InputStream(fs, true))
				bz.CopyTo(output);
			return Qoi.Decode(output.ToArray());
		}

		// ---------- sounds ----------

		public RawSound ReadSound(string name)
		{
			if (!Sounds.TryGetValue(name, out long a))
				return null;

			var sound = new RawSound
			{
				Name = name,
				Volume = F32(a + 20),
				Pitch = F32(a + 24),
			};
			string file = Str(U32(a + 12));
			int group = I32(a + 28);
			int audioId = I32(a + 32);
			string dir = System.IO.Path.GetDirectoryName(Path);

			if (audioId >= 0 && group == 0)
			{
				audoEntries ??= PointerList("AUDO");
				if (audioId < audoEntries.Count)
				{
					uint e = audoEntries[audioId];
					sound.Data = Bytes(e + 4, (int)U32(e));
				}
			}
			else if (audioId >= 0 && group > 0)
			{
				// Sound lives in a separate audio group file next to data.win
				string groupPath = System.IO.Path.Combine(dir, $"audiogroup{group}.dat");
				if (File.Exists(groupPath))
				{
					using var groupFile = new DataWin(groupPath);
					var entries = groupFile.PointerList("AUDO");
					if (audioId < entries.Count)
					{
						uint e = entries[audioId];
						sound.Data = groupFile.Bytes(e + 4, (int)groupFile.U32(e));
					}
				}
			}
			else if (!string.IsNullOrEmpty(file))
			{
				// Streamed sound stored as its own file (usually music)
				foreach (string candidate in new[] { file, file + ".ogg", System.IO.Path.Combine("mus", file) })
				{
					string full = System.IO.Path.Combine(dir, candidate);
					if (File.Exists(full))
					{
						sound.Data = File.ReadAllBytes(full);
						break;
					}
				}
			}

			return sound.Data == null ? null : sound;
		}
	}

	/// <summary>GameMaker's QOI variant ("fioq"), ported from UndertaleModTool's QoiConverter. Outputs RGBA.</summary>
	public static class Qoi
	{
		public static (int w, int h, byte[] rgba) Decode(byte[] bytes)
		{
			if (bytes.Length < 12 || bytes[0] != 'f' || bytes[1] != 'i' || bytes[2] != 'o' || bytes[3] != 'q')
				throw new InvalidDataException("Invalid GameMaker QOI header");

			int width = bytes[4] | bytes[5] << 8;
			int height = bytes[6] | bytes[7] << 8;
			int length = bytes[8] | bytes[9] << 8 | bytes[10] << 16 | bytes[11] << 24;
			int dataEnd = Math.Min(bytes.Length, 12 + length);

			byte[] output = new byte[width * height * 4];
			byte[] index = new byte[64 * 4];
			int pos = 12, run = 0;
			byte r = 0, g = 0, b = 0, a = 255;

			for (int o = 0; o < output.Length; o += 4)
			{
				if (run > 0)
				{
					run--;
				}
				else if (pos < dataEnd)
				{
					int b1 = bytes[pos++];

					if ((b1 & 0xc0) == 0x00)
					{
						int ip = (b1 ^ 0x00) << 2;
						r = index[ip]; g = index[ip + 1]; b = index[ip + 2]; a = index[ip + 3];
					}
					else if ((b1 & 0xe0) == 0x40)
					{
						run = b1 & 0x1f;
					}
					else if ((b1 & 0xe0) == 0x60)
					{
						int b2 = bytes[pos++];
						run = (((b1 & 0x1f) << 8) | b2) + 32;
					}
					else if ((b1 & 0xc0) == 0x80)
					{
						r += (byte)(((b1 & 48) << 26 >> 30) & 0xff);
						g += (byte)(((b1 & 12) << 28 >> 22 >> 8) & 0xff);
						b += (byte)(((b1 & 3) << 30 >> 14 >> 16) & 0xff);
					}
					else if ((b1 & 0xe0) == 0xc0)
					{
						int b2 = bytes[pos++];
						int merged = b1 << 8 | b2;
						r += (byte)(((merged & 7936) << 19 >> 27) & 0xff);
						g += (byte)(((merged & 240) << 24 >> 20 >> 8) & 0xff);
						b += (byte)(((merged & 15) << 28 >> 12 >> 16) & 0xff);
					}
					else if ((b1 & 0xf0) == 0xe0)
					{
						int b2 = bytes[pos++];
						int b3 = bytes[pos++];
						int merged = b1 << 16 | b2 << 8 | b3;
						r += (byte)(((merged & 1015808) << 12 >> 27) & 0xff);
						g += (byte)(((merged & 31744) << 17 >> 19 >> 8) & 0xff);
						b += (byte)(((merged & 992) << 22 >> 11 >> 16) & 0xff);
						a += (byte)(((merged & 31) << 27 >> 3 >> 24) & 0xff);
					}
					else if ((b1 & 0xf0) == 0xf0)
					{
						if ((b1 & 8) != 0) r = bytes[pos++];
						if ((b1 & 4) != 0) g = bytes[pos++];
						if ((b1 & 2) != 0) b = bytes[pos++];
						if ((b1 & 1) != 0) a = bytes[pos++];
					}

					int ip2 = ((r ^ g ^ b ^ a) & 63) << 2;
					index[ip2] = r; index[ip2 + 1] = g; index[ip2 + 2] = b; index[ip2 + 3] = a;
				}

				output[o] = r;
				output[o + 1] = g;
				output[o + 2] = b;
				output[o + 3] = a;
			}

			return (width, height, output);
		}
	}
}
