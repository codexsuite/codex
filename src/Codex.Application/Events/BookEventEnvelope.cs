using Codex.Contracts.Events;

namespace Codex.Application.Events;

// One NOTIFY payload on BookEventsNotifier.Channel: the event in its wire shape (the "type" discriminator and, when
// set, clientId) plus the instance id of the process that published it, so that process can skip its own echo.
public sealed record BookEventEnvelope(Guid Origin, BookEvent Event);
