using ARC.Data.A1;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Synthetic;

/// <summary>
/// Deterministic generator for the assignment synthetic corpus (~2,500 dealers × 12 months).
/// All financial/legal values derive from the seed. Marked Synthetic / Assignment Evaluation Only.
/// </summary>
public sealed class SyntheticDatasetGenerator
{
    private static readonly string[] Regions = ["West", "North", "South", "East"];
    private static readonly string[] Depots =
    [
        "Mumbai-Andheri", "Pune-East", "Delhi-NCR", "Jaipur", "Chennai-South",
        "Bengaluru", "Kolkata", "Guwahati", "Ahmedabad", "Hyderabad"
    ];
    private static readonly string[] BusinessLines = ["DECO", "IND", "PROT", "AUTO"];
    private static readonly string[] CustTypes = ["DLR", "DIST", "OEM"];

    public SyntheticDataset Generate(SyntheticDatasetOptions? options = null)
    {
        options ??= new SyntheticDatasetOptions();
        if (options.DealerCount < 20)
            throw new ArgumentOutOfRangeException(nameof(options), "DealerCount must be at least 20 for canonical scenarios.");
        if (options.HistoryMonths < 1 || options.HistoryMonths > 24)
            throw new ArgumentOutOfRangeException(nameof(options), "HistoryMonths must be 1–24.");

        var asOf = options.AsOf;
        var limits = new ConfigurableBusinessLineLimitProvider(new BusinessLineLimitOptions
        {
            DefaultLimit = options.DefaultBusinessLimit,
            ByBusinessLine = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["DECO"] = options.DefaultBusinessLimit,
                ["IND"] = options.DefaultBusinessLimit * 1.5m,
                ["PROT"] = options.DefaultBusinessLimit * 0.8m,
                ["AUTO"] = options.DefaultBusinessLimit * 2m
            }
        });

        var dealers = new List<SyntheticDealerProfile>(options.DealerCount);
        var openings = new List<SyntheticOdosOpeningRow>(options.DealerCount * options.HistoryMonths);
        var adjustments = new List<LedgerAdjustmentFact>();
        var recovery = new List<SyntheticRecoveryHeader>();
        var cheques = new List<SecurityCheque>();
        var memos = new List<ChequeReturnMemo>();
        var disputes = new List<Dispute>();
        var payments = new List<SyntheticPaymentHistoryFact>();
        var ptps = new List<SyntheticPtpFact>();
        var visits = new List<SyntheticFieldVisitFact>();
        var evidence = new List<SyntheticEvidenceFact>();
        var notices = new List<SyntheticDemandNoticeFact>();
        var s138 = new List<SyntheticSection138Fact>();
        var motherLinks = new List<SyntheticMotherAccountLink>();
        var identity = new List<DealerSourceMapping>();
        var scenarioMap = new Dictionary<SyntheticScenarioTag, SyntheticDealerProfile>();

