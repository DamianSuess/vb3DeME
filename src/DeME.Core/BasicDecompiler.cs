using System.Text;

namespace DeME.Core;

/// <summary>Reconstructs supported VB3 stack operations and reports every undecoded statement.</summary>
public sealed class BasicDecompiler
{
  private static readonly string[] Methods = { "PrintForm", "LinkSend", "AddItem", "RemoveItem", "Refresh", "LinkPoke", "LinkRequest", "LinkExecute", "GetText", "GetData", "SetText", "SetData", "Clear", "GetFormat", "Arrange", "Show", "Hide", "EndDoc", "NewPage", "SetFocus", "Drag", "Move", "ZOrder", "Close", "Delete" };
  private readonly CodeProject _project;
  private readonly VbModule _module;
  private readonly VbProcedure _procedure;
  private readonly List<string> _stack = new();
  private readonly List<string> _lines = new();
  private readonly Dictionary<ushort, (string Name, string Type, string Storage, bool Array)> _variables = new();
  private readonly Dictionary<ushort, string> _parameters = new();
  private readonly Dictionary<int, string> _labels = new();
  private readonly Stack<bool> _selectCases = new();
  private readonly List<string> _inputVariables = new();
  private int _indent = 1;
  private string? _condition;
  private string? _printTarget;
  private string? _inputFile;
  private string _printItems = string.Empty;
  private int _printDepth;
  private bool _printFile;
  private bool _singleLineIf;
  private bool _missingLayoutReported;
  private bool _incomplete;

  public BasicDecompiler(CodeProject project, VbModule module, VbProcedure procedure)
  {
    _project = project;
    _module = module;
    _procedure = procedure;
  }

  public List<string> Warnings { get; } = new();

  public static string DataType(int type) => type switch
  {
    1 => "Integer",
    2 => "Long",
    3 => "Single",
    4 => "Double",
    5 => "Currency",
    7 => "String",
    _ => "Variant",
  };

  public string Decompile()
  {
    if (!_procedure.IsLocal)
    {
      Warnings.Add($"{_procedure.Name}: external parameter types and passing conventions have not been inferred; declaration is a placeholder.");
      string parameters = string.Join(", ", Enumerable.Range(1, _procedure.ScopeFlags >> 9).Select(i => $"p{i} As Any"));
      string library = _procedure.Library.Length == 0 ? "UNKNOWN" : _procedure.Library;
      string alias = _procedure.Alias.Length == 0 ? string.Empty : " Alias " + VbText.Quote(_procedure.Alias);
      return $"' DeME: external declaration requires parameter verification.\nDeclare {(_procedure.IsFunction ? "Function" : "Sub")} {_procedure.Name} Lib {VbText.Quote(library)}{alias} ({parameters}){(_procedure.IsFunction ? " As " + _procedure.ReturnType : string.Empty)}\n";
    }

    try
    {
      IReadOnlyList<PcodeInstruction> instructions = PcodeReader.Read(_procedure.Bytecode);
      foreach (PcodeInstruction instruction in instructions)
      {
        string keyword = instruction.Opcode.Keyword.Trim();
        if ((keyword is "GoTo" or "GoSub" or "On Error" or "Resume") && (instruction.Operands.Length > 0))
        {
          int target = instruction.Operands[0] - _procedure.CodeOffset;
          if (instructions.Any(item => item.Offset == target))
            _labels[target] = $"L{instruction.Operands[0]:X4}";
        }
      }

      foreach (PcodeInstruction instruction in instructions)
      {
        if (_labels.TryGetValue(instruction.Offset, out string? label))
          _lines.Add(label + ":");

        Decode(instruction);
      }

      Flush();
    }
    catch (InvalidDataException error)
    {
      Note($"Procedure decoding stopped: {error.Message}");
    }

    if (_procedure.Bytecode.Length == 0)
      Note("No bytecode recovered for this local procedure.");

    return Render();
  }

  public string Listing()
  {
    var result = new StringBuilder();
    try
    {
      foreach (PcodeInstruction instruction in PcodeReader.Read(_procedure.Bytecode))
      {
        string operands = string.Join(" ", instruction.Operands.Select(value => $"{value:X4}"));
        string text = instruction.StringData is null ? string.Empty : " " + VbText.Quote(VbText.Decode(instruction.StringData));
        result.AppendLine($"{instruction.Offset + _procedure.CodeOffset:X4}  {instruction.RawToken:X4}  {instruction.Opcode.TokenID,3}  {instruction.Opcode.Keyword,-16} {operands}{text}");
      }
    }
    catch (InvalidDataException error)
    {
      result.AppendLine("; " + error.Message);
    }

    return result.ToString();
  }

