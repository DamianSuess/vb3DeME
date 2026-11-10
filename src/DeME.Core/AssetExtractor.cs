namespace DeME.Core;

public static class AssetExtractor
{
  public static string DetectExtension(ReadOnlySpan<byte> data)
  {
    if ((data.Length >= 2) && (data[0] == 'B') && (data[1] == 'M'))
      return "bmp";

    if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
      return "png";

    if (data.StartsWith(new byte[] { 255, 216, 255 }))
      return "jpg";

    if ((data.Length >= 6) && (System.Text.Encoding.ASCII.GetString(data[..6]) == "GIF87a"))
      return "gif";

    if ((data.Length >= 6) && (System.Text.Encoding.ASCII.GetString(data[..6]) == "GIF89a"))
      return "gif";

    if ((data.Length >= 4) && (data[0] == 0) && (data[1] == 0) && (data[3] == 0))
    {
      if (data[2] == 1)
        return "ico";

      if (data[2] == 2)
        return "cur";
    }

    if (data.StartsWith(new byte[] { 0xd7, 0xcd, 0xc6, 0x9a }) || data.StartsWith(new byte[] { 1, 0, 9, 0 }))
      return "wmf";

    if ((data.Length >= 44) && data[..4].SequenceEqual(new byte[] { 1, 0, 0, 0 }) && (System.Text.Encoding.ASCII.GetString(data.Slice(40, 4)) is " EMF" or "EMF "))
      return "emf";

    if ((data.Length >= 12) && (System.Text.Encoding.ASCII.GetString(data[..4]) == "RIFF") && (System.Text.Encoding.ASCII.GetString(data.Slice(8, 4)) == "WAVE"))
      return "wav";

    if ((data.Length >= 4) && (System.Text.Encoding.ASCII.GetString(data[..4]) == "MThd"))
      return "mid";

    return "bin";
  }

  internal static IEnumerable<(string Name, byte[] Data)> Extract(DecodedForm form)
  {
    var data = new BinaryData(form.Frx);
    int position = 0;
    while (position < data.Length)
    {
      int offset = position;
      uint rawLength = data.DWord(position);
      if (rawLength > int.MaxValue)
        throw new InvalidDataException("FRX asset length exceeds supported range.");

      int length = (int)rawLength;
      position += 4;
      byte[] payload = data.Bytes(position, length);
      position += length;
      yield return ($"{Path.GetFileNameWithoutExtension(form.Reference.FileName)}_{offset:X4}.{DetectExtension(payload)}", payload);
    }
  }
}
