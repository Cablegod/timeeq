﻿using FS.TimeTracking.Core.Interfaces.Application.Services.Administration;
using FS.TimeTracking.Core.Interfaces.Application.Services.Shared;
using FS.TimeTracking.Core.Interfaces.Repository.Services.Database;
using FS.TimeTracking.Core.Models.Application.Administration;
using FS.TimeTracking.Core.Models.Application.MasterData;
using FS.TimeTracking.Core.Models.Application.TimeTracking;
using FS.TimeTracking.Core.Models.Configuration;
using FS.TimeTracking.Core.Models.Filter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FS.TimeTracking.Application.Services.Administration;

/// <inheritdoc />
public class MaintenanceService : IMaintenanceService
{
    private readonly IDbRepository _dbRepository;
    private readonly IFilterFactory _filterFactory;
    private readonly TimeTrackingConfiguration _configuration;
    private readonly ILogger<MaintenanceService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaintenanceService"/> class.
    /// </summary>
    /// <param name="dbRepository">The database repository.</param>
    /// <param name="filterFactory">The filter factory.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <autogeneratedoc />
    public MaintenanceService(IDbRepository dbRepository, IFilterFactory filterFactory, IOptions<TimeTrackingConfiguration> configuration, ILogger<MaintenanceService> logger)
    {
        _dbRepository = dbRepository;
        _filterFactory = filterFactory;
        _logger = logger;
        _configuration = configuration.Value;
    }

    /// <inheritdoc />
    public async Task<JObject> ExportData(TimeSheetFilterSet filters)
    {
        var holidayFilter = await _filterFactory.CreateHolidayFilter(filters);
        var customerFilter = await _filterFactory.CreateCustomerFilter(filters);
        var projectFilter = await _filterFactory.CreateProjectFilter(filters);
        var activityFilter = await _filterFactory.CreateActivityFilter(filters);
        var orderFilter = await _filterFactory.CreateOrderFilter(filters);
        var timeSheetFilter = await _filterFactory.CreateTimeSheetFilter(filters);

        var databaseModelHash = await _dbRepository.GetDatabaseModelHash();
        var settings = await _dbRepository.Get((Setting x) => x, orderBy: o => o.OrderBy(x => x.Key));
        var holidays = await _dbRepository.Get((Holiday x) => x, holidayFilter, orderBy: o => o.OrderBy(x => x.StartDateLocal).ThenBy(x => x.Title));
        var customers = await _dbRepository.Get((Customer x) => x, customerFilter, orderBy: o => o.OrderBy(x => x.Title));
        var projects = await _dbRepository.Get((Project x) => x, projectFilter, orderBy: o => o.OrderBy(x => x.Title));
        var activities = await _dbRepository.Get((Activity x) => x, activityFilter, orderBy: o => o.OrderBy(x => x.Title));
        var orders = await _dbRepository.Get((Order x) => x, orderFilter, orderBy: o => o.OrderBy(x => x.StartDateLocal).ThenBy(x => x.Title));
        var timeSheets = await _dbRepository.Get((TimeSheet x) => x, timeSheetFilter, orderBy: o => o.OrderBy(x => x.StartDateLocal));

        var export = new DatabaseExport
        {
            DatabaseModelHash = databaseModelHash,
            Settings = settings,
            Holidays = holidays,
            Customers = customers,
            Projects = projects,
            Activities = activities,
            Orders = orders,
            TimeSheets = timeSheets,
        };

        return JObject.FromObject(export);
    }