  private static uint DWord(ushort[] words)
  {
    if (words.Length < 2)
      throw new InvalidDataException("Truncated numeric literal.");

    return (uint)words[0] | ((uint)words[1] << 16);
  }

  private void Decode(PcodeInstruction instruction)
  {
    OpcodeInfo opcode = instruction.Opcode;
    string keyword = opcode.Keyword.Trim();
    ushort[] args = instruction.Operands;
    if (instruction.StringData is not null)
    {
      Push(VbText.Quote(VbText.Decode(instruction.StringData)));
      return;
    }

    if ((opcode.Case is 4 or 5) || (keyword is "nl" or "eos"))
    {
      Flush();
      if (_condition is not null)
      {
        Line($"If {_condition} Then");
        _condition = null;
        _indent++;
      }

      return;
    }

    if (opcode.Case == 13)
    {
      Unsupported(instruction);
      return;
    }

    if (opcode.TokenID is 242 or 286)
    {
      Push("<default>");
      return;
    }

    if ((keyword == "v0") || (opcode.TokenID == 91))
    {
      _printTarget = Pop();
      _printDepth = _stack.Count;
      _printItems = string.Empty;
      _printFile = opcode.TokenID == 91;
      return;
    }

    if (_printTarget is not null)
    {
      if (opcode.TokenID is 112 or 113 or 115 or 116)
      {
        if (_stack.Count > _printDepth)
          _printItems += Pop();

        _printItems += keyword + " ";
        return;
      }

      if (keyword is "eol" or "eop")
      {
        if (_stack.Count > _printDepth)
          _printItems += Pop();

        string print = _printFile ? "Print " + _printTarget + ", " : _printTarget == "Me" ? "Print " : _printTarget + ".Print ";
        Statement(print + _printItems.TrimEnd());
        _printTarget = null;
        return;
      }

      if ((keyword == "eom") || (opcode.TokenID is >= 92 and <= 95))
        return;
    }

    switch (keyword)
    {
      case "exe":
      case "pop.":
      case "C<typ>":
      case "As":
      case "Let":
      case "bpt":
        return;
      case "c%":
        Push(VbText.Number(args.Length > 0 ? (short)args[0] : ((short)(instruction.AlternateToken & 0xfc00)) >> 10));
        return;
      case "c&":
        Push(VbText.Number(args.Length >= 2 ? unchecked((int)((uint)args[0] | ((uint)args[1] << 16))) : ((short)(instruction.AlternateToken & 0xfc00)) >> 10) + "&");
        return;
      case "c!":
        Push(VbText.Number(BitConverter.Int32BitsToSingle(unchecked((int)DWord(args)))));
        return;
      case "c#":
        if (args.Length < 4)
          throw new InvalidDataException("Truncated Double literal.");

        ulong bits = (ulong)args[0] | ((ulong)args[1] << 16) | ((ulong)args[2] << 32) | ((ulong)args[3] << 48);
        Push(VbText.Number(BitConverter.Int64BitsToDouble(unchecked((long)bits))));
        return;
      case "&Hc%":
      case "&Hc&":
        Push($"&H{(args.Length > 1 ? DWord(args) : args[0]):X}{(keyword == "&Hc&" ? "&" : string.Empty)}");
        return;
      case "True":
      case "False":
      case "Null":
      case "Timer":
        Push(keyword);
        return;
      case "+":
      case "*":
      case "/":
      case "\\":
      case "^":
      case "Mod":
      case "&":
      case "=":
      case "<>":
      case "<":
      case "<=":
      case ">":
      case ">=":
      case "And":
      case "Or":
      case "Xor":
      case "Eqv":
      case "Imp":
      case "Like":
      case "Is":
        string right = Pop();
        Push($"({Pop()} {keyword} {right})");
        return;
      case "-":
        string operand = Pop();
        Push(opcode.TokenID == 249 ? $"(-{operand})" : $"({Pop()} - {operand})");
        return;
      case "Not":
        Push($"(Not {Pop()})");
        return;
      case "var":
      case "var=":
        string variable = Variable(args[0], instruction);
        if (keyword == "var=")
          Statement($"{variable} = {Pop()}");
        else
          Push(variable);

        return;
      case "var()":
      case "var()=":
        if ((args.Length < 2) || ((args[0] & 0x8000) != 0))
        {
          Unsupported(instruction);
          return;
        }

        string indices = Arguments(args[0]);
        string array = Variable(args[1], instruction) + $"({indices})";
        if (keyword.EndsWith('='))
          Statement($"{array} = {Pop()}");
        else
          Push(array);

        return;
      case "pop.var":
      case "pop.var=":
      case "pop.var()":
      case "pop.var()=":
        string target = Pop();
        string index = keyword.Contains("()", StringComparison.Ordinal) ? "(" + Arguments(args[0]) + ")" : string.Empty;
        ushort propertyId = args[^1];
        string member = Property(target, propertyId);
        string expression = ((target == "Me") && ((propertyId & 0xc000) == 0x8000) ? member : target + "." + member) + index;
        if (keyword.EndsWith('='))
          Statement($"{expression} = {Pop()}");
        else
          Push(expression);

        return;
      case "met":
        string methodArgs = Arguments(args[1]);
        string receiver = Pop();
        int methodId = args[0] & 0xfff;
        string method = methodId < Methods.Length ? Methods[methodId] : $"Method_{methodId}";
        bool function = (args[0] >= 0xc000) || (methodId is 6 or 8 or 9 or 13);
        Push(function ? $"{receiver}.{method}({methodArgs})" : $"{receiver}.{method} {methodArgs}".TrimEnd());
        return;
      case "call":
      case "Call":
        string callArgs = Arguments(args[0]);
        string callTarget;
        if ((args.Length > 1) && _project.ProcedurePointers.TryGetValue(args[1], out VbProcedure? called))
        {
          callTarget = called.Name;
        }
        else if (_stack.Count > 0)
        {
          callTarget = Pop();
          if (callTarget.StartsWith('l') && _variables.Any(item => item.Value.Name == callTarget))
            Note($"Call target {callTarget} is unresolved.");
        }
        else
        {
          Unsupported(instruction);
          return;
        }

        Statement($"Call {callTarget}({callArgs})");
        return;
      case "eom":
        Flush();
        return;
      case ":":
        Flush();
        return;
      case "If":
        _condition = Pop();
        return;
      case "Else":
        if (_singleLineIf && (_lines.Count > 0))
        {
          _condition = string.Empty;
          return;
        }

        _indent = Math.Max(1, _indent - 1);
        Line("Else");
        _indent++;
        return;
      case "ElseIf":
        _indent = Math.Max(1, _indent - 1);
        Line($"ElseIf {Pop()} Then");
        _indent++;
        return;
      case "End If":
      case "Wend":
      case "Loop":
      case "Next":
        _indent = Math.Max(1, _indent - 1);
        Line(keyword);
        return;
      case "While":
      case "Do While":
      case "Do Until":
        Line($"{keyword} {Pop()}");
        _indent++;
        return;
      case "Do":
        Line("Do");
        _indent++;
        return;
      case "Loop While":
      case "Loop Until":
        _indent = Math.Max(1, _indent - 1);
        Line($"{keyword} {Pop()}");
        return;
      case "For":
        string step = opcode.TokenID == 62 ? string.Empty : " Step " + Pop();
        string upper = Pop();
        string lower = Pop();
        Line($"For {Pop()} = {lower} To {upper}{step}");
        _indent++;
        return;
      case "Select Case":
        Line("Select Case " + Pop());
        _indent++;
        _selectCases.Push(false);
        return;
      case "Case":
      case "Case Else":
        if ((_selectCases.Count > 0) && _selectCases.Pop())
          _indent--;

        string caseValue = keyword == "Case Else" ? "Else" : opcode.BitForward2 == 2 ? CaseRange() : Pop();
        Line("Case " + caseValue);
        _indent++;
        _selectCases.Push(true);
        return;
      case "End Select":
        if ((_selectCases.Count > 0) && _selectCases.Pop())
          _indent--;

        _indent = Math.Max(1, _indent - 1);
        Line("End Select");
        return;
      case "GoTo":
      case "GoSub":
        if ((args.Length > 0) && _labels.ContainsKey(args[0] - _procedure.CodeOffset))
          Statement($"{keyword} L{args[0]:X4}");
        else
          Unsupported(instruction);

        return;
      case "Exit":
        Statement(_procedure.IsFunction ? "Exit Function" : "Exit Sub");
        return;
      case "On Error":
        if ((args.Length > 0) && (args[0] == 0xfffe))
          Statement("On Error Resume Next");
        else if ((args.Length > 0) && _labels.ContainsKey(args[0] - _procedure.CodeOffset))
          Statement($"On Error GoTo L{args[0]:X4}");
        else
          Unsupported(instruction);

        return;
      case "Resume":
      case "Resume Next":
        if (args.Length == 0)
          Statement(keyword);
        else if (_labels.ContainsKey(args[0] - _procedure.CodeOffset))
          Statement($"Resume L{args[0]:X4}");
        else
          Unsupported(instruction);

        return;
      case "Exit Sub":
      case "Exit Function":
      case "Exit For":
      case "Exit Do":
      case "Return":
      case "Beep":
      case "DoEvents":
        Statement(keyword);
        return;
      case "End":
        if (opcode.TokenID != 57)
          Statement("End");

        return;
      case "Load":
      case "Unload":
      case "Print":
      case "Randomize":
        Statement(keyword + ((opcode.BitForward2 > 0) || (_stack.Count > 0) ? " " + Pop() : string.Empty));
        return;
      case "ByVal":
        Push("ByVal " + Pop());
        return;
      case "#":
        Push("#" + Pop());
        return;
      case "Open":
        string mode = args[0] switch { 1 => "Input", 2 => "Output", 4 => "Random", 8 => "Append", 32 => "Binary", _ => string.Empty };
        if (mode.Length == 0)
        {
          Unsupported(instruction);
          return;
        }

        string recordLength = opcode.TokenID == 143 ? " Len = " + Pop() : string.Empty;
        string fileNumber = Pop();
        Statement($"Open {Pop()} For {mode} As {fileNumber}{recordLength}");
        return;
      case "Close":
        Statement("Close " + Arguments(args[0]));
        return;
      case "Get":
      case "Put":
        string destination = Pop();
        string record = opcode.TokenID == 128 ? string.Empty : Pop();
        Statement($"{keyword} {Pop()}, {record}, {destination}");
        return;
      case "Line Input":
        Statement($"Line Input {_inputFile}, {Pop()}");
        _inputFile = null;
        return;
      case "fin":
        if ((args.Length == 2) && (args[0] == 0x8043))
          Push($"LoadPicture({Arguments(args[1])})");
        else
          Unsupported(instruction);

        return;
      case "MsgBox":
      case "InputBox$":
      case "GetObject":
      case "SendKeys":
        int optionalCount = Math.Min(_stack.Count, keyword switch { "MsgBox" => 3, "InputBox$" => 5, _ => 2 });
        string optionalArguments = Arguments(optionalCount);
        if (opcode.Flags == 3)
          Statement(keyword + " " + optionalArguments);
        else
          Push($"{keyword}({optionalArguments})");

        return;
    }

    if (opcode.TokenID == 103)
    {
      _inputFile = Pop();
      _inputVariables.Clear();
      return;
    }

    if (opcode.TokenID == 130)
    {
      _inputVariables.Add(Pop());
      return;
    }

    if (opcode.TokenID == 104)
    {
      Statement($"Input {_inputFile}, {string.Join(", ", _inputVariables)}");
      _inputFile = null;
      return;
    }

    if (opcode.TokenID is >= 40 and <= 45)
    {
      Push(keyword + " " + Pop());
      return;
    }

    if ((opcode.TokenID == 238) || ((keyword.Length == 0) && (opcode.Flags == 8) && (opcode.BitForward2 == 1)))
    {
      Push("(" + Pop() + ")");
      return;
    }

    string[] functions = { "Len", "Asc", "Chr$", "Space$", "Hex$", "Oct$", "Trim$", "LTrim$", "RTrim$", "Rnd", "Int", "Fix", "Abs", "Sgn", "Sqr", "Sin", "Cos", "Tan", "LCase$", "UCase$", "Str$", "Val", "DateValue", "TimeValue", "Day", "Month", "Year", "Weekday", "Hour", "Minute", "Second", "IsEmpty", "IsNull", "IsNumeric", "VarType", "Error$", "Left$", "Right$", "InStr", "String$", "Format$", "MsgBox", "InputBox$", "UBound", "LBound", "EOF", "LOF", "FreeFile", "Log", "Exp", "Atn" };
    if (functions.Contains(keyword, StringComparer.Ordinal) || ((keyword == "Mid$") && (opcode.Flags == 8)))
    {
      int count = opcode.BitForward2;
      if (keyword is "Len" or "Asc")
        count = 1;

      if (count == 0)
        Push(keyword);
      else
        Push($"{keyword}({Arguments(count)})");

      return;
    }

    if ((opcode.Flags == 8) && (opcode.BitForward2 > 0) && OutputNames.IsIdentifier(keyword.TrimEnd('$')))
    {
      Push($"{keyword}({Arguments(opcode.BitForward2)})");
      return;
    }

    if ((opcode.Flags == 4) && (opcode.BitForward2 == 0) && OutputNames.IsIdentifier(keyword.TrimEnd('$')))
    {
      Push(keyword);
      return;
    }

    if ((opcode.Flags == 3) && (keyword is "ChDir" or "ChDrive" or "Erase" or "Error" or "FileCopy" or "Kill" or "MkDir" or "Reset" or "RmDir" or "SetAttr"))
    {
      Statement(keyword + " " + Arguments(opcode.BitForward2));
      return;
    }

    Unsupported(instruction);
  }

