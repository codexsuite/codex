namespace Codex.AcceptanceTests.Pages.References;

// The source detail panel beside the table: verdict, checklist, uses, details, history and removal.
public sealed class SourcePanel(IPage page)
{
    private const string ToastId = "editing-toast";

    public ILocator Root => page.GetByTestId(ReferencesTestIds.Panel);

    public ILocator Title => page.GetByTestId(ReferencesTestIds.PanelTitle);

    public ILocator Banner => page.GetByTestId(ReferencesTestIds.PanelBanner);

    public ILocator VerdictStatus => page.GetByTestId(ReferencesTestIds.VerdictStatus);

    public ILocator VerdictReason => page.GetByTestId(ReferencesTestIds.VerdictReason);

    public ILocator VerdictAction => page.GetByTestId(ReferencesTestIds.VerdictAction);

    public ILocator SignedOff => page.GetByTestId(ReferencesTestIds.SignedOff);

    public ILocator FairUseReason => page.GetByTestId(ReferencesTestIds.FairUseReason);

    public ILocator FairUseAccept => page.GetByTestId(ReferencesTestIds.FairUseAccept);

    public ILocator Check(string key) => page.GetByTestId(ReferencesTestIds.Check(key));

    public ILocator Use(Guid useId) => page.GetByTestId(ReferencesTestIds.Use(useId));

    public ILocator UseKind(Guid useId) => page.GetByTestId(ReferencesTestIds.UseKind(useId));

    public ILocator UseExcerpt(Guid useId) => page.GetByTestId(ReferencesTestIds.UseExcerpt(useId));

    public ILocator UseNew(Guid useId) => page.GetByTestId(ReferencesTestIds.UseNew(useId));

    public ILocator UseWrite(Guid useId) => page.GetByTestId(ReferencesTestIds.UseWrite(useId));

    public ILocator Field(string name) => page.GetByTestId(ReferencesTestIds.Field(name));

    public ILocator Verification(string field) => page.GetByTestId(ReferencesTestIds.Verification(field));

    public ILocator UpdateAddress(string field) => page.GetByTestId(ReferencesTestIds.UpdateAddress(field));

    public ILocator Permission => page.GetByTestId(ReferencesTestIds.Permission);

    public ILocator PermStatus => page.GetByTestId(ReferencesTestIds.PermStatus);

    public ILocator PermTo => page.GetByTestId(ReferencesTestIds.PermTo);

    public ILocator PermLetter => page.GetByTestId(ReferencesTestIds.PermLetter);

    public ILocator PermRemind => page.GetByTestId(ReferencesTestIds.PermRemind);

    public ILocator PermSentLabel => page.GetByTestId(ReferencesTestIds.PermSentLabel);

    public ILocator PermRemindLabel => page.GetByTestId(ReferencesTestIds.PermRemindLabel);

    public ILocator PermRespondedLabel => page.GetByTestId(ReferencesTestIds.PermRespondedLabel);

    public ILocator PermCredit => page.GetByTestId(ReferencesTestIds.PermCredit);

    public ILocator PermNote => page.GetByTestId(ReferencesTestIds.PermNote);

    public ILocator PermAttachment => page.GetByTestId(ReferencesTestIds.PermAttachment);

    public ILocator PermDraft => page.GetByTestId(ReferencesTestIds.PermDraft);

    public ILocator PermCopy => page.GetByTestId(ReferencesTestIds.PermCopy);

    public ILocator PermSend => page.GetByTestId(ReferencesTestIds.PermSend);

    public ILocator PermWithdraw => page.GetByTestId(ReferencesTestIds.PermWithdraw);

    public ILocator PermGranted => page.GetByTestId(ReferencesTestIds.PermGranted);

    public ILocator PermRefused => page.GetByTestId(ReferencesTestIds.PermRefused);

    public ILocator PermGrantForm => page.GetByTestId(ReferencesTestIds.PermGrantForm);

    public ILocator PermFile => page.GetByTestId(ReferencesTestIds.PermFile);

    public ILocator PermCreditInput => page.GetByTestId(ReferencesTestIds.PermCreditInput);

    public ILocator PermTermsInput => page.GetByTestId(ReferencesTestIds.PermTermsInput);

    public ILocator PermGrantSubmit => page.GetByTestId(ReferencesTestIds.PermGrantSubmit);

    public ILocator PermRefuseNote => page.GetByTestId(ReferencesTestIds.PermRefuseNote);

    public ILocator PermRefuseSubmit => page.GetByTestId(ReferencesTestIds.PermRefuseSubmit);

    public ILocator Menu => page.GetByTestId(ReferencesTestIds.PanelMenu);

    public ILocator MenuMerge => page.GetByTestId(ReferencesTestIds.PanelMenuMerge);

    public ILocator MenuRemove => page.GetByTestId(ReferencesTestIds.PanelMenuRemove);

    public ILocator MergeDialog => page.GetByTestId(ReferencesTestIds.MergeDialog);

    public ILocator MergeTarget => page.GetByTestId(ReferencesTestIds.MergeTarget);

    public ILocator MergeSummary => page.GetByTestId(ReferencesTestIds.MergeSummary);

    public ILocator MergeConfirm => page.GetByTestId(ReferencesTestIds.MergeConfirm);

    public ILocator RemoveCancel => page.GetByTestId(ReferencesTestIds.RemoveCancel);

    public ILocator PermRemindIn(int days) => page.GetByTestId(ReferencesTestIds.PermRemindIn(days));

    public ILocator PermCopyFollowUp => page.GetByTestId(ReferencesTestIds.PermCopyFollowUp);

    public ILocator PermFollowedUp => page.GetByTestId(ReferencesTestIds.PermFollowedUp);

    public ILocator PermFollowedUpLabel => page.GetByTestId(ReferencesTestIds.PermFollowedUpLabel);

    public ILocator PermFeeInput => page.GetByTestId(ReferencesTestIds.PermFeeInput);

    public ILocator PermFormError => page.GetByTestId(ReferencesTestIds.PermFormError);

    public ILocator PermGrantCancel => page.GetByTestId(ReferencesTestIds.PermGrantCancel);

    public ILocator PermAttachmentRemove => page.GetByTestId(ReferencesTestIds.PermAttachmentRemove);

    public ILocator PermAttachmentConfirm => page.GetByTestId(ReferencesTestIds.PermAttachmentConfirm);

    public ILocator PermAttachInput => page.GetByTestId(ReferencesTestIds.PermAttachInput);

    public ILocator VerdictCut => page.GetByTestId(ReferencesTestIds.VerdictCut);

    public ILocator Recheck => page.GetByTestId(ReferencesTestIds.Recheck);

    public ILocator HistoryItems => page.GetByTestId(ReferencesTestIds.HistoryItem);

    // The Codex link: the select that sets the knowledge base entry this source belongs to.
    public ILocator Entry => page.GetByTestId(ReferencesTestIds.Entry);

    public ILocator Remove => page.GetByTestId(ReferencesTestIds.Remove);

    public ILocator RemoveConfirm => page.GetByTestId(ReferencesTestIds.RemoveConfirm);

    public ILocator Toast => page.GetByTestId(ToastId);
}
