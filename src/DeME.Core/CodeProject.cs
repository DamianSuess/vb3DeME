namespace DeME.Core;

public sealed class CodeProject
{
  public List<VbModule> Modules { get; } = new();

  public List<VbProcedure> Procedures { get; } = new();

  public List<string> Warnings { get; } = new();

  public List<string> GlobalDeclarations { get; } = new();

  internal Dictionary<ushort, VbProcedure> ProcedurePointers { get; } = new();

  public static CodeProject Read(NeExecutable executable, VbProject project, IReadOnlyList<DecodedForm> forms)
  {
    var result = new CodeProject();
    NeResource resource = executable.FindResource(10, 2) ?? throw new InvalidDataException("VB3 code directory (RT_RCDATA 2) is missing.");
    var data = new BinaryData(executable.ReadResource(resource));
    data.Span(0, 0x60);
    NeSegment segment = executable.Segments.FirstOrDefault(item => item.HasRelocations && (item.Index != executable.EntrySegment))
      ?? executable.Segments.ElementAtOrDefault(2) ?? throw new InvalidDataException("VB3 descriptor segment is missing.");
    var descriptors = new BinaryData(executable.ReadSegment(segment));
    Dictionary<int, int> targets = ReadRelocations(executable, segment, descriptors);
    ushort modulePointer = data.Word(0x5a);
    ushort pointer = modulePointer;
    var visited = new HashSet<ushort>();
    int moduleNumber = 0;
    while (pointer is not (0 or 0xffff))
    {
      if (!visited.Add(pointer))
        throw new InvalidDataException("Cyclic VB3 module descriptor chain.");

      int offset = DescriptorOffset(pointer);
      descriptors.Span(offset, 64);
      ushort next = descriptors.Word(offset + 30);
      if ((pointer != modulePointer) || (descriptors.Word(offset + 26) != 0))
      {
        FormReference? form = project.Forms.FirstOrDefault(item => item.ModuleId == pointer);
        result.Modules.Add(new VbModule { Id = pointer, Name = form?.Name ?? $"Module{++moduleNumber}", Form = form });
      }

      pointer = next;
    }

    int declarationBase = DeclarationBase(data);
    pointer = data.Word(0x56);
    visited.Clear();
    while (pointer is not (0 or 0xffff))
    {
      if (!visited.Add(pointer))
        throw new InvalidDataException("Cyclic VB3 procedure descriptor chain.");

      int offset = DescriptorOffset(pointer);
      descriptors.Span(offset, 48);
      ushort flags = descriptors.Word(offset + 12);
      bool local = (descriptors.Word(offset + 22) & 1) != 0;
      var procedure = new VbProcedure
      {
        Pointer = pointer,
        DescriptorOffset = (ushort)offset,
        ModuleId = descriptors.Word(offset + 20),
        Name = $"{(((flags >> 8) & 7) == 0 ? "sub" : "fn")}{descriptors.Word(offset + 4):X4}",
        TypeFlags = flags,
        ScopeFlags = descriptors.Word(offset + 14),
        IsLocal = local,
        CodeSegment = targets.GetValueOrDefault(offset),
        CodeOffset = descriptors.Word(offset + 24),
      };
      if (local)
      {
        if ((procedure.CodeSegment <= 0) || (procedure.CodeSegment > executable.Segments.Count))
        {
          result.Warnings.Add($"{procedure.Name}: code segment could not be resolved.");
        }
        else
        {
          var code = new BinaryData(executable.ReadSegment(executable.Segments[procedure.CodeSegment - 1]));
          int end = descriptors.Word(offset + 36);
          if (end < procedure.CodeOffset)
            throw new InvalidDataException("Procedure end precedes its start.");

          procedure.Bytecode = code.Bytes(procedure.CodeOffset, end - procedure.CodeOffset);
        }
      }
      else if (declarationBase >= 0)
      {
        ushort library = descriptors.Word(offset + 40);
        ushort alias = descriptors.Word(offset + 46);
        procedure.Library = library == 0 ? string.Empty : data.Pascal(declarationBase + library).TrimEnd('.');
        procedure.Alias = alias == 0 ? string.Empty : data.Pascal(declarationBase + alias);
      }

      result.Procedures.Add(procedure);
      result.ProcedurePointers[procedure.Pointer] = procedure;
      result.ProcedurePointers[procedure.DescriptorOffset] = procedure;
      VbModule? module = result.Modules.FirstOrDefault(item => item.Id == procedure.ModuleId);
      if (module is null)
      {
        result.Warnings.Add($"{procedure.Name}: missing owning module; recovered into Module{++moduleNumber}.");
        module = new VbModule { Id = procedure.ModuleId, Name = $"Module{moduleNumber}" };
        result.Modules.Add(module);
      }

      module.Procedures.Add(procedure);
      pointer = descriptors.Word(offset + 26);
    }

    result.BindEvents(forms);
    result.ReadModuleData(data, project, forms);
    VariableResolver.Resolve(result);
    return result;
  }

  private static int DescriptorOffset(ushort pointer) => (((pointer >> 3) | ((pointer & 7) << 13)) & 0x1fff) * 8;

  private static int DeclarationBase(BinaryData data)
  {
    int position = 0x60;
    if (position + 2 > data.Length)
      return -1;

    position += 2 + data.Word(position) + 2;
    for (int i = 0; i < 2; i++)
    {
      if (position + 2 > data.Length)
        return -1;

      position += 2 + data.Word(position);
    }

    return position + 5 <= data.Length ? position + 5 : -1;
  }

