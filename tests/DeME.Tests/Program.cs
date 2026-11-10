using System.Text;
using System.Text.Json;
using DeME.Core;

namespace DeME.Tests;

internal static class Program
{
  private static int Main(string[] args)
  {
    if ((args.Length == 2) && (args[0] == "--write-fixture"))
    {
      File.WriteAllBytes(args[1], Fixture.Executable());
      return 0;
    }

    var tests = new (string Name, Action Run)[]
    {
      ("NE headers, resources, imports and immutable input", ReadExecutable),
      ("Reject truncated input at every byte boundary", RejectTruncation),
      ("Reject invalid signatures, offsets and alignments", RejectInvalidNe),
      ("Recover project, form names and control names", ReadProject),
      ("Read control-type records without interpreting type IDs as name lengths", ReadControlTypes),
      ("Decode CommandButton using runtime property layout", DecodeButton),
      ("Preserve controls with truncated properties and warnings", TruncatedProperty),
      ("Reject broken control framing", RejectControl),
      ("Decode control array index and container nesting", DecodeArray),
      ("Follow descriptor and relocation chains and bind Click", ReadProcedures),
      ("Reject relocation and descriptor cycles", RejectCycles),
      ("Decode typed literals, expressions and assignments", DecompileExpressions),
      ("Report unsupported opcodes without silent loss", UnsupportedOpcode),
      ("Reject invalid and truncated P-code", RejectPcode),
      ("Transcode Windows-1252 and quote control characters", EncodingRoundTrip),
      ("Detect standalone media formats", DetectAssets),
      ("Export project, source, bytecode and machine-readable report", Export),
      ("Refuse existing output and preserve files", ExistingOutput),
      ("Reject executable-provided path traversal", RejectTraversal),
      ("Cancel without publishing output", Cancel),
      ("Honor output switches and UTF-8 encoding", OutputOptions),
      ("Decode variable-length P-code string literals and MsgBox", StringAndDialog),
      ("Decode structured If/Else and Select Case", ControlFlow),
      ("Decode file opening and closing statements", FileStatements),
      ("Reconstruct FRX pictures and standalone media", PictureAssets),
      ("Preserve failed form and code recovery with diagnostics", FailedRecovery),
      ("Report missing custom controls", MissingCustomControls),
      ("Resolve global variable addressing and declarations", GlobalVariables),
      ("Read custom VBX models without loading native code", CustomControlModels),
    };
    int failed = 0;
    foreach (var test in tests)
    {
      try
      {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
      }
      catch (Exception error)
      {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
      }
    }

    Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
    return failed == 0 ? 0 : 1;
  }

  private static void ReadExecutable()
  {
    byte[] bytes = Fixture.Executable();
    NeExecutable executable = NeExecutable.Parse(bytes);
    Assert(executable.IsVisualBasic3 && (executable.Segments.Count == 4) && (executable.Resources.Count == 4));
    Array.Clear(bytes);
    Assert(executable.ReadResource(executable.FindResource(10, 1)!)[6] == 'F');
    Assert(executable.Segments[1].Length == 0);
    Assert(executable.Segments[1].MinimumAllocation == 65536);
  }

  private static void RejectTruncation()
  {
    byte[] bytes = Fixture.Executable();
    for (int length = 0; length < bytes.Length; length++)
    {
      byte[] truncated = bytes[..length];
      Throws<InvalidDataException>(() => NeExecutable.Parse(truncated));
    }
  }

  private static void RejectInvalidNe()
  {
    byte[] bytes = Fixture.Executable();
    bytes[0] = 0;
    Throws<InvalidDataException>(() => NeExecutable.Parse(bytes));
    bytes = Fixture.Executable();
    bytes[128] = (byte)'P';
    Throws<InvalidDataException>(() => NeExecutable.Parse(bytes));
    bytes = Fixture.Executable();
    Fixture.Word(bytes, 128 + 0x32, 33);
    Throws<InvalidDataException>(() => NeExecutable.Parse(bytes));
    bytes = Fixture.Executable();
    Fixture.DWord(bytes, 0x3c, uint.MaxValue);
    Throws<InvalidDataException>(() => NeExecutable.Parse(bytes));
  }

