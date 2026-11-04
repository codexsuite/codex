namespace Codex.AcceptanceTests.Pages.Auth;

// Mirror of src/Codex.Client/src/features/auth/testIds.ts (prefix auth-).
public static class AuthTestIds
{
    public const string LoginPage = "auth-login-page";
    public const string LoginEmail = "auth-login-email";
    public const string LoginPassword = "auth-login-password";
    public const string LoginSubmit = "auth-login-submit";
    public const string LoginError = "auth-login-error";
    public const string UserMenuName = "auth-user-menu-name";
    public const string UserMenuAccount = "auth-user-menu-account";
    public const string UserMenuAi = "auth-user-menu-ai";
    public const string UserMenuUsers = "auth-user-menu-users";
    public const string UserMenuSignOut = "auth-user-menu-sign-out";
    public const string AccountPage = "auth-account-page";
    public const string AccountDisplayName = "auth-account-display-name";
    public const string AccountSaveName = "auth-account-save-name";
    public const string AccountNameSaved = "auth-account-name-saved";
    public const string PasswordPage = "auth-password-page";
    public const string PasswordMustChange = "auth-password-must-change";
    public const string PasswordCurrent = "auth-password-current";
    public const string PasswordNew = "auth-password-new";
    public const string PasswordConfirm = "auth-password-confirm";
    public const string PasswordSubmit = "auth-password-submit";
    public const string PasswordError = "auth-password-error";
    public const string PasswordSaved = "auth-password-saved";
    public const string UsersPage = "auth-users-page";
    public const string UsersError = "auth-users-error";
    public const string AddUserEmail = "auth-add-user-email";
    public const string AddUserName = "auth-add-user-name";
    public const string AddUserSubmit = "auth-add-user-submit";
    public const string OneTimePassword = "auth-one-time-password";
    public const string OneTimePasswordCopy = "auth-one-time-password-copy";
    public const string OneTimePasswordDismiss = "auth-one-time-password-dismiss";
    public const string TokensSection = "auth-tokens-section";
    public const string TokensError = "auth-tokens-error";
    public const string TokensEmpty = "auth-tokens-empty";
    public const string TokenName = "auth-token-name";
    public const string TokenExpiry = "auth-token-expiry";
    public const string TokenCreate = "auth-token-create";
    public const string TokenSecret = "auth-token-secret";
    public const string TokenSecretCopy = "auth-token-secret-copy";
    public const string TokenSecretDismiss = "auth-token-secret-dismiss";
    public const string DevicesSection = "auth-devices-section";
    public const string DevicesError = "auth-devices-error";
    public const string DevicesEmpty = "auth-devices-empty";

    public static string UserRow(string userId) => $"auth-user-row-{userId}";

    public static string UserStatus(string userId) => $"auth-user-status-{userId}";

    public static string UserToggleAdmin(string userId) => $"auth-user-toggle-admin-{userId}";

    public static string UserToggleDisabled(string userId) => $"auth-user-toggle-disabled-{userId}";

    public static string UserUnlock(string userId) => $"auth-user-unlock-{userId}";

    public static string UserResetPassword(string userId) => $"auth-user-reset-password-{userId}";

    public static string UserTokenCount(string userId) => $"auth-user-token-count-{userId}";

    public static string UserRevokeTokens(string userId) => $"auth-user-revoke-tokens-{userId}";

    // scope: "write", "references" or "generate" (the codex:* scope without its prefix).
    public static string TokenScope(string scope) => $"auth-token-scope-{scope}";

    public static string TokenRow(string tokenId) => $"auth-token-row-{tokenId}";

    public static string TokenRevoke(string tokenId) => $"auth-token-revoke-{tokenId}";

    public static string DeviceRow(string sessionId) => $"auth-device-row-{sessionId}";

    public static string DeviceSignOut(string sessionId) => $"auth-device-sign-out-{sessionId}";
}
