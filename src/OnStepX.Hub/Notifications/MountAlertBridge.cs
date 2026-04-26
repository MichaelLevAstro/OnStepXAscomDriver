using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.Notifications
{
    // Translates MountSession events into NotificationService calls. Kept separate
    // from NotificationService so the service stays event-source-agnostic and a
    // future second source (e.g. ConnectionChanged, park-complete) just adds a
    // subscriber here.
    internal static class MountAlertBridge
    {
        private static bool _attached;

        public static void Attach(MountSession session)
        {
            if (_attached || session == null) return;
            _attached = true;
            session.LimitWarning += OnLimitWarning;
        }

        private static void OnLimitWarning(string reason)
        {
            // Suppress during teardown: a pending event can fire after the user
            // disconnects, and a "limit reached" toast in that window is noise.
            if (!MountSession.Instance.IsOpen) return;
            NotificationService.Show("Mount limit reached", reason ?? "", NotificationSeverity.Warning);
        }
    }
}
