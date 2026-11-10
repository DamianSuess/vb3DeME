namespace DeME.Core;

internal sealed record ControlProperty
{
  public ControlProperty(string name, uint flags, int standard)
  {
    Name = name;
    Flags = flags;
    Standard = standard;
  }

  public string Name { get; }

  public uint Flags { get; }

  public int Standard { get; }

  public int DataType => (int)(Flags & 0x7f);
}
