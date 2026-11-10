namespace DeME.Core;

public sealed record FormReference
{
  public FormReference(int index, ushort resourceId, string fileName, string name, ushort instanceTag, ushort moduleId, IReadOnlyDictionary<int, string> controlNames)
  {
    Index = index;
    ResourceId = resourceId;
    FileName = fileName;
    Name = name;
    InstanceTag = instanceTag;
    ModuleId = moduleId;
    ControlNames = controlNames;
  }

  public int Index { get; }

  public ushort ResourceId { get; }

  public string FileName { get; }

  public string Name { get; }

  public ushort InstanceTag { get; }

  public ushort ModuleId { get; }

  public IReadOnlyDictionary<int, string> ControlNames { get; }
}
