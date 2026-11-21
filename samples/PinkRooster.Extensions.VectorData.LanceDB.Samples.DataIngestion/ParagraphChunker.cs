using System.Runtime.CompilerServices;
using Microsoft.Extensions.DataIngestion;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion;

/// <summary>Turns each paragraph into one chunk; the sample needs no tokenizer.</summary>
internal sealed class ParagraphChunker : IngestionChunker<string>
{
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (IngestionDocumentSection section in document.Sections)
        {
            foreach (IngestionDocumentElement element in section.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element is IngestionDocumentParagraph && element.GetMarkdown() is { Length: > 0 } text)
                {
                    yield return new IngestionChunk<string>(text, document);
                }
            }
        }

        await Task.CompletedTask;
    }
}