  private string Variable(ushort slot, PcodeInstruction instruction)
  {
    if (_module.References.TryGetValue(slot, out string? reference))
      return reference;

    short bp = slot + 2 <= _module.Data.Length ? (short)_module.Data.Word(slot) : (short)0;
    if ((slot + 2 > _module.Data.Length) && !_missingLayoutReported)
    {
      Note("Module variable layout is missing; variable storage and parameter classification require review.");
      _missingLayoutReported = true;
    }

    int typeCode = instruction.RawToken + 1 < RuntimeTables.ControlTokens.Length ? RuntimeTables.ControlTokens[instruction.RawToken + 1] : 0;
    string type = DataType(typeCode);
    int returnBp = ((_procedure.TypeFlags >> 8) & 7) switch { 1 => -24, 2 or 3 => -26, 4 or 5 => -30, 6 => -38, 7 => 1, _ => 0 };
    if (_procedure.IsFunction && (returnBp == bp) && (_module.Data.Length > 0))
      return _procedure.Name;

    if (_variables.TryGetValue(slot, out var existing))
    {
      if ((existing.Type == "Variant") && (type != "Variant"))
        _variables[slot] = (existing.Name, type, existing.Storage, existing.Array);

      return existing.Name;
    }

    string name = $"l{slot:X4}";
    string storage = bp == 0 ? "Static" : "Dim";
    if ((bp >= 6) && ((bp & 1) == 0) && (bp <= 60))
    {
      int eventIndex = _procedure.EventParameters.Count - 1 - ((bp - 6) / 4);
      if (_procedure.IsEvent && ((bp - 6) % 4 == 0) && (eventIndex >= 0) && (eventIndex < _procedure.EventParameters.Count))
        return _procedure.EventParameters[eventIndex].Name;

      name = $"p{slot:X4}";
      _parameters[slot] = $"{name} As {type}";
    }

    bool isArray = instruction.Opcode.Keyword.Contains("()", StringComparison.Ordinal);
    if (isArray)
      Note($"Array bounds for {name} require reconstruction.");

    _variables[slot] = (name, type, storage, isArray);
    return name;
  }

