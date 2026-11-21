# Samples

Run a sample from the repository root with `dotnet run --project samples/PinkRooster.Extensions.VectorData.LanceDB.Samples.<Name>` (use `Hello` or `DataIngestion`). Tests in `tests/PinkRooster.Extensions.VectorData.LanceDB.Samples.Tests` exercise both samples. Neither sample needs credentials.

| Sample | Shows |
|---|---|
| [`Hello`](PinkRooster.Extensions.VectorData.LanceDB.Samples.Hello/README.md) | Local LanceDB vector store and collection initialization |
| [`DataIngestion`](PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion/README.md) | A DataIngestion pipeline writing into LanceDB, re-ingesting a changed document, then hybrid search |
