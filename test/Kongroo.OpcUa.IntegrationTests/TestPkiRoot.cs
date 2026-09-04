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
    /// Reserves a store path unique to the caller. Nothing is written to disk
    /// here; the stack creates the directory when it writes the application
    /// certificate, so a path that is never used leaves nothing behind.
    /// </summary>
    /// <returns>
    /// An absolute path below the temporary directory, suitable for
    /// <c>PkiRoot</c> and for <see cref="Delete"/>.
    /// </returns>
    internal static string Create() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Removes a store created by <see cref="Create"/>. Deletion is
    /// best-effort: the certificate stores may still hold file handles when
    /// a session tears down, and a leftover temporary directory must never
    /// fail a test run.
    /// </summary>
    /// <param name="pkiRoot">
    /// A path from <see cref="Create"/>. A path that was never used, or was
    /// already removed, is ignored.
    /// </param>
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