  private string Property(string target, ushort id)
  {
    if ((id & 0xc000) == 0x8000)
    {
      Note($"Control reference {target}.control{id & 0x3fff} uses a generated name.");
      return $"control{id & 0x3fff}";
    }

    ControlModel? model = _module.Models.GetValueOrDefault(target) ?? RuntimeTables.Models.GetValueOrDefault(target);
    if (((id & 0xc000) == 0xc000) && (model is not null) && ((id & 0x3fff) < model.Properties.Length))
      return model.Properties[id & 0x3fff].Name;

    Note($"Unresolved property {target}.Prop_{id:X4}.");
    return $"Prop_{id:X4}";
  }

  private string Arguments(int count)
  {
    if ((count < 0) || (count > _stack.Count))
      throw new InvalidDataException($"P-code expression stack underflow ({count} arguments, {_stack.Count} values).");

    string[] args = _stack.Skip(_stack.Count - count).ToArray();
    _stack.RemoveRange(_stack.Count - count, count);
    int last = args.Length;
    while ((last > 0) && args[last - 1].StartsWith("<default", StringComparison.Ordinal))
      last--;

    return string.Join(", ", args.Take(last).Select(value => value.StartsWith("<default", StringComparison.Ordinal) ? string.Empty : value));
  }

  private string CaseRange()
  {
    string upper = Pop();
    return Pop() + " To " + upper;
  }

