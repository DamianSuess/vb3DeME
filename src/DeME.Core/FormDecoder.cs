// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

public static class FormDecoder
{
  private static readonly Dictionary<byte, (string Model, string Type)> Builtins = new()
  {
    [0] = ("Picture", "PictureBox"),
    [1] = ("Label", "Label"),
    [2] = ("Text", "TextBox"),
    [3] = ("Frame", "Frame"),
    [4] = ("Command", "CommandButton"),
    [5] = ("Check", "CheckBox"),
    [6] = ("Option", "OptionButton"),
    [7] = ("Combo", "ComboBox"),
    [8] = ("List", "ListBox"),
    [9] = ("HScroll", "HScrollBar"),
    [10] = ("VScroll", "VScrollBar"),
    [11] = ("Timer", "Timer"),
    [13] = ("Form", "Form"),
    [16] = ("Drive", "DriveListBox"),
    [17] = ("Dir", "DirListBox"),
    [18] = ("File", "FileListBox"),
    [19] = ("Menu", "Menu"),
    [20] = ("MDIForm", "MDIForm"),
    [23] = ("Line", "Line"),
    [24] = ("Image", "Image"),
  };

  public static DecodedForm Decode(byte[] stream, FormReference reference) => Decode(stream, reference, new Dictionary<string, ControlModel>());

  internal static DecodedForm Decode(byte[] stream, FormReference reference, IReadOnlyDictionary<string, ControlModel> customModels)
  {
    var data = new BinaryData(stream);
    data.Span(0, 13);
    if ((data.Byte(0) != 0xff) || (data.Byte(1) != 0xcc) || (data.Byte(2) != 0x2c))
      throw new InvalidDataException("Invalid VB3 form signature; expected FF CC 2C.");

    var form = new DecodedForm { Reference = reference };
    using var frx = new MemoryStream();
    form.Root = ReadRecord(data, 9, true, reference, customModels, form, frx, out int position);
    var containers = new Stack<ControlNode>();
    containers.Push(form.Root);
    ControlNode? last = null;
    bool ended = false;
    while (position < data.Length)
    {
      byte tag = data.Byte(position++);
      if (tag == 4)
      {
        ended = true;
        break;
      }

      if (tag == 2)
      {
        if (containers.Count > 1)
          containers.Pop();

        continue;
      }

      if (tag == 5)
      {
        if (last is not null)
          containers.Push(last);

        continue;
      }

      if (tag is not (1 or 3))
        throw new InvalidDataException($"Unknown form record tag 0x{tag:X2} at 0x{position - 1:X}.");

      if ((tag == 1) && (last is not null))
        containers.Push(last);

      if (containers.Count > 256)
        throw new InvalidDataException("Form nesting exceeds 256 levels.");

      last = ReadRecord(data, position, false, reference, customModels, form, frx, out position);
      containers.Peek().Children.Add(last);
      form.Controls.Add(last);
    }

    if (!ended)
      form.Warnings.Add("Form stream has no end marker.");

    form.Frx = frx.ToArray();
    return form;
  }

  private static ControlNode ReadRecord(BinaryData stream, int start, bool isForm, FormReference reference, IReadOnlyDictionary<string, ControlModel> customModels, DecodedForm form, MemoryStream frx, out int end)
  {
    uint rawLength = stream.DWord(start);
    int length = (int)(rawLength & 0x7fffffff);
    if (length < 7)
      throw new InvalidDataException("Form/control record is too short.");

    var record = new BinaryData(stream.Bytes(start, length));
    end = start + length;
    var node = new ControlNode { Id = isForm ? 0 : record.Byte(4) };
    int position = 5;
    if (!isForm && ((rawLength & 0x80000000) != 0))
    {
      node.ArrayIndex = (short)record.Word(position);
      position += 2;
    }

    string inline = record.Pascal(position);
    position += 1 + record.Byte(position);
    byte type = record.Byte(position++);
    node.Name = isForm ? reference.Name : reference.ControlNames.GetValueOrDefault(node.Id) ?? (inline.Length > 0 ? inline : $"control{node.Id}");
    if (isForm && (inline.Length > 0))
      node.Name = inline;

    if (type == 0xff)
    {
      node.TypeName = record.Pascal(position);
      position += 1 + record.Byte(position);
      customModels.TryGetValue(node.TypeName, out ControlModel? custom);
      node.Model = custom;
    }
    else if (Builtins.TryGetValue(type, out var builtin))
    {
      node.TypeName = builtin.Type;
      node.Model = RuntimeTables.Models.GetValueOrDefault(builtin.Model);
    }
    else
    {
      node.TypeName = $"UnknownControl_0x{type:X2}";
    }

    if (node.Model is null)
      form.Warnings.Add($"{node.Name}: no model for {node.TypeName}; properties unavailable.");

    bool separator = false;
    int eventPosition = -1;
    int comboStyle = -1;
    while ((position < record.Length) && (node.Model is not null))
    {
      byte id = record.Byte(position++);
      if (id == 0xff)
      {
        if (separator)
        {
          eventPosition = position - 1;
          break;
        }

        separator = true;
        continue;
      }

      try
      {
        if (id >= node.Model.Properties.Length)
          throw new InvalidDataException($"Property ID {id} is outside the model.");

        ControlProperty property = node.Model.Properties[id];
        if ((node.TypeName == "ComboBox") && (property.Standard == 27) && (comboStyle == 2))
          continue;

        int size = PayloadSize(property, record, position, isForm);
        var payload = new BinaryData(record.Bytes(position, size));
        if ((node.TypeName == "ComboBox") && (property.Name == "Style"))
          comboStyle = payload.Byte(0);

        ApplyProperty(node, property, payload, isForm, reference.FileName, frx);
        position += size;
      }
      catch (InvalidDataException error)
      {
        form.Warnings.Add($"{node.Name}: {error.Message} Remaining properties skipped.");
        break;
      }
    }

    ReadEvents(node, record, eventPosition);
    if (isForm)
      AdjustFormBounds(node);

    return node;
  }

