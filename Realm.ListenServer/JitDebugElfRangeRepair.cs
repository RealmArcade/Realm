using System;
using System.Buffers.Binary;
using System.Text;

namespace Realm.ListenServer;

/// <summary>
///     Repairs the DWARF range gaps a JIT ELF leaves around map-script functions, so a debugger can resolve the line-table
///     addresses that fall between the function's first range entry and its next recorded range.
/// </summary>
public static class JitDebugElfRangeRepair
{
	private const int ElfHeaderMinimumSize = 64;
	private const int SectionHeaderSize = 64;
	private const int SymbolEntrySize = 24;

	/// <summary>
	///     Extends map-script function range entries to the next range start.
	/// </summary>
	/// <param name="elfBytes">The JIT ELF bytes, modified in place when a gap is found.</param>
	/// <returns>The number of range entries that were repaired.</returns>
	public static int Repair(Span<byte> elfBytes)
	{
		if (elfBytes.Length < ElfHeaderMinimumSize) return 0;
		if (elfBytes[0] != 0x7F || elfBytes[1] != (byte)'E' || elfBytes[2] != (byte)'L' || elfBytes[3] != (byte)'F') return 0;
		if (elfBytes[4] != 2 || elfBytes[5] != 1) return 0;

		ulong sectionTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(0x28));
		int sectionEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(elfBytes.Slice(0x3A));
		int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(elfBytes.Slice(0x3C));
		int sectionNamesIndex = BinaryPrimitives.ReadUInt16LittleEndian(elfBytes.Slice(0x3E));
		if (sectionEntrySize < SectionHeaderSize || sectionCount <= 0 || sectionNamesIndex >= sectionCount) return 0;

