# Contributing

Bug reports and focused pull requests are welcome. Search existing issues before opening a new one. For a behavior change, describe the expected behavior and how it differs from the current one. Do not include API keys, private state, or live responses in issues or fixtures.

Keep changes close to the existing code. Add tests when changing behavior; the xUnit v3 projects under `tests/` cover provider behavior, conformance, samples, and repository checks. Run these commands from the repository root before a pull request:

```text
dotnet build PinkRooster.Extensions.VectorData.LanceDB.slnx -c Release
dotnet test --solution PinkRooster.Extensions.VectorData.LanceDB.slnx -c Release
```

Warnings are errors; in `src` that includes a public member without an XML doc. The provider's public API snapshot is in `tests/PinkRooster.Extensions.VectorData.LanceDB.Repository.Tests/PublicApi/`. For an intentional public API change, run the tests with `PINKROOSTER_UPDATE_PUBLIC_API=1` and commit the updated snapshot. Use a Conventional Commit subject such as `fix: handle an empty name`. In a pull request, explain the behavior change, list the checks run, and link a related issue when there is one. Security issues belong in the private reporting channel described in [SECURITY.md](SECURITY.md).
