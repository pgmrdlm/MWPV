using System.Text;
using Microsoft.Data.Sqlite;

namespace KeyFileLogic;

public static class KeyFileStore
{
    private const string TableName = "KeyFilePayload";

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS KeyFilePayload (
            KeyFilePayloadId INTEGER PRIMARY KEY,
            Value BLOB NOT NULL
        );
        """;

    private const string SelectAllPayloadsSql = """
        SELECT KeyFilePayloadId, Value
        FROM KeyFilePayload
        ORDER BY KeyFilePayloadId;
        """;

    private const string SelectPayloadSql = """
        SELECT Value
        FROM KeyFilePayload
        WHERE KeyFilePayloadId = $id;
        """;

    private const string UpsertPayloadSql = """
        INSERT INTO KeyFilePayload (KeyFilePayloadId, Value)
        VALUES ($id, $value)
        ON CONFLICT(KeyFilePayloadId) DO UPDATE SET
            Value = excluded.Value;
        """;

    private const string ValidateTableSql = """
        SELECT name
        FROM sqlite_master
        WHERE type = 'table'
          AND name = 'KeyFilePayload';
        """;

    private const string TableInfoSql = "PRAGMA table_info(KeyFilePayload);";

    static KeyFileStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public static string BuildKeyFilePath(
        string keyFileDirectory,
        string keyFileName)
    {
        if (string.IsNullOrWhiteSpace(keyFileDirectory))
            throw new ArgumentException("Key file directory cannot be null or empty.", nameof(keyFileDirectory));

        if (string.IsNullOrWhiteSpace(keyFileName))
            throw new ArgumentException("Key file name cannot be null or empty.", nameof(keyFileName));

        if (keyFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Key file name contains invalid file-name characters.", nameof(keyFileName));

        if (!string.Equals(keyFileName, Path.GetFileName(keyFileName), StringComparison.Ordinal))
            throw new ArgumentException("Key file name must be a file name, not a path.", nameof(keyFileName));

        if (keyFileName is "." or "..")
            throw new ArgumentException("Key file name cannot contain path traversal.", nameof(keyFileName));

        try
        {
            var fullDirectory = Path.GetFullPath(keyFileDirectory);
            return Path.GetFullPath(Path.Combine(fullDirectory, keyFileName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The combined key file path is invalid.", nameof(keyFileDirectory), ex);
        }
    }

    public static void Initialize(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword)
    {
        var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);

        using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWriteCreate);
        InitializeSchema(connection);
        ValidateSchemaOrThrow(connection);
    }

    public static IReadOnlyList<KeyFilePayload> LoadAllPayloads(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword)
    {
        var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);

        using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWrite);
        ValidateSchemaOrThrow(connection);

        using var command = connection.CreateCommand();
        command.CommandText = SelectAllPayloadsSql;

        var payloads = new List<KeyFilePayload>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var payloadId = reader.GetInt64(0);
            var value = (byte[])reader["Value"];
            payloads.Add(new KeyFilePayload(payloadId, value));
        }

        return payloads;
    }

    public static byte[] ReadPayloadBytes(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword,
        long payloadId)
    {
        ValidatePayloadId(payloadId);
        var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);

        using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWrite);
        ValidateSchemaOrThrow(connection);

        using var command = connection.CreateCommand();
        command.CommandText = SelectPayloadSql;
        command.Parameters.AddWithValue("$id", payloadId);

        var value = command.ExecuteScalar();
        if (value is null || value == DBNull.Value)
            throw new KeyNotFoundException($"Payload ID {payloadId} was not found in the key file.");

        return (byte[])value;
    }

    public static void SavePayload(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword,
        long payloadId,
        byte[] value)
    {
        ValidatePayloadId(payloadId);
        ArgumentNullException.ThrowIfNull(value);
        var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);

        using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWriteCreate);
        InitializeSchema(connection);

        using var command = connection.CreateCommand();
        command.CommandText = UpsertPayloadSql;
        AddPayloadParameters(command, payloadId, value);
        command.ExecuteNonQuery();
    }

    public static void SaveAllPayloads(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword,
        IReadOnlyList<KeyFilePayload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        foreach (var payload in payloads)
        {
            ValidatePayloadId(payload.PayloadId);
            ArgumentNullException.ThrowIfNull(payload.Value);
        }

        var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);

        using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWriteCreate);
        InitializeSchema(connection);

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var payload in payloads)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = UpsertPayloadSql;
                AddPayloadParameters(command, payload.PayloadId, payload.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static bool CanOpenAndValidateSchema(
        string keyFileDirectory,
        string keyFileName,
        char[] keyPassword,
        out string reason)
    {
        reason = string.Empty;

        try
        {
            var keyFilePath = BuildAndValidateExistingDirectory(keyFileDirectory, keyFileName);
            if (!File.Exists(keyFilePath))
            {
                reason = "Key file does not exist.";
                return false;
            }

            using var connection = OpenConnection(keyFilePath, keyPassword, SqliteOpenMode.ReadWrite);
            ValidateSchemaOrThrow(connection);
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static string BuildAndValidateExistingDirectory(string keyFileDirectory, string keyFileName)
    {
        var keyFilePath = BuildKeyFilePath(keyFileDirectory, keyFileName);
        var directory = Path.GetDirectoryName(keyFilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("Key file directory does not exist.");

        return keyFilePath;
    }

    private static SqliteConnection OpenConnection(
        string keyFilePath,
        char[] keyPassword,
        SqliteOpenMode mode)
    {
        if (keyPassword is null || keyPassword.Length == 0)
            throw new ArgumentException("Key file password cannot be null or empty.", nameof(keyPassword));

        string? password = null;
        try
        {
            password = new string(keyPassword);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = keyFilePath,
                Mode = mode,
                Password = password
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
        finally
        {
            if (password is not null)
                WipeString(ref password);
        }
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CreateTableSql;
        command.ExecuteNonQuery();
    }

    private static void ValidateSchemaOrThrow(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = ValidateTableSql;
            var tableName = command.ExecuteScalar() as string;
            if (!string.Equals(tableName, TableName, StringComparison.Ordinal))
                throw new InvalidOperationException("Key file schema is missing the KeyFilePayload table.");
        }

        var columns = new Dictionary<string, (string Type, bool NotNull, bool PrimaryKey)>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = TableInfoSql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var notNull = reader.GetInt32(3) == 1;
                var primaryKey = reader.GetInt32(5) == 1;
                columns[name] = (type, notNull, primaryKey);
            }
        }

        if (!columns.TryGetValue("KeyFilePayloadId", out var idColumn) ||
            !idColumn.PrimaryKey ||
            !string.Equals(idColumn.Type, "INTEGER", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Key file schema has an invalid KeyFilePayloadId column.");
        }

        if (!columns.TryGetValue("Value", out var valueColumn) ||
            !valueColumn.NotNull ||
            !string.Equals(valueColumn.Type, "BLOB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Key file schema has an invalid Value column.");
        }
    }

    private static void AddPayloadParameters(SqliteCommand command, long payloadId, byte[] value)
    {
        command.Parameters.AddWithValue("$id", payloadId);

        var valueParameter = command.CreateParameter();
        valueParameter.ParameterName = "$value";
        valueParameter.SqliteType = SqliteType.Blob;
        valueParameter.Value = value;
        command.Parameters.Add(valueParameter);
    }

    private static void ValidatePayloadId(long payloadId)
    {
        if (payloadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(payloadId), payloadId, "Payload ID must be positive.");
    }

    private static void WipeString(ref string value)
    {
        unsafe
        {
            fixed (char* chars = value)
            {
                for (var i = 0; i < value.Length; i++)
                    chars[i] = '\0';
            }
        }

        value = string.Empty;
    }
}
