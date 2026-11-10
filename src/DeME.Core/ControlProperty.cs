// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

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
