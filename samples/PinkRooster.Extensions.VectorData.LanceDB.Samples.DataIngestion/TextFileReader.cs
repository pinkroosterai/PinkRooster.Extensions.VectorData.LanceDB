using Microsoft.Extensions.DataIngestion;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion;

/// <summary>Reads a plain-text file as a document with one paragraph per blank-line-separated block, with no network or model.</summary>
internal sealed class TextFileReader : IngestionDocumentReader
{
    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        using StreamReader reader = new(source, leaveOpen: true);
        string text = await reader.ReadToEndAsync(cancellationToken);

        IngestionDocumentSection section = new();
        foreach (string block in text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            section.Elements.Add(new IngestionDocumentParagraph(block));
        }

        IngestionDocument document = new(identifier);
        document.Sections.Add(section);
        return document;
    }
}
