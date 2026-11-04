namespace Codex.Application.Features.Print.Export;

// A manuscript copy ready to store: its bytes, content type, file name and what it left out.
public sealed record BuiltManuscript(byte[] Bytes, string Mime, string Filename, IReadOnlyList<string> Notes);
