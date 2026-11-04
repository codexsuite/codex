namespace Codex.Contracts.Manuscript;

// HeadBody stays in the scene (at Version); TailBody becomes the body of the new scene that follows it.
public sealed record SplitSceneRequest(string HeadBody, string TailBody, int Version);
