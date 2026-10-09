using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace eldenRingApi.Controllers;

[ApiController]
[Route("api/bosses")]
public sealed class BossesController(IBossesService bossesService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] CollectionQuery query, CancellationToken cancellationToken)
    {
        if (!query.IsValid) return BadRequest(new { success = false });
        var result = await bossesService.GetAllAsync(query, cancellationToken);
        return Ok(new { success = true, count = result.Items.Count, total = result.Total, data = result.Items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        var item = await bossesService.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound(new { success = false }) : Ok(new { success = true, count = 1, data = item });
    }
}
