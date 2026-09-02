namespace SyncClipboard.Shared.Profiles;

public enum TransferDataValidationMode
{
    None,
    Full,
    PreferTransferDataHash,
}

public readonly record struct TransferDataValidation(
    TransferDataValidationMode Mode,
    string? ExpectedTransferDataHash = null)
{
    public static TransferDataValidation Unverified => default;

    public static TransferDataValidation Full(string? expectedTransferDataHash = null)
    {
        return new(TransferDataValidationMode.Full, expectedTransferDataHash);
    }

    public static TransferDataValidation PreferTransferDataHash(string? expectedTransferDataHash)
    {
        return new(TransferDataValidationMode.PreferTransferDataHash, expectedTransferDataHash);
    }

    public bool RequiresVerification => Mode is not TransferDataValidationMode.None;

    public bool CanSkipProfileSemanticValidation =>
        Mode is TransferDataValidationMode.PreferTransferDataHash &&
        Profile.IsValidTransferDataHash(ExpectedTransferDataHash);

    public void EnsureTransferDataHashMatches(string actualTransferDataHash, string dataDescription)
    {
        if (!RequiresVerification ||
            !Profile.IsValidTransferDataHash(ExpectedTransferDataHash) ||
            string.Equals(
                actualTransferDataHash,
                ExpectedTransferDataHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidDataException(
            $"{dataDescription} hash mismatch. Expected: {ExpectedTransferDataHash}, Actual: {actualTransferDataHash}.");
    }
}
