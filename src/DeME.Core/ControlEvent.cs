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
