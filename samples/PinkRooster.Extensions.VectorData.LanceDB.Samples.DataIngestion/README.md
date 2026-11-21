# DataIngestion

Writes a document into a local LanceDB collection through a `Microsoft.Extensions.DataIngestion` pipeline, ingests a changed version of it, and then runs a hybrid search. After the second ingestion the collection holds only the new version's chunks, because the writer deletes the old chunks once the new ones are in.

The sample runs offline. It reads a plain-text file with its own small reader, makes one chunk per paragraph, and embeds each chunk with a deterministic word-hashing generator, so no model or network is involved.

The CLI creates and removes its own scratch database. If you call `Program.RunAsync` directly, give it a new or empty directory for one run and clean it up afterward. The sample keeps its handbook in a separate temporary directory and removes it after ingestion.

Two things to know:

- The package `Microsoft.Extensions.DataIngestion` is a preview release. Only this sample references it; the LanceDB package does not.
- The writer's own collection has no full-text index. The sample creates the collection first, with the writer's column names (`key`, `embedding`, `content`, `context`, `documentid`) plus `IsFullTextIndexed` on `content`, and the writer then uses that collection. If a later preview renames the columns, the sample test fails.

```text
dotnet run --project samples/PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion
```
