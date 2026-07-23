using System;
using System.Security.Cryptography;
using System.Text;

namespace CoderCommander.Services;

/// <summary>
/// Шифрование/дешифрование учётных данных через DPAPI (Data Protection API).
/// Encrypts/decrypts credentials via DPAPI (tied to Windows user account).
/// Зашифрованные значения имеют префикс "dpapi:v1:" для различения с открытым текстом.
/// Encrypted values have prefix "dpapi:v1:" to distinguish from plaintext.
/// </summary>
internal static class CredentialProtector
{
    private const string Prefix = "dpapi:v1:";

    /// <summary>
    /// Шифрует строку через DPAPI. Возвращает "dpapi:v1:{base64}".
    /// Encrypts a string via DPAPI. Returns "dpapi:v1:{base64}".
    /// </summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Дешифрует строку. Если префикса нет — возвращает как есть (обратная совместимость).
    /// Decrypts a string. If no prefix — returns as-is (backward compatibility).
    /// </summary>
    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            return cipherText;

        try
        {
            var base64 = cipherText[Prefix.Length..];
            var encrypted = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return cipherText;
        }
    }

    /// <summary>
    /// Проверяет, зашифровано ли значение.
    /// Checks whether a value is encrypted.
    /// </summary>
    public static bool IsProtected(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
}
