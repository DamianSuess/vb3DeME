// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public sealed class VbModule
{
  public ushort Id { get; internal set; }

  public string Name { get; internal set; } = string.Empty;

  public FormReference? Form { get; internal set; }

  public List<VbProcedure> Procedures { get; } = new();

  public List<string> Declarations { get; } = new();

  internal BinaryData Data { get; set; } = new(Array.Empty<byte>());

  internal Dictionary<ushort, string> References { get; } = new();

  internal Dictionary<string, ControlModel> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
}