  private static Dictionary<int, int> ReadRelocations(NeExecutable executable, NeSegment segment, BinaryData descriptors)
  {
    var targets = new Dictionary<int, int>();
    if (!segment.HasRelocations)
      return targets;

    BinaryData data = executable.Data;
    int start = segment.Offset + segment.Length;
    int count = data.Word(start);
    data.Span(start + 2, count * 8);
    for (int i = 0; i < count; i++)
    {
      int position = start + 2 + (i * 8);
      byte sourceType = data.Byte(position);
      byte flags = data.Byte(position + 1);
      if (((flags & 3) != 0) || (sourceType is not (2 or 3)))
        continue;

      ushort link = data.Word(position + 2);
      int target = data.Byte(position + 4);
      var visited = new HashSet<ushort>();
      while (link != 0xffff)
      {
        if (!visited.Add(link))
          throw new InvalidDataException("Cyclic VB3 relocation chain.");

        if (link < 0x26)
          throw new InvalidDataException("Invalid VB3 descriptor relocation offset.");

        targets[link - 0x26] = target;
        if ((flags & 4) != 0)
          break;

        link = descriptors.Word(link);
      }
    }

    return targets;
  }

  private static IEnumerable<(string Name, string Type)> EventParameters(ControlEvent definition)
  {
    string profile = definition.Profile;
    if (profile.Length == 0)
    {
      profile = definition.Name switch
      {
        "MouseDown" or "MouseUp" or "MouseMove" => "Button As Integer, Shift As Integer, X As Single, Y As Single",
        "KeyDown" or "KeyUp" => "KeyCode As Integer, Shift As Integer",
        "KeyPress" => "KeyAscii As Integer",
        "Unload" or "LinkOpen" => "Cancel As Integer",
        "QueryUnload" => "Cancel As Integer, UnloadMode As Integer",
        "DragDrop" => "Source As Control, X As Single, Y As Single",
        "DragOver" => "Source As Control, X As Single, Y As Single, State As Integer",
        "LinkError" => "LinkErr As Integer",
        "LinkExecute" => "CmdStr As String, Cancel As Integer",
        _ => string.Join(", ", Enumerable.Range(1, definition.Parameters).Select(i => $"p{i} As Variant")),
      };
    }

    foreach (string parameter in profile.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string[] parts = parameter.Split(" As ", StringSplitOptions.None);
      yield return (parts[0], parts.Length > 1 ? parts[1] : "Variant");
    }
  }

  private void BindEvents(IReadOnlyList<DecodedForm> forms)
  {
    foreach (VbModule module in Modules)
    {
      DecodedForm? form = forms.FirstOrDefault(item => item.Reference.ModuleId == module.Id);
      if (form is null)
        continue;

      foreach (ControlNode node in form.Controls.Prepend(form.Root))
      {
        if (node.Model is not null)
        {
          module.Models[node.Name] = node.Model;
          if (node == form.Root)
            module.Models["Me"] = node.Model;
        }

        foreach (var binding in node.Events)
        {
          if (!ProcedurePointers.TryGetValue(binding.Key, out VbProcedure? procedure) || (procedure.ModuleId != module.Id))
            continue;

          procedure.Name = $"{(node == form.Root ? "Form" : node.Name)}_{binding.Value.Name}";
          procedure.IsEvent = true;
          if (node.ArrayIndex.HasValue)
            procedure.EventParameters.Add(("Index", "Integer"));

          procedure.EventParameters.AddRange(EventParameters(binding.Value));
        }
      }
    }
  }

  private void ReadModuleData(BinaryData data, VbProject project, IReadOnlyList<DecodedForm> forms)
  {
    int position = 0x60;
    if (data.Length < position + 2)
      return;

    position += 2 + data.Word(position) + 2;
    if (position + 2 > data.Length)
      return;

    position += 2 + data.Word(position);
    for (int i = 0; i < 2; i++)
    {
      if (position + 2 > data.Length)
        return;

      position += 2 + data.Word(position);
    }

    var globalTags = new Dictionary<byte, string> { [0x32] = "Printer", [0x33] = "Screen", [0x34] = "Clipboard", [0x3d] = "App" };
    foreach (FormReference form in project.Forms)
      globalTags[(byte)form.InstanceTag] = form.Name;

    while (position + 4 <= data.Length)
    {
      ushort id = data.Word(position);
      if (id is 0 or 0xffff)
        break;

      int length = data.Word(position + 2);
      position += 4;
      var moduleData = new BinaryData(data.Bytes(position, length));
      position += length;
      VbModule? module = Modules.FirstOrDefault(item => item.Id == id);
      if (module is not null)
      {
        module.Data = moduleData;
        for (int i = 0; i + 4 <= length; i += 2)
        {
          ushort tag = moduleData.Word(i);
          if (((tag & 0xff00) == 0x8000) && (moduleData.Word(i + 2) != 0) && globalTags.TryGetValue((byte)tag, out string? name))
            module.References[(ushort)(i + 2)] = name;
        }
      }

      while ((position + 6 <= data.Length) && (data.Word(position + 4) == 0x1e))
      {
        int size = data.Word(position);
        int count = data.Word(position + 2);
        position += 6;
        int byteCount = count == 0 ? ((size - 4) / 3) * 3 : count * 2;
        data.Span(position, byteCount);
        if ((count == 0) && (module is not null))
        {
          DecodedForm? form = forms.FirstOrDefault(item => item.Reference.ModuleId == module.Id);
          for (int i = 0; i < byteCount; i += 3)
          {
            ushort offset = data.Word(position + i + 1);
            if ((data.Byte(position + i) != 9) || (offset + 2 > length))
              continue;

            ushort value = moduleData.Word(offset);
            if ((value & 0xf000) == 0x8000)
              module.References[offset] = form?.Controls.FirstOrDefault(item => item.Id == (value & 0xff))?.Name ?? $"control{value & 0xff}";
          }
        }

        position += byteCount;
      }
    }
  }
}
