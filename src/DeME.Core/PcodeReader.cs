// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public static class PcodeReader
{
  public static IReadOnlyList<PcodeInstruction> Read(byte[] bytecode)
  {
    var data = new BinaryData(bytecode);
    var result = new List<PcodeInstruction>();
    int position = 0;
    while (position < data.Length)
    {
      int start = position;
      ushort raw = data.Word(position);
      position += 2;
      int index = raw / 3;
      if ((index >= RuntimeTables.FlagTokens.Length) || ((raw != 0) && (RuntimeTables.FlagTokens[index] == 0)))
        throw new InvalidDataException($"Unknown P-code token 0x{raw:X4} at 0x{start:X4}; decoding stopped to preserve alignment.");

      ushort alternate = RuntimeTables.FlagTokens[index];
      OpcodeInfo opcode = RuntimeTables.Opcodes[alternate & 0x1ff];
      if (opcode.Case == 0)
        throw new InvalidDataException($"Undefined P-code opcode {opcode.TokenID} at 0x{start:X4}.");

      byte[]? text = null;
      ushort[] operands;
      if (opcode.Case == 8)
      {
        int length = data.Word(position);
        if (length < 2)
          throw new InvalidDataException("Invalid P-code string record length.");

        data.Span(position, length + 2);
        int stringLength = length > 2 ? data.Word(position + 4) : 0;
        if (stringLength > length - 4)
        {
          if (stringLength != 0)
            throw new InvalidDataException("P-code string exceeds its record.");
        }

        text = data.Bytes(position + (length > 2 ? 6 : 4), stringLength);
        position += length + 2;
        operands = Array.Empty<ushort>();
      }
      else
      {
        int count = opcode.NumParams;
        if (opcode.Case == 13)
        {
          int length = data.Word(position);
          if ((length & 1) != 0)
            throw new InvalidDataException("Odd length in P-code branch table.");

          position += 2;
          count = length / 2;
        }

        data.Span(position, count * 2);
        operands = new ushort[count];
        for (int i = 0; i < count; i++)
          operands[i] = data.Word(position + (i * 2));

        position += count * 2;
      }

      result.Add(new PcodeInstruction(start, raw, alternate, opcode, operands, text));
      if (opcode.Case == 4)
        break;
    }

    return result.AsReadOnly();
  }
}
