using CognitivePlatform.Api.Domains.DailyRecord;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/daily")]
public sealed class DailyRecordController : ControllerBase
{
    private readonly IDailyRecordService _dailyRecordService;

    public DailyRecordController(IDailyRecordService dailyRecordService)
    {
        _dailyRecordService = dailyRecordService;
    }

    /// <summary>Returns today's daily record, or 404 if no plan has been submitted yet.</summary>
    [HttpGet("today")]
    public ActionResult<DailyRecord> GetToday()
    {
        var record = _dailyRecordService.GetToday();

        if (record is null)
            return NotFound("No daily record for today. Submit a Plan: to start your day.");

        return Ok(record);
    }

    /// <summary>Returns the daily record for the given date (yyyy-MM-dd), or 404 if none exists.</summary>
    [HttpGet("{date}")]
    public ActionResult<DailyRecord> GetByDate([FromRoute] string date)
    {
        if (DateOnly.TryParse(date, out var dateOnly)
                    .Not())
            return BadRequest("Date must be in yyyy-MM-dd format (e.g. 2026-04-19).");

        var record = _dailyRecordService.GetByDate(dateOnly);

        if (record is null)
            return NotFound($"No daily record for {date}.");

        return Ok(record);
    }

    /// <summary>
    /// Returns daily records whose date falls within [from, to] inclusive.
    /// Both parameters are required and must be in yyyy-MM-dd format.
    /// </summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<DailyRecord>> GetRange( [FromQuery] string? from
                                                             , [FromQuery] string? to )
    {
        var noFrom = from?.HasNoValue() ?? true;
        var noTo   = to?.HasNoValue()   ?? true;
        
        if (noFrom || noTo)
            return BadRequest("Both 'from' and 'to' query parameters are required (yyyy-MM-dd).");

        if (DateOnly.TryParse(from, out var fromDate).Not())
            return BadRequest("'from' must be in yyyy-MM-dd format.");

        if (DateOnly.TryParse(to, out var toDate).Not())
            return BadRequest("'to' must be in yyyy-MM-dd format.");

        if (fromDate > toDate)
            return BadRequest("'from' must be on or before 'to'.");

        var records = _dailyRecordService.GetRange(fromDate, toDate);

        return Ok(records);
    }
}