  private static void ReadProject()
  {
    VbProject project = VbProject.Read(NeExecutable.Parse(Fixture.Executable()));
    Assert((project.Title == "Fixture") && (project.Forms.Count == 1));
    Assert(project.Forms[0].Name == "Main");
    Assert(project.Forms[0].ControlNames[1] == "Command1");
  }

  private static void ReadControlTypes()
  {
    byte[] bytes = Fixture.Executable();
    int position = 3072 + 19 + 11 + "MAIN.FRM".Length + 2;
    foreach (byte type in new byte[] { 0, 1, 7, 13, 0xff })
    {
      bytes[position] = 0x58;
      bytes[position + 1] = type;
      Fixture.Word(bytes, position + 11, 0);
      position += 13;
    }

    // A following form proves that each control-type record preserves alignment.
    byte[] form = bytes.AsSpan(3072 + 19, 11 + "MAIN.FRM".Length + 2).ToArray();
    form.CopyTo(bytes, position);
    VbProject project = VbProject.Read(NeExecutable.Parse(bytes));
    Assert(project.Forms.Count == 2);
    Assert(project.Forms.All(item => item.FileName == "MAIN.FRM"));
    Assert(project.CustomControls.Count == 0);

    // Control-type records still have a bounds-checked sub-item array.
    Fixture.Word(bytes, position - 2, 0x7fff);
    Throws<InvalidDataException>(() => VbProject.Read(NeExecutable.Parse(bytes)));
  }

  private static FormReference Reference() => new(1, 4, "MAIN.FRM", "Main", 0x48, 0x40, new Dictionary<int, string>());

  private static void DecodeButton()
  {
    byte[] button = Fixture.Record(1, 7, 4, 0xff, 0, 3, 79, 75, 46, 4, 0x78, 0, 0xf0, 0, 0x7f, 8, 0x77, 1, 0x0b, 4, 65, 114, 105, 97, 0, 0, 0x1c, 0x41, 1, 0x11, 2, 0, 0xff, 0);
    DecodedForm form = FormDecoder.Decode(Fixture.Form(button, new byte[] { 2 }), Reference());
    Assert(form.Warnings.Count == 0, string.Join("; ", form.Warnings));
    ControlNode control = form.Controls.Single();
    Assert(control.Properties["Caption"] == "\"OK.\"");
    Assert(control.Properties["Left"] == "120");
    Assert(control.Properties["Height"] == "375");
    Assert(control.Properties["FontSize"] == "9.75");
    Assert(control.Properties["FontBold"] == "-1");
    Assert(control.Properties["TabIndex"] == "2");
  }

  private static void TruncatedProperty()
  {
    DecodedForm form = FormDecoder.Decode(Fixture.Form(Fixture.Record(1, 1, 1, 0xff, 0)), Reference());
    Assert((form.Controls.Count == 1) && (form.Warnings.Count == 1));
  }

  private static void RejectControl()
  {
    Throws<InvalidDataException>(() => FormDecoder.Decode(Fixture.Form(new byte[] { 1, 4, 0, 0, 0 }), Reference()));
  }

  private static void DecodeArray()
  {
    byte[] array = { 1, 12, 0, 0, 0x80, 1, 3, 0, 0, 4, 0xff, 0xff, 0 };
    DecodedForm form = FormDecoder.Decode(Fixture.Form(array, new byte[] { 2 }), Reference());
    Assert(form.Controls[0].ArrayIndex == 3);
    Assert(form.ToFormText().Contains("Index = 3", StringComparison.Ordinal));
  }

