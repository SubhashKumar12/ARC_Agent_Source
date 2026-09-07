using System.Text;

namespace ARC.Knowledge.Chunking;

/// <summary>
/// Chunks notice templates by logical section (heading, demand, legal wording, closing).
/// Does NOT chunk filled notices with dealer-specific financial values.
/// Deterministic and idempotent.
/// </summary>
public sealed class TemplateChunker : IDocumentChunker
{
    private const int MaxChunkSize = 1000;
    private static readonly string[] SectionMarkers = new[]
    {
        "HEADING:",
        "SUBJECT:",
        "DEMAND:",
        "AMOUNT DUE:",
        "LEGAL NOTICE:",
        "STATUTORY NOTICE:",
        "CONSEQUENCES:",
        "PAYMENT INSTRUCTIONS:",
        "CLOSING:",
        "SIGNATURE:",
        "FOOTER:"
    };

    public IReadOnlySet<string> SupportedCategories { get; } = new HashSet<string>
    {
        "NoticeTemplate",
        "Template"
    };

    public IReadOnlyList<ChunkResult> ChunkDocument(SourceDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Content))
            return Array.Empty<ChunkResult>();

        var sections = ExtractSections(document.Content);
        var chunks = new List<ChunkResult>();

        // Fallback: if no sections extracted, treat entire document as single chunk
        if (sections.Count == 0)
        {
            return new List<ChunkResult>
            {
                new()
                {
                    Content = document.Content.Trim(),
                    PageOrSection = "SECTION-1",
                    Title = "Template Content",
                    Metadata = new Dictionary<string, string>
                    {
                        ["sectionNumber"] = "1",
                        ["sectionIdentifier"] = "SECTION-1",
                        ["sectionType"] = "CONTENT"
                    }
                }
            };
        }

        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (string.IsNullOrWhiteSpace(section.Content))
                continue;

            if (section.Content.Length <= MaxChunkSize)
            {
                chunks.Add(new ChunkResult
                {
                    Content = section.Content.Trim(),
                    PageOrSection = section.Identifier,
                    Title = section.Title,
                    Metadata = new Dictionary<string, string>
                    {
                        ["sectionNumber"] = (i + 1).ToString(),
                        ["sectionIdentifier"] = section.Identifier,
                        ["sectionType"] = section.Type
                    }
                });
            }
            else
            {
                // Very large section: split by paragraph
                var subChunks = SplitLargeSection(section, i);
                chunks.AddRange(subChunks);
            }
        }

        return chunks;
    }

    private List<TemplateSection> ExtractSections(string content)
    {
        var sections = new List<TemplateSection>();
        var lines = content.Split('\n');
        TemplateSection? currentSection = null;
        int sectionCounter = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Check if line starts a new section
            var marker = SectionMarkers.FirstOrDefault(m => line.StartsWith(m, StringComparison.OrdinalIgnoreCase));
            
            if (marker != null)
            {
                // Save previous section
                if (currentSection != null && !string.IsNullOrWhiteSpace(currentSection.Content))
                {
                    sections.Add(currentSection);
                }

                // Start new section
                sectionCounter++;
                var sectionType = marker.TrimEnd(':').ToUpperInvariant().Replace(" ", "_");
                currentSection = new TemplateSection
                {
                    Identifier = $"SECTION-{sectionCounter}",
                    Title = marker.TrimEnd(':'),
                    Type = sectionType,
                    Content = line.Substring(marker.Length).Trim()
                };
            }
            else if (currentSection != null)
            {
                currentSection.Content += (currentSection.Content.Length > 0 ? "\n" : "") + line;
            }
            else
            {
                // No section started yet, create implicit first section
                if (sections.Count == 0)
                {
                    sectionCounter++;
                    currentSection = new TemplateSection
                    {
                        Identifier = $"SECTION-{sectionCounter}",
                        Title = "Template Header",
                        Type = "HEADER",
                        Content = line
                    };
                }
            }
        }

        // Add final section
        if (currentSection != null && !string.IsNullOrWhiteSpace(currentSection.Content))
        {
            sections.Add(currentSection);
        }

        // If no sections were found using markers, create a single section for entire content
        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            sections.Add(new TemplateSection
            {
                Identifier = "SECTION-1",
                Title = "Template Content",
                Type = "CONTENT",
                Content = content
            });
        }

        return sections;
    }

    private List<ChunkResult> SplitLargeSection(TemplateSection section, int sectionIndex)
    {
        var chunks = new List<ChunkResult>();
        var paragraphs = section.Content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();
        int subChunkIndex = 1;

        foreach (var paragraph in paragraphs)
        {
            if (currentChunk.Length > 0 && currentChunk.Length + paragraph.Length > MaxChunkSize)
            {
                // Flush current chunk
                chunks.Add(new ChunkResult
                {
                    Content = currentChunk.ToString().Trim(),
                    PageOrSection = $"{section.Identifier}-{subChunkIndex}",
                    Title = $"{section.Title} (Part {subChunkIndex})",
                    Metadata = new Dictionary<string, string>
                    {
                        ["sectionNumber"] = (sectionIndex + 1).ToString(),
                        ["sectionIdentifier"] = section.Identifier,
                        ["sectionType"] = section.Type,
                        ["subChunk"] = subChunkIndex.ToString()
                    }
                });

                currentChunk.Clear();
                subChunkIndex++;
            }

            currentChunk.Append(paragraph);
            currentChunk.Append("\n\n");
        }

        // Flush remaining
        if (currentChunk.Length > 0)
        {
            chunks.Add(new ChunkResult
            {
                Content = currentChunk.ToString().Trim(),
                PageOrSection = subChunkIndex == 1 ? section.Identifier : $"{section.Identifier}-{subChunkIndex}",
                Title = subChunkIndex == 1 ? section.Title : $"{section.Title} (Part {subChunkIndex})",
                Metadata = new Dictionary<string, string>
                {
                    ["sectionNumber"] = (sectionIndex + 1).ToString(),
                    ["sectionIdentifier"] = section.Identifier,
                    ["sectionType"] = section.Type,
                    ["subChunk"] = subChunkIndex.ToString()
                }
            });
        }

        return chunks;
    }

    private sealed class TemplateSection
    {
        public required string Identifier { get; set; }
        public required string Title { get; set; }
        public required string Type { get; set; }
        public string Content { get; set; } = "";
    }
}
