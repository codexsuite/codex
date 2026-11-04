namespace Codex.Application.Features.Import;

// A knowledge base category the import creates with Details fields (kb_category.field_defs, in order) and a tag.
// Entries name it by Name; a category no ImportCategoryPlan describes gets no fields.
public sealed record ImportCategoryPlan(string Name, string? Tag, IReadOnlyList<ImportFieldPlan> Fields);
