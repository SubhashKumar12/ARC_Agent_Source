namespace ARC.Data.Odos;

/// <summary>
/// Row from ODOS.usp_GetLegalDocumentsODOS (documented columns).
/// </summary>
internal sealed class OdosLegalDocumentRow
{
    public int? ldoc_id { get; set; }
    public long? ldoc_legal_id { get; set; }
    public string? ldoc_category_code { get; set; }
    public string? ldoc_file_name { get; set; }
    public DateTime? ldoc_generated_date { get; set; }
}
