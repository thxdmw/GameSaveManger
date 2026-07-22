using GameSaveManager.Application.Discovery;
using GameSaveManager.Infrastructure.Discovery;

namespace GameSaveManager.Verification;

internal static class LudusaviManifestVerification
{
    public static void VerifyStoreConditionsAndGlobExpansion()
    {
        string root = CreateSteamGameLayout(out string installDirectory);
        string manifestPath = Path.Combine(root, "manifest.yaml");
        try
        {
            CreateFile(Path.Combine(root, "userdata", "1001", "42", "remote", "slot1.sav"));
            CreateFile(Path.Combine(root, "userdata", "1002", "42", "remote", "slot2.sav"));
            CreateFile(Path.Combine(installDirectory, "rejected", "slot.sav"));
            File.WriteAllText(manifestPath, """
                Test Game:
                  installDir:
                    TestGame: {}
                  steam:
                    id: 42
                  files:
                    "<root>/userdata/<storeUserId>/<storeGameId>/remote/*.sav":
                      when:
                        - os: windows
                          store: steam
                    "<base>/rejected/*.sav":
                      when:
                        - os: windows
                          store: gog
                """);

            var game = new GameIdentity("Unrelated name", GameIdentity.Steam, "42", installDirectory, null, null);
            IReadOnlyList<SaveLocationCandidate> candidates =
                LudusaviManifestDetector.DetectFromManifestFile(game, manifestPath);

            Ensure(candidates.Count == 2, "Steam 用户目录 Glob 应返回两个实际匹配目录。");
            Ensure(candidates.All(candidate => candidate.Path.Contains("userdata", StringComparison.OrdinalIgnoreCase)),
                "when.store 不匹配的规则不应产生候选目录。");
            Ensure(candidates.All(candidate => Directory.Exists(candidate.Path)), "Glob 结果必须是实际存在的目录。");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static void VerifyInstallDirectoryAliasCycleAndSecondaryManifest()
    {
        string root = CreateSteamGameLayout(out string installDirectory);
        string installManifest = Path.Combine(root, "install.yaml");
        string cycleManifest = Path.Combine(root, "cycle.yaml");
        string taggedManifest = Path.Combine(root, "tagged.yaml");
        string secondaryManifest = Path.Combine(installDirectory, ".ludusavi.yaml");
        try
        {
            CreateFile(Path.Combine(installDirectory, "saves", "slot.sav"));
            File.WriteAllText(installManifest, """
                Canonical Game:
                  installDir:
                    TestGame: {}
                  files:
                    "<base>/saves/*.sav": {}
                """);
            var localGame = new GameIdentity("Different name", GameIdentity.Local, null, installDirectory, null, null);
            IReadOnlyList<SaveLocationCandidate> installCandidates =
                LudusaviManifestDetector.DetectFromManifestFile(localGame, installManifest);
            Ensure(installCandidates.Count == 1, "应按 installDir 映射匹配游戏。");

            File.WriteAllText(cycleManifest, """
                Cycle A:
                  alias: Cycle B
                  files:
                    "<base>/saves": {}
                Cycle B:
                  alias: Cycle A
                  files:
                    "<base>/saves": {}
                """);
            var cycleGame = localGame with { Name = "Cycle A" };
            Ensure(LudusaviManifestDetector.DetectFromManifestFile(cycleGame, cycleManifest).Count == 0,
                "Alias 循环必须被拒绝。");

            CreateFile(Path.Combine(installDirectory, "settings.txt"));
            File.WriteAllText(taggedManifest, """
                Tagged Game:
                  installDir:
                    TestGame: {}
                  files:
                    "<base>/settings.txt":
                      tags:
                        - config
                    "<base>/missing/savegame.sav":
                      tags:
                        - save
                """);
            Ensure(LudusaviManifestDetector.DetectFromManifestFile(localGame, taggedManifest).Count == 0,
                "仅命中 config 文件时不能把游戏安装根目录误报为存档目录。");

            CreateFile(Path.Combine(installDirectory, "ManifestFolder", "slot.sav"));
            File.WriteAllText(secondaryManifest, """
                installDir:
                  ManifestFolder: {}
                files:
                  "<base>/<game>/*.sav": {}
                """);
            Ensure(LudusaviManifestDetector.Detect(localGame, CancellationToken.None).Count == 1,
                "游戏目录中的二级 Manifest 应优先生效。");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateSteamGameLayout(out string installDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        installDirectory = Path.Combine(root, "steamapps", "common", "TestGame");
        Directory.CreateDirectory(installDirectory);
        return root;
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "verification");
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
