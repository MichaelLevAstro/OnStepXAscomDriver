using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Version/Title/Company/Product come from OnStepX.Core.csproj via GenerateAssemblyInfo.
// Keep only attributes the SDK does not emit automatically.

[assembly: ComVisible(false)]

// Most of what moved here from the hub was internal and stayed internal. The
// hub's views bind to those view models, so it needs to see them.
[assembly: InternalsVisibleTo("OnStepX.Hub")]
[assembly: InternalsVisibleTo("OnStepX.Hub.Avalonia")]
