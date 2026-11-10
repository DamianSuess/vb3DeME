using System.Text;

namespace DeME.Core;

public sealed class DecodedForm
{
  public FormReference Reference { get; internal set; } = null!;

  public ControlNode Root { get; internal set; } = null!;

  public List<ControlNode> Controls { get; } = new();

  public byte[] Frx { get; internal set; } = Array.Empty<byte>();

  public List<string> Warnings { get; } = new();

  public string ToFormText()
  {
    var result = new StringBuilder("VERSION 2.00\r\n");
    WriteNode(result, Root, 0);
    return result.ToString();
  }

  private static void WriteNode(StringBuilder result, ControlNode node, int depth)
  {
    string indent = new(' ', depth * 3);
    result.AppendLine($"{indent}Begin {node.TypeName} {node.Name}");
    if (node.ArrayIndex.HasValue)
      result.AppendLine($"{indent}   Index = {node.ArrayIndex.Value}");

    foreach (var property in node.Properties.OrderBy(item => item.Key, StringComparer.Ordinal))
      result.AppendLine($"{indent}   {property.Key,-16}= {property.Value}");

    foreach (ControlNode child in node.Children)
      WriteNode(result, child, depth + 1);

    result.AppendLine($"{indent}End");
  }
}
