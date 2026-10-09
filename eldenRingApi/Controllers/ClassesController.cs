using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace eldenRingApi.Controllers;
[ApiController, Route("api/classes")]
public sealed class ClassesController(IClassesService service) : ControllerBase { [HttpGet] public async Task<IActionResult> GetAll([FromQuery] CollectionQuery query, CancellationToken ct) { if (!query.IsValid) return BadRequest(new { success = false }); var result = await service.GetAllAsync(query, ct); return Ok(new { success = true, count = result.Items.Count, total = result.Total, data = result.Items }); } [HttpGet("{id}")] public async Task<IActionResult> GetById(string id, CancellationToken ct) { var item = await service.GetByIdAsync(id, ct); return item is null ? NotFound(new { success = false }) : Ok(new { success = true, count = 1, data = item }); } }
