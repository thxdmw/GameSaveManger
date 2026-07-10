# V2 build note

The V2 branch targets .NET 10 and WPF. Build with:

```powershell
dotnet build .\GameSaveManager.V2.sln -c Debug
```

The current implementation environment could modify and inspect the GitHub branch through the repository connector, but its local shell could not resolve `github.com`, so a clone-and-build verification was not performed before the draft PR was opened.
