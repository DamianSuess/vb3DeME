using System.Text.Json;

namespace DeME.Core;

internal static class RuntimeTables
{
  internal static readonly OpcodeInfo[] Opcodes = Read<OpcodeInfo[]>("Opcodes");
  internal static readonly ushort[] FlagTokens = Read<ushort[]>("FlagTokens");
  internal static readonly byte[] ControlTokens = Read<int[]>("ControlTokens").Select(value => checked((byte)value)).ToArray();
  internal static readonly Dictionary<string, ControlModel> Models = Read<Dictionary<string, ControlModel>>("Models");
  internal static readonly ControlProperty[] StandardProperties = Read<ControlProperty[]>("StandardProperties");
  internal static readonly ControlEvent[] StandardEvents = Read<ControlEvent[]>("StandardEvents");

  internal static T Read<T>(string name)
  {
    using Stream stream = typeof(RuntimeTables).Assembly.GetManifestResourceStream($"DeME.Core.Tables.{name}.json")
      ?? throw new InvalidOperationException($"Missing embedded table {name}.");
    return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException($"Invalid runtime table {name}.");
  }
}
