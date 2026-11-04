using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Infrastructure;

// Shared HTTP-layer plumbing so every action stays a one-line delegation to a
// service: ApiException→status mapping is ApiErrorServices; response helpers here.
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected CreatedAtActionResult CreatedAtGet<T>(T viewModel) where T : IHasId
        => CreatedAtAction("Get", new { id = viewModel.Id }, viewModel);

    protected async Task<NoContentResult> NoContentAfter(Task task)
    {
        await task;
        return NoContent();
    }
}
