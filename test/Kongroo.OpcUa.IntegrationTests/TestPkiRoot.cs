namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// Ephemeral certificate stores for the in-process server and its test
/// clients. A fresh directory per run keeps one run from poisoning
/// another's trust lists.
/// </summary>
internal static class TestPkiRoot
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "KongrooOpcUaTests");

    /// <summary>
    /// Returns a directory path unique to the caller. The stack creates the
    /// directory itself when it writes the application certificate.
    /// </summary>
    internal static string Create() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Removes a store created by <see cref="Create"/>. Deletion is
    /// best-effort: the certificate stores may still hold file handles when
    /// a session tears down, and a leftover temporary directory must never
    /// fail a test run.
    /// </summary>
    internal static void Delete(string pkiRoot)
    {
        if (!Directory.Exists(pkiRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(pkiRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Intentionally ignored: see the remarks above.
        }
    }
}
