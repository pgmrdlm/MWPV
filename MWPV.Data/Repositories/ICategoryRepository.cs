using MWPV.Data.Models;

namespace MWPV.Data.Repositories;

/// <summary>
/// CRUD contract for Category records.
/// Matches the schema columns: Category_Key, Category_Name, Category_Description, IsActive.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Adds a new category and returns its generated key.</summary>
    Task<long> AddAsync(Category category);

    /// <summary>Gets a category by primary key; returns null if not found.</summary>
    Task<Category?> GetByIdAsync(long id);

    /// <summary>Returns all categories ordered by name.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync();

    /// <summary>Updates an existing category; returns affected rows (0 or 1).</summary>
    Task<int> UpdateAsync(Category category);

    /// <summary>Deletes a category by key; returns affected rows (0 or 1).</summary>
    Task<int> DeleteAsync(long id);
}