  private static int PayloadSize(ControlProperty property, BinaryData data, int offset, bool isForm)
  {
    if (property.Standard == 5)
      return isForm ? 16 : 8;

    if (property.Standard == 13)
      return 6 + data.Byte(offset);

    if ((property.Name == "ScaleMode") && (property.DataType == 6))
      return 4;

    return property.DataType switch
    {
      1 => 1 + data.Byte(offset),
      2 or 61 => 2,
      3 or 5 or 7 or 8 or 9 or 10 or 11 => 4,
      4 or 6 => 1,
      12 => data.DWord(offset) == uint.MaxValue ? 4 : checked(4 + (int)data.DWord(offset)),
      _ => throw new InvalidDataException($"Unsupported property type {property.DataType} ({property.Name})."),
    };
  }

  private static void ApplyProperty(ControlNode node, ControlProperty property, BinaryData data, bool isForm, string fileName, MemoryStream frx)
  {
    if (!OutputNames.IsIdentifier(property.Name))
      return;

    if (property.Standard == 5)
    {
      string[] names = { "Left", "Top", "Width", "Height" };
      for (int i = 0; i < names.Length; i++)
        node.Properties[(isForm ? "Client" : string.Empty) + names[i]] = VbText.Number(isForm ? (int)data.DWord(i * 4) : (short)data.Word(i * 2));

      return;
    }

    if (property.Standard == 13)
    {
      int nameLength = data.Byte(0);
      node.Properties["FontName"] = VbText.Quote(data.Pascal(0));
      node.Properties["FontSize"] = VbText.Number(BitConverter.Int32BitsToSingle((int)data.DWord(nameLength + 1)));
      string[] attributes = { "FontBold", "FontItalic", "FontUnderline", "FontStrikethru" };
      for (int i = 0; i < attributes.Length; i++)
        node.Properties[attributes[i]] = (data.Byte(nameLength + 5) & (1 << i)) != 0 ? "-1" : "0";

      return;
    }

    if ((property.Name == "ScaleMode") && (property.DataType == 6))
    {
      node.Properties[property.Name] = VbText.Number((short)data.Word(0));
      return;
    }

    if (property.DataType == 12)
    {
      if ((data.DWord(0) == uint.MaxValue) || (data.DWord(0) == 0))
        return;

      long offset = frx.Position;
      frx.Write(data.Span(0, data.Length));
      node.Properties[property.Name] = $"{Path.GetFileNameWithoutExtension(fileName)}.FRX:{offset:X4}";
      return;
    }

    node.Properties[property.Name] = property.DataType switch
    {
      1 => VbText.Quote(data.Pascal(0)),
      2 or 61 => VbText.Number((short)data.Word(0)),
      3 or 8 or 9 or 10 or 11 => VbText.Number((int)data.DWord(0)),
      4 => data.Byte(0) == 0 ? "0" : "-1",
      5 => $"&H{data.DWord(0):X8}&",
      6 => VbText.Number(data.Byte(0)),
      7 => VbText.Number(BitConverter.Int32BitsToSingle((int)data.DWord(0))),
      _ => throw new InvalidDataException("Unsupported property value."),
    };
  }

  private static void ReadEvents(ControlNode node, BinaryData record, int position)
  {
    if (position < 0)
    {
      for (int i = record.Length - 2; i >= 0; i--)
      {
        if (record.Byte(i) != 0xff)
          continue;

        int remaining = record.Length - (i + 2 + (record.Byte(i + 1) * 2));
        if ((record.Byte(i + 1) > 0) && (remaining >= 0) && (remaining <= 4))
        {
          position = i;
          break;
        }
      }
    }

    if ((position < 0) || (position + 2 > record.Length))
      return;

    int count = record.Byte(position + 1);
    record.Span(position + 2, count * 2);
    for (int i = 0; i < count; i++)
    {
      ushort pointer = record.Word(position + 2 + (i * 2));
      if (((pointer & 1) != 0) && (node.Model is not null) && (i < node.Model.Events.Length))
        node.Events[(ushort)(pointer & 0xfffe)] = node.Model.Events[i];
    }
  }

  private static void AdjustFormBounds(ControlNode node)
  {
    if (!node.Properties.TryGetValue("ClientLeft", out string? left))
      return;

    int border = node.Properties.GetValueOrDefault("BorderStyle") switch { "1" => 15, "3" => 60, _ => 0 };
    int caption = border == 0 ? 0 : 450;
    node.Properties["Left"] = VbText.Number(int.Parse(left, System.Globalization.CultureInfo.InvariantCulture) - border);
    node.Properties["Top"] = VbText.Number(int.Parse(node.Properties["ClientTop"], System.Globalization.CultureInfo.InvariantCulture) - caption);
    node.Properties["Width"] = VbText.Number(int.Parse(node.Properties["ClientWidth"], System.Globalization.CultureInfo.InvariantCulture) + (border * 2));
    node.Properties["Height"] = VbText.Number(int.Parse(node.Properties["ClientHeight"], System.Globalization.CultureInfo.InvariantCulture) + caption + border);
  }
}
