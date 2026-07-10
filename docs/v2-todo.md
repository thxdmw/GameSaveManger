# GameSave Manager V2 TODO

- Replace bootstrap in-memory hash cache with SQLite.
- Add process start/exit monitoring with periodic reconciliation.
- Add `FileSystemWatcher` dirty marker; never treat watcher events as snapshot truth.
- Add GameSave API client and Windows Credential Manager token storage.
- Add object missing check/upload flow.
- Add immutable snapshot commit and sync HEAD conflict UI.
- Add restore journal, staging verification and safety snapshot.
