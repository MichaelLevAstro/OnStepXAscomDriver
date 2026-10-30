using System.Threading.Tasks;

namespace ASCOM.OnStepX.Platform
{
    // Open/save file chooser. Only the sites import/export uses one.
    //
    // The filter string keeps WPF's "Label (*.ext)|*.ext|..." form because that
    // is what the two call sites already pass; other toolkits take a list of
    // extensions, which is a parse away from this.
    public interface IFilePicker
    {
        // Full path, or null if the user cancelled.
        Task<string> OpenAsync(string title, string filter);

        Task<string> SaveAsync(string title, string filter, string suggestedName);
    }
}
