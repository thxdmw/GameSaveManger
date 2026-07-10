# GameSave Manager V2

## Boundary

- `GameSaveManager.App`: WPF presentation and ViewModels.
- `GameSaveManager.Application`: snapshot manifest and use-case contracts.
- `GameSaveManager.Domain`: immutable game/snapshot models.
- `GameSaveManager.Infrastructure`: Windows file system, hashing, persistence and API implementations.

Legacy WinForms remains untouched during the V2 migration.

## Snapshot rule

`FileSystemWatcher` is only a dirty trigger. A snapshot is built from a complete directory scan after the game exits. SHA-256 is the canonical content identity shared with the CMS `module.file` service.

## Current vertical slice

1. Enter a save directory in the WPF shell.
2. Scan all regular files recursively, skipping reparse points.
3. Reuse SHA-256 when path + size + last-write-time matches the cache.
4. Stream-hash cache misses with a 1 MiB buffer.
5. Build an immutable manifest of relative path + SHA-256 + size.

The bootstrap cache is currently in memory. SQLite persistence, process monitoring, dirty tracking and Sync API integration are the next milestones.
