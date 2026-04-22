using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Driver
{
    // regasm /codebase fallback path — writes only InprocServer32 CLSID keys
    // plus the ASCOM Profile entry. Installer writes the same keys directly and
    // remains authoritative; this attribute handler only exists so dev builds
    // (regasm ASCOM.OnStepX.Telescope.dll /codebase) register correctly without
    // the installer. Drop LocalServer32 / AppID / /embedding surface entirely.
    internal static class DriverRegistration
    {
        private const string DeviceType = "Telescope";

        [ComRegisterFunction]
        public static void RegisterAsCom(Type t)
        {
            // regasm already writes HKCR\CLSID\{guid}\InprocServer32 with the
            // .NET runtime shim — we only need to add ProgID + ASCOM profile.
            string progId = GetProgId(t);
            string friendly = GetFriendlyName(t) ?? progId;
            string clsid = "{" + t.GUID.ToString().ToUpperInvariant() + "}";

            using (var clsidKey = Registry.ClassesRoot.CreateSubKey(@"CLSID\" + clsid))
            {
                clsidKey?.SetValue(null, friendly);
                using (var pid = clsidKey.CreateSubKey("ProgId")) pid?.SetValue(null, progId);
                using (var prog = clsidKey.CreateSubKey("Programmable")) { }
                // Force apartment threading on the InprocServer32 subkey regasm already wrote.
                using (var is32 = clsidKey.OpenSubKey("InprocServer32", true))
                    is32?.SetValue("ThreadingModel", "Apartment");
            }

            using (var progKey = Registry.ClassesRoot.CreateSubKey(progId))
            {
                progKey?.SetValue(null, friendly);
                using (var c = progKey.CreateSubKey("CLSID")) c?.SetValue(null, clsid);
            }

            try { AscomProfileRegister(progId, friendly, true); }
            catch { /* ASCOM Platform may not be installed on dev box; ignore */ }
        }

        [ComUnregisterFunction]
        public static void UnregisterAsCom(Type t)
        {
            string progId = GetProgId(t);
            try { AscomProfileRegister(progId, null, false); } catch { }
            try { Registry.ClassesRoot.DeleteSubKeyTree(progId, false); } catch { }
            // Leave CLSID tree — regasm strips it on its own pass.
        }

        private static string GetProgId(Type t)
        {
            var a = (ProgIdAttribute)Attribute.GetCustomAttribute(t, typeof(ProgIdAttribute));
            return a?.Value ?? t.FullName;
        }

        private static string GetFriendlyName(Type t)
        {
            var a = (ServedClassNameAttribute)Attribute.GetCustomAttribute(t, typeof(ServedClassNameAttribute));
            return a?.DisplayName;
        }

        private static void AscomProfileRegister(string progId, string friendly, bool register)
        {
            Type profType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profType == null) return;
            object profile = Activator.CreateInstance(profType);
            try
            {
                profType.GetProperty("DeviceType").SetValue(profile, DeviceType, null);
                bool isReg = (bool)profType.GetMethod("IsRegistered").Invoke(profile, new object[] { progId });
                if (register && !isReg)
                    profType.GetMethod("Register").Invoke(profile, new object[] { progId, friendly ?? progId });
                else if (!register && isReg)
                    profType.GetMethod("Unregister").Invoke(profile, new object[] { progId });
            }
            finally { Marshal.ReleaseComObject(profile); }
        }
    }

    // Display-name attribute formerly in LocalServer.cs. Only used by the
    // registration helper above.
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ServedClassNameAttribute : Attribute
    {
        public string DisplayName { get; }
        public ServedClassNameAttribute(string name) { DisplayName = name; }
    }
}
