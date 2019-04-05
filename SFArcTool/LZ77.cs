using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SFArcTool {
	/// <summary>
	/// A helper class that provides methods for LZ77-compressed data.
	/// </summary>
	public static class LZ77 {
		/// <summary>
		/// Decompresses LZ77-compressed data from the given input stream to the given output stream.
		/// </summary>
		/// <param name="input">The input stream to read from.</param>
		/// <param name="output">The output stream to write to.</param>
		/// <returns>true if the data in the given input stream was decompressed successfully; otherwise, false.</returns>
		public static bool Decompress(Stream input, Stream output) {
			if (input == null)
				throw new ArgumentNullException(nameof(input), "The input stream cannot be null.");
			if (!input.CanRead)
				throw new ArgumentException("The input stream does not support reading.", nameof(input));

			// Check LZ77 type.
			int lzType = input.ReadByte();
			if (lzType != 0x10 && lzType != 0x11) {
				return false;
			}

			// Read the decompressed size.
			int size = input.ReadByte() | (input.ReadByte() << 8) | (input.ReadByte() << 16);
			if (size < 0) {
				return false;
			}

			// Create decompression stream.
			using (MemoryStream temp = new MemoryStream(size)) {
				// Begin decompression.
				while (temp.Length < size) {
					// Load flags for the next 8 blocks.
					int flagByte = input.ReadByte();
					if (flagByte < 0) {
						return false;
					}

					// Process the next 8 blocks unless all data has been decompressed.
					for (int i = 0; i < 8 && temp.Length < size; i++) {
						// Check if the block is compressed.
						if ((flagByte & (0x80 >> i)) == 0) {
							// Uncompressed block; copy single byte.
							int b = input.ReadByte();
							if (b < 0) {
								return false;
							}

							temp.WriteByte((byte)b);
						} else {
							// Compressed block; read block.
							int block = (input.ReadByte() << 8) | input.ReadByte();
							if (block < 0) {
								return false;
							}
							int blockType = (block >> 12);

							int count, disp;
							if (lzType == 0x11 && blockType == 1) {
								// Read second part of block.
								int block2 = (input.ReadByte() << 8) | input.ReadByte();
								if (block2 < 0) {
									return false;
								}
								// Get byte count. [273..65808]
								count = (((block & 0xFFF) << 4) | (block2 >> 12)) + 273;
								// Get displacement. [1..4096]
								disp = (block2 & 0xFFF) + 1;
							} else if (lzType == 0x11 && blockType == 0) {
								// Read second part of block.
								int block2 = input.ReadByte();
								if (block2 < 0) {
									return false;
								}
								// Get byte count. [17..272]
								count = (block >> 4) + 17;
								// Get displacement. [1..4096]
								disp = (((block & 0xF) << 8) | block2) + 1;
							} else if (lzType == 0x11) {
								// Get byte count. [1..16]
								count = blockType + 1;
								// Get displacement. [1..4096]
								disp = (block & 0xFFF) + 1;
							} else {
								// Get byte count. [3..18]
								count = blockType + 3;
								// Get displacement. [1..4096]
								disp = (block & 0xFFF) + 1;
							}

							// Check for invalid displacement.
							if (disp > temp.Position) {
								return false;
							}

							// Save current position and copying position.
							long outPos = temp.Position;
							long copyPos = temp.Position - disp;

							// Copy all bytes.
							for (int j = 0; j < count; j++) {
								// Read byte to be copied.
								temp.Position = copyPos++;
								int b = temp.ReadByte();

								if (b < 0) {
									return false;
								}

								// Write byte to be copied.
								temp.Position = outPos++;
								temp.WriteByte((byte)b);
							}
						}
					}
				}

				// Write the decompressed data to the output stream.
				temp.WriteTo(output);
				return true;
			}
		}
	}
}
