namespace Codex.Application.Mcp;

// One step that reverses part of an MCP write, stored in change_set.undo and run by the undo tool in reverse order.
// Op names the command it calls:
//   prose          restore Body to scene Id, which the write left at Version (conflict if it has moved on since)
//   move_act       put act Id after After
//   move_chapter   put chapter Id in act Parent after After
//   move_scene     put scene Id in chapter Parent after After
//   archive_scene / restore_scene
//   archive_chapter / restore_chapter
//   archive_act / restore_act
//   patch_scene    set scene Id's Title, Summary, Status and Body (its plan notes) back ("" clears, null leaves)
//   patch_chapter  set chapter Id's Title, Summary, Body (its notes) and Flag (is numbered) back ("" clears)
//   patch_act      set act Id's Title back ("" leaves it untitled)
//   set_labels     set scene Id's labels back to Labels
//   discard_draft  discard generation Id (undoes submit_draft and draft_prose)
//   undo_apply     undo applying generation Id, which left scene Parent at Version
//   restore_draft  bring discarded generation Id back to pending
//   unlink_scene / link_scene        scene Parent and entry Id
//   beat_context   set beat Parent's context entries back to Labels
//   patch_book_settings  set book Id's Title, Summary (author), Status, Version (target words), Body (style guide), Subtitle and Description back
//   set_layout     put book Id's layout back: template Title, trim Summary, options Body (JSON object)
//   patch_entry    set entry Id's fields back (Title = name, Summary, Body, Texts = aliases, Flag = always in context, Fields; Tags and Exclude = tags and excluded-from-auto-context, when the write changed them)
//   archive_entry  archive an entry the write created
//   archive_source        archive source Id (a source cite_source or add_source added)
//   restore_source        bring source Id back from archive_source (the tool), and point the open quote flags in Labels back at it
//   patch_source          set source Id's fields back (Body = the details as JSON, Parent = its Codex entry, null = none)
//   set_use_kind          set use Id's kind back to Title
//   set_use_locator       set use Id's locator back to Title ("" = none)
//   set_source_extras     set source Id's cite key and permissions contact back (Body = SourceExtrasDto as JSON)
//   withdraw_permission   permission request Id of source Parent
//   unmerge_source        undo merge_sources: duplicate Id back from Parent (the kept source), Body = the SourceMergeSnapshot as JSON
//   patch_figure   set figure Id's image (Parent), placement (Status), caption (Summary) and alt text (Body) back
//   set_opener     set act or chapter Id's (Title: "act" or "chapter") opener image (Parent, null = none) and caption (Summary) back
//   patch_category        set category Id's name (Title) and colour (Summary) back; a null member is left
//   move_category         put category Id back after category After (null = first) among its book's or series' categories
//   delete_category       delete category Id (a soft delete: archived_at), which the write created and which must still be empty
//   restore_category      bring deleted category Id back (clears archived_at; its archived entries were never touched); refused while a live category has its name
//   set_entry_category    move entry Id back to category Parent (delete_category's move_to; steps run in the entries' order)
//   relate_entries / unrelate_entries   add or remove the related pair of entry Id and entry Parent
//   restore_entry         undo delete_kb_entry: entry Id back from the deletion, Body = the KbUndoRecordDto as JSON
//   unmerge_entry         undo merge_kb_entries: duplicate Id back from Parent (the kept entry), Body = the KbMergeSnapshot as JSON
//   archive_series / restore_series  archive series Id (a series create_series made, still empty) or bring it back (update_series archive records restore_series: the books' rows were never touched)
//   patch_series  set series Id's name (Title) and description (Description, "" clears) back
//   set_series_order  put series Id's live books back in the order of Labels (their ids)
//   (a sign-off is never redone over MCP: it is the author's call, M6)
// Now, when set, is the state the write left, which McpUndoChecks requires before undo runs the step.
public sealed record McpUndoStep(
    string Op,
    Guid Id,
    Guid? Parent = null,
    Guid? After = null,
    string? Body = null,
    int? Version = null,
    string? Title = null,
    string? Summary = null,
    string? Status = null,
    IReadOnlyList<Guid>? Labels = null,
    IReadOnlyList<string>? Texts = null,
    bool? Flag = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    McpUndoState? Now = null,
    IReadOnlyList<string>? Tags = null,
    bool? Exclude = null,
    string? Subtitle = null,
    string? Description = null);
