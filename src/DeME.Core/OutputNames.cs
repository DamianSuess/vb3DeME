namespace DeME.Core;

internal static class OutputNames
{
  internal static bool IsIdentifier(string name) => (name.Length > 0) && (char.IsAsciiLetter(name[0]) || (name[0] == '_')) && name.All(ch => char.IsAsciiLetterOrDigit(ch) || (ch == '_'));

  internal static string FileName(string name)
  {
    if (string.IsNullOrWhiteSpace(name) || name.Any(ch => (ch < 32) || "<>:\"/\\|?*".Contains(ch)) || name.EndsWith('.') || name.EndsWith(' ') || (name is "." or ".."))
      throw new InvalidDataException($"Unsafe output filename: {name}");

    string stem = name.Split('.')[0].ToUpperInvariant();
    if ((stem is "CON" or "PRN" or "AUX" or "NUL") || ((stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && (stem.Length == 4) && char.IsDigit(stem[3])))
      throw new InvalidDataException($"Reserved output filename: {name}");

    return name;
  }
}