  private static (CodeProject Code, VbModule Module, VbProcedure Procedure) Code(byte[]? bytes = null)
  {
    NeExecutable executable = NeExecutable.Parse(bytes ?? Fixture.Executable());
    VbProject project = VbProject.Read(executable);
    FormReference reference = project.Forms.Single();
    DecodedForm form = FormDecoder.Decode(executable.ReadResource(executable.FindResource(10, 4)!), reference);
    CodeProject code = CodeProject.Read(executable, project, new[] { form });
    return (code, code.Modules.Single(), code.Procedures.Single());
  }

  private static void ReadProcedures()
  {
    var code = Code();
    Assert(code.Procedure.Name == "Command1_Click");
    Assert(code.Procedure.CodeSegment == 4);
    Assert(code.Procedure.Bytecode.Length > 0);
    string source = new BasicDecompiler(code.Code, code.Module, code.Procedure).Decompile();
    Assert(source.Contains("Sub Command1_Click", StringComparison.Ordinal));
    Assert(source.Contains("l0040 = 42", StringComparison.Ordinal));
  }

  private static void RejectCycles()
  {
    byte[] bytes = Fixture.Executable();
    Fixture.Word(bytes, 1024 + 128 + 38, 128 + 38);
    Throws<InvalidDataException>(() => Code(bytes));
    bytes = Fixture.Executable();
    Fixture.Word(bytes, 1024 + 64 + 30, 0x40);
    Throws<InvalidDataException>(() => Code(bytes));
  }

  private static void DecompileExpressions()
  {
    byte[] bytecode = Fixture.Bytecode(("c%", new ushort[] { 2 }), ("c%", new ushort[] { 3 }), ("+", Array.Empty<ushort>()), ("var=", new ushort[] { 0x50 }), ("eos", Array.Empty<ushort>()));
    var code = Code(Fixture.Executable(bytecode: bytecode));
    var decompiler = new BasicDecompiler(code.Code, code.Module, code.Procedure);
    Assert(decompiler.Decompile().Contains("l0050 = (2 + 3)", StringComparison.Ordinal));
    Assert(decompiler.Warnings.Count == 0);
  }

  private static void UnsupportedOpcode()
  {
    byte[] bytecode = Fixture.Bytecode(("Write ", Array.Empty<ushort>()), ("eos", Array.Empty<ushort>()));
    var code = Code(Fixture.Executable(bytecode: bytecode));
    var decompiler = new BasicDecompiler(code.Code, code.Module, code.Procedure);
    Assert(decompiler.Decompile().Contains("PARTIAL RECOVERY", StringComparison.Ordinal));
    Assert(decompiler.Warnings.Count > 0);
  }

  private static void RejectPcode()
  {
    Throws<InvalidDataException>(() => PcodeReader.Read(new byte[] { 0xff }));
    Throws<InvalidDataException>(() => PcodeReader.Read(new byte[] { 0xff, 0xff }));
    byte[] bytes = Fixture.Bytecode(("c%", new ushort[] { 42 }));
    Throws<InvalidDataException>(() => PcodeReader.Read(bytes[..^1]));
  }

  private static void EncodingRoundTrip()
  {
    byte[] bytes = { 0x80, 0x8b, 0xab, 0x97 };
    Assert(VbText.Decode(bytes) == "€‹«—");
    Assert(VbText.Encode(VbText.Decode(bytes)).SequenceEqual(bytes));
    Assert(VbText.Quote("a\"b\r") == "\"a\"\"b\" & Chr$(13)");
    Assert(Encoding.UTF8.GetString(VbText.Encode("a\nb\r\nc", true)) == "a\r\nb\r\nc");
    Throws<EncoderFallbackException>(() => VbText.Encode("☃"));
  }

  private static void DetectAssets()
  {
    Assert(AssetExtractor.DetectExtension(new byte[] { 0, 0, 1, 0 }) == "ico");
    Assert(AssetExtractor.DetectExtension(Encoding.ASCII.GetBytes("BMtest")) == "bmp");
    Assert(AssetExtractor.DetectExtension(Encoding.ASCII.GetBytes("RIFF1234WAVE")) == "wav");
    Assert(AssetExtractor.DetectExtension(Array.Empty<byte>()) == "bin");
  }

