﻿using AutoMapper;
using AutoMapper.QueryableExtensions;
using FS.TimeTracking.Core.Extensions;
using FS.TimeTracking.Core.Interfaces.Models;
using FS.TimeTracking.Core.Interfaces.Repository.Services.Database;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace FS.TimeTracking.Repository.Services;

/// <inheritdoc />
public partial class DbRepository<TDbContext> : IDbRepository where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly AsyncLock _saveChangesLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DbRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="mapper">The mapper to project models to desired result.</param>
    /// <autogeneratedoc />
    public DbRepository(TDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<List<TResult>> Get<TEntity, TResult>(
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includes = null,
        bool distinct = false,
        int? skip = null,
        int? take = null,
        bool tracked = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x.Select(select), where, orderBy, null, includes, distinct, skip, take, tracked)
            .ToListAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<List<TDto>> Get<TEntity, TDto>(
        Expression<Func<TEntity, bool>> where = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includes = null,
        bool distinct = false,
        int? skip = null,
        int? take = null,
        bool tracked = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x, where, orderBy, null, includes, distinct, skip, take, tracked)
            .ProjectTo<TDto>(_mapper.ConfigurationProvider)
            .ToListAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<List<TResult>> GetGrouped<TEntity, TGroupByKey, TResult>(
        Expression<Func<TEntity, TGroupByKey>> groupBy,
        Expression<Func<IGrouping<TGroupByKey, TEntity>, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        Func<IQueryable<TResult>, IOrderedQueryable<TResult>> orderBy = null,
        string[] includes = null,
        bool distinct = false,
        int? skip = null,
        int? take = null,
        bool tracked = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x.GroupBy(groupBy).Select(select), where, null, orderBy, includes, distinct, skip, take, tracked)
            .ToListAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<TResult> FirstOrDefault<TEntity, TResult>(
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includes = null,
        int? skip = null,
        bool tracked = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x.Select(select), where, orderBy, null, includes, false, skip, null, tracked)
            .FirstOrDefaultAsyncEF(cancellationToken);

    /// <inheritdoc />
    public Task<TDto> FirstOrDefault<TEntity, TDto>(
        Expression<Func<TEntity, bool>> where = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includes = null,
        int? skip = null,
        bool tracked = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => GetInternal(x => x, where, orderBy, null, includes, false, skip, null, tracked)
            .ProjectTo<TDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<long> Count<TEntity, TResult>(
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        bool distinct = false,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x.Select(select), where, null, null, null, distinct, null, null, false)
            .LongCountAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<long> Sum<TEntity>(
        Expression<Func<TEntity, long>> select,
        Expression<Func<TEntity, bool>> where = null,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x, where, null, null, null, false, null, null, false)
            .SumAsyncEF(select, cancellationToken);

    /// <inheritdoc />
    public async Task<TResult> Min<TEntity, TResult>(
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x, where, null, null, null, false, null, null, false)
            .MinAsyncEF(select, cancellationToken);

    /// <inheritdoc />
    public async Task<TResult> Max<TEntity, TResult>(
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>> where = null,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => await GetInternal(x => x, where, null, null, null, false, null, null, false)
            .MaxAsyncEF(select, cancellationToken);

    /// <inheritdoc />
    public Task<bool> Exists<TEntity>(
        Expression<Func<TEntity, bool>> where = null,
        CancellationToken cancellationToken = default
    ) where TEntity : class
        => GetInternal(x => x.Select(_ => default(object)), where, null, null, null, false, null, null, false)
            .AnyAsyncEF(cancellationToken);

    /// <inheritdoc />
    public async Task<TEntity> Add<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class, IEntityModel
        => (await AddRange(new[] { entity }.ToList(), cancellationToken)).First();

    /// <inheritdoc />
    public async Task<List<TEntity>> AddRange<TEntity>(List<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class, IEntityModel
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entity in entities)
        {
            if (entity is IIdEntityModel idEntityModel && idEntityModel.Id == Guid.Empty)
                idEntityModel.Id = Guid.NewGuid();

            entity.Created = utcNow;
            entity.Modified = utcNow;
        }

        await _dbContext.AddRangeAsync(entities, cancellationToken);
        return entities;
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetPendingMigrations(CancellationToken cancellationToken = default)
        => _dbContext.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetAppliedMigrations(CancellationToken cancellationToken = default)
        => _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<string> GetDatabaseModelHash(CancellationToken cancellationToken = default)
    {
        var databaseModel = _dbContext.GetService<IDesignTimeModel>().Model.ToDebugString();
        databaseModel = LineEndings().Replace(databaseModel, "\n");
        return Task.FromResult(databaseModel.HashSHA256());
    }

    /// <inheritdoc />
    public async Task<List<TEntity>> BulkAddRange<TEntity>(List<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class, IEntityModel
    {
        var bulkCopyOptions = new BulkCopyOptions { KeepIdentity = true, CheckConstraints = true };
        await _dbContext.BulkCopyAsync(bulkCopyOptions, entities, cancellationToken);
        return entities;
    }

    /// <inheritdoc />
    public TEntity Update<TEntity>(TEntity entity) where TEntity : class, IEntityModel
    {
        entity.Modified = DateTime.UtcNow;
        var result = _dbContext.Update(entity);
        result.Property(x => x.Created).IsModified = false;
        return result.Entity;
    }

    /// <inheritdoc />
    public async Task<int> BulkUpdate<TEntity>(
        Expression<Func<TEntity, bool>> where,
        Expression<Func<TEntity, TEntity>> setter
    ) where TEntity : class
    {
        var query = GetInternal(e => e, where, null, null, null, false, null, null, false);
        return await query.UpdateAsync(setter);
    }

    /// <inheritdoc />
    public TEntity Remove<TEntity>(TEntity entity) where TEntity : class
        => _dbContext.Remove(entity).Entity;

    /// <inheritdoc />
    public List<TEntity> Remove<TEntity>(List<TEntity> entities) where TEntity : class
        => entities
            .Select(entity => _dbContext.Remove(entity).Entity)
            .ToList();

    /// <inheritdoc />
    public async Task<int> BulkRemove<TEntity>(Expression<Func<TEntity, bool>> where = null) where TEntity : class
    {
        var query = _dbContext
            .Set<TEntity>()
            .AsQueryable();

        if (where != null)
            query = query.Where(where);

        return await query.DeleteAsync();
    }

    /// <inheritdoc />
    public TransactionScope CreateTransactionScope()
        => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

    /// <inheritdoc />
    public async Task<int> SaveChanges(CancellationToken cancellationToken = default)
    {
        using var async = await _saveChangesLock.LockAsync();
        var result = await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return result;
    }

    private IQueryable<TResult> GetInternal<TEntity, TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>> select,
        Expression<Func<TEntity, bool>> where,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> entityOrderBy,
        Func<IQueryable<TResult>, IOrderedQueryable<TResult>> resultOrderBy,
        string[] includes,
        bool distinct,
        int? skip,
        int? take,
        bool tracked
    ) where TEntity : class
    {
        var query = _dbContext
            .Set<TEntity>()
            .AsQueryable();

        if (!tracked)
            query = query.AsNoTracking();

        if (where != null)
            query = query.Where(where);

        if (includes != null)
            foreach (var include in includes)
                query = query.Include(include);

        if (entityOrderBy != null)
            query = entityOrderBy(query);

        var result = select(query);

        if (resultOrderBy != null)
            result = resultOrderBy(result);

        if (distinct)
            result = result.Distinct();

        if (skip != null)
            result = result.Skip(skip.Value);

        if (take != null)
            result = result.Take(take.Value);

        return result;
    }

    [GeneratedRegex("(\r\n|\r|\n)")]
    private static partial Regex LineEndings();
}