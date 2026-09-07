using ARC.Data.Exceptions;

namespace ARC.Data.Odos;
/// Period and user scope required by ODOS.usp_GetDealerDetails and related SPs.
/// Values are configuration — not derived in repository code.
/// </summary>
public sealed class OdosSqlSessionOptions
{
    public const string SectionName = "ArcData:Odos:Session";

    public string CommtYear { get; set; } = "";
    public string CommtMonth { get; set; } = "";
    public string UserId { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CommtYear)
        && !string.IsNullOrWhiteSpace(CommtMonth)
        && !string.IsNullOrWhiteSpace(UserId);

    public void Validate()
    {
        if (!IsConfigured)
            throw new OdosSessionNotConfiguredException();
    }
}
