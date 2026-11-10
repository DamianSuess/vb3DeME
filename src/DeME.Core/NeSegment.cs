namespace DeME.Core;

public sealed record NeSegment
{
  public NeSegment(int index, int offset, int length, ushort flags, int minimumAllocation)
  {
    Index = index;
    Offset = offset;
    Length = length;
    Flags = flags;
    MinimumAllocation = minimumAllocation;
  }

  public int Index { get; }

  public int Offset { get; }

  public int Length { get; }

  public ushort Flags { get; }

  public int MinimumAllocation { get; }

  public bool IsData => (Flags & 1) != 0;

  public bool HasRelocations => (Flags & 0x100) != 0;
}