		var sections = new ElfSectionHeader[sectionCount];
		for (int index = 0; index < sectionCount; index++)
		{
			ulong headerOffset = sectionTableOffset + (ulong)(index * sectionEntrySize);
			if (headerOffset + SectionHeaderSize > (ulong)elfBytes.Length) return 0;

			int offset = (int)headerOffset;
			sections[index] = new ElfSectionHeader
			{
				NameOffset = BinaryPrimitives.ReadUInt32LittleEndian(elfBytes.Slice(offset)),
				Address = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset + 16)),
				Offset = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset + 24)),
				Size = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset + 32)),
				EntrySize = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset + 56))
			};
		}

		ReadOnlySpan<byte> sectionNames = Slice(elfBytes, sections[sectionNamesIndex].Offset, sections[sectionNamesIndex].Size);
		if (sectionNames.IsEmpty) return 0;

		int textSectionIndex = -1;
		ulong textAddress = 0;
		ElfSectionHeader rangesSection = default;
		ElfSectionHeader symbolsSection = default;
		ElfSectionHeader stringsSection = default;

		for (int index = 0; index < sections.Length; index++)
		{
			string name = ReadName(sectionNames, sections[index].NameOffset);
			switch (name)
			{
				case ".text":
					textSectionIndex = index;
					textAddress = sections[index].Address;
					break;
				case ".debug_ranges":
					rangesSection = sections[index];
					break;
				case ".symtab":
					symbolsSection = sections[index];
					break;
				case ".strtab":
					stringsSection = sections[index];
					break;
			}
		}

		if (textSectionIndex < 0 || rangesSection.Size == 0 || symbolsSection.Size == 0 || stringsSection.Size == 0) return 0;

		ReadOnlySpan<byte> symbolData = Slice(elfBytes, symbolsSection.Offset, symbolsSection.Size);
		ReadOnlySpan<byte> stringData = Slice(elfBytes, stringsSection.Offset, stringsSection.Size);
		if (symbolData.IsEmpty || stringData.IsEmpty) return 0;

		int rangesStart = (int)rangesSection.Offset;
		int rangesEnd = checked((int)(rangesSection.Offset + rangesSection.Size));
		if (rangesStart < 0 || rangesEnd > elfBytes.Length) return 0;

		int symbolEntrySize = symbolsSection.EntrySize > 0 ? (int)symbolsSection.EntrySize : SymbolEntrySize;
		int repairedCount = 0;

		for (int symbolOffset = 0; symbolOffset + symbolEntrySize <= symbolData.Length; symbolOffset += symbolEntrySize)
		{
			uint nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(symbolData.Slice(symbolOffset));
			ushort sectionIndex = BinaryPrimitives.ReadUInt16LittleEndian(symbolData.Slice(symbolOffset + 6));
			ulong symbolValue = BinaryPrimitives.ReadUInt64LittleEndian(symbolData.Slice(symbolOffset + 8));
			ulong symbolSize = BinaryPrimitives.ReadUInt64LittleEndian(symbolData.Slice(symbolOffset + 16));
			if (sectionIndex != textSectionIndex || symbolSize == 0) continue;

			string name = ReadName(stringData, nameOffset);
			if (!name.Contains("Realm_Maps_", StringComparison.Ordinal) || name.Contains("Attribution", StringComparison.Ordinal)) continue;

			ulong functionLowAddress = textAddress + symbolValue;
			ulong functionHighAddress = functionLowAddress + symbolSize;
			int rangeOffset = FindRangeStart(elfBytes, rangesStart, rangesEnd, functionLowAddress, functionHighAddress);
			if (rangeOffset < 0) continue;

			ulong rangeEnd = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(rangeOffset + 8));
			ulong nextRangeStart = FindNextRangeStart(elfBytes, rangesEnd, rangeOffset, rangeEnd, functionHighAddress);
			if (nextRangeStart <= rangeEnd) continue;

			BinaryPrimitives.WriteUInt64LittleEndian(elfBytes.Slice(rangeOffset + 8), nextRangeStart);
			repairedCount++;
		}

		return repairedCount;
	}

	private static int FindRangeStart(Span<byte> elfBytes, int rangesStart, int rangesEnd, ulong functionLowAddress, ulong functionHighAddress)
	{
		for (int offset = rangesStart; offset + 16 <= rangesEnd; offset += 16)
		{
			ulong startAddress = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset));
			ulong endAddress = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(offset + 8));
			if (startAddress == functionLowAddress && endAddress < functionHighAddress) return offset;
		}

		return -1;
	}

	private static ulong FindNextRangeStart(Span<byte> elfBytes, int rangesEnd, int rangeOffset, ulong rangeEnd, ulong functionHighAddress)
	{
		if (rangeOffset + 32 > rangesEnd) return functionHighAddress;

		ulong nextStart = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(rangeOffset + 16));
		ulong nextEnd = BinaryPrimitives.ReadUInt64LittleEndian(elfBytes.Slice(rangeOffset + 24));
		if (nextStart == 0 && nextEnd == 0) return functionHighAddress;
		if (nextStart == rangeEnd) return rangeEnd;
		if (rangeEnd < nextStart && nextStart < functionHighAddress) return nextStart;
		return functionHighAddress;
	}

	private static ReadOnlySpan<byte> Slice(Span<byte> data, ulong offset, ulong size)
	{
		if (offset > (ulong)data.Length) return ReadOnlySpan<byte>.Empty;
		int start = (int)offset;
		int length = (int)Math.Min(size, (ulong)(data.Length - start));
		return data.Slice(start, length);
	}

	private static string ReadName(ReadOnlySpan<byte> data, uint offset)
	{
		if (offset >= data.Length) return string.Empty;
		int start = (int)offset;
		int end = start;
		while (end < data.Length && data[end] != 0) end++;
		return Encoding.ASCII.GetString(data.Slice(start, end - start));
	}

	private struct ElfSectionHeader
	{
		public uint NameOffset;
		public ulong Address;
		public ulong Offset;
		public ulong Size;
		public ulong EntrySize;
	}
}
