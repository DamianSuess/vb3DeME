namespace DeME.Core;

internal sealed record ControlModel
{
  public ControlModel(string name, ControlProperty[] properties, ControlEvent[] events)
  {
    Name = name;
    Properties = properties;
    Events = events;
  }

  public string Name { get; }

  public ControlProperty[] Properties { get; }

  public ControlEvent[] Events { get; }
}
