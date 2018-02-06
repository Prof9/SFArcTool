using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SFArcTool {
	class Program {
		struct SubFile {
			public uint Offset { get; set; }
			public int Size { get; set; }
			public bool Compressed { get; set; }

			public byte[] Data { get; set; }
		}

		static int Main(string[] args) {
			Console.WriteLine("Star Force Archive Tool v1.0 by Prof. 9");
#if !DEBUG
			try {
#endif
				IEnumerator<string> argEnum = ((IEnumerable<string>)args).GetEnumerator();

				string inPath = null;
				string outPath = null;
				string mode = null;

				// Read flags.
				while (argEnum.MoveNext()) {
					switch (argEnum.Current) {
					case "-i":
						AdvanceArgs(argEnum, 1);
						if (inPath is null) {
							inPath = argEnum.Current;
						} else {
							throw new Exception("-i already set.");
						}
						break;
					case "-o":
						AdvanceArgs(argEnum, 1);
						if (outPath is null) {
							outPath = argEnum.Current;
						} else {
							throw new Exception("-o already set.");
						}
						break;
					case "-x":
					case "-p":
						if (mode is null) {
							mode = argEnum.Current;
						} else {
							throw new Exception("Mode already set (" + mode + ").");
						}
						break;
					default:
						throw new Exception("Unknown option " + argEnum.Current + ".");
					}
				}

				// Process command.
				switch (mode) {
				case "-x":
					if (inPath is null)
						throw new Exception(mode + " needs an input path.");
					if (outPath is null)
						throw new Exception(mode + " needs an output path.");

					Extract(inPath, outPath);
					break;
				case "-p":
					if (inPath is null)
						throw new Exception(mode + " needs an input path.");
					if (outPath is null)
						throw new Exception(mode + " needs an output path.");

					Pack(inPath, outPath);
					break;
				default:
					PrintUsage();
					break;
				}
#if !DEBUG
			} catch (Exception ex) {
				Console.WriteLine("ERROR: " + ex.Message);
				return 1;
			}
#endif

			return 0;
		}

		static void PrintUsage() {
			Console.WriteLine("");
			Console.WriteLine("Usage:  SFArcTool.exe <options>");
			Console.WriteLine("Options:");
			Console.WriteLine("        -i [path]       Specifies input path.");
			Console.WriteLine("        -o [path]       Specifies output path.");
			Console.WriteLine("        -x              Unpacks archive to folder. Requires -i and -o.");
			Console.WriteLine("        -p              Packs folder to archive. Requires -i and -o.");
			Console.WriteLine();
			Console.WriteLine("For option -p, subfiles in the input directory must be named as \"XXX.ext\" or \"name_XXX.ext\", where \"name\" is an arbitrary string not containing '.' or '_', \"XXX\" is the subfile number and \"ext\" is any extension (multiple extensions are allowed. Any files that do not adhere to this format will be skipped.");
		}

		static void AdvanceArgs(IEnumerator<string> argEnum, int count) {
			while (count-- > 0) {
				if (!argEnum.MoveNext()) {
					throw new InvalidDataException("Unexpected end of arguments.");
				}
			}
		}

		static void Extract(string inFile, string outPath) {
			List<SubFile> entries = new List<SubFile>();

			using (FileStream arcFile = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				BinaryReader br = new BinaryReader(arcFile);
				long headerEnd = arcFile.Length;

				// Load header.
				while (arcFile.Position < headerEnd) {
					uint offset = br.ReadUInt32();
					uint size = br.ReadUInt32();

					SubFile entry = new SubFile() {
						Offset = offset,
						Size = (int)(size & 0x7FFFFFFF),
						Compressed = (size & 0x80000000) != 0
					};
					entries.Add(entry);

					headerEnd = Math.Min(entry.Offset, headerEnd);
				}
				if (arcFile.Position != headerEnd) {
					throw new InvalidDataException("Invalid archive file header.");
				}

				// Create directory to hold files.
				if (!outPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) {
					outPath += Path.DirectorySeparatorChar;
				}
				Directory.CreateDirectory(outPath);

				int digits = (entries.Count - 1).ToString().Length;

				// Extract the files.
				for (int i = 0; i < entries.Count; i++) {
					SubFile entry = entries[i];
					long start = entry.Offset;

					// Skip last size 0xFFFF entry.
					if (i == entries.Count - 1 && start == arcFile.Length && entry.Size == 0xFFFF && !entry.Compressed) {
						entries.RemoveAt(i);
						continue;
					}

					string outFilePath = outPath + Path.GetFileNameWithoutExtension(inFile) + "_" + i.ToString().PadLeft(digits, '0') + ".bin";

					// Get size of the file.
					long size;
					if (entry.Compressed) {
						// Get compressed size by decompressing, and check if decompressed size matches size in file entry.
						arcFile.Position = start;
						using (MemoryStream uncompressed = new MemoryStream(entry.Size)) {
							if (!LZ77.Decompress(arcFile, uncompressed) || uncompressed.Length != entry.Size) {
								throw new InvalidDataException("Could not read subfile " + i + ": invalid LZ77 compressed data.");
							}
						}
						size = arcFile.Position - start;
					} else {
						size = entry.Size;
					}
					if (size < 0 || size > int.MaxValue) {
						throw new InvalidDataException("Could not read subfile " + i + ": invalid size.");
					}

					// Read the file.
					arcFile.Position = start;
					entry.Data = br.ReadBytes((int)size);

					// Write the file.
					using (FileStream subFile = new FileStream(outFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
						subFile.Write(entry.Data, 0, entry.Data.Length);
					}
				}
			}

			Console.WriteLine("Extracted " + entries.Count + " subfiles from archive " + Path.GetFileName(inFile) + ".");
		}

		static void Pack(string inPath, string outFile) {
			List<SubFile?> entries = new List<SubFile?>();

			// Parse every file.
			foreach (string file in Directory.GetFiles(inPath)) {
				// Parse filename.
				string fileName = Path.GetFileName(file);

				int dotPos = fileName.IndexOf('.');
				if (dotPos < 0) {
					dotPos = fileName.Length;
				}
				int uscPos = fileName.LastIndexOf('_', dotPos);

				string numString = fileName.Substring(uscPos + 1, dotPos - uscPos - 1);
				if (!Int32.TryParse(numString, out int fileNum)) {
					// No number string found; skip this file.
					continue;
				}

				// Expand file entries if necessary.
				while (entries.Count <= fileNum) {
					entries.Add(null);
				}

				// Read the file.
				byte[] buffer;
				long size;
				bool compressed;
				using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					// Check if the file is compressed.
					using (MemoryStream ms = new MemoryStream()) {
						fs.Position = 0;
						ms.Position = 0;
						if (LZ77.Decompress(fs, ms)) {
							compressed = true;
							size = ms.Position;
						} else {
							compressed = false;
							size = fs.Length;
						}
					}

					if (size > int.MaxValue) {
						if (compressed) {
							throw new IOException("Uncompressed size of file " + fileName + " exceeds " + int.MaxValue + " bytes.");
						} else {
							throw new IOException("Size of file " + fileName + " exceeds " + int.MaxValue + " bytes.");
						}
					}

					buffer = new byte[fs.Length];
					fs.Position = 0;
					if (fs.Read(buffer, 0, buffer.Length) != fs.Length) {
						throw new IOException("Could not read entirety of input file " + fileName + ".");
					}
				}

				SubFile entry = new SubFile() {
					Size = (int)size,
					Compressed = compressed,
					Data = buffer
				};
				entries[fileNum] = entry;
			}

			// Create directory for the output file.
			string dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
			if (dir.Length > 0) {
				Directory.CreateDirectory(dir);
			}

			// Create the output file.
			using (FileStream fs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.Write)) {
				BinaryWriter bw = new BinaryWriter(fs);

				// Write the header. (Account for terminator entry.)
				long filePos = (entries.Count + 1) * 8;
				for (int i = 0; i < entries.Count; i++) {
					if (entries[i] == null) {
						// Write empty entry.
						bw.Write((uint)filePos);
						bw.Write((uint)0);
						continue;
					}

					// Update entry with offset.
					SubFile entry = (SubFile)entries[i];
					entry.Offset = (uint)filePos;
					entries[i] = entry;

					// Write entry.
					bw.Write((uint)entry.Offset);
					bw.Write((uint)((uint)entry.Size | (entry.Compressed ? 0x80000000 : 0)));

					// If file was compressed, round up to multiple of 4.
					long size = entry.Data.Length;
					if (entry.Compressed && size % 4 != 0) {
						size += 4 - (size % 4);
					}

					filePos += entry.Data.LongLength;
					// Round next file offset up to multiple of 4. (Optional?)
					if (true && filePos % 4 != 0) {
						filePos += 4 - (filePos % 4);
					}

					if (filePos > uint.MaxValue) {
						throw new IOException("Maximum file size for archive exceeded.");
					}
				}
				// Write terminator entry.
				bw.Write((uint)filePos);
				bw.Write((uint)0xFFFF);

				// Write the files.
				for (int i = 0; i < entries.Count; i++) {
					if (entries[i] is null) {
						continue;
					}
					SubFile entry = (SubFile)entries[i];

					// Advance to actual offset of file.
					uint offset = entry.Offset;
					while (fs.Position < offset) {
						fs.WriteByte(0);
					}

					// Write the file data.
					fs.Write(entry.Data, 0, entry.Data.Length);
				}
				// Advance to end of file.
				while (fs.Position < filePos) {
					fs.WriteByte(0);
				}
			}

			Console.WriteLine("Created archive " + Path.GetFileName(outFile) + " with " + entries.Count + " subfiles.");
		}
	}
}
