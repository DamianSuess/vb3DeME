// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public sealed class ControlNode
{
  public int Id { get; internal set; }

  public string Name { get; internal set; } = string.Empty;

  public string TypeName { get; internal set; } = string.Empty;

  public int? ArrayIndex { get; internal set; }

  public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

  public List<ControlNode> Children { get; } = new();

  internal ControlModel? Model { get; set; }

  internal Dictionary<ushort, ControlEvent> Events { get; } = new();
}
