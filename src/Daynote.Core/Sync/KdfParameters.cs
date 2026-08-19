using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

public enum KdfAlgorithm
{
    Argon2id,
    Pbkdf2Sha256,
}

/// <summary>
/// The client-side key-derivation parameters, stored on the server verbatim and echoed back at
/// login so a client can re-derive with the parameters in force when the account was created.
/// </summary>
/// <remarks>
/// The server treats this as an opaque object it only shape-checks (see
/// <c>cloud/worker/src/validate.ts</c>); the wire JSON is defined here and nowhere else. Raising the
/// cost later means writing new parameters at the next password change, so old accounts keep working
/// with the values they were created with.
/// </remarks>
public readonly record struct KdfParameters
{
    private const string Argon2idName = "argon2id";
    private const string Pbkdf2Name = "pbkdf2-sha256";

    /// <summary>Current defaults: 64 MiB, 3 passes, 4 lanes — the OWASP-recommended Argon2id profile.</summary>
    public static KdfParameters Argon2idDefault { get; } = new(KdfAlgorithm.Argon2id, 65_536, 3, 4);

    /// <summary>
    /// Fallback for a build that cannot take the Argon2id dependency. 600 000 iterations is the OWASP
    /// figure for PBKDF2-HMAC-SHA256; it is meaningfully weaker than Argon2id against GPU attack,
    /// which matters here because this key guards ciphertext held on someone else's server.
    /// </summary>
    public static KdfParameters Pbkdf2Default { get; } = new(KdfAlgorithm.Pbkdf2Sha256, 0, 600_000, 0);

    private KdfParameters(KdfAlgorithm algorithm, int memoryKib, int iterations, int parallelism)
    {
        Algorithm = algorithm;
        MemoryKib = memoryKib;
        Iterations = iterations;
        Parallelism = parallelism;
    }

    public KdfAlgorithm Algorithm { get; }

    /// <summary>Argon2id memory cost in KiB. Zero for PBKDF2.</summary>
    public int MemoryKib { get; }

    /// <summary>Argon2id passes, or PBKDF2 iterations.</summary>
    public int Iterations { get; }

    /// <summary>Argon2id lanes. Zero for PBKDF2.</summary>
    public int Parallelism { get; }

    /// <summary>The wire form: <c>{"kdf":"argon2id","m":65536,"t":3,"p":4,"v":1}</c>.</summary>
    public string ToJson()
    {
        JsonObject json = Algorithm == KdfAlgorithm.Argon2id
            ? new JsonObject
            {
                ["kdf"] = Argon2idName,
                ["m"] = MemoryKib,
                ["t"] = Iterations,
                ["p"] = Parallelism,
                ["v"] = 1,
            }
            : new JsonObject
            {
                ["kdf"] = Pbkdf2Name,
                ["i"] = Iterations,
                ["v"] = 1,
            };

        return json.ToJsonString();
    }

    public static DomainResult<KdfParameters> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("KDF parameters are required.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Invalid("KDF parameters must be JSON.");
        }

        if (root is not JsonObject obj)
        {
            return Invalid("KDF parameters must be a JSON object.");
        }

        string? kdf = obj["kdf"]?.GetValue<string>();
        return kdf switch
        {
            Argon2idName => ParseArgon2id(obj),
            Pbkdf2Name => ParsePbkdf2(obj),
            _ => Invalid($"Unsupported KDF '{kdf}'."),
        };
    }

    private static DomainResult<KdfParameters> ParseArgon2id(JsonObject obj)
    {
        if (!TryInt(obj, "m", out int memory) ||
            !TryInt(obj, "t", out int iterations) ||
            !TryInt(obj, "p", out int parallelism))
        {
            return Invalid("Argon2id parameters must include integer m, t, and p.");
        }

        // Bounds exist to stop a hostile or corrupt server from handing back parameters that either
        // weaken the derivation to nothing or wedge the app allocating gigabytes at the login screen.
        if (memory is < 8_192 or > 1_048_576)
        {
            return Invalid("Argon2id memory must be between 8 MiB and 1 GiB.");
        }
        if (iterations is < 1 or > 16)
        {
            return Invalid("Argon2id iterations must be between 1 and 16.");
        }
        if (parallelism is < 1 or > 16)
        {
            return Invalid("Argon2id parallelism must be between 1 and 16.");
        }

        return DomainResult<KdfParameters>.Success(
            new KdfParameters(KdfAlgorithm.Argon2id, memory, iterations, parallelism));
    }

    private static DomainResult<KdfParameters> ParsePbkdf2(JsonObject obj)
    {
        if (!TryInt(obj, "i", out int iterations))
        {
            return Invalid("PBKDF2 parameters must include an integer i.");
        }

        if (iterations is < 100_000 or > 10_000_000)
        {
            return Invalid("PBKDF2 iterations must be between 100 000 and 10 000 000.");
        }

        return DomainResult<KdfParameters>.Success(
            new KdfParameters(KdfAlgorithm.Pbkdf2Sha256, 0, iterations, 0));
    }

    private static bool TryInt(JsonObject obj, string name, out int value)
    {
        value = 0;
        JsonNode? node = obj[name];
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<int>();
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static DomainResult<KdfParameters> Invalid(string message) =>
        DomainResult<KdfParameters>.Failure(DomainErrorCode.InvalidKdfParameters, message);

    public override string ToString() => Algorithm == KdfAlgorithm.Argon2id
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"argon2id(m={MemoryKib},t={Iterations},p={Parallelism})")
        : string.Create(CultureInfo.InvariantCulture, $"pbkdf2-sha256(i={Iterations})");
}
