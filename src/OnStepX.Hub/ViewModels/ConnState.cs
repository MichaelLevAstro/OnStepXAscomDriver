namespace ASCOM.OnStepX.ViewModels
{
    // Mirror of HubForm.ConnState. Controls overall enable/disable of section
    // controls per the legacy hub's _connectionControls/_mountActionControls
    // groups.
    public enum ConnState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public enum StatusKind { Neutral, Ok, Warn, Err, Info }
}
