# Samples

One console project for each feature of PinkRooster.Extensions.VectorData.LanceDB. Run any sample with `dotnet run --project samples/<name>`. Each sample has a `RunAsync` that a test in `tests/PinkRooster.Extensions.VectorData.LanceDB.Samples.Tests` runs, and none needs a secret.

| Sample | Shows |
|---|---|
| [`Hello`](PinkRooster.Extensions.VectorData.LanceDB.Samples.Hello/README.md) | Local LanceDB vector store and collection initialization |
| [`DataIngestion`](PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion/README.md) | A DataIngestion pipeline writing into LanceDB, re-ingesting a changed document, then hybrid search |