  private void Push(string value) => _stack.Add(value);

  private string Pop()
  {
    if (_stack.Count == 0)
      throw new InvalidDataException("P-code expression stack underflow.");

    string value = _stack[^1];
    _stack.RemoveAt(_stack.Count - 1);
    return value;
  }

  private void Flush()
  {
    if (_stack.Count == 1)
    {
      Statement(Pop());
    }
    else if (_stack.Count > 1)
    {
      Note("Unconsumed expressions: " + string.Join(" | ", _stack));
      _stack.Clear();
    }
  }

  private void Statement(string text)
  {
    if (_condition is not null)
    {
      if ((_condition.Length == 0) && _singleLineIf && (_lines.Count > 0))
      {
        _lines[^1] += " Else " + text;
        _condition = null;
        return;
      }

      text = $"If {_condition} Then {text}";
      _condition = null;
      _singleLineIf = true;
    }
    else
    {
      _singleLineIf = false;
    }

    Line(text);
  }

  private void Line(string text) => _lines.Add(new string(' ', _indent * 2) + text);

  private void Note(string message)
  {
    _incomplete = true;
    Warnings.Add($"{_module.Name}.{_procedure.Name}: {message}");
    Line("' DeME: " + message.Replace('\r', ' ').Replace('\n', ' '));
  }

