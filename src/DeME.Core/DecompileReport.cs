namespace DeME.Core;

public sealed record DecompileReport
{
  public DecompileReport(string inputFile, string sha256, string title, int forms, int controls, int modules, int procedures, string textEncoding, IReadOnlyList<string> files, IReadOnlyList<string> warnings)
  {
    InputFile = inputFile;
    Sha256 = sha256;
    Title = title;
    Forms = forms;
    Controls = controls;
    Modules = modules;
    Procedures = procedures;
    TextEncoding = textEncoding;
    Files = files;
    Warnings = warnings;
  }

  public string InputFile { get; }

  public string Sha256 { get; }

  public string Title { get; }

  public int Forms { get; }

  public int Controls { get; }

  public int Modules { get; }

  public int Procedures { get; }

  public string TextEncoding { get; }

  public IReadOnlyList<string> Files { get; }

  public IReadOnlyList<string> Warnings { get; }

  public bool HasWarnings => Warnings.Count > 0;
}
