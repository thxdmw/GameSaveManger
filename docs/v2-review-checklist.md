# Review checklist

- [ ] Run `dotnet build .\GameSaveManager.V2.sln -c Debug` with .NET 10 SDK on Windows.
- [ ] Scan an empty directory and a directory with nested files.
- [ ] Modify a file during hashing and verify Manifest construction fails safely.
- [ ] Confirm reparse points are not traversed.
- [ ] Replace `InMemoryFileHashCache` with SQLite before treating hash cache as durable.
