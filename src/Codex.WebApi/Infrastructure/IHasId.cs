namespace Codex.WebApi.Infrastructure;

// Lets ApiControllerBase build CreatedAtAction responses from any view model.
public interface IHasId
{
    int Id { get; }
}
