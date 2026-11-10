// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public sealed record PcodeInstruction
{
  public PcodeInstruction(int offset, ushort rawToken, ushort alternateToken, OpcodeInfo opcode, ushort[] operands, byte[]? stringData)
  {
    Offset = offset;
    RawToken = rawToken;
    AlternateToken = alternateToken;
    Opcode = opcode;
    Operands = operands;
    StringData = stringData;
  }

  public int Offset { get; }

  public ushort RawToken { get; }

  public ushort AlternateToken { get; }

  public OpcodeInfo Opcode { get; }

  public ushort[] Operands { get; }

  public byte[]? StringData { get; }
}
