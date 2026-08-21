using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SecurePassword.Tests;

public class VaultImportTransactionTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _originalAppDataDir;

    public VaultImportTransactionTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), $"SP_TxTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBaseDir);
        _originalAppDataDir = FileWorker.TestingAppDataDirectory ?? string.Empty;
        FileWorker.TestingAppDataDirectory = _testBaseDir;
        VaultImportTransaction.TestingFailPointHook = null;
    }

    public void Dispose()
    {
        VaultImportTransaction.TestingFailPointHook = null;
        FileWorker.TestingAppDataDirectory = _originalAppDataDir;
        try
        {
            if (Directory.Exists(_testBaseDir))
                Directory.Delete(_testBaseDir, recursive: true);
        }
        catch
        {
        }
    }

    private static byte[] CreateTestArchive(
        Dictionary<string, byte[]> files,
        bool includeKeys = true)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeKeys && !files.ContainsKey("keys.dat"))
            {
                var entry = archive.CreateEntry("keys.dat");
                using var es = entry.Open();
                byte[] sampleKey = Encoding.UTF8.GetBytes("SPK1-SampleKeysDat");
                es.Write(sampleKey);
            }

            foreach (var kvp in files)
            {
                var entry = archive.CreateEntry(kvp.Key);
                using var es = entry.Open();
                es.Write(kvp.Value);
            }
        }
        return ms.ToArray();
    }

    private void SeedInitialVault(byte[] keys, byte[] passwords, byte[] cards, byte[] notes)
    {
        File.WriteAllBytes(Path.Combine(_testBaseDir, "keys.dat"), keys);
        File.WriteAllBytes(Path.Combine(_testBaseDir, "passwords.dat"), passwords);
        File.WriteAllBytes(Path.Combine(_testBaseDir, "cards.dat"), cards);
        File.WriteAllBytes(Path.Combine(_testBaseDir, "notes.dat"), notes);
    }

    [Fact]
    public void Test13_ValidImport_Succeeds()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        SeedInitialVault(oldKeys, oldPass, Encoding.UTF8.GetBytes("OLD_CARDS"), Encoding.UTF8.GetBytes("OLD_NOTES"));

        byte[] newKeys = Encoding.UTF8.GetBytes("NEW_KEYS");
        byte[] newPass = Encoding.UTF8.GetBytes("NEW_PASSWORDS");
        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = newKeys,
            ["passwords.dat"] = newPass
        }, includeKeys: false);

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        tx.Commit();

        Assert.Equal(newKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(newPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
    }

    [Fact]
    public void Test14_CorruptedArchive_RejectedBeforeCommit()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        SeedInitialVault(oldKeys, Encoding.UTF8.GetBytes("OLD_P"), Encoding.UTF8.GetBytes("OLD_C"), Encoding.UTF8.GetBytes("OLD_N"));

        byte[] badArchive = [0x01, 0x02, 0x03, 0x04];
        var tx = new VaultImportTransaction();

        Assert.ThrowsAny<Exception>(() => tx.Prepare(badArchive));
        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
    }

    [Fact]
    public void Test15_ZipSlipArchive_Rejected()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        SeedInitialVault(oldKeys, Encoding.UTF8.GetBytes("OLD_P"), Encoding.UTF8.GetBytes("OLD_C"), Encoding.UTF8.GetBytes("OLD_N"));

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var es = entry.Open();
            es.Write(Encoding.UTF8.GetBytes("EVIL"));
        }

        byte[] zipSlipArchive = ms.ToArray();
        var tx = new VaultImportTransaction();

        Assert.Throws<InvalidDataException>(() => tx.Prepare(zipSlipArchive));
        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
    }

    [Fact]
    public void Test16_MissingKeysDat_Rejected()
    {
        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["passwords.dat"] = Encoding.UTF8.GetBytes("NEW_PASSWORDS")
        }, includeKeys: false);

        var tx = new VaultImportTransaction();
        Assert.Throws<InvalidDataException>(() => tx.Prepare(archive));
    }

    [Fact]
    public void Test17_CrashBeforePrepared_LeavesOldVaultIntact()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        SeedInitialVault(oldKeys, Encoding.UTF8.GetBytes("OLD_P"), Encoding.UTF8.GetBytes("OLD_C"), Encoding.UTF8.GetBytes("OLD_N"));

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["passwords.dat"] = Encoding.UTF8.GetBytes("NEW_PASSWORDS")
        });

        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.BeforePrepared)
                throw new InvalidOperationException("Simulated crash before Prepared.");
        };

        var tx = new VaultImportTransaction();
        Assert.Throws<InvalidOperationException>(() => tx.Prepare(archive));

        // Restart recovery
        VaultImportTransaction.RecoverPendingTransactions();

        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
    }

    [Fact]
    public void Test18_CrashAfterPrepared_LeavesOldVaultIntact()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        SeedInitialVault(oldKeys, Encoding.UTF8.GetBytes("OLD_P"), Encoding.UTF8.GetBytes("OLD_C"), Encoding.UTF8.GetBytes("OLD_N"));

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["passwords.dat"] = Encoding.UTF8.GetBytes("NEW_PASSWORDS")
        });

        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterPrepared)
                throw new InvalidOperationException("Simulated crash after Prepared.");
        };

        var tx = new VaultImportTransaction();
        Assert.Throws<InvalidOperationException>(() => tx.Prepare(archive));

        // Recovery
        VaultImportTransaction.RecoverPendingTransactions();

        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
    }

    [Fact]
    public void Test19_CrashAfterFirstReplacedFile_RestoresCompleteOldVault()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        byte[] oldCards = Encoding.UTF8.GetBytes("OLD_CARDS");
        byte[] oldNotes = Encoding.UTF8.GetBytes("OLD_NOTES");
        SeedInitialVault(oldKeys, oldPass, oldCards, oldNotes);

        byte[] newKeys = Encoding.UTF8.GetBytes("NEW_KEYS");
        byte[] newPass = Encoding.UTF8.GetBytes("NEW_PASSWORDS");
        byte[] newCards = Encoding.UTF8.GetBytes("NEW_CARDS");
        byte[] newNotes = Encoding.UTF8.GetBytes("NEW_NOTES");

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = newKeys,
            ["passwords.dat"] = newPass,
            ["cards.dat"] = newCards,
            ["notes.dat"] = newNotes
        }, includeKeys: false);

        int replacedCount = 0;
        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterFileReplaced)
            {
                replacedCount++;
                if (replacedCount == 1)
                    throw new InvalidOperationException("Simulated crash after 1st file replaced.");
            }
        };

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        // Simulated startup recovery
        VaultImportTransaction.RecoverPendingTransactions();

        // 100% old vault must be restored
        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(oldPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
        Assert.Equal(oldCards, File.ReadAllBytes(Path.Combine(_testBaseDir, "cards.dat")));
        Assert.Equal(oldNotes, File.ReadAllBytes(Path.Combine(_testBaseDir, "notes.dat")));
    }

    [Fact]
    public void Test20_CrashAfterSecondReplacedFile_RestoresCompleteOldVault()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        byte[] oldCards = Encoding.UTF8.GetBytes("OLD_CARDS");
        byte[] oldNotes = Encoding.UTF8.GetBytes("OLD_NOTES");
        SeedInitialVault(oldKeys, oldPass, oldCards, oldNotes);

        byte[] newKeys = Encoding.UTF8.GetBytes("NEW_KEYS");
        byte[] newPass = Encoding.UTF8.GetBytes("NEW_PASSWORDS");
        byte[] newCards = Encoding.UTF8.GetBytes("NEW_CARDS");
        byte[] newNotes = Encoding.UTF8.GetBytes("NEW_NOTES");

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = newKeys,
            ["passwords.dat"] = newPass,
            ["cards.dat"] = newCards,
            ["notes.dat"] = newNotes
        }, includeKeys: false);

        int replacedCount = 0;
        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterFileReplaced)
            {
                replacedCount++;
                if (replacedCount == 2)
                    throw new InvalidOperationException("Simulated crash after 2nd file replaced.");
            }
        };

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        // Recovery
        VaultImportTransaction.RecoverPendingTransactions();

        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(oldPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
        Assert.Equal(oldCards, File.ReadAllBytes(Path.Combine(_testBaseDir, "cards.dat")));
        Assert.Equal(oldNotes, File.ReadAllBytes(Path.Combine(_testBaseDir, "notes.dat")));
    }

    [Fact]
    public void Test21_CrashAfterThirdReplacedFile_RestoresCompleteOldVault()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        byte[] oldCards = Encoding.UTF8.GetBytes("OLD_CARDS");
        byte[] oldNotes = Encoding.UTF8.GetBytes("OLD_NOTES");
        SeedInitialVault(oldKeys, oldPass, oldCards, oldNotes);

        byte[] newKeys = Encoding.UTF8.GetBytes("NEW_KEYS");
        byte[] newPass = Encoding.UTF8.GetBytes("NEW_PASSWORDS");
        byte[] newCards = Encoding.UTF8.GetBytes("NEW_CARDS");
        byte[] newNotes = Encoding.UTF8.GetBytes("NEW_NOTES");

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = newKeys,
            ["passwords.dat"] = newPass,
            ["cards.dat"] = newCards,
            ["notes.dat"] = newNotes
        }, includeKeys: false);

        int replacedCount = 0;
        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterFileReplaced)
            {
                replacedCount++;
                if (replacedCount == 3)
                    throw new InvalidOperationException("Simulated crash after 3rd file replaced.");
            }
        };

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        // Recovery
        VaultImportTransaction.RecoverPendingTransactions();

        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(oldPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
        Assert.Equal(oldCards, File.ReadAllBytes(Path.Combine(_testBaseDir, "cards.dat")));
        Assert.Equal(oldNotes, File.ReadAllBytes(Path.Combine(_testBaseDir, "notes.dat")));
    }

    [Fact]
    public void Test22_SuccessfulCommit_ResultsInCompleteNewVault()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        SeedInitialVault(oldKeys, oldPass, Encoding.UTF8.GetBytes("OLD_CARDS"), Encoding.UTF8.GetBytes("OLD_NOTES"));

        byte[] newKeys = Encoding.UTF8.GetBytes("NEW_KEYS");
        byte[] newPass = Encoding.UTF8.GetBytes("NEW_PASSWORDS");
        byte[] newCards = Encoding.UTF8.GetBytes("NEW_CARDS");
        byte[] newNotes = Encoding.UTF8.GetBytes("NEW_NOTES");

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = newKeys,
            ["passwords.dat"] = newPass,
            ["cards.dat"] = newCards,
            ["notes.dat"] = newNotes
        }, includeKeys: false);

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        tx.Commit();

        Assert.Equal(newKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(newPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
        Assert.Equal(newCards, File.ReadAllBytes(Path.Combine(_testBaseDir, "cards.dat")));
        Assert.Equal(newNotes, File.ReadAllBytes(Path.Combine(_testBaseDir, "notes.dat")));
    }

    [Fact]
    public void Test23_RecoveryIsIdempotent()
    {
        byte[] oldKeys = Encoding.UTF8.GetBytes("OLD_KEYS");
        byte[] oldPass = Encoding.UTF8.GetBytes("OLD_PASSWORDS");
        SeedInitialVault(oldKeys, oldPass, Encoding.UTF8.GetBytes("OLD_CARDS"), Encoding.UTF8.GetBytes("OLD_NOTES"));

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["keys.dat"] = Encoding.UTF8.GetBytes("NEW_KEYS"),
            ["passwords.dat"] = Encoding.UTF8.GetBytes("NEW_PASSWORDS")
        }, includeKeys: false);

        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterFileReplaced)
                throw new InvalidOperationException("Crash during commit.");
        };

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        // Run recovery multiple times
        VaultImportTransaction.RecoverPendingTransactions();
        VaultImportTransaction.RecoverPendingTransactions();
        VaultImportTransaction.RecoverPendingTransactions();

        Assert.Equal(oldKeys, File.ReadAllBytes(Path.Combine(_testBaseDir, "keys.dat")));
        Assert.Equal(oldPass, File.ReadAllBytes(Path.Combine(_testBaseDir, "passwords.dat")));
    }

    [Fact]
    public void Test24_TransactionFilesCleanedAfterCompletedRecovery()
    {
        SeedInitialVault(Encoding.UTF8.GetBytes("K"), Encoding.UTF8.GetBytes("P"), Encoding.UTF8.GetBytes("C"), Encoding.UTF8.GetBytes("N"));

        byte[] archive = CreateTestArchive(new Dictionary<string, byte[]>
        {
            ["passwords.dat"] = Encoding.UTF8.GetBytes("NEW_P")
        });

        VaultImportTransaction.TestingFailPointHook = (id, stage, file) =>
        {
            if (stage == TransactionFailPoint.AfterFileReplaced)
                throw new InvalidOperationException("Crash");
        };

        var tx = new VaultImportTransaction();
        tx.Prepare(archive);
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        var directoriesBefore = Directory.GetDirectories(_testBaseDir, ".vault-import-*");
        Assert.NotEmpty(directoriesBefore);

        VaultImportTransaction.RecoverPendingTransactions();

        var directoriesAfter = Directory.GetDirectories(_testBaseDir, ".vault-import-*");
        Assert.Empty(directoriesAfter);
    }
}
