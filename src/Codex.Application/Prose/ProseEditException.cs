namespace Codex.Application.Prose;

// Thrown when an edit cannot be applied. The body is never partly edited: documents are immutable and an edit
// either returns a new document or throws this.
public sealed class ProseEditException(ProseEditFailure failure, string message) : Exception(message)
{
    public ProseEditFailure Failure { get; } = failure;
}