        // Indices 0..9 reserved for canonical S1–S9 + R6 (index 9 = R6)
        for (var i = 0; i < options.DealerCount; i++)
        {
            var tag = i switch
            {
                0 => SyntheticScenarioTag.S1_CleanOverdue,
                1 => SyntheticScenarioTag.S2_HighCreditR1b,
                2 => SyntheticScenarioTag.S3_QualifyingBounce,
                3 => SyntheticScenarioTag.S4_NonQualifyingCheque,
                4 => SyntheticScenarioTag.S5_LimitationT2,
                5 => SyntheticScenarioTag.S6_DisputeHold,
                6 => SyntheticScenarioTag.S7_MoratoriumHold,
                7 => SyntheticScenarioTag.S8_LowConfidencePtp,
                8 => SyntheticScenarioTag.S9_MotherAccountDuplicate,
                9 => SyntheticScenarioTag.R6_FullLineage,
                _ => SyntheticScenarioTag.None
            };

            var region = Regions[i % Regions.Length];
            var depot = Depots[i % Depots.Length];
            var sbl = BusinessLines[i % BusinessLines.Length];
            var dealerCode = $"D{i + 1:D5}";
            var urnValue = tag == SyntheticScenarioTag.None
                ? $"dealer:synth:{dealerCode.ToLowerInvariant()}"
                : $"dealer:synth:{tag.ToString().ToLowerInvariant()}";
            var urn = new DealerUrn(urnValue);
            var motherCode = tag == SyntheticScenarioTag.S9_MotherAccountDuplicate
                ? "MOTHER-S9"
                : (i % 37 == 0 ? $"MOTHER-{(i / 37):D3}" : null);

            var profile = new SyntheticDealerProfile(
                CanonicalUrn: urn,
                DealerCode: dealerCode,
                DepotCode: depot,
                Region: region,
                BusinessLine: sbl,
                CustomerType: CustTypes[i % CustTypes.Length],
                BillTo: $"BT-{dealerCode}",
                MotherAccountCode: motherCode,
                SapCode: $"SAP-{dealerCode}",
                PortalId: $"PORTAL-{dealerCode}",
                CoveringTsi: $"tsi.{region.ToLowerInvariant()}@paintco.local",
                UnderInsolvencyMoratorium: tag == SyntheticScenarioTag.S7_MoratoriumHold,
                ScenarioTag: tag);

            dealers.Add(profile);
            if (tag != SyntheticScenarioTag.None)
                scenarioMap[tag] = profile;

            identity.Add(new DealerSourceMapping("SAP", profile.SapCode!, urn, DealerMatchKind.ExactIdentifier));
            identity.Add(new DealerSourceMapping("PORTAL", profile.PortalId!, urn, DealerMatchKind.ExactIdentifier));

            // 12 months of Oracle-shaped opening rows (ageing buckets supplied, not recalculated)
            for (var m = 0; m < options.HistoryMonths; m++)
            {
                var period = asOf.AddMonths(-(options.HistoryMonths - 1 - m));
                var baseAmt = DeterministicAmount(options.Seed, i, m, 25_000m, 250_000m);
                var snapshot = BuildOpening(
                    options.Company,
                    profile,
                    period.Year,
                    period.Month,
                    baseAmt,
                    options.Seed,
                    i,
                    m);
                openings.Add(new SyntheticOdosOpeningRow(urn, snapshot));

                if (m == options.HistoryMonths - 1
                    && OdosEligibility.ExceedsOutstandingLimit(snapshot.OsAmtUpdt, profile.BusinessLine, limits))
                {
                    recovery.Add(new SyntheticRecoveryHeader(
                        urn,
                        $"{period.Year:D4}-{period.Month:D2}-odos",
                        snapshot.OsAmtUpdt,
                        "Open",
                        asOf));
                }
            }

            // Sparse population payment history (assignment-only)
            if (i % 5 == 0)
            {
                payments.Add(new SyntheticPaymentHistoryFact(
                    urn,
                    asOf.AddDays(-(10 + (i % 20))),
                    DeterministicAmount(options.Seed, i, 99, 1_000m, 50_000m),
                    i % 2 == 0 ? "NEFT" : "CHEQUE"));
            }

            if (i % 11 == 0)
            {
                visits.Add(new SyntheticFieldVisitFact(
                    urn,
                    asOf.AddDays(-(i % 28)),
                    i % 2 == 0 ? "Promised" : "NoContact",
                    profile.CoveringTsi!));
            }

            SeedScenarioFacts(
                tag,
                profile,
                asOf,
                adjustments,
                cheques,
                memos,
                disputes,
                ptps,
                evidence,
                notices,
                s138,
                motherLinks,
                openings);
        }

        // Ensure S9 mother child exists with duplicate identity path
        EnsureMotherChild(scenarioMap, dealers, motherLinks, identity, options, asOf, openings, recovery, limits);

        var r6 = BuildR6Case(scenarioMap[SyntheticScenarioTag.R6_FullLineage], adjustments, openings, asOf);

