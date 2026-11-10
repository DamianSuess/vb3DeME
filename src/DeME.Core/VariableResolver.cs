// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

internal static class VariableResolver
{
  internal static void Resolve(CodeProject project)
  {
    var globals = new Dictionary<ushort, (string Type, bool Array)>();
    foreach (VbModule module in project.Modules)
    {
      var references = new Dictionary<ushort, (int Count, string Type, bool Array)>();
      foreach (VbProcedure procedure in module.Procedures.Where(item => item.IsLocal))
      {
        var seen = new HashSet<ushort>();
        IReadOnlyList<PcodeInstruction> instructions;
        try
        {
          instructions = PcodeReader.Read(procedure.Bytecode);
        }
        catch (InvalidDataException)
        {
          // The source decoder reports the error and preserves the raw procedure.
          continue;
        }

        foreach (PcodeInstruction instruction in instructions)
        {
          if (!instruction.Opcode.Keyword.StartsWith("var", StringComparison.Ordinal) || (instruction.Operands.Length == 0))
            continue;

          bool array = instruction.Opcode.Keyword.Contains("()", StringComparison.Ordinal);
          ushort slot = array && (instruction.Operands.Length > 1) ? instruction.Operands[1] : instruction.Operands[0];
          if ((slot + 2 > module.Data.Length) || (instruction.RawToken + 1 >= RuntimeTables.ControlTokens.Length))
            continue;

          int mode = RuntimeTables.ControlTokens[instruction.RawToken];
          int kind = RuntimeTables.ControlTokens[instruction.RawToken + 1];
          ushort value = module.Data.Word(slot);
          if ((kind == 12) && (project.ProcedurePointers.TryGetValue(value, out VbProcedure? target) || project.ProcedurePointers.TryGetValue((ushort)(value & 0xfffe), out target)))
          {
            module.References[slot] = target.Name;
            continue;
          }

          if (module.References.ContainsKey(slot))
            continue;

          if ((mode == 10) && (kind is not (9 or 12)))
          {
            string type = BasicDecompiler.DataType(kind);
            if ((kind == 10) && (slot + 6 <= module.Data.Length))
              type = BasicDecompiler.DataType(module.Data.Word(slot + 4) & 7);

            if (globals.TryGetValue(value, out var previous) && (type == "Variant"))
              type = previous.Type;

            globals[value] = (type, array || (kind == 10));
            module.References[slot] = $"gv{value:X4}";
          }
          else if ((kind is >= 1 and <= 7) && (value == 0) && seen.Add(slot))
          {
            var previous = references.GetValueOrDefault(slot);
            references[slot] = (previous.Count + 1, BasicDecompiler.DataType(kind), previous.Array || array);
          }
        }
      }

      foreach (var reference in references.Where(item => item.Value.Count > 1).OrderBy(item => item.Key))
      {
        string name = $"m{reference.Key:X4}";
        module.References[reference.Key] = name;
        module.Declarations.Add($"Dim {name}{(reference.Value.Array ? "()" : string.Empty)} As {reference.Value.Type}");
        if (reference.Value.Array)
          project.Warnings.Add($"{module.Name}.{name}: array bounds require manual reconstruction.");
      }
    }

    foreach (var global in globals.OrderBy(item => item.Key))
    {
      project.GlobalDeclarations.Add($"Global gv{global.Key:X4}{(global.Value.Array ? "()" : string.Empty)} As {global.Value.Type}");
      if (global.Value.Array)
        project.Warnings.Add($"gv{global.Key:X4}: global array bounds require manual reconstruction.");
    }
  }
}
