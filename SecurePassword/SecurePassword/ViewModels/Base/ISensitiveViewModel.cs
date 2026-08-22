namespace SecurePassword.ViewModels.Base;

/// <summary>
/// Defines a contract for ViewModels that hold sensitive in-memory state
/// (e.g., passwords, card numbers, CVVs, plaintext notes) and must safely
/// clear references upon lock, logout, or session termination.
/// </summary>
public interface ISensitiveViewModel
{
    /// <summary>
    /// Clears any cached sensitive plaintext strings, credentials, or references.
    /// </summary>
    void ClearSensitiveData();
}
