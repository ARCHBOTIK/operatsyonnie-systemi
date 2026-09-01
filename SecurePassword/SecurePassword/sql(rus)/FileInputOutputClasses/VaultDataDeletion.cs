namespace SecurePassword;

internal static class VaultDataDeletion
{
    public static void DeleteAll()
    {
        // Settle any interrupted import before deleting the resulting vault. Running
        // recovery after deletion could restore backups from a Committing transaction.
        VaultImportTransaction.RecoverPendingTransactions();
        FileWorker.CleanupLeftoverTempFiles();

        foreach (string file in VaultImportTransaction.DataFiles)
        {
            DeleteIfPresent(file);
        }

        // Keep the key until all encrypted data files have been removed successfully.
        DeleteIfPresent("keys.dat");
        FileWorker.CleanupLeftoverTempFiles();
    }

    private static void DeleteIfPresent(string fileName)
    {
        string path = FileWorker.ResolvePath(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
