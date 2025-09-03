using System.Data.Common;
using MWPV.Data.Abstractions;
using MWPV.Data.Models;
using MWPV.Data.Internal; // for SqlCatalog / Require()

namespace MWPV.Data.Repositories;

public sealed class CategoryRepository
{
    private readonly ISqlExecutor _db;

    public CategoryRepository(ISqlExecutor db) => _db = db;

    // ---- Reads -------------------------------------------------------------

    public Category? GetById(long id) =>
        _db.QuerySingle<Category>(
            SqlCatalog.Require("Category_SelectById.sql"),
            new { id });

    public IEnumerable<Category> ListAll() =>
        _db.Query<Category>(
            SqlCatalog.Require("Category_SelectAll.sql"));

    public IEnumerable<Category> ListActive() =>
        _db.Query<Category>(
            SqlCatalog.Require("Category_SelectActive.sql"));

    public bool ExistsByName(string name) =>
        _db.QuerySingle<long>(
            SqlCatalog.Require("Category_ExistsByName.sql"),
            new { name }) > 0;

    // ---- Writes ------------------------------------------------------------

    public long Add(Category c, DbTransaction? tx = null) =>
        _db.QuerySingle<long>(
            SqlCatalog.Require("Category_Insert.sql"),
            c, tx);

    public int Update(Category c, DbTransaction? tx = null) =>
        _db.Execute(
            SqlCatalog.Require("Category_Update.sql"),
            c, tx);

    public int Deactivate(long id, DbTransaction? tx = null) =>
        _db.Execute(
            SqlCatalog.Require("Category_Deactivate.sql"),
            new { id }, tx);

    public int Delete(long id, DbTransaction? tx = null) =>
        _db.Execute(
            SqlCatalog.Require("Category_Delete.sql"),
            new { id }, tx);
}