  private static void WithDirectory(Action<string, string, string> action)
  {
    string root = Path.Combine(Path.GetTempPath(), "deme-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string input = Path.Combine(root, "fixture.exe");
      File.WriteAllBytes(input, Fixture.Executable());
      action(root, input, Path.Combine(root, "recovered"));
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static void Export() => WithDirectory((_, input, output) =>
  {
    DecompileReport report = Decompiler.Decompile(input, output);
    Assert((report.Forms == 1) && (report.Procedures == 1));
    Assert(report.Warnings.Count == 0, string.Join("; ", report.Warnings));
    Assert(File.ReadAllText(Path.Combine(output, "MAIN.FRM")).Contains("Sub Command1_Click", StringComparison.Ordinal));
    Assert(File.Exists(Path.Combine(output, "fixture.MAK")));
    Assert(Directory.GetFiles(Path.Combine(output, "pcode")).Length == 2);
    DecompileReport decoded = JsonSerializer.Deserialize<DecompileReport>(File.ReadAllText(Path.Combine(output, "report.json")))!;
    Assert((decoded.Sha256.Length == 64) && decoded.Files.Contains("report.json"));
  });

  private static void ExistingOutput() => WithDirectory((_, input, output) =>
  {
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
    Throws<IOException>(() => Decompiler.Decompile(input, output));
    Assert(File.ReadAllText(Path.Combine(output, "keep.txt")) == "keep");
  });

  private static void RejectTraversal() => WithDirectory((root, input, output) =>
  {
    File.WriteAllBytes(input, Fixture.Executable("../EVIL.FRM"));
    Throws<InvalidDataException>(() => Decompiler.Decompile(input, output));
    Assert(!File.Exists(Path.Combine(root, "EVIL.FRM")) && !Directory.Exists(output));
  });

  private static void Cancel() => WithDirectory((_, input, output) =>
  {
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => Decompiler.Decompile(input, output, cancellationToken: cancellation.Token));
    Assert(!Directory.Exists(output));
  });

  private static void OutputOptions() => WithDirectory((_, input, output) =>
  {
    DecompileReport report = Decompiler.Decompile(input, output, new DecompileOptions { Code = false, RawResources = true, Utf8 = true });
    Assert(report.TextEncoding == "utf-8");
    Assert(!Directory.Exists(Path.Combine(output, "pcode")));
    Assert(Directory.GetFiles(Path.Combine(output, "resources")).Length == 4);
  });

  private static string DecompileBytecode(byte[] bytes)
  {
    var code = Code(Fixture.Executable(bytecode: bytes));
    var decompiler = new BasicDecompiler(code.Code, code.Module, code.Procedure);
    string source = decompiler.Decompile();
    Assert(decompiler.Warnings.Count == 0, string.Join("; ", decompiler.Warnings));
    return source;
  }

  private static void StringAndDialog()
  {
    byte[] literal = Fixture.StringLiteral("Café");
    PcodeInstruction instruction = PcodeReader.Read(literal).Single();
    Assert(VbText.Decode(instruction.StringData!) == "Café");
    byte[] bytecode = literal.Concat(Fixture.Bytecode(("MsgBox", Array.Empty<ushort>()), ("eos", Array.Empty<ushort>()))).ToArray();
    Assert(DecompileBytecode(bytecode).Contains("MsgBox \"Café\"", StringComparison.Ordinal));
    Fixture.Word(literal, 6, 0x7fff);
    Throws<InvalidDataException>(() => PcodeReader.Read(literal));
  }

  private static void ControlFlow()
  {
    byte[] bytecode = Fixture.Bytecode(("True", Array.Empty<ushort>()), ("If ", new ushort[] { 0 }), ("nl", Array.Empty<ushort>()), ("Beep", Array.Empty<ushort>()), ("nl", Array.Empty<ushort>()), ("Else", new ushort[] { 0 }), ("DoEvents", Array.Empty<ushort>()), ("End If", Array.Empty<ushort>()), ("c%", new ushort[] { 3 }), ("Select Case", new ushort[] { 0 }), ("c%", new ushort[] { 3 }), ("Case ", Array.Empty<ushort>()), ("Beep", Array.Empty<ushort>()), ("Case Else", Array.Empty<ushort>()), ("DoEvents", Array.Empty<ushort>()), ("End Select", Array.Empty<ushort>()), ("eos", Array.Empty<ushort>()));
    string source = DecompileBytecode(bytecode);
    Assert(source.Contains("If True Then", StringComparison.Ordinal));
    Assert(source.Contains("Select Case 3", StringComparison.Ordinal));
    Assert(source.Contains("Case Else", StringComparison.Ordinal));
    Assert(source.Contains("End Select", StringComparison.Ordinal));
  }

  private static void FileStatements()
  {
    byte[] tail = Fixture.Bytecode(("c%", new ushort[] { 1 }), ("#", Array.Empty<ushort>()), ("Open ", new ushort[] { 1 }), ("c%", new ushort[] { 1 }), ("#", Array.Empty<ushort>()), ("Close", new ushort[] { 1 }), ("eos", Array.Empty<ushort>()));
    string source = DecompileBytecode(Fixture.StringLiteral("data.txt").Concat(tail).ToArray());
    Assert(source.Contains("Open \"data.txt\" For Input As #1", StringComparison.Ordinal));
    Assert(source.Contains("Close #1", StringComparison.Ordinal));
  }

  private static void PictureAssets() => WithDirectory((_, input, output) =>
  {
    using Stream tableStream = typeof(NeExecutable).Assembly.GetManifestResourceStream("DeME.Core.Tables.Models.json")!;
    using JsonDocument tables = JsonDocument.Parse(tableStream);
    JsonElement properties = tables.RootElement.GetProperty("Form").GetProperty("Properties");
    int pictureIndex = properties.EnumerateArray().Select((property, index) => (Property: property, Index: index)).First(item => item.Property.GetProperty("Name").GetString() == "Picture").Index;
    byte[] picture = { 0x42, 0x4d, 1, 2, 3, 4 };
    byte[] payload = new byte[] { 0xff, (byte)pictureIndex, 6, 0, 0, 0 }.Concat(picture).Concat(new byte[] { 0xff, 0 }).ToArray();
    byte[] form = new byte[] { 0xff, 0xcc, 0x2c, 0, 0, 0, 0, 0, 0 }.Concat(Fixture.Record(0, 0, 13, payload)).Append((byte)4).ToArray();
    byte[] executable = Fixture.Executable();
    Array.Clear(executable, 3584, 256);
    form.CopyTo(executable, 3584);
    File.WriteAllBytes(input, executable);
    Decompiler.Decompile(input, output, new DecompileOptions { Code = false });
    byte[] frx = File.ReadAllBytes(Path.Combine(output, "MAIN.FRX"));
    Assert(frx.AsSpan(4).SequenceEqual(picture));
    Assert(File.ReadAllBytes(Directory.GetFiles(Path.Combine(output, "assets")).Single()).SequenceEqual(picture));
  });

  private static void FailedRecovery() => WithDirectory((_, input, output) =>
  {
    byte[] executable = Fixture.Executable();
    executable[3584] = 0;
    Fixture.Word(executable, 1024 + 128 + 26, 0x80);
    File.WriteAllBytes(input, executable);
    DecompileReport report = Decompiler.Decompile(input, output);
    Assert(report.Warnings.Count >= 2);
    Assert(File.Exists(Path.Combine(output, "raw", "form_4.bin")));
    Assert(File.Exists(Path.Combine(output, "raw", "segment_3.bin")));
  });

  private static void MissingCustomControls() => WithDirectory((_, input, output) =>
  {
    byte[] executable = Fixture.Executable();
    int position = 3072 + 19 + 11 + "MAIN.FRM".Length + 2;
    executable[position] = 0x43;
    executable[position + 1] = 8;
    Encoding.ASCII.GetBytes("TEST.VBX").CopyTo(executable, position + 11);
    File.WriteAllBytes(input, executable);
    DecompileReport report = Decompiler.Decompile(input, output);
    Assert(report.Warnings.Any(warning => warning.Contains("TEST.VBX is missing", StringComparison.Ordinal)));
  });

  private static void GlobalVariables() => WithDirectory((_, input, output) =>
  {
    byte[] bytecode = Fixture.Bytecode(("c%", new ushort[] { 42 }), ("var=", new ushort[] { 0x40 }), ("eos", Array.Empty<ushort>()));
    Fixture.Word(bytecode, 4, 11602);
    byte[] executable = Fixture.Executable(bytecode: bytecode);
    Fixture.Word(executable, 3328 + 0x6e + 0x40, 0x24);
    File.WriteAllBytes(input, executable);
    Decompiler.Decompile(input, output);
    Assert(File.ReadAllText(Path.Combine(output, "Globals.bas")).Contains("Global gv0024 As Integer", StringComparison.Ordinal));
    Assert(File.ReadAllText(Path.Combine(output, "MAIN.FRM")).Contains("gv0024 = 42", StringComparison.Ordinal));
    Assert(File.ReadAllText(Path.Combine(output, "fixture.MAK")).Contains("Globals.bas", StringComparison.Ordinal));
  });

  private static void CustomControlModels() => WithDirectory((root, input, output) =>
  {
    byte[] library = Fixture.Executable();
    Fixture.Word(library, 200, 160);
    Fixture.Word(library, 202, 256);
    Fixture.Word(library, 2560 + 32, 0x300);
    Fixture.Word(library, 2560 + 32 + 0x14, 128);
    Fixture.Word(library, 2560 + 32 + 0x16, 144);
    Fixture.Word(library, 2560 + 32 + 0x1a, 96);
    Encoding.ASCII.GetBytes("Widget").CopyTo(library, 2560 + 128);
    Encoding.ASCII.GetBytes("Widget").CopyTo(library, 2560 + 144);
    Fixture.Word(library, 2560 + 96, 160);
    Fixture.Word(library, 2560 + 160, 176);
    Fixture.DWord(library, 2560 + 162, 2);
    Encoding.ASCII.GetBytes("Value").CopyTo(library, 2560 + 176);
    File.WriteAllBytes(Path.Combine(root, "TEST.VBX"), library);
    byte[] executable = Fixture.Executable();
    int position = 3072 + 19 + 11 + "MAIN.FRM".Length + 2;
    executable[position] = 0x43;
    executable[position + 1] = 8;
    Encoding.ASCII.GetBytes("TEST.VBX").CopyTo(executable, position + 11);
    byte[] control = Fixture.Record(1, 1, 0xff, 6, 87, 105, 100, 103, 101, 116, 0xff, 0, 123, 0, 0xff, 0);
    Array.Clear(executable, 3584, 256);
    Fixture.Form(control, new byte[] { 2 }).CopyTo(executable, 3584);
    File.WriteAllBytes(input, executable);
    DecompileReport report = Decompiler.Decompile(input, output, new DecompileOptions { Code = false });
    Assert(report.Warnings.Count == 0, string.Join("; ", report.Warnings));
    string text = File.ReadAllText(Path.Combine(output, "MAIN.FRM"));
    Assert(text.Contains("Begin Widget Command1", StringComparison.Ordinal));
    Assert(text.Contains("= 123", StringComparison.Ordinal));
  });

  private static void Assert(bool condition, string? message = null)
  {
    if (!condition)
      throw new InvalidOperationException(message ?? "Assertion failed.");
  }

  private static void Throws<T>(Action action)
    where T : Exception
  {
    try
    {
      action();
    }
    catch (T)
    {
      return;
    }

    throw new InvalidOperationException("Expected " + typeof(T).Name);
  }
}
