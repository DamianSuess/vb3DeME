using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeME.Core;

public static class Decompiler
{
  public static DecompileReport Decompile(string inputFile, string outputDirectory, DecompileOptions? options = null, CancellationToken cancellationToken = default)
  {
    options ??= new DecompileOptions();
    if (!options.Forms && !options.Code && !options.RawResources)
      throw new ArgumentException("Enable forms, code, or raw resource extraction.", nameof(options));

    string input = Path.GetFullPath(inputFile);
    string output = Path.GetFullPath(outputDirectory);
    if (Directory.Exists(output) || File.Exists(output))
      throw new IOException("The output path already exists. Choose a new directory to preserve existing files.");

    byte[] original = File.ReadAllBytes(input);
    NeExecutable executable = NeExecutable.Parse(original);
    VbProject project = VbProject.Read(executable);
    var warnings = new List<string>();
    var forms = new List<DecodedForm>();
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    string inputDirectory = Path.GetDirectoryName(input)!;
    Dictionary<string, ControlModel> custom = CustomControlReader.Read(inputDirectory, project.CustomControls, warnings);
    foreach (FormReference reference in project.Forms)
    {
      cancellationToken.ThrowIfCancellationRequested();
      OutputNames.FileName(reference.FileName);
      if (!reference.FileName.EndsWith(".frm", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Expected a .FRM filename, found {reference.FileName}.");

      NeResource resource = executable.FindResource(10, reference.ResourceId) ?? throw new InvalidDataException($"Form resource {reference.ResourceId} is missing.");
      byte[] raw = executable.ReadResource(resource);
      try
      {
        DecodedForm form = FormDecoder.Decode(raw, reference, custom);
        foreach (ControlNode node in form.Controls.Prepend(form.Root))
        {
          if (!OutputNames.IsIdentifier(node.Name) || !OutputNames.IsIdentifier(node.TypeName))
            throw new InvalidDataException("Invalid form/control identifier in executable.");
        }

        forms.Add(form);
        warnings.AddRange(form.Warnings.Select(message => reference.FileName + ": " + message));
        if (options.Forms)
        {
          Add(files, reference.FileName, VbText.Encode(form.ToFormText(), options.Utf8));
          if (form.Frx.Length > 0)
            Add(files, Path.ChangeExtension(reference.FileName, ".FRX"), form.Frx);

          if (options.Assets)
          {
            foreach (var asset in AssetExtractor.Extract(form))
              Add(files, "assets/" + asset.Name, asset.Data);
          }
        }
      }
      catch (InvalidDataException error)
      {
        warnings.Add($"{reference.FileName}: form recovery failed: {error.Message}");
        Add(files, $"raw/form_{reference.ResourceId}.bin", raw);
      }
    }

    CodeProject? code = null;
    if (options.Code)
    {
      try
      {
        code = CodeProject.Read(executable, project, forms);
        warnings.AddRange(code.Warnings);
        if (code.GlobalDeclarations.Count > 0)
          Add(files, "Globals.bas", VbText.Encode("Option Explicit\n\n" + string.Join("\n", code.GlobalDeclarations) + "\n", options.Utf8));

        foreach (VbModule module in code.Modules)
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (!OutputNames.IsIdentifier(module.Name))
            throw new InvalidDataException("Invalid module identifier.");

          var text = new StringBuilder("\r\nOption Explicit\r\n\r\n");
          foreach (string declaration in module.Declarations)
            text.AppendLine(declaration);

          foreach (VbProcedure procedure in module.Procedures.OrderBy(item => item.IsLocal))
          {
            var decompiler = new BasicDecompiler(code, module, procedure);
            text.AppendLine(decompiler.Decompile());
            warnings.AddRange(decompiler.Warnings);
            if (procedure.IsLocal)
            {
              string basename = $"pcode/{module.Name}_{procedure.Pointer:X4}";
              Add(files, basename + ".pcode.bin", procedure.Bytecode);
              Add(files, basename + ".pcode.txt", Encoding.UTF8.GetBytes(decompiler.Listing()));
            }
          }

          byte[] bytes = VbText.Encode(text.ToString(), options.Utf8);
          if ((module.Form is not null) && files.TryGetValue(module.Form.FileName, out byte[]? formText))
            files[module.Form.FileName] = formText.Concat(bytes).ToArray();
          else
            Add(files, module.Name + ".bas", bytes);
        }
      }
      catch (InvalidDataException error)
      {
        warnings.Add("Code recovery failed: " + error.Message);
        foreach (NeSegment segment in executable.Segments.Where(item => item.Length > 0))
          Add(files, $"raw/segment_{segment.Index}.bin", executable.ReadSegment(segment));
      }
    }

    if (options.RawResources)
    {
      foreach (NeResource resource in executable.Resources.Where(item => item.Length > 0))
        Add(files, $"resources/{resource.Type:X4}_{resource.Id:X4}_{resource.Offset:X8}.bin", executable.ReadResource(resource));
    }

    if (options.CopyDependencies)
    {
      foreach (string dependency in Directory.EnumerateFiles(inputDirectory).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".dll" or ".vbx"))
        Add(files, Path.GetFileName(dependency), File.ReadAllBytes(dependency));
    }

    string projectFile = OutputNames.FileName(Path.GetFileNameWithoutExtension(input) + ".MAK");
    var mak = new StringBuilder();
    foreach (string filename in files.Keys.Where(name => !name.Contains('/') && name.EndsWith(".frm", StringComparison.OrdinalIgnoreCase)))
      mak.AppendLine(filename);

    foreach (string filename in files.Keys.Where(name => !name.Contains('/') && name.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)))
      mak.AppendLine(filename);

