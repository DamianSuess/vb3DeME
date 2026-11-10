namespace DeME.Core;

public sealed record NeResource
{
  public NeResource(ushort type, string typeName, ushort id, string? name, int offset, int length, ushort flags)
  {
    Type = type;
    TypeName = typeName;
    Id = id;
    Name = name;
    Offset = offset;
    Length = length;
    Flags = flags;
  }

  public ushort Type { get; }

  public string TypeName { get; }

  public ushort Id { get; }

  public string? Name { get; }

  public int Offset { get; }

  public int Length { get; }

  public ushort Flags { get; }
}
