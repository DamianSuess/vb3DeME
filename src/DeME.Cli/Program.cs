// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using DeME.Core;

namespace DeME.Cli;

internal static class Program
{
  private static void Help()
  {
    Console.WriteLine("DeME - Visual Basic 3 reverse engineering (100% managed C# 10)");
    Console.WriteLine("  deme inspect <program.exe> [--json]");
    Console.WriteLine("  deme decompile <program.exe> [options]");
    Console.WriteLine("  --out, -o <directory>  New output directory (default: <name>_decompiled)");
    Console.WriteLine("  --no-forms            Omit .FRM/.FRX output");
    Console.WriteLine("  --no-code             Omit source and P-code output");
    Console.WriteLine("  --no-assets           Omit standalone media extraction");
    Console.WriteLine("  --raw                 Export original NE resource payloads");
    Console.WriteLine("  --utf8                Write modern UTF-8 text; default is VB3 Windows-1252");
    Console.WriteLine("  --copy-dependencies   Copy companion DLL/VBX files from the input directory");
    Console.WriteLine("  --strict              Exit 3 on recovery warnings (outputs are retained)");
    Console.WriteLine("  --help | --version");
  }

  private static int Main(string[] args)
  {
    if ((args.Length == 0) || args.Contains("--help") || args.Contains("-h"))
    {
      Help();
      return 0;
    }

    if ((args.Length == 1) && (args[0] == "--version"))
    {
      Console.WriteLine("DeME 1.0.0 (C# 10; vb3dec reference af94edf)");
      return 0;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
      eventArgs.Cancel = true;
      cancellation.Cancel();
    };

    try
    {
      if ((args[0] == "inspect") && (args.Length is 2 or 3) && ((args.Length == 2) || (args[2] == "--json")))
      {
        NeExecutable executable = NeExecutable.Open(args[1]);
        VbProject? project = executable.IsVisualBasic3 ? VbProject.Read(executable) : null;
        if (args.Length == 3)
        {
          Console.WriteLine(JsonSerializer.Serialize(new { executable.HeaderOffset, executable.EntrySegment, executable.IsVisualBasic3, executable.Imports, executable.Segments, executable.Resources, Project = project }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"16-bit NE: {Path.GetFullPath(args[1])}");
          Console.WriteLine($"VB3: {executable.IsVisualBasic3}; {executable.Segments.Count} segments; {executable.Resources.Count} resources");
          Console.WriteLine("Imports: " + string.Join(", ", executable.Imports));

          if (project is not null)
            Console.WriteLine($"Project: {project.Title}; {project.Forms.Count} forms; names stripped: {project.NamesStripped}");
        }

        return 0;
      }

      if ((args[0] != "decompile") || (args.Length < 2))
        throw new ArgumentException("Use inspect <exe> or decompile <exe>. See --help.");

      string input = args[1];
      string output = Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(input) + "_decompiled");
      var options = new DecompileOptions();
      bool strict = false;
      for (int i = 2; i < args.Length; i++)
      {
        switch (args[i])
        {
          case "--out":
          case "-o":
            if (++i >= args.Length)
              throw new ArgumentException("--out requires a directory.");

            output = args[i];
            break;

          case "--no-forms":
            options = options with { Forms = false };
            break;

          case "--no-code":
            options = options with { Code = false };
            break;

          case "--no-assets":
            options = options with { Assets = false };
            break;

          case "--raw":
            options = options with { RawResources = true };
            break;

          case "--utf8":
            options = options with { Utf8 = true };
            break;

          case "--copy-dependencies":
            options = options with { CopyDependencies = true };
            break;

          case "--strict":
            strict = true;
            break;

          default:
            throw new ArgumentException($"Unknown option: {args[i]}");
        }
      }

      DecompileReport report = Decompiler.Decompile(input, output, options, cancellation.Token);
      Console.WriteLine($"Recovered {report.Title}: {report.Forms} forms, {report.Controls} controls, {report.Modules} modules, {report.Procedures} procedures.");
      Console.WriteLine($"Wrote {report.Files.Count} files to {Path.GetFullPath(output)}");
      foreach (string warning in report.Warnings)
        Console.Error.WriteLine("Warning: " + warning);

      if (report.HasWarnings)
        Console.Error.WriteLine("Recovery needs review. See report.json and the P-code listings.");

      return strict && report.HasWarnings ? 3 : 0;
    }
    catch (OperationCanceledException)
    {
      Console.Error.WriteLine("Cancelled.");
      return 130;
    }
    catch (ArgumentException error)
    {
      Console.Error.WriteLine("Usage error: " + error.Message);
      return 2;
    }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException or System.Text.EncoderFallbackException)
    {
      Console.Error.WriteLine("Error: " + error.Message);
      return 1;
    }
  }
}
