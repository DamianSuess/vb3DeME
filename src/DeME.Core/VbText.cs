using System.Globalization;
using System.Text;

namespace DeME.Core;

public static class VbText
{
  private static readonly Encoding WindowsEncoding = CreateEncoding();

  public static string Decode(ReadOnlySpan<byte> bytes) => WindowsEncoding.GetString(bytes);

  public static byte[] Encode(string text, bool utf8 = false)
  {
    text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);
    return (utf8 ? new UTF8Encoding(false, true) : WindowsEncoding).GetBytes(text);
  }

  public static string Quote(string value)
  {
    var parts = new List<string>();
    var literal = new StringBuilder();
    foreach (char ch in value)
    {
      if ((ch >= ' ') && (ch != '\u007f'))
      {
        literal.Append(ch == '"' ? "\"\"" : ch);
        continue;
      }

      if (literal.Length > 0)
      {
        parts.Add("\"" + literal + "\"");
        literal.Clear();
      }

      parts.Add($"Chr$({(int)ch})");
    }

    if ((literal.Length > 0) || (parts.Count == 0))
      parts.Add("\"" + literal + "\"");

    return string.Join(" & ", parts);
  }

  internal static string Number(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";

  private static Encoding CreateEncoding()
  {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
  }
}
