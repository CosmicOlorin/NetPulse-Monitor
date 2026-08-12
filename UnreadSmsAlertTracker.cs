using System.Security.Cryptography;
using System.Text;

namespace NetPulseMonitor;

internal sealed class UnreadSmsAlertTracker
{
    private const int MaximumRememberedMessages = 4096;
    private readonly string? _historyPath;
    private readonly HashSet<string> _notifiedHashes = new(StringComparer.Ordinal);
    private readonly List<string> _hashOrder = [];

    public UnreadSmsAlertTracker(string? historyPath = null)
    {
        _historyPath = historyPath;
        Load();
    }

    public IReadOnlyList<string> FindNew(IEnumerable<string> unreadIdentities)
    {
        var result = new List<string>();
        bool changed = false;
        foreach (string identity in unreadIdentities
                     .Where(identity => !string.IsNullOrWhiteSpace(identity))
                     .Distinct(StringComparer.Ordinal))
        {
            string hash = Hash(identity);
            if (_notifiedHashes.Add(hash))
            {
                _hashOrder.Add(hash);
                result.Add(identity);
                changed = true;
            }
        }
        if (changed)
        {
            Trim();
            Save();
        }
        return result;
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_historyPath))
            return;
        try
        {
            foreach (string hash in File.ReadLines(_historyPath)
                         .Select(value => value.Trim().ToUpperInvariant())
                         .Where(IsHash)
                         .TakeLast(MaximumRememberedMessages))
            {
                if (_notifiedHashes.Add(hash))
                    _hashOrder.Add(hash);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_historyPath))
            return;
        string? directory = Path.GetDirectoryName(_historyPath);
        string temporaryPath = _historyPath + ".tmp";
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllLines(temporaryPath, _hashOrder);
            File.Move(temporaryPath, _historyPath, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private void Trim()
    {
        int removeCount = _hashOrder.Count - MaximumRememberedMessages;
        if (removeCount <= 0)
            return;
        foreach (string hash in _hashOrder.Take(removeCount))
            _notifiedHashes.Remove(hash);
        _hashOrder.RemoveRange(0, removeCount);
    }

    private static string Hash(string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
