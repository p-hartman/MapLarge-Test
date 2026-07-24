using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace GenerateApiTokens;

public static class Program
{
    private const string WebProjectFileName = "TestProject.csproj";
    private const string ReaderKey = "FileBrowser:ReaderApiToken";
    private const string OperatorKey = "FileBrowser:OperatorApiToken";
    private const int TokenByteLength = 32;

    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
            var webProjectPath = Path.Combine(repoRoot, WebProjectFileName);
            if (!File.Exists(webProjectPath))
            {
                Console.Error.WriteLine($"Could not find {WebProjectFileName} under {repoRoot}");
                return 1;
            }

            var userSecretsId = ReadUserSecretsId(webProjectPath);
            if (string.IsNullOrWhiteSpace(userSecretsId))
            {
                Console.Error.WriteLine($"{WebProjectFileName} is missing <UserSecretsId>.");
                return 1;
            }

            var secretsPath = GetUserSecretsFilePath(userSecretsId);
            Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);

            var readerToken = CreateToken();
            var operatorToken = CreateToken();
            while (FixedTimeEquals(readerToken, operatorToken))
            {
                operatorToken = CreateToken();
            }

            MergeAndWriteSecrets(secretsPath, readerToken, operatorToken);
            readerToken = string.Empty;
            operatorToken = string.Empty;

            Console.WriteLine("API tokens written to the local .NET User Secrets key store.");
            Console.WriteLine("Values were NOT printed.");
            Console.WriteLine();
            Console.WriteLine("Preferred workflow: `dotnet run` on TestProject generates session");
            Console.WriteLine("tokens automatically and clears them when you stop the app (Ctrl+C).");
            Console.WriteLine();
            Console.WriteLine("Key store file (open to copy a plaintext hex token):");
            Console.WriteLine(secretsPath);
            Console.WriteLine();
            Console.WriteLine("Keys:");
            Console.WriteLine($"  {ReaderKey}");
            Console.WriteLine($"  {OperatorKey}");
            Console.WriteLine();
            Console.WriteLine("Wrong file warning: ignore ASP.NET DataProtection-Keys *.xml");
            Console.WriteLine("(those contain DPAPI blobs, not FileBrowser API tokens).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Token generation failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength)).ToLowerInvariant();

    private static bool FixedTimeEquals(string a, string b)
    {
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }

    private static string GetUserSecretsFilePath(string userSecretsId)
    {
        string root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets");
        return Path.Combine(root, userSecretsId, "secrets.json");
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

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tempPath = secretsPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Copy(tempPath, secretsPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static string ReadUserSecretsId(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("UserSecretsId").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, WebProjectFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return start;
    }
}
