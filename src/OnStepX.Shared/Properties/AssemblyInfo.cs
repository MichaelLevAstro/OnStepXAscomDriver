using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Version/Title/Company/Product come from OnStepX.Shared.csproj via GenerateAssemblyInfo.
// Keep only attributes the SDK does not emit automatically.

[assembly: ComVisible(false)]

// Driver + Hub are the only intended consumers of internal types (LX200Protocol,
// CoordFormat, ITransport, PipeTransport, TransportLogger).
[assembly: InternalsVisibleTo("ASCOM.OnStepX.Telescope")]
[assembly: InternalsVisibleTo("OnStepX.Hub")]
[assembly: InternalsVisibleTo("OnStepX.Hub.Wpf")]
