using System.Buffers.Binary;
using System.Text;

namespace DeME.Core;

internal sealed class BinaryData
{
  private readonly byte[] _data;

  public BinaryData(byte[] data)
  {
    _data = data;
  }

  public int Length => _data.Length;

  public byte Byte(int offset) => Span(offset, 1)[0];

  public ushort Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Span(offset, 2));

  public uint DWord(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Span(offset, 4));

  public byte[] Bytes(int offset, int count) => Span(offset, count).ToArray();

  public ReadOnlySpan<byte> Span(int offset, int count)
  {
    if ((offset < 0) || (count < 0) || (offset > _data.Length - count))
      throw new InvalidDataException($"Truncated data at 0x{offset:X}: requested {count} bytes (file size {_data.Length}).");

    return _data.AsSpan(offset, count);
  }

  public string Pascal(int offset) => VbText.Decode(Span(offset + 1, Byte(offset))).TrimEnd('\0');

  public int Scaled(ushort value, int shift)
  {
    if ((shift < 0) || (shift > 16))
      throw new InvalidDataException($"Unsupported alignment shift {shift}.");

    long result = (long)value << shift;
    if (result > int.MaxValue)
      throw new InvalidDataException("Aligned file offset exceeds supported file size.");

    return (int)result;
  }
}
