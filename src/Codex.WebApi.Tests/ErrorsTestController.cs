using System.ComponentModel.DataAnnotations;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Tests;

// Only loaded by ApiErrorTests, which adds this assembly as an application part of its own test host.
[Route("test-errors")]
public sealed class ErrorsTestController : ApiControllerBase
{
    [HttpPost("model")]
    public IActionResult Post(NewThing thing) => Ok(thing);

    [HttpGet("controller-conflict")]
    public IActionResult ThrowConflict() => throw ApiException.Conflict("Stale version.");

    // MvcServices: the service owns validation, so a non-nullable property is not implicitly required...
    [HttpPost("unchecked")]
    public IActionResult PostUnchecked(UncheckedThing thing) => Ok(thing);

    // ...and a null result is 200 with no body, not 204.
    [HttpGet("null")]
    public Task<NewThing?> GetNull() => Task.FromResult<NewThing?>(null);

    // ...and a blank query value reaches the action as itself, not null.
    [HttpGet("echo")]
    public IActionResult Echo(string? q) => Ok(new { q });

    public sealed record NewThing([Required(ErrorMessage = "Title is required.")] string Title);

    public sealed record UncheckedThing(string Title);
}
