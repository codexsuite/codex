namespace Codex.Contracts.Import;

// One line of the Import dialog's step 2: how many of something will be created and where it lands ("Plan · acts").
public sealed record ImportCountDto(string Item, int Count, string Destination);