    /// <inheritdoc />
    public async Task ImportData(JObject databaseExport)
    {
        var import = databaseExport.ToObject<DatabaseExport>();

        var currentModelHash = await _dbRepository.GetDatabaseModelHash();
        var importModelHash = import.DatabaseModelHash;
        if (!currentModelHash.Equals(importModelHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The database schema of data to import must match current database schema. Current database version {currentModelHash}, database version to import: {importModelHash}");

        await TruncateData();

        _logger.LogInformation("Import database ...");

        using var scope = _dbRepository.CreateTransactionScope();

        await _dbRepository.BulkAddRange(import.Settings);
        await _dbRepository.BulkAddRange(import.Holidays);
        await _dbRepository.BulkAddRange(import.Customers);
        await _dbRepository.BulkAddRange(import.Projects);
        await _dbRepository.BulkAddRange(import.Activities);
        await _dbRepository.BulkAddRange(import.Orders);
        await _dbRepository.BulkAddRange(import.TimeSheets);

        scope.Complete();
    }

    /// <inheritdoc />
    public async Task TruncateData()
    {
        _logger.LogInformation("Truncate database...");

        using var scope = _dbRepository.CreateTransactionScope();

        await _dbRepository.BulkRemove<TimeSheet>();
        await _dbRepository.BulkRemove<Order>();
        await _dbRepository.BulkRemove<Activity>();
        await _dbRepository.BulkRemove<Project>();
        await _dbRepository.BulkRemove<Customer>();
        await _dbRepository.BulkRemove<Holiday>();
        await _dbRepository.BulkRemove<Setting>();

        scope.Complete();
    }

    /// <inheritdoc />
    public async Task ResetDatabase()
    {
        if (!_configuration.DataReset.Enabled)
            return;

        _logger.LogInformation("Start database reset...");

        _logger.LogInformation($"Read database backup from '{_configuration.DataReset.TestDatabaseSource}' ...");
        var importDataJson = await File.ReadAllTextAsync(_configuration.DataReset.TestDatabaseSource);
        var importData = JObject.Parse(importDataJson);

        _logger.LogInformation("Truncate database...");
        await TruncateData();

        _logger.LogInformation($"Import database backup");
        await ImportData(importData);

        if (_configuration.DataReset.AdjustTimeStamps)
        {
            _logger.LogInformation("Adjust timestamps...");
            await AdjustTimeStamps();
        }

        _logger.LogInformation("Database reset completed.");
    }

    private async Task AdjustTimeStamps()
    {
        var minTimeSheetStartDate = await _dbRepository.Min((TimeSheet timeSheet) => timeSheet.StartDateLocal);
        var maxTimeSheetStartDate = await _dbRepository.Max((TimeSheet timeSheet) => timeSheet.StartDateLocal);
        var timeSheetsPeriodInMonths = GetAbsoluteMonth(maxTimeSheetStartDate) - GetAbsoluteMonth(minTimeSheetStartDate) + 1;

        var thisYear = DateTime.UtcNow.Year;
        var firstOfThisMonth = new DateTime(thisYear, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var newTimeSheetStartDate = firstOfThisMonth.AddMonths(-timeSheetsPeriodInMonths);

        var monthsToShift = GetAbsoluteMonth(newTimeSheetStartDate) - GetAbsoluteMonth(minTimeSheetStartDate);
        var yearsToShift = newTimeSheetStartDate.Year - minTimeSheetStartDate.Year;

        await _dbRepository
            .BulkUpdate(
                (TimeSheet timeSheet) => true,
                timeSheet => new TimeSheet
                {
                    StartDateLocal = timeSheet.StartDateLocal.AddMonths(monthsToShift),
                    EndDateLocal = timeSheet.EndDateLocal.Value.AddMonths(monthsToShift),
                    Created = newTimeSheetStartDate,
                    Modified = newTimeSheetStartDate,
                }
            );

        await _dbRepository
            .BulkUpdate(
                (Order order) => true,
                order => new Order
                {
                    StartDateLocal = order.StartDateLocal.AddMonths(monthsToShift),
                    DueDateLocal = order.DueDateLocal.AddMonths(monthsToShift),
                    Created = newTimeSheetStartDate,
                    Modified = newTimeSheetStartDate,
                }
            );

        await _dbRepository
            .BulkUpdate(
                (Holiday holiday) => true,
                holiday => new Holiday
                {
                    StartDateLocal = holiday.StartDateLocal.AddYears(yearsToShift),
                    EndDateLocal = holiday.EndDateLocal.AddYears(yearsToShift),
                    Created = newTimeSheetStartDate,
                    Modified = newTimeSheetStartDate,
                }
            );
    }

    private static int GetAbsoluteMonth(DateTime date)
        => (date.Year * 12) + date.Month;
}