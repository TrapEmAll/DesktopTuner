using System.Buffers.Binary;

namespace DesktopTuner;

public static class ShellClipboardPolicy
{
    private const uint DropEffectMove = 0x00000002;
    private const int MaximumShellClipboardItems = 4096;

    public static bool IsCutDropEffect(uint effect) => effect == DropEffectMove;

    public static bool TryReadShellIdListArray(ReadOnlySpan<byte> data, out int parentOffset, out int[] itemOffsets)
    {
        parentOffset = 0;
        itemOffsets = [];
        if (data.Length < sizeof(uint)) return false;

        var itemCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (itemCount == 0 || itemCount > MaximumShellClipboardItems) return false;
        var headerSize = sizeof(uint) + checked(((int)itemCount + 1) * sizeof(uint));
        if (data.Length < headerSize) return false;

        var offsets = new int[checked((int)itemCount + 1)];
        for (var index = 0; index < offsets.Length; index++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(sizeof(uint) + index * sizeof(uint), sizeof(uint)));
            if (offset < headerSize || offset > int.MaxValue || offset >= data.Length) return false;
            offsets[index] = (int)offset;
        }

        var sortedOffsets = offsets.Order().ToArray();
        if (sortedOffsets.Distinct().Count() != sortedOffsets.Length) return false;
        for (var index = 0; index < sortedOffsets.Length; index++)
        {
            var end = index + 1 < sortedOffsets.Length ? sortedOffsets[index + 1] : data.Length;
            if (!IsValidPidl(data, sortedOffsets[index], end)) return false;
        }

        parentOffset = offsets[0];
        itemOffsets = offsets[1..];
        return true;
    }

    private static bool IsValidPidl(ReadOnlySpan<byte> data, int offset, int end)
    {
        while (offset <= end - sizeof(ushort))
        {
            var itemSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));
            if (itemSize == 0) return true;
            if (itemSize < sizeof(ushort) || itemSize > end - offset) return false;
            offset += itemSize;
        }
        return false;
    }
}
