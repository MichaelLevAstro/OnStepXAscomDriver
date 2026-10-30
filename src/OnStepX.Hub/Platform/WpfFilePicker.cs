using System.Threading.Tasks;
using ASCOM.OnStepX.Platform;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Hub.Platform
{
    // WPF's common file dialogs. Both complete synchronously, as ShowDialog
    // blocks — same note as everywhere else in this folder.
    internal sealed class WpfFilePicker : IFilePicker
    {
        public Task<string> OpenAsync(string title, string filter)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter };
            return Task.FromResult(dlg.ShowDialog() == true ? dlg.FileName : null);
        }

        public Task<string> SaveAsync(string title, string filter, string suggestedName)
        {
            var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = suggestedName };
            return Task.FromResult(dlg.ShowDialog() == true ? dlg.FileName : null);
        }
    }
}