  private void Unsupported(PcodeInstruction instruction)
  {
    Note($"Unhandled P-code {instruction.Opcode.TokenID} ({instruction.Opcode.Keyword}) at 0x{instruction.Offset + _procedure.CodeOffset:X4}; inspect the .pcode.txt and .pcode.bin files.");
    _stack.Clear();
    _condition = null;
  }

  private string Render()
  {
    var result = new StringBuilder();
    if (_incomplete)
      result.AppendLine("' DeME: PARTIAL RECOVERY. This procedure needs manual reconstruction.");

    string kind = _procedure.IsFunction ? "Function" : "Sub";
    IEnumerable<string> parameters = _procedure.IsEvent
      ? _procedure.EventParameters.Select(item => $"{item.Name} As {item.Type}")
      : _parameters.OrderByDescending(item => item.Key + 2 <= _module.Data.Length ? (int)(short)_module.Data.Word(item.Key) : item.Key).Select(item => item.Value);
    int expected = _procedure.ScopeFlags >> 9;
    if (!_procedure.IsEvent && (expected > 0))
      Warnings.Add($"{_procedure.Name}: parameter passing conventions require manual verification.");

    var parameterList = parameters.ToList();
    if (!_procedure.IsEvent && (parameterList.Count != expected))
    {
      Warnings.Add($"{_procedure.Name}: recovered {parameterList.Count} of {expected} parameters; signature requires review.");
      result.AppendLine("' DeME: parameter list requires verification.");
      while (parameterList.Count < expected)
        parameterList.Add($"unknown{parameterList.Count + 1} As Variant");
    }

    result.AppendLine($"{kind} {_procedure.Name} ({string.Join(", ", parameterList)}){(_procedure.IsFunction ? " As " + _procedure.ReturnType : string.Empty)}");
    foreach (var variable in _variables.OrderBy(item => item.Key))
    {
      if (!_parameters.ContainsKey(variable.Key))
        result.AppendLine($"  {variable.Value.Storage} {variable.Value.Name}{(variable.Value.Array ? "()" : string.Empty)} As {variable.Value.Type}");
    }

    foreach (string line in _lines)
      result.AppendLine(line);

    result.AppendLine($"End {kind}");
    return result.ToString();
  }
}
