using ARC.Knowledge.Chunking;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Vector;

namespace ARC.Eval.Retrieval.Offline;

/// <summary>
/// Synthetic knowledge fixtures for Stage 2 retrieval evaluation only.
/// NOT real NI Act / legal policy. Marked clearly for test use.
/// </summary>
public static class SyntheticKnowledgeCorpus
{
    public const string NoticePolicySource = "SYN-POLICY-NOTICE";
    public const string RecoverPolicySource = "SYN-POLICY-RECOVER";
    public const string TemplateSource = "SYN-TEMPLATE-DEMAND";
    public const string VersionCurrent = "current";
    public const string VersionLegacy = "legacy";

    public static IReadOnlyList<IndexedDocument> CreateDocuments()
    {
        var docs = new List<IndexedDocument>();

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-1",
            "Response Window",
            "Dealer must respond within the prescribed notice period of fourteen days after service of demand.");

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-2",
            "Credit Note Hold",
            "When unapplied credit notes exceed forty percent of gross AR the notice must be held for finance reconcile.");

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-3",
            "Dispute Under Review",
            "Open disputes under formal review require Hold rather than Issue until the dispute closes.");

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "WEST", null, "CLAUSE-4",
            "West Region Escalation",
            "WEST region escalations route demand notices through the west depot manager gate.");

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "EAST", null, "CLAUSE-5",
            "East Region Escalation",
            "EAST region escalations route demand notices through the east depot manager gate.");

        // Legacy version — historical only; must not leak when published version is current.
        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionLegacy, "GLOBAL", null, "CLAUSE-1",
            "Response Window Legacy",
            "Dealer must respond within the prescribed notice period of seven days after service of demand.");

        Add(docs, NoticePolicySource, "Policy", "NoticePolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-99",
            "Retired Moratorium Guidance",
            "Historical insolvency moratorium text retained for audit only.",
            status: "INACTIVE");

        Add(docs, RecoverPolicySource, "Policy", "RecoveryPolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-1",
            "Visit Planning",
            "Field visits are scheduled after advocate signature when the recoverability score is in the notice tier.");

        Add(docs, RecoverPolicySource, "Policy", "RecoveryPolicy", VersionCurrent, "GLOBAL", "dealer:west-1", "CLAUSE-2",
            "Dealer Specific Visit Cap",
            "Dealer dealer:west-1 has a maximum of two field visits per recovery cycle.");

        Add(docs, TemplateSource, "Template", "NoticeTemplate", VersionCurrent, "GLOBAL", null, "SECTION-DEMAND",
            "Demand Heading",
            "HEADING: Final Demand\nDEMAND: Payment of the stated net exposure is required within fourteen days.\nCLOSING: Regards, PaintCo Collections.");

        Add(docs, TemplateSource, "Template", "NoticeTemplate", VersionCurrent, "GLOBAL", null, "SECTION-CLOSING",
            "Closing Block",
            "CLOSING: This demand is issued without prejudice to further recovery remedies available to PaintCo.");

        Add(docs, "SYN-UNRELATED", "Policy", "NoticePolicy", VersionCurrent, "GLOBAL", null, "CLAUSE-X",
            "Paint Mixing Temperature",
            "Warehouse paint mixing temperature must remain between eighteen and twenty-two degrees Celsius.");

        return docs;
    }

    private static void Add(
        List<IndexedDocument> docs,
        string source,
        string documentType,
        string category,
        string version,
        string region,
        string? dealerUrn,
        string section,
        string title,
        string content,
        string status = "ACTIVE")
    {
        var embedding = BagOfWordsEmbeddingProvider.EmbedText(content + " " + title);
        docs.Add(new IndexedDocument
        {
            id = IndexedDocumentId.Create(source, version, section),
            documentType = documentType,
            documentCategory = category,
            status = status,
            version = version,
            regionScope = new[] { region },
            dealerUrn = dealerUrn,
            sourceDocumentId = source,
            blobLocation = $"synthetic/{source}/{version}/{section}.txt",
            pageOrSection = section,
            title = title,
            content = content,
            contentHash = ContentHasher.ComputeHash(content),
            embedding = embedding,
            embeddingModel = "eval-bag-of-words:64d",
            embeddedUtc = DateTimeOffset.UtcNow
        });
    }
}
