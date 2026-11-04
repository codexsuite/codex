namespace Codex.Application.Features.Print.Render;

// A step of a PDF render, for a job's export_job.step / progress and its JobProgress event: Step is the label
// ("Assembling", "Placing images", "Converting", "Typesetting"), Fraction 0 to 1.
public sealed record RenderProgress(string Step, double Fraction);