        return new SyntheticDataset
        {
            Seed = options.Seed,
            AsOf = asOf,
            Dealers = dealers,
            OpeningHistory = openings,
            Adjustments = adjustments,
            RecoveryHeaders = recovery,
            Cheques = cheques,
            ReturnMemos = memos,
            Disputes = disputes,
            PaymentHistory = payments,
            Ptps = ptps,
            FieldVisits = visits,
            Evidence = evidence,
            DemandNotices = notices,
            Section138Facts = s138,
            MotherAccountLinks = motherLinks,
            IdentityMappings = identity,
            R6LineageCase = r6,
            ScenarioDealers = scenarioMap,
            DataClassification = SyntheticAssignmentLabels.Marker
        };
    }

    private static void SeedScenarioFacts(
        SyntheticScenarioTag tag,
        SyntheticDealerProfile profile,
        DateOnly asOf,
        List<LedgerAdjustmentFact> adjustments,
        List<SecurityCheque> cheques,
        List<ChequeReturnMemo> memos,
        List<Dispute> disputes,
        List<SyntheticPtpFact> ptps,
        List<SyntheticEvidenceFact> evidence,
        List<SyntheticDemandNoticeFact> notices,
        List<SyntheticSection138Fact> s138,
        List<SyntheticMotherAccountLink> motherLinks,
        List<SyntheticOdosOpeningRow> openings)
    {
        var urn = profile.CanonicalUrn;
        var latestGross = openings
            .Where(o => o.DealerUrn.Equals(urn))
            .OrderByDescending(o => o.Snapshot.Year)
            .ThenByDescending(o => o.Snapshot.Month)
            .Select(o => o.Snapshot.OsAmtUpdt)
            .FirstOrDefault();

        switch (tag)
        {
            case SyntheticScenarioTag.S1_CleanOverdue:
                // Gross only — no adjustments
                break;

            case SyntheticScenarioTag.S2_HighCreditR1b:
                // ~77% credit of gross for R1b
                var credit = Math.Round(latestGross * 0.77m, 2);
                adjustments.Add(Adj(urn, "CreditNote", credit, asOf, "CN-S2-R1B"));
                break;

            case SyntheticScenarioTag.S3_QualifyingBounce:
                cheques.Add(new SecurityCheque(
                    urn, "CHQ-S3-9001", new Money(100_000m), ChequeStatus.Bounced,
                    micr: "400002000", depositDate: asOf.AddDays(-40), validityEnd: asOf.AddYears(1)));
                memos.Add(new ChequeReturnMemo(urn, "CHQ-S3-9001", "FUNDS_INSUFFICIENT", asOf.AddDays(-35), asOf.AddDays(-35)));
                s138.Add(new SyntheticSection138Fact(urn, "CHQ-S3-9001", "FUNDS_INSUFFICIENT", asOf.AddDays(-35), asOf.AddDays(-20), true));
                notices.Add(new SyntheticDemandNoticeFact(urn, $"{asOf:yyyy-MM}-s3", asOf.AddDays(-20), 100_000m, asOf.AddDays(-20)));
                foreach (var artefact in Enum.GetNames<DocumentType>())
                    evidence.Add(new SyntheticEvidenceFact(urn, artefact, $"synthetic-worm/{urn.Value}/{artefact}.pdf"));
                break;

            case SyntheticScenarioTag.S4_NonQualifyingCheque:
                cheques.Add(new SecurityCheque(
                    urn, "CHQ-S4-1001", new Money(50_000m), ChequeStatus.Bounced,
                    micr: "400002001", depositDate: asOf.AddDays(-10), validityEnd: asOf.AddDays(-1)));
                memos.Add(new ChequeReturnMemo(urn, "CHQ-S4-1001", "SIGNATURE_MISMATCH", asOf.AddDays(-8), asOf.AddDays(-8)));
                s138.Add(new SyntheticSection138Fact(urn, "CHQ-S4-1001", "SIGNATURE_MISMATCH", asOf.AddDays(-8), asOf.AddDays(-5), false));
                break;

            case SyntheticScenarioTag.S5_LimitationT2:
                cheques.Add(new SecurityCheque(
                    urn, "CHQ-S5-7001", new Money(80_000m), ChequeStatus.Bounced,
                    depositDate: asOf.AddDays(-120), validityEnd: asOf.AddYears(1)));
                memos.Add(new ChequeReturnMemo(urn, "CHQ-S5-7001", "FUNDS_INSUFFICIENT", asOf.AddDays(-100), asOf.AddDays(-100)));
                notices.Add(new SyntheticDemandNoticeFact(urn, $"{asOf:yyyy-MM}-s5", asOf.AddDays(-70), 80_000m, asOf.AddDays(-70)));
                s138.Add(new SyntheticSection138Fact(urn, "CHQ-S5-7001", "FUNDS_INSUFFICIENT", asOf.AddDays(-100), asOf.AddDays(-70), true));
                break;

            case SyntheticScenarioTag.S6_DisputeHold:
                adjustments.Add(Adj(urn, "Dispute", Math.Round(latestGross * 0.15m, 2), asOf, "DSP-S6"));
                disputes.Add(new Dispute(urn, DisputeStatus.UnderReview, "DSP-S6-UR"));
                break;

            case SyntheticScenarioTag.S7_MoratoriumHold:
                // Moratorium flag on dealer profile
                break;

            case SyntheticScenarioTag.S8_LowConfidencePtp:
                ptps.Add(new SyntheticPtpFact(urn, asOf.AddDays(7), 25_000m, ConfirmedByTsi: false, Confidence: 0.42m));
                break;

            case SyntheticScenarioTag.S9_MotherAccountDuplicate:
                motherLinks.Add(new SyntheticMotherAccountLink(profile.DealerCode, "MOTHER-S9", urn));
                break;

            case SyntheticScenarioTag.R6_FullLineage:
                adjustments.Add(Adj(urn, "CreditNote", 12_000m, asOf, "CN-R6"));
                adjustments.Add(Adj(urn, "SchemeRebate", 3_500m, asOf, "RB-R6"));
                adjustments.Add(Adj(urn, "GoodsReturn", 2_000m, asOf, "RT-R6"));
                adjustments.Add(Adj(urn, "ChequeClearing", 1_500m, asOf, "CL-R6"));
                break;
        }
    }

    private static void EnsureMotherChild(
        Dictionary<SyntheticScenarioTag, SyntheticDealerProfile> scenarioMap,
        List<SyntheticDealerProfile> dealers,
        List<SyntheticMotherAccountLink> motherLinks,
        List<DealerSourceMapping> identity,
        SyntheticDatasetOptions options,
        DateOnly asOf,
        List<SyntheticOdosOpeningRow> openings,
        List<SyntheticRecoveryHeader> recovery,
        IBusinessLineLimitProvider limits)
    {
        if (!scenarioMap.TryGetValue(SyntheticScenarioTag.S9_MotherAccountDuplicate, out var mother))
            return;

        var childUrn = new DealerUrn("dealer:synth:s9_child");
        var child = new SyntheticDealerProfile(
            childUrn,
            "D9CHILD",
            mother.DepotCode,
            mother.Region,
            mother.BusinessLine,
            mother.CustomerType,
            mother.BillTo,
            "MOTHER-S9",
            "SAP-D9CHILD",
            "PORTAL-D9CHILD",
            mother.CoveringTsi,
            false,
            SyntheticScenarioTag.S9_MotherAccountDuplicate);

        dealers.Add(child);
        motherLinks.Add(new SyntheticMotherAccountLink(child.DealerCode, "MOTHER-S9", mother.CanonicalUrn));
        // Duplicate identity: child SAP also maps toward mother (mstr_dlr-style rollup target)
        identity.Add(new DealerSourceMapping("SAP", child.SapCode!, mother.CanonicalUrn, DealerMatchKind.ExactIdentifier));
        identity.Add(new DealerSourceMapping("SAP", child.SapCode!, childUrn, DealerMatchKind.ExactIdentifier));

        for (var m = 0; m < options.HistoryMonths; m++)
        {
            var period = asOf.AddMonths(-(options.HistoryMonths - 1 - m));
            var snapshot = BuildOpening(
                options.Company, child, period.Year, period.Month,
                DeterministicAmount(options.Seed, 9001, m, 30_000m, 90_000m),
                options.Seed, 9001, m);
            openings.Add(new SyntheticOdosOpeningRow(childUrn, snapshot));
            if (m == options.HistoryMonths - 1
                && OdosEligibility.ExceedsOutstandingLimit(snapshot.OsAmtUpdt, child.BusinessLine, limits))
            {
                recovery.Add(new SyntheticRecoveryHeader(
                    childUrn, $"{period.Year:D4}-{period.Month:D2}-odos", snapshot.OsAmtUpdt, "Open", asOf));
            }
        }
    }

    private static SyntheticR6LineageCase BuildR6Case(
        SyntheticDealerProfile profile,
        List<LedgerAdjustmentFact> adjustments,
        List<SyntheticOdosOpeningRow> openings,
        DateOnly asOf)
    {
        var urn = profile.CanonicalUrn;
        var opening = openings
            .Where(o => o.DealerUrn.Equals(urn))
            .OrderByDescending(o => o.Snapshot.Year)
            .ThenByDescending(o => o.Snapshot.Month)
            .Select(o => o.Snapshot)
            .First();

        var adj = adjustments.Where(a => a.DealerUrn.Equals(urn)).ToList();
        var lines = OdosOpeningLedgerProjector.Project(urn, opening, adj, openingIsSynthetic: true);
        var buckets = Classify(lines);
        var exposure = MetricContract.Compute(
            urn,
            asOf,
            buckets.Gross,
            buckets.Credits,
            buckets.Rebates,
            buckets.Returns,
            buckets.Clearing,
            buckets.Disputed,
            lines.Select(l => l.Lineage).ToList(),
            fullyReconciled: true);

        var drafted = exposure.NetRecoverableExposure.Amount;
        return new SyntheticR6LineageCase(
            urn,
            exposure.Lineage.ToList(),
            exposure.NetRecoverableExposure.Amount,
            Decision: "Issue",
            DraftedNoticeAmount: drafted);
    }

    private static (Money Gross, Money Credits, Money Rebates, Money Returns, Money Clearing, Money Disputed)
        Classify(IReadOnlyList<LedgerPosition> lines)
    {
        Money gross = Money.Zero, credits = Money.Zero, rebates = Money.Zero, returns = Money.Zero, clearing = Money.Zero, disputed = Money.Zero;
        foreach (var line in lines)
        {
            var t = line.DocumentType.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
            if (Contains(t, "Invoice", "AR", "Gross", "Receivable")) gross += line.Amount;
            else if (Contains(t, "CreditNote", "Credit")) credits += Abs(line.Amount);
            else if (Contains(t, "Rebate", "Scheme")) rebates += Abs(line.Amount);
            else if (Contains(t, "Return")) returns += Abs(line.Amount);
            else if (Contains(t, "Clearing")) clearing += Abs(line.Amount);
            else if (Contains(t, "Dispute")) disputed += Abs(line.Amount);
        }
        return (gross, credits, rebates, returns, clearing, disputed);
    }

    private static bool Contains(string value, params string[] tokens)
        => tokens.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static Money Abs(Money money) => new(Math.Abs(money.Amount), money.Currency);

    private static LedgerAdjustmentFact Adj(
        DealerUrn urn,
        string documentType,
        decimal amount,
        DateOnly asOf,
        string key)
        => new(
            urn,
            documentType,
            asOf.AddDays(-15),
            asOf.AddDays(-20),
            new Money(amount),
            new LineItemRef(
                SyntheticAssignmentLabels.SourceSystem,
                SyntheticAssignmentLabels.AdjustmentTable,
                key,
                amount,
                asOf.AddDays(-20)),
            IsAssignmentEvaluationOnly: true);

    private static OdosOpeningSnapshot BuildOpening(
        string company,
        SyntheticDealerProfile profile,
        int year,
        int month,
        decimal osAmtUpdt,
        int seed,
        int dealerIndex,
        int monthIndex)
    {
        // Ageing buckets are assigned proportionally (Oracle-supplied analogue) — NOT derived from due dates.
        var w0 = 0.40m;
        var w1 = 0.25m;
        var w2 = 0.15m;
        var w3 = 0.12m;
        var amt0 = Math.Round(osAmtUpdt * w0, 2);
        var amt1 = Math.Round(osAmtUpdt * w1, 2);
        var amt2 = Math.Round(osAmtUpdt * w2, 2);
        var amt3 = Math.Round(osAmtUpdt * w3, 2);
        var amt4 = Math.Round(osAmtUpdt - amt0 - amt1 - amt2 - amt3, 2);

        var trxDay = 1 + ((dealerIndex + monthIndex) % 27);
        var trxDate = new DateOnly(year, month, Math.Min(trxDay, DateTime.DaysInMonth(year, month)));

        return new OdosOpeningSnapshot(
            Company: company,
            Year: year,
            Month: month,
            DepotCode: profile.DepotCode,
            DealerCode: profile.DealerCode,
            BusinessLine: profile.BusinessLine,
            CustomerType: profile.CustomerType,
            BillTo: profile.BillTo,
            TrxId: $"TRX-{seed:X}-{dealerIndex:D5}-{monthIndex:D2}",
            TrxDate: trxDate,
            DocType: "OS",
            DocNo: $"DOC-{dealerIndex:D5}-{year}{month:D2}",
            OsAmt0: amt0,
            OsAmt1: amt1,
            OsAmt2: amt2,
            OsAmt3: amt3,
            OsAmt4: amt4,
            OsAmtUpdt: osAmtUpdt);
    }

    /// <summary>Deterministic decimal in [min,max] from seed + indices (no Random drift).</summary>
    public static decimal DeterministicAmount(int seed, int a, int b, decimal min, decimal max)
    {
        unchecked
        {
            var hash = (uint)(seed * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791);
            var unit = (hash % 10_000_000u) / 10_000_000m;
            return Math.Round(min + (max - min) * unit, 2);
        }
    }
}
