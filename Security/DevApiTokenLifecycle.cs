using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TestProject.Security;

// lifecycle only for local development. For Production,
// I would expect credentials from deployment configuration or a secret manager
public static class DevApiTokenLifecycle
{
    public const string SkipEnvironmentVariable = "FILEBROWSER_SKIP_DEV_TOKEN_LIFECYCLE";
    public const string ReaderKey = "FileBrowser:ReaderApiToken";
    public const string OperatorKey = "FileBrowser:OperatorApiToken";
    public const string UserSecretsId = "TestProject-FileBrowser-8f3c9a2e-7b14-4d6a-9e21-0c5a8f4d2b17";

    private const int TokenByteLength = 32;
    private static int _cleanupRegistered;
    private static string? _secretsPathManagedThisSession;

    public static bool IsSkipped =>
        string.Equals(Environment.GetEnvironmentVariable(SkipEnvironmentVariable), "1", StringComparison.Ordinal);

    public static string GenerateAndPersistForSession()
    {
        var secretsPath = GetUserSecretsFilePath(UserSecretsId);
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);

        var readerToken = CreateToken();
        var operatorToken = CreateToken();
        while (FixedTimeEquals(readerToken, operatorToken))
        {
            operatorToken = CreateToken();
        }

        MergeAndWriteSecrets(secretsPath, readerToken, operatorToken);
        _secretsPathManagedThisSession = secretsPath;

        readerToken = string.Empty;
        operatorToken = string.Empty;

        return secretsPath;
    }

    public static void ClearSessionTokens()
    {
        var secretsPath = _secretsPathManagedThisSession ?? GetUserSecretsFilePath(UserSecretsId);
        try
        {
            if (!File.Exists(secretsPath))
            {
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(secretsPath)) as JsonObject ?? new JsonObject();
            root.Remove(ReaderKey);
            root.Remove(OperatorKey);

            if (root.Count == 0)
            {
                File.Delete(secretsPath);
            }
            else
            {
                var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                var tempPath = secretsPath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Copy(tempPath, secretsPath, overwrite: true);
                File.Delete(tempPath);
            }
        }
        catch
        {
            // shutdown cleanup best-effort because a forced kill can bypass it
        }
        finally
        {
            _secretsPathManagedThisSession = null;
        }
    }

    public static void RegisterCleanupHandlers(IHostApplicationLifetime lifetime, ILogger logger)
    {
        if (Interlocked.Exchange(ref _cleanupRegistered, 1) == 1)
        {
            // guard this because WebApplicationFactory can create several hosts per process
            return;
        }

        void Cleanup(string reason)
        {
            ClearSessionTokens();
            try
            {
                logger.LogInformation("Cleared Development API tokens from User Secrets ({Reason})", reason);
            }
            catch
            {
                // ignore this because logging will be disposed during ProcessExit
            }
        }

        lifetime.ApplicationStopping.Register(() => Cleanup("ApplicationStopping"));

        Console.CancelKeyPress += (_, args) =>
        {
            Cleanup("CancelKeyPress");
            args.Cancel = false;
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup("ProcessExit");
    }

    public static string GetUserSecretsFilePath(string userSecretsId)
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets");
        }
        else
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets");
        }

        return Path.Combine(root, userSecretsId, "secrets.json");
    }

    public static string? TryReadUserSecretsIdFromCsproj(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return null;
        }

        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("UserSecretsId").FirstOrDefault()?.Value?.Trim();
    }

    private static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength)).ToLowerInvariant();

    private static bool FixedTimeEquals(string a, string b)
    {
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }

    private static void MergeAndWriteSecrets(string secretsPath, string readerToken, string operatorToken)
    {
        JsonObject root;
        if (File.Exists(secretsPath))
        {
            var existing = File.ReadAllText(secretsPath);
            root = string.IsNullOrWhiteSpace(existing)
                ? new JsonObject()
                : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root[ReaderKey] = readerToken;
        root[OperatorKey] = operatorToken;

        // write through a temporary file so readers cannot observe partial JSON.
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tempPath = secretsPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Copy(tempPath, secretsPath, overwrite: true);
        File.Delete(tempPath);
    }
}
