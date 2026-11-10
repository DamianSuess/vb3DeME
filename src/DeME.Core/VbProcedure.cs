namespace DeME.Core;

public sealed class VbProcedure
{
  public string Name { get; internal set; } = string.Empty;

  public ushort Pointer { get; internal set; }

  public ushort DescriptorOffset { get; internal set; }

  public ushort ModuleId { get; internal set; }

  public ushort TypeFlags { get; internal set; }

  public ushort ScopeFlags { get; internal set; }

  public bool IsLocal { get; internal set; }

  public int CodeSegment { get; internal set; }

  public ushort CodeOffset { get; internal set; }

  public byte[] Bytecode { get; internal set; } = Array.Empty<byte>();

  public string Library { get; internal set; } = string.Empty;

  public string Alias { get; internal set; } = string.Empty;

  public bool IsFunction => ((TypeFlags >> 8) & 7) != 0;

  public string ReturnType => BasicDecompiler.DataType((TypeFlags >> 8) & 7);

  internal List<(string Name, string Type)> EventParameters { get; } = new();

  internal bool IsEvent { get; set; }
}
