using System.Text;
using System.Text.RegularExpressions;

namespace ARC.Knowledge.Chunking;

/// <summary>
/// Chunks recovery/notice policies by clause/heading. Target 300-800 chars per chunk where natural boundaries allow.
/// Deterministic and idempotent.
/// </summary>
public sealed partial class PolicyClauseChunker : IDocumentChunker
{
    private const int MinChunkSize = 150;
    private const int TargetChunkSize = 500;
    private const int MaxChunkSize = 1000;
    private const string DefaultClausePrefix = "CLAUSE-";

    public IReadOnlySet<string> SupportedCategories { get; } = new HashSet<string>
    {
        "RecoveryPolicy",
        "NoticePolicy",
        "Policy"
    };

    public IReadOnlyList<ChunkResult> ChunkDocument(SourceDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));
            
        if (string.IsNullOrWhiteSpace(document.Content))
            return Array.Empty<ChunkResult>();

        var clauses = ExtractClauses(document.Content);
        var chunks = new List<ChunkResult>();

        // Fallback: if no clauses extracted, treat entire document as single chunk
        if (clauses.Count == 0)
        {
            return new List<ChunkResult>
            {
                new()
                {
                    Content = document.Content.Trim(),
                    PageOrSection = "CLAUSE-1",
                    Title = "Document Content",
                    Metadata = new Dictionary<string, string>
                    {
                        ["clauseNumber"] = "1",
                        ["clauseIdentifier"] = "CLAUSE-1"
                    }
                }
            };
        }

        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            if (string.IsNullOrWhiteSpace(clause.Content))
                continue;

            // If clause is naturally within target range, keep as single chunk
            // Include heading in content for proper change detection and context
            var fullContent = string.IsNullOrWhiteSpace(clause.Heading)
                ? clause.Content.Trim()
                : $"{clause.Heading}\n{clause.Content}".Trim();
                
            if (fullContent.Length <= MaxChunkSize)
            {
                chunks.Add(new ChunkResult
                {
                    Content = fullContent,
                    PageOrSection = clause.Identifier,
                    Title = clause.Heading ?? $"Clause {i + 1}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["clauseNumber"] = clause.Number?.ToString() ?? (i + 1).ToString(),
                        ["clauseIdentifier"] = clause.Identifier
                    }
                });
            }
            else
            {
                // Very large clause: split by sentence boundaries only if necessary
                var subChunks = SplitLargeClause(clause, i);
                chunks.AddRange(subChunks);
            }
        }

        return chunks;
    }

    private List<Clause> ExtractClauses(string content)
    {
        var clauses = new List<Clause>();
        
        // Match common policy clause patterns:
        // "1. Heading" or "1.1 Heading" or "Section 1:" or "CLAUSE 1:" etc.
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Clause? currentClause = null;
        int clauseCounter = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var clauseMatch = ClauseHeaderRegex().Match(line);
            
            if (clauseMatch.Success)
            {
                // Save previous clause if exists
                if (currentClause != null)
                {
                    // Ensure clause has content
                    if (string.IsNullOrWhiteSpace(currentClause.Content))
                    {
                        currentClause.Content = currentClause.Heading ?? "Content";
                    }
                    clauses.Add(currentClause);
                }

                // Start new clause
                clauseCounter++;
                var number = clauseMatch.Groups[1].Value;
                var heading = clauseMatch.Groups[2].Value.Trim();
                
                if (string.IsNullOrWhiteSpace(heading))
                    heading = $"Clause {number}";

                currentClause = new Clause
                {
                    Number = int.TryParse(number.Split('.')[0], out var n) ? n : clauseCounter,
                    Identifier = $"{DefaultClausePrefix}{number}",
                    Heading = heading,
                    Content = ""
                };
            }
            else if (currentClause != null)
            {
                // Add to current clause content
                if (currentClause.Content.Length > 0)
                    currentClause.Content += "\n";
                currentClause.Content += line;
            }
            else
            {
                // No clause started yet, create implicit first clause
                if (clauses.Count == 0)
                {
                    clauseCounter++;
                    currentClause = new Clause
                    {
                        Number = clauseCounter,
                        Identifier = $"{DefaultClausePrefix}{clauseCounter}",
                        Heading = "Preamble",
                        Content = line
                    };
                }
            }
        }

        // Add final clause
        if (currentClause != null)
        {
            // Ensure clause has content
            if (string.IsNullOrWhiteSpace(currentClause.Content))
            {
                currentClause.Content = currentClause.Heading ?? "Content";
            }
            clauses.Add(currentClause);
        }

        return clauses;
    }

    private List<ChunkResult> SplitLargeClause(Clause clause, int clauseIndex)
    {
        var chunks = new List<ChunkResult>();
        
        // Include heading in first chunk for context and change detection
        var headingPrefix = string.IsNullOrWhiteSpace(clause.Heading) ? "" : $"{clause.Heading}\n";
        var sentences = SplitIntoSentences(clause.Content);
        var currentChunk = new StringBuilder(headingPrefix);
        int subChunkIndex = 1;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length > 0 && currentChunk.Length + sentence.Length > MaxChunkSize)
            {
                // Flush current chunk
                chunks.Add(new ChunkResult
                {
                    Content = currentChunk.ToString().Trim(),
                    PageOrSection = $"{clause.Identifier}-{subChunkIndex}",
                    Title = $"{clause.Heading} (Part {subChunkIndex})",
                    Metadata = new Dictionary<string, string>
                    {
                        ["clauseNumber"] = clause.Number?.ToString() ?? (clauseIndex + 1).ToString(),
                        ["clauseIdentifier"] = clause.Identifier,
                        ["subChunk"] = subChunkIndex.ToString()
                    }
                });

                currentChunk.Clear();
                subChunkIndex++;
            }

            currentChunk.Append(sentence);
            currentChunk.Append(' ');
        }

        // Flush remaining
        if (currentChunk.Length > 0)
        {
            chunks.Add(new ChunkResult
            {
                Content = currentChunk.ToString().Trim(),
                PageOrSection = subChunkIndex == 1 ? clause.Identifier : $"{clause.Identifier}-{subChunkIndex}",
                Title = subChunkIndex == 1 ? clause.Heading ?? "Clause" : $"{clause.Heading} (Part {subChunkIndex})",
                Metadata = new Dictionary<string, string>
                {
                    ["clauseNumber"] = clause.Number?.ToString() ?? (clauseIndex + 1).ToString(),
                    ["clauseIdentifier"] = clause.Identifier,
                    ["subChunk"] = subChunkIndex.ToString()
                }
            });
        }

        return chunks;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        // Simple sentence boundary detection (period, exclamation, question mark followed by space/newline)
        var sentences = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);

            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') && 
                (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                sentences.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            sentences.Add(current.ToString());

        return sentences;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)[\.:\)]\s*(.*)$", 
        RegexOptions.None)]
    private static partial Regex ClauseHeaderRegex();

    private sealed class Clause
    {
        public int? Number { get; set; }
        public string Identifier { get; set; } = "";
        public string Heading { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
