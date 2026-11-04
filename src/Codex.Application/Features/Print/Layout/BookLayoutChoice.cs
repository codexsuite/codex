using Agoda.IoC.Core;
using Codex.Contracts.Print;

namespace Codex.Application.Features.Print.Layout;

// IBookLayoutChoice for other features: the Layout view's own SelectAsync, so the checks stay in one place.
[RegisterSingleton]
public sealed class BookLayoutChoice(ILayoutService layout) : IBookLayoutChoice
{
    public Task ChooseAsync(Guid bookId, LayoutSelectionRequest request, CancellationToken cancellationToken)
        => layout.SelectAsync(bookId, request, cancellationToken);
}
