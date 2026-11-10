using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DeME.Core;

namespace DeME.Tests;

internal static class Fixture
{
  internal static byte[] Bytecode(params (string Keyword, ushort[] Args)[] instructions)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    using Stream opcodesStream = typeof(NeExecutable).Assembly.GetManifestResourceStream("DeME.Core.Tables.Opcodes.json")!;
    using Stream flagsStream = typeof(NeExecutable).Assembly.GetManifestResourceStream("DeME.Core.Tables.FlagTokens.json")!;
    OpcodeInfo[] opcodes = JsonSerializer.Deserialize<OpcodeInfo[]>(opcodesStream)!;
    ushort[] flags = JsonSerializer.Deserialize<ushort[]>(flagsStream)!;
    foreach (var instruction in instructions)
    {
      OpcodeInfo opcode = opcodes.First(item => (item.Keyword == instruction.Keyword) && (item.NumParams == instruction.Args.Length));
      int index = Array.FindIndex(flags, value => (value & 0x1ff) == opcode.TokenID);
      if (index < 0)
        throw new InvalidOperationException("Missing token mapping for " + instruction.Keyword);

      writer.Write((ushort)(index * 3));
      foreach (ushort arg in instruction.Args)
        writer.Write(arg);
    }

    return stream.ToArray();
  }

  internal static byte[] Record(byte tag, byte id, byte type, params byte[] properties)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    if (tag != 0)
      writer.Write(tag);

    writer.Write(7 + properties.Length);
    writer.Write(id);
    writer.Write((byte)0);
    writer.Write(type);
    writer.Write(properties);
    return stream.ToArray();
  }

  internal static byte[] StringLiteral(string value)
  {
    using Stream flagsStream = typeof(NeExecutable).Assembly.GetManifestResourceStream("DeME.Core.Tables.FlagTokens.json")!;
    ushort[] flags = JsonSerializer.Deserialize<ushort[]>(flagsStream)!;
    byte[] text = VbText.Encode(value);
    int padding = text.Length & 1;
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)(Array.FindIndex(flags, flag => (flag & 0x1ff) == 237) * 3));
    writer.Write((ushort)(4 + text.Length + padding));
    writer.Write((ushort)0);
    writer.Write((ushort)text.Length);
    writer.Write(text);
    if (padding != 0)
      writer.Write((byte)0);

    return stream.ToArray();
  }

  internal static byte[] Form(params byte[][] controls)
  {
    var bytes = new List<byte> { 0xff, 0xcc, 0x2c, 0, 0, 0, 0, 0, 0 };
    bytes.AddRange(Record(0, 0, 13, 0xff, 0xff, 0));
    foreach (byte[] control in controls)
      bytes.AddRange(control);

    bytes.Add(4);
    return bytes.ToArray();
  }

  internal static byte[] Executable(string filename = "MAIN.FRM", byte[]? bytecode = null)
  {
    bytecode ??= Bytecode(("c%", new ushort[] { 42 }), ("var=", new ushort[] { 0x40 }), ("nl", Array.Empty<ushort>()), ("eos", Array.Empty<ushort>()));
    var data = new byte[4096];
    Word(data, 0, 0x5a4d);
    DWord(data, 0x3c, 128);
    Word(data, 128, 0x454e);
    Word(data, 128 + 0x16, 1);
    Word(data, 128 + 0x1c, 4);
    Word(data, 128 + 0x1e, 1);
    Word(data, 128 + 0x22, 64);
    Word(data, 128 + 0x24, 128);
    Word(data, 128 + 0x26, 256);
    Word(data, 128 + 0x28, 272);
    Word(data, 128 + 0x2a, 288);
    Word(data, 128 + 0x32, 4);
    Segment(data, 192, 512, 4, 0);
    Segment(data, 200, 0, 0, 1);
    Segment(data, 208, 1024, 512, 0x100);
    Segment(data, 216, 2048, bytecode.Length, 0);
    Word(data, 256, 4);
    Word(data, 258, 0x800a);
    Word(data, 260, 4);
    int[] resourceIds = { 1, 2, 4, 5 };
    for (int i = 0; i < 4; i++)
    {
      int position = 266 + (i * 12);
      Word(data, position, (ushort)((3072 + (i * 256)) / 16));
      Word(data, position + 2, 16);
      Word(data, position + 6, (ushort)(0x8000 | resourceIds[i]));
    }

    Word(data, 400, 0);
    Pascal(data, 416, "VBRUN300");
    Word(data, 3072 + 4, 0);
    Encoding.ASCII.GetBytes("Fixture").CopyTo(data, 3072 + 6);
    int formRecord = 3072 + 19;
    data[formRecord] = 0x46;
    data[formRecord + 1] = (byte)filename.Length;
    Word(data, formRecord + 2, 0x8004);
    Word(data, formRecord + 4, 0x48);
    Word(data, formRecord + 9, 0x40);
    Encoding.ASCII.GetBytes(filename).CopyTo(data, formRecord + 11);
    Word(data, 3328 + 0x56, 0x80);
    Word(data, 3328 + 0x5a, 0x40);
    Word(data, 3328 + 0x6a, 0x40);
    Word(data, 3328 + 0x6c, 0x70);
    Word(data, 3328 + 0x6e + 0x40, 0xfff8);
    Word(data, 3328 + 0x6e + 0x50, 0xfff6);
    Word(data, 1024 + 64 + 26, 1);
    Word(data, 1024 + 64 + 30, 0xffff);
    Word(data, 1024 + 128 + 4, 0x123);
    Word(data, 1024 + 128 + 20, 0x40);
    Word(data, 1024 + 128 + 22, 1);
    Word(data, 1024 + 128 + 26, 0xffff);
    Word(data, 1024 + 128 + 36, (ushort)bytecode.Length);
    Word(data, 1024 + 128 + 38, 0xffff);
    Word(data, 1536, 1);
    data[1538] = 2;
    Word(data, 1540, 128 + 38);
    Word(data, 1542, 4);
    bytecode.CopyTo(data, 2048);
    byte[] button = Record(1, 1, 4, 0xff, 0, 2, (byte)'O', (byte)'K', 0xff, 1, 0x81, 0);
    Form(button, new byte[] { 2 }).CopyTo(data, 3584);
    Pascal(data, 3840, "Main");
    Pascal(data, 3845, "Command1");
    return data;
  }

  internal static void Word(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

  internal static void DWord(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

  private static void Segment(byte[] data, int offset, int fileOffset, int length, ushort flags)
  {
    Word(data, offset, (ushort)(fileOffset / 16));
    Word(data, offset + 2, (ushort)length);
    Word(data, offset + 4, flags);
  }

  private static void Pascal(byte[] data, int offset, string value)
  {
    data[offset] = (byte)value.Length;
    Encoding.ASCII.GetBytes(value).CopyTo(data, offset + 1);
  }
}
