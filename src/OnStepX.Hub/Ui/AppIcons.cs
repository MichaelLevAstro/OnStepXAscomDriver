using System.Drawing;
using System.Reflection;

namespace ASCOM.OnStepX.Ui
{
    internal static class AppIcons
    {
        private static Icon _app;

        // Multi-resolution ICO is embedded into the exe via <ApplicationIcon>, but
        // Form.Icon and NotifyIcon.Icon need an Icon instance — load the same file
        // from the manifest-resource copy (EmbeddedResource in csproj).
        public static Icon App
        {
            get
            {
                if (_app != null) return _app;
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("ASCOM.OnStepX.AppIcon.ico"))
                    _app = s != null ? new Icon(s) : SystemIcons.Application;
                return _app;
            }
        }
    }
}
