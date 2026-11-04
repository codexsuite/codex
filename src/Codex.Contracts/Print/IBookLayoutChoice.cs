namespace Codex.Contracts.Print;

// Picks a book's layout template, trim and options as the Layout view does (ILayoutService.SelectAsync), for features
// outside the print lane (the import sets Fantasy RPG with drop caps for a dndbook source). 404 unknown book or
// template; 400 a trim or option the template does not have.
public interface IBookLayoutChoice
{
    Task ChooseAsync(Guid bookId, LayoutSelectionRequest request, CancellationToken cancellationToken);
}
