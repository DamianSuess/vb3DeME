// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

internal sealed record ControlEvent
{
  public ControlEvent(string name, int parameters, string profile)
  {
    Name = name;
    Parameters = parameters;
    Profile = profile;
  }

  public string Name { get; }

  public int Parameters { get; }

  public string Profile { get; }
}
