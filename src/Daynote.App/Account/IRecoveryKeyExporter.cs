namespace Daynote.App.Account;

/// <summary>
/// Gets the one-time recovery key out of the app and somewhere the user actually keeps things.
/// </summary>
/// <remarks>
/// An interface rather than a direct clipboard call so the view model stays testable and, more to
/// the point, so the key never travels through anything that logs. Both methods report failure
/// instead of throwing: the key is on screen either way, and an exception here would replace the one
/// chance to save it with an error dialog.
/// </remarks>
public interface IRecoveryKeyExporter
{
    bool TryCopyToClipboard(string recoveryKey);

    bool TrySaveToFile(string recoveryKey);
}
