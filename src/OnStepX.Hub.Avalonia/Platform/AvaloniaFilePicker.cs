using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IFilePicker over Avalonia's StorageProvider, which maps to the native
    // chooser on each platform (common dialog on Windows, the portal or GTK
    // chooser on Linux, NSOpenPanel on macOS).
    //
    // The filter string keeps the WPF form the two call sites already pass —
    // "Label (*.ext)|*.ext|Label (*.*)|*.*" — and is parsed into the
    // FilePickerFileType list Avalonia wants.
    internal sealed class AvaloniaFilePicker : IFilePicker
    {
        private static Window Owner =>
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow
                : null;

        public async Task<string> OpenAsync(string title, string filter)
        {
            var sp = Owner?.StorageProvider;
            if (sp == null) return null;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = ParseFilter(filter),
            });

            var f = files?.FirstOrDefault();
            return f?.TryGetLocalPath();
        }

        public async Task<string> SaveAsync(string title, string filter, string suggestedName)
        {
            var sp = Owner?.StorageProvider;
            if (sp == null) return null;

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = ParseFilter(filter),
                DefaultExtension = DefaultExtension(suggestedName),
            });

            return file?.TryGetLocalPath();
        }

        private static string DefaultExtension(string suggestedName)
        {
            var dot = suggestedName?.LastIndexOf('.') ?? -1;
            return dot > 0 ? suggestedName.Substring(dot + 1) : null;
        }

        // "Sites (*.json)|*.json|All files (*.*)|*.*" -> two entries. Patterns
        // arrive semicolon-separated in the WPF form; "*.*" means everything,
        // which Avalonia spells as the wildcard on its own.
        private static List<FilePickerFileType> ParseFilter(string filter)
        {
            var types = new List<FilePickerFileType>();
            if (string.IsNullOrWhiteSpace(filter)) return types;

            var parts = filter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                var label = parts[i].Trim();
                var patterns = parts[i + 1]
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();

                if (patterns.Count == 0) continue;
                types.Add(new FilePickerFileType(label) { Patterns = patterns });
            }

            return types;
        }
    }
}
