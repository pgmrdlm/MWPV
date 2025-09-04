using MWPV.Data.Abstractions;
using MWPV.Data.Models;

namespace MWPV.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ICategoryRepository"/>.
/// Uses parameterized commands and maps to schema columns:
/// Category_Key, Category_Name, Category_Description, IsActive.
/// </summary>
public sealed class CategoryRepository : ICategoryRepository
{
    private readonly IConnectionFactory _factory;
    private readonly IDataLogSink _log;

    public CategoryRepository(IConnectionFactory factory, IDataLogSink log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<long> AddAsync(Category category)
    {
        const string Sql = @"
INSERT INTO Categories (Category_Name, Category_Description, IsActive)
VALUES ($name, $desc, $active);
SELECT last_insert_rowid();";

        try
        {
            await using var conn = _factory.CreateConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Sql;
            cmd.Parameters.AddWithValue("$name", category.Category_Name);
            cmd.Parameters.AddWithValue("$desc", (object?)category.Category_Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$active", category.IsActive);

            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            var id = Convert.ToInt64(result);
            _log.Info("CategoryRepository.Add.Success", new { id, category.Category_Name });
            return id;
        }
        catch (Exception ex)
        {
            _log.Error("CategoryRepository.Add.Failed", new { category.Category_Name }, ex);
            throw;
        }
    }

    public async Task<Category?> GetByIdAsync(long id)
    {
        const string Sql = @"
SELECT Category_Key, Category_Name, Category_Description, IsActive
FROM Categories
WHERE Category_Key = $id;";

        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Sql;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new Category
            {
                Category_Key = reader.GetInt64(0),
                Category_Name = reader.GetString(1),
                Category_Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive = reader.GetBoolean(3)
            };
        }

        return null;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync()
    {
        const string Sql = @"
SELECT Category_Key, Category_Name, Category_Description, IsActive
FROM Categories
ORDER BY Category_Name;";

        var list = new List<Category>();

        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Sql;

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new Category
            {
                Category_Key = reader.GetInt64(0),
                Category_Name = reader.GetString(1),
                Category_Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive = reader.GetBoolean(3)
            });
        }

        return list;
    }

    public async Task<int> UpdateAsync(Category category)
    {
        const string Sql = @"
UPDATE Categories
SET Category_Name = $name,
    Category_Description = $desc,
    IsActive = $active
WHERE Category_Key = $id;";

        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Sql;
        cmd.Parameters.AddWithValue("$id", category.Category_Key);
        cmd.Parameters.AddWithValue("$name", category.Category_Name);
        cmd.Parameters.AddWithValue("$desc", (object?)category.Category_Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$active", category.IsActive);

        var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        _log.Info("CategoryRepository.Update", new { category.Category_Key, affected });
        return affected;
    }

    public async Task<int> DeleteAsync(long id)
    {
        const string Sql = @"DELETE FROM Categories WHERE Category_Key = $id;";

        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Sql;
        cmd.Parameters.AddWithValue("$id", id);

        var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        _log.Info("CategoryRepository.Delete", new { id, affected });
        return affected;
    }
}
