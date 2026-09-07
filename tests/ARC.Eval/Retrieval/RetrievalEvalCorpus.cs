using static ARC.Eval.Retrieval.Offline.SyntheticKnowledgeCorpus;

namespace ARC.Eval.Retrieval;

/// <summary>
/// Deterministic 40-case Stage 2 retrieval corpus over synthetic knowledge fixtures.
/// </summary>
public static class RetrievalEvalCorpus
{
    public static IReadOnlyList<RetrievalEvalCase> Create()
    {
        var noticeV2C1 = LogicalChunkKey.Create(NoticePolicySource, VersionCurrent, "CLAUSE-1");
        var noticeV2C2 = LogicalChunkKey.Create(NoticePolicySource, VersionCurrent, "CLAUSE-2");
        var noticeV2C3 = LogicalChunkKey.Create(NoticePolicySource, VersionCurrent, "CLAUSE-3");
        var noticeV2C4 = LogicalChunkKey.Create(NoticePolicySource, VersionCurrent, "CLAUSE-4");
        var noticeV2C5 = LogicalChunkKey.Create(NoticePolicySource, VersionCurrent, "CLAUSE-5");
        var recoverC1 = LogicalChunkKey.Create(RecoverPolicySource, VersionCurrent, "CLAUSE-1");
        var recoverDealer = LogicalChunkKey.Create(RecoverPolicySource, VersionCurrent, "CLAUSE-2");
        var templateDemand = LogicalChunkKey.Create(TemplateSource, VersionCurrent, "SECTION-DEMAND");
        var templateClosing = LogicalChunkKey.Create(TemplateSource, VersionCurrent, "SECTION-CLOSING");

        return new List<RetrievalEvalCase>
        {
            // A. Exact / identifier
            Case("E01", "CLAUSE-1", RetrievalEvalCategory.ExactIdentifier, [noticeV2C1],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent,
                Notes: "Exact clause id lexical"),
            Case("E02", "CLAUSE-2", RetrievalEvalCategory.ExactIdentifier, [noticeV2C2],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent),
            Case("E03", "CLAUSE-3", RetrievalEvalCategory.ExactIdentifier, [noticeV2C3],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent),
            Case("E04", "SECTION-DEMAND", RetrievalEvalCategory.ExactIdentifier, [templateDemand],
                ExpectedDocumentType: "Template", ExpectedDocumentCategory: "NoticeTemplate",
                ExpectedVersion: VersionCurrent),
            Case("E05", "Response Window", RetrievalEvalCategory.ExactIdentifier, [noticeV2C1],
                ExpectedVersion: VersionCurrent, Notes: "Exact clause heading"),
            Case("E06", "Credit Note Hold", RetrievalEvalCategory.ExactIdentifier, [noticeV2C2],
                ExpectedVersion: VersionCurrent, Notes: "Exact heading"),

            // B. Semantic paraphrase
            Case("S01", "What is the response time allowed after notice?", RetrievalEvalCategory.SemanticParaphrase,
                [noticeV2C1], ExpectedVersion: VersionCurrent),
            Case("S02", "When should a demand be delayed because credits dominate exposure?", RetrievalEvalCategory.SemanticParaphrase,
                [noticeV2C2], ExpectedVersion: VersionCurrent),
            Case("S03", "How do we treat an open dispute that is still being reviewed?", RetrievalEvalCategory.SemanticParaphrase,
                [noticeV2C3], ExpectedVersion: VersionCurrent),
            Case("S04", "When do we plan a field visit after signature?", RetrievalEvalCategory.SemanticParaphrase,
                [recoverC1], ExpectedDocumentCategory: "RecoveryPolicy", ExpectedVersion: VersionCurrent),
            Case("S05", "What closing language belongs on a demand letter?", RetrievalEvalCategory.SemanticParaphrase,
                [templateClosing, templateDemand], ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),

            // C. Hybrid (exact term + semantic intent)
            Case("H01", "fourteen days notice response window for the dealer", RetrievalEvalCategory.Hybrid,
                [noticeV2C1], ExpectedVersion: VersionCurrent),
            Case("H02", "forty percent credit note hold for finance reconcile", RetrievalEvalCategory.Hybrid,
                [noticeV2C2], ExpectedVersion: VersionCurrent),
            Case("H03", "Final Demand template payment within fourteen days", RetrievalEvalCategory.Hybrid,
                [templateDemand], ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),
            Case("H04", "recoverability score notice tier visit planning", RetrievalEvalCategory.Hybrid,
                [recoverC1], ExpectedDocumentCategory: "RecoveryPolicy", ExpectedVersion: VersionCurrent),

            // D. Policy / A3 support
            Case("P01", "prescribed notice period", RetrievalEvalCategory.Policy, [noticeV2C1],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent),
            Case("P02", "unapplied credit notes exceed forty percent", RetrievalEvalCategory.Policy, [noticeV2C2],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent),
            Case("P03", "dispute under formal review Hold rather than Issue", RetrievalEvalCategory.Policy, [noticeV2C3],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedVersion: VersionCurrent),
            Case("P04", "west depot manager gate", RetrievalEvalCategory.Policy, [noticeV2C4],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedRegion: "WEST", ExpectedVersion: VersionCurrent),
            Case("P05", "east depot manager gate", RetrievalEvalCategory.Policy, [noticeV2C5],
                ExpectedDocumentCategory: "NoticePolicy", ExpectedRegion: "EAST", ExpectedVersion: VersionCurrent),

            // E. Template / A5
            Case("T01", "HEADING: Final Demand", RetrievalEvalCategory.Template, [templateDemand],
                ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),
            Case("T02", "without prejudice to further recovery remedies", RetrievalEvalCategory.Template, [templateClosing],
                ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),
            Case("T03", "Payment of the stated net exposure is required", RetrievalEvalCategory.Template, [templateDemand],
                ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),
            Case("T04", "SECTION-CLOSING", RetrievalEvalCategory.Template, [templateClosing],
                ExpectedDocumentType: "Template", ExpectedVersion: VersionCurrent),

            // F. Supervisory / A8
            Case("A01", "Explain when notice issuance is held for finance reconcile", RetrievalEvalCategory.Supervisory,
                [noticeV2C2], ExpectedVersion: VersionCurrent),
            Case("A02", "Summarize the dealer response obligation after demand service", RetrievalEvalCategory.Supervisory,
                [noticeV2C1], ExpectedVersion: VersionCurrent),
            Case("A03", "Why would an open dispute block issuing a notice?", RetrievalEvalCategory.Supervisory,
                [noticeV2C3], ExpectedVersion: VersionCurrent),
            Case("A04", "When is a field visit scheduled in the recovery workflow?", RetrievalEvalCategory.Supervisory,
                [recoverC1], ExpectedVersion: VersionCurrent),

            // G. Metadata isolation
            Case("M01", "prescribed notice period of fourteen days", RetrievalEvalCategory.MetadataIsolation,
                [noticeV2C1], ExpectedVersion: VersionCurrent,
                Notes: "Must not return legacy seven-day clause"),
            Case("M02", "west depot manager gate", RetrievalEvalCategory.MetadataIsolation,
                [noticeV2C4], ExpectedRegion: "WEST", ExpectedVersion: VersionCurrent,
                Notes: "EAST-only clause must not leak"),
            Case("M03", "east depot manager gate", RetrievalEvalCategory.MetadataIsolation,
                [noticeV2C5], ExpectedRegion: "EAST", ExpectedVersion: VersionCurrent),
            Case("M04", "maximum of two field visits per recovery cycle", RetrievalEvalCategory.MetadataIsolation,
                [recoverDealer], ExpectedDealerUrn: "dealer:west-1", ExpectedVersion: VersionCurrent,
                Notes: "Dealer-scoped content"),
            Case("M05", "Historical insolvency moratorium text retained for audit only", RetrievalEvalCategory.MetadataIsolation,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent,
                Notes: "INACTIVE chunk must never appear"),
            Case("M06", "seven days after service of demand", RetrievalEvalCategory.MetadataIsolation,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent,
                Notes: "Legacy version text must not appear under current published version"),

            // H. Negative / no-answer
            Case("N01", "What is the statutory interest formula for bounced cheques?", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent),
            Case("N02", "Warehouse paint mixing temperature policy for resin batching", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent,
                Notes: "Unrelated synthetic content exists but should not satisfy notice/policy intent labels"),
            Case("N03", "How many directors must sign a Section 138 complaint?", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent),
            Case("N04", "What is the courier SLA for wet paint deliveries?", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent),
            Case("N05", "CLAUSE-404", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent),
            Case("N06", "bitcoin settlement instructions for dealers", RetrievalEvalCategory.Negative,
                [], ExpectNoRelevantHit: true, ExpectedVersion: VersionCurrent),
        };
    }

    private static RetrievalEvalCase Case(
        string id,
        string query,
        RetrievalEvalCategory category,
        IReadOnlyList<string> relevant,
        string? ExpectedDocumentType = null,
        string? ExpectedDocumentCategory = null,
        string? ExpectedVersion = null,
        string? ExpectedRegion = null,
        string? ExpectedDealerUrn = null,
        bool ExpectNoRelevantHit = false,
        string Notes = "")
        => new(
            id,
            query,
            category,
            relevant,
            ExpectedDocumentType,
            ExpectedDocumentCategory,
            ExpectedVersion,
            ExpectedRegion,
            ExpectedDealerUrn,
            ExpectNoRelevantHit,
            Notes);
}
