// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public sealed record DecompileOptions
{
  public bool Forms { get; init; } = true;

  public bool Code { get; init; } = true;

  public bool Assets { get; init; } = true;

  public bool RawResources { get; init; }

  public bool Utf8 { get; init; }

  public bool CopyDependencies { get; init; }
}
