namespace ASCOM.OnStepX.Platform
{
    // Flat string key/value store behind DriverSettings. HKCU on Windows, a
    // JSON file under ~/.config/onstepx elsewhere.
    //
    // Everything is a string because that is what the registry store has
    // always written — DriverSettings already parses on the way out, and
    // keeping the wire format identical is what lets the Windows
    // implementation read the values users already have.
    //
    // Implementations never throw: a store that cannot be read behaves as
    // empty, and one that cannot be written drops the value. Settings are not
    // worth taking the hub down for.
    public interface ISettingsStore
    {
        string Read(string name, string fallback);

        void Write(string name, string value);
    }
}
