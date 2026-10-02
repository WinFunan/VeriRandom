using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class VerificationSettingsConfig : ObservableObject
{
    [ObservableProperty] private VerificationMode _mode = VerificationMode.Ordinary;

    // Ordinary-mode-only opt-in. When on, the ordinary proof seed comes from an external beacon pulse
    // instead of the local CSPRNG, so the operator cannot inject custom entropy into the draw.
    [ObservableProperty] private bool _beaconEntropyEnabled;

    // Advanced override. The default is the official NIST Beacon v2 base endpoint; a mirror is allowed
    // because the proof records the raw pulse and its signature, so a substituted pulse stays detectable.
    [ObservableProperty] private string _beaconEndpoint = "https://beacon.nist.gov/beacon/2.0/";
}
