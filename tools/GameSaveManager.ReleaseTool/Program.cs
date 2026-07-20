using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

return args.FirstOrDefault() switch
{
    "generate-key" => GenerateKey(ParseOptions(args[1..])),
    "create-manifest" => CreateManifest(ParseOptions(args[1..])),
    "verify" => Verify(ParseOptions(args[1..])),
    _ => Usage()
};

static int GenerateKey(IReadOnlyDictionary<string, string> options)
{
    string privatePath = Required(options, "private");
    string publicPath = Required(options, "public");
    RefuseOverwrite(privatePath);
    RefuseOverwrite(publicPath);
    EnsureParent(privatePath);
    EnsureParent(publicPath);
    using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllBytes(privatePath, key.ExportPkcs8PrivateKey());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem(), new UTF8Encoding(false));
    return 0;
}

static int CreateManifest(IReadOnlyDictionary<string, string> options)
{
    string installerPath = Required(options, "installer");
    string outputPath = Required(options, "output");
    string signaturePath = Required(options, "signature");
    string privateKeyPath = Required(options, "private-key");
    string version = Required(options, "version");
    string channel = Required(options, "channel");
    string releasePageUrl = RequireHttps(options, "release-page-url");
    string installerUrl = RequireHttps(options, "installer-url");
    string publisherCertificateSha256 = RequireSha256(options, "publisher-certificate-sha256");
    if (!File.Exists(installerPath)) throw new FileNotFoundException("Installer was not found.", installerPath);

    var installer = new FileInfo(installerPath);
    string installerSha256;
    using (FileStream stream = installer.OpenRead())
        installerSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    var manifest = new UpdateManifest(
        1,
        "GameSaveManager",
        version,
        channel,
        DateTimeOffset.UtcNow,
        releasePageUrl,
        new UpdateInstaller(
            installer.Name,
            installerUrl,
            installer.Length,
            installerSha256,
            publisherCertificateSha256));
    byte[] content = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
    EnsureParent(outputPath);
    EnsureParent(signaturePath);
    File.WriteAllBytes(outputPath, content);
    using ECDsa key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(File.ReadAllBytes(privateKeyPath), out _);
    byte[] signature = key.SignData(content, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    File.WriteAllText(signaturePath, Convert.ToBase64String(signature), new UTF8Encoding(false));
    return 0;
}

static int Verify(IReadOnlyDictionary<string, string> options)
{
    byte[] content = File.ReadAllBytes(Required(options, "input"));
    byte[] signature = Convert.FromBase64String(File.ReadAllText(Required(options, "signature")).Trim());
    using ECDsa key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Required(options, "public-key")));
    if (!key.VerifyData(content, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        throw new CryptographicException("Update manifest signature verification failed.");
    Console.WriteLine("Update manifest signature verified.");
    return 0;
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Options must use --name value pairs.");
        result[values[index][2..]] = values[index + 1];
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}.");

static string RequireHttps(IReadOnlyDictionary<string, string> options, string name)
{
    string value = Required(options, name);
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        throw new ArgumentException($"--{name} must be an absolute HTTPS URL.");
    return uri.AbsoluteUri;
}

static string RequireSha256(IReadOnlyDictionary<string, string> options, string name)
{
    string value = Required(options, name).ToLowerInvariant();
    if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        throw new ArgumentException($"--{name} must be a SHA-256 certificate hash.");
    return value;
}

static void RefuseOverwrite(string path)
{
    if (File.Exists(path)) throw new IOException($"Refusing to overwrite existing key: {path}");
}

static void EnsureParent(string path)
{
    string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
}

static int Usage()
{
    Console.Error.WriteLine("Commands: generate-key, create-manifest, verify");
    return 2;
}

internal sealed record UpdateManifest(
    int SchemaVersion,
    string Product,
    string Version,
    string Channel,
    DateTimeOffset PublishedAtUtc,
    string ReleasePageUrl,
    UpdateInstaller Installer);

internal sealed record UpdateInstaller(
    string Name,
    string Url,
    long Size,
    string Sha256,
    string PublisherCertificateSha256);
