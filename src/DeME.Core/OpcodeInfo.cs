namespace DeME.Core;

public sealed record OpcodeInfo
{
  public OpcodeInfo(int tokenID, string keyword, int @case, int numParams, int flags, int bitForward2)
  {
    TokenID = tokenID;
    Keyword = keyword;
    Case = @case;
    NumParams = numParams;
    Flags = flags;
    BitForward2 = bitForward2;
  }

  public int TokenID { get; }

  public string Keyword { get; }

  public int Case { get; }

  public int NumParams { get; }

  public int Flags { get; }

  public int BitForward2 { get; }
}
