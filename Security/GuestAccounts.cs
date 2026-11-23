namespace Shoura.API.Security;

/// <summary>
/// حسابات الزوار تُنشأ من الواجهة ببريد مولّد ينتهي بهذا اللاحق.
/// يستطيع الزائر الكتابة والتعليق والإعجاب، لكن لا يرفع صوراً أو مقاطع.
/// </summary>
public static class GuestAccounts
{
    public const string EmailSuffix = "@guest.invalid";
    public const string UploadMessage = "رفع الصور والمقاطع يتطلب حساباً مسجلاً. سجّل حساباً للمتابعة.";

    public static bool IsGuestEmail(string? email) =>
        !string.IsNullOrEmpty(email) && email.EndsWith(EmailSuffix, StringComparison.OrdinalIgnoreCase);
}
