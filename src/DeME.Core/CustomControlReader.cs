// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace DeME.Core;

internal static class CustomControlReader
{
  internal static Dictionary<string, ControlModel> Read(string directory, IEnumerable<string> libraries, List<string> warnings)
  {
    var result = new Dictionary<string, ControlModel>(StringComparer.OrdinalIgnoreCase);
    var versions = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
    foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      OutputNames.FileName(library);
      string? path = Directory.EnumerateFiles(directory).FirstOrDefault(file => string.Equals(Path.GetFileName(file), library, StringComparison.OrdinalIgnoreCase));
      if (path is null)
      {
        warnings.Add($"Custom control library {library} is missing; its properties cannot be decoded.");
        continue;
      }

      try
      {
        NeExecutable executable = NeExecutable.Open(path);
        foreach (NeSegment segment in executable.Segments.Where(item => item.IsData && (item.Length > 0)))
        {
          var data = new BinaryData(executable.ReadSegment(segment));
          for (int position = 0; position + 32 <= data.Length; position++)
          {
            ushort version = data.Word(position);
            if (version is not (0x100 or 0x200 or 0x300))
              continue;

            string? name = CString(data, data.Word(position + 0x14));
            string? className = CString(data, data.Word(position + 0x16));
            if ((name is null) || (className is null) || !OutputNames.IsIdentifier(name) || !OutputNames.IsIdentifier(className))
              continue;

            try
            {
              ControlProperty[] properties = Properties(data, data.Word(position + 0x1a));
              if (properties.Length == 0)
                continue;

              ControlEvent[] events = Events(data, data.Word(position + 0x1c));
              if (version >= versions.GetValueOrDefault(className))
              {
                versions[className] = version;
                result[className] = new ControlModel(name, properties, events);
              }
            }
            catch (InvalidDataException)
            {
              // A candidate structure can overlap ordinary data in a VBX.
            }
          }
        }
      }
      catch (InvalidDataException error)
      {
        warnings.Add($"Cannot read models in {library}: {error.Message}");
      }
    }

    return result;
  }

  private static ControlProperty[] Properties(BinaryData data, int offset)
  {
    var result = new List<ControlProperty>();
    for (int i = 0; i <= 255; i++)
    {
      ushort pointer = data.Word(offset + (i * 2));
      if (pointer == 0)
        return result.ToArray();

      int standard = 0xffff - pointer;
      if (standard < RuntimeTables.StandardProperties.Length)
      {
        result.Add(RuntimeTables.StandardProperties[standard]);
        continue;
      }

      string name = CString(data, data.Word(pointer)) ?? throw new InvalidDataException("Invalid property name.");
      result.Add(new ControlProperty(name, data.DWord(pointer + 2), -1));
    }

    throw new InvalidDataException("Unterminated VBX property list.");
  }

  private static ControlEvent[] Events(BinaryData data, int offset)
  {
    if (offset == 0)
      return Array.Empty<ControlEvent>();

    var result = new List<ControlEvent>();
    for (int i = 0; i <= 255; i++)
    {
      ushort pointer = data.Word(offset + (i * 2));
      if (pointer == 0)
        return result.ToArray();

      int standard = 0xffff - pointer;
      if (standard < RuntimeTables.StandardEvents.Length)
      {
        result.Add(RuntimeTables.StandardEvents[standard]);
        continue;
      }

      string name = CString(data, data.Word(pointer)) ?? throw new InvalidDataException("Invalid event name.");
      int parameters = data.Word(pointer + 2);
      if (!OutputNames.IsIdentifier(name) || (parameters > 16))
        throw new InvalidDataException("Invalid event definition.");

      string profile = CString(data, data.Word(pointer + 8)) ?? string.Empty;
      result.Add(new ControlEvent(name, parameters, profile.Contains(" As ", StringComparison.Ordinal) ? profile : string.Empty));
    }

    throw new InvalidDataException("Unterminated VBX event list.");
  }

  private static string? CString(BinaryData data, int offset)
  {
    if ((offset <= 0) || (offset >= data.Length))
      return null;

    for (int i = offset; (i < data.Length) && (i <= offset + 255); i++)
    {
      byte value = data.Byte(i);
      if (value == 0)
        return i == offset ? null : VbText.Decode(data.Span(offset, i - offset));

      if ((value < 32) || (value > 126))
        return null;
    }

    return null;
  }
}