    foreach (string control in project.CustomControls)
      mak.AppendLine(OutputNames.FileName(control));

    mak.AppendLine("Title=" + VbText.Quote(project.Title));
    string? startup = project.Forms.ElementAtOrDefault(project.StartupIndex)?.Name;
    if (project.StartupIndex < 0)
      warnings.Add("The Sub Main entry point is not yet identified; set Startup in the VB3 IDE before running recovered code.");
    else if (startup is not null)
      mak.AppendLine("IconForm=" + VbText.Quote(startup));

    mak.AppendLine("ExeName=" + VbText.Quote(Path.GetFileName(input)));
    mak.AppendLine("ProjWinSize=152,402,248,215");
    mak.AppendLine("ProjWinShow=1");

    Add(files, projectFile, VbText.Encode(mak.ToString(), options.Utf8));
    string hash = Convert.ToHexString(SHA256.HashData(original));
    var report = new DecompileReport(input, hash, project.Title, forms.Count, forms.Sum(form => form.Controls.Count), code?.Modules.Count ?? 0, code?.Procedures.Count ?? 0, options.Utf8 ? "utf-8" : "windows-1252", files.Keys.Append("report.json").OrderBy(name => name, StringComparer.Ordinal).ToArray(), warnings.AsReadOnly());
    Add(files, "report.json", JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
    WriteOutput(files, output, cancellationToken);
    return report;
  }

  private static void Add(Dictionary<string, byte[]> files, string name, byte[] bytes)
  {
    foreach (string part in name.Split('/'))
      OutputNames.FileName(part);

    if (!files.TryAdd(name, bytes))
      throw new InvalidDataException($"Duplicate output filename: {name}");
  }

  private static void WriteOutput(Dictionary<string, byte[]> files, string output, CancellationToken cancellationToken)
  {
    string parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output must have a parent directory.", nameof(output));
    Directory.CreateDirectory(parent);
    string staging = Path.Combine(parent, ".deme-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(staging);
    try
    {
      foreach (var file in files)
      {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(staging, file.Key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, file.Value);
      }

      cancellationToken.ThrowIfCancellationRequested();
      Directory.Move(staging, output);
    }
    finally
    {
      if (Directory.Exists(staging))
        Directory.Delete(staging, true);
    }
  }
}
