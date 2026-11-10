namespace DeME.Core;

public sealed record VbProject
{
  public VbProject(string title, short startupIndex, bool namesStripped, IReadOnlyList<FormReference> forms, IReadOnlyList<string> customControls)
  {
    Title = title;
    StartupIndex = startupIndex;
    NamesStripped = namesStripped;
    Forms = forms;
    CustomControls = customControls;
  }

  public string Title { get; }

  public short StartupIndex { get; }

  public bool NamesStripped { get; }

  public IReadOnlyList<FormReference> Forms { get; }

  public IReadOnlyList<string> CustomControls { get; }

  public static VbProject Read(NeExecutable executable)
  {
    if (!executable.IsVisualBasic3)
      throw new InvalidDataException("The executable does not import VBRUN300; it is not a supported VB3 application.");

    NeResource resource = executable.FindResource(10, 1) ?? throw new InvalidDataException("VB3 project directory (RT_RCDATA 1) is missing.");
    var data = new BinaryData(executable.ReadResource(resource));
    data.Span(0, 19);
    short startup = (short)data.Word(4);
    string title = VbText.Decode(data.Span(6, 9)).TrimEnd('\0');
    int titleLength = data.Word(15);
    int helpLength = data.Word(17);
    int position = 19;
    if (titleLength > 0)
      title = VbText.Decode(data.Span(position, titleLength)).TrimEnd('\0');

    position += titleLength;
    data.Span(position, helpLength);
    position += helpLength;
    bool stripped = false;
    var forms = new List<FormReference>();
    var controls = new List<string>();
    while ((position < data.Length) && (data.Byte(position) != 0))
    {
      data.Span(position, 11);
      byte type = data.Byte(position);
      int nameLength = data.Byte(position + 1);
      ushort id = (ushort)(data.Word(position + 2) & 0x7fff);
      ushort tag = data.Word(position + 4);
      ushort module = data.Word(position + 9);
      position += 11;
      if (nameLength == 0xff)
        nameLength = data.Byte(position++);

      string name = VbText.Decode(data.Span(position, nameLength)).TrimEnd('\0');
      position += nameLength;
      if (type is 0x46 or 0x58)
      {
        short count = (short)data.Word(position);
        position += 2;
        if (count < 0)
          throw new InvalidDataException("Negative VB3 project sub-item count.");

        data.Span(position, count * 4);
        position += count * 4;
      }

      if ((type == 0x43) && (name.Length > 0))
        controls.Add(name);

      if (type != 0x46)
        continue;

      int index = forms.Count + 1;
      stripped |= name.Length == 0;
      string filename = name.Length == 0 ? $"FRM{index}.FRM" : name;
      string formName = name.Length == 0 ? $"frm{index}" : Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
      var names = new Dictionary<int, string>();
      NeResource? nameTable = executable.FindResource(10, (ushort)(id + 1));
      if (nameTable is { Offset: > 0, Length: > 0 })
      {
        var blob = new BinaryData(executable.ReadResource(nameTable));
        int cursor = 0;
        int nameIndex = 0;
        while ((cursor < blob.Length) && (blob.Byte(cursor) != 0))
        {
          names[nameIndex++] = blob.Pascal(cursor);
          cursor += 1 + blob.Byte(cursor);
        }

        if (names.TryGetValue(0, out string? recovered))
          formName = recovered;
      }
      else if (nameTable is { Offset: 0 })
      {
        stripped = true;
      }

      forms.Add(new FormReference(index, id, filename, formName, tag, module, names));
    }

    return new VbProject(title, startup, stripped, forms.AsReadOnly(), controls.AsReadOnly());
  }
}
