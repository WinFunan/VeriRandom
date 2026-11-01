using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class ProofRetentionConfig : ObservableObject
{
    // Zero keeps local proof files until the user removes them manually. That is the default because a deleted proof
    // cannot take part in any later re-check: retention is the one setting whose cost is paid entirely in evidential
    // value, and the disk cost is small (one proof is a few kilobytes). The first-run guide asks about it explicitly
    // instead of trimming a fresh install silently.
    [ObservableProperty] private int _retentionDays;

    // Zero means no size cap. A cap paired with "keep forever" would still delete the history the user chose to keep,
    // so the two defaults have to agree.
    [ObservableProperty] private long _maximumStorageBytes;
}
