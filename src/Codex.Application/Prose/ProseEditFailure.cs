namespace Codex.Application.Prose;

public enum ProseEditFailure
{
    // The beat, addition or reference use id is not in the body.
    TargetNotFound,

    // The passage is not in the body, or it sits somewhere a reference mark cannot go.
    PassageNotWrappable,

    // The text to insert is empty.
    EmptyInsert,
}
