namespace OsoujiSystem.Domain.DomainServices;

internal static class CommonRotationLayout
{
    public static int?[] BuildDistributedSlotLayout(int spotCount, int memberCount)
    {
        var offDutyCount = memberCount - spotCount;
        var layout = new int?[memberCount];
        var spotIndex = 0;

        for (var position = 0; position < memberCount; position++)
        {
            var currentBucket = (position * offDutyCount) / memberCount;
            var nextBucket = ((position + 1) * offDutyCount) / memberCount;
            var isOffDutyPosition = nextBucket > currentBucket;

            layout[position] = isOffDutyPosition ? null : spotIndex++;
        }

        return layout;
    }

    public static int ProjectedPosition(int memberIndex, int phase, int memberCount)
    {
        return ((memberIndex - phase) % memberCount + memberCount) % memberCount;
    }
}