namespace GPOSettingsExplorer.Services;

/// <summary>
/// AD gPLink stores links in inverse GPMC display order: the rightmost
/// entry has link order 1 (highest precedence within a container).
/// </summary>
public static class GpoLinkOrder
{
    public static int FromStorageIndex(int count, int storageIndex)
    {
        if (count < 1 || storageIndex < 0 || storageIndex >= count)
            throw new ArgumentOutOfRangeException(nameof(storageIndex));
        return count - storageIndex;
    }

    // There are remainingCount links after the moved/new GPO has been removed.
    // Desired order is counted from the rightmost link (order 1).
    public static int InsertionIndex(int remainingCount, int desiredOrder)
    {
        if (remainingCount < 0 || desiredOrder < 1 || desiredOrder > remainingCount + 1)
            throw new ArgumentOutOfRangeException(nameof(desiredOrder));
        return remainingCount + 1 - desiredOrder;
    }

    public static int ClampOrder(int remainingCount, int requestedOrder) =>
        Math.Clamp(requestedOrder <= 0 ? remainingCount + 1 : requestedOrder, 1,
            remainingCount + 1);
}
