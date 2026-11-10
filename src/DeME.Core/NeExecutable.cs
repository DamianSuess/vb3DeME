namespace DeME.Core;

/// <summary>Reads Windows 16-bit NE containers without loading or executing them.</summary>
public sealed class NeExecutable
{
  private readonly BinaryData _data;

  private NeExecutable(BinaryData data, int headerOffset, ushort entrySegment, IReadOnlyList<NeSegment> segments, IReadOnlyList<NeResource> resources, IReadOnlyList<string> imports)
  {
    _data = data;
    HeaderOffset = headerOffset;
    EntrySegment = entrySegment;
    Segments = segments;
    Resources = resources;
    Imports = imports;
  }

  public int HeaderOffset { get; }

  public ushort EntrySegment { get; }

  public IReadOnlyList<NeSegment> Segments { get; }

  public IReadOnlyList<NeResource> Resources { get; }

  public IReadOnlyList<string> Imports { get; }

  public bool IsVisualBasic3 => Imports.Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), "VBRUN300", StringComparison.OrdinalIgnoreCase));

  internal BinaryData Data => _data;

  public static NeExecutable Open(string path) => Parse(File.ReadAllBytes(path));

  public static NeExecutable Parse(byte[] bytes)
  {
    var data = new BinaryData((byte[])bytes.Clone());
    data.Span(0, 64);
    if (data.Word(0) != 0x5a4d)
      throw new InvalidDataException("Expected a DOS MZ executable.");

    uint rawHeader = data.DWord(0x3c);
    if (rawHeader > int.MaxValue)
      throw new InvalidDataException("NE header is outside the file.");

    int header = (int)rawHeader;
    data.Span(header, 64);
    if (data.Word(header) != 0x454e)
      throw new InvalidDataException("Expected a 16-bit Windows NE executable. PE/VB4/VB5/VB6 executables are unsupported.");

    int shift = data.Word(header + 0x32);
    if (shift == 0)
      shift = 9;

    data.Scaled(0, shift);
    int segmentTable = header + data.Word(header + 0x22);
    int count = data.Word(header + 0x1c);
    data.Span(segmentTable, count * 8);
    var segments = new List<NeSegment>();
    for (int i = 0; i < count; i++)
    {
      int position = segmentTable + (i * 8);
      ushort sector = data.Word(position);
      int offset = data.Scaled(sector, shift);
      int length = sector == 0 ? 0 : data.Word(position + 2) == 0 ? 65536 : data.Word(position + 2);
      data.Span(offset, length);
      int allocation = data.Word(position + 6);
      segments.Add(new NeSegment(i + 1, offset, length, data.Word(position + 4), allocation == 0 ? 65536 : allocation));
    }

    var resources = new List<NeResource>();
    int table = header + data.Word(header + 0x24);
    int tableEnd = header + data.Word(header + 0x26);
    if (table != tableEnd)
    {
      if (tableEnd < table + 2)
        throw new InvalidDataException("Resource table ends before it starts.");

      data.Span(table, tableEnd - table);
      int resourceShift = data.Word(table);
      data.Scaled(0, resourceShift);
      int cursor = table + 2;
      while (true)
      {
        RequireTable(cursor, 2, tableEnd);
        ushort type = data.Word(cursor);
        if (type == 0)
          break;

        RequireTable(cursor, 8, tableEnd);
        int resourceCount = data.Word(cursor + 2);
        cursor += 8;
        string typeName = (type & 0x8000) != 0 ? $"RT_{type & 0x7fff}" : ReadTableName(data, table, type, tableEnd);
        for (int i = 0; i < resourceCount; i++)
        {
          RequireTable(cursor, 12, tableEnd);
          ushort rawOffset = data.Word(cursor);
          int offset = data.Scaled(rawOffset, resourceShift);
          int length = rawOffset == 0 ? 0 : data.Scaled(data.Word(cursor + 2), resourceShift);
          data.Span(offset, length);
          ushort id = data.Word(cursor + 6);
          string? name = (id & 0x8000) == 0 ? ReadTableName(data, table, id, tableEnd) : null;
          resources.Add(new NeResource(type, typeName, (ushort)(id & 0x7fff), name, offset, length, data.Word(cursor + 4)));
          cursor += 12;
        }
      }
    }

    var imports = new List<string>();
    int references = header + data.Word(header + 0x28);
    int names = header + data.Word(header + 0x2a);
    for (int i = 0; i < data.Word(header + 0x1e); i++)
      imports.Add(data.Pascal(names + data.Word(references + (i * 2))));

    return new NeExecutable(data, header, data.Word(header + 0x16), segments.AsReadOnly(), resources.AsReadOnly(), imports.AsReadOnly());
  }

  public byte[] ReadSegment(NeSegment segment) => _data.Bytes(segment.Offset, segment.Length);

  public byte[] ReadResource(NeResource resource) => _data.Bytes(resource.Offset, resource.Length);

  public NeResource? FindResource(ushort type, ushort id) => Resources.FirstOrDefault(resource => (resource.Type == (type | 0x8000)) && (resource.Id == id) && (resource.Name is null));

  private static void RequireTable(int offset, int length, int end)
  {
    if ((offset > end - length) || (offset < 0))
      throw new InvalidDataException("Truncated NE resource table.");
  }

  private static string ReadTableName(BinaryData data, int start, int offset, int end)
  {
    RequireTable(start + offset, 1, end);
    RequireTable(start + offset + 1, data.Byte(start + offset), end);
    return data.Pascal(start + offset);
  }
}
