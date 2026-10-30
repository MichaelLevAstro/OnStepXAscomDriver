using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input.Platform;
using Avalonia.Media;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Views
{
    // Result of a MessageDialog. Deliberately three-valued so the one window
    // serves OK, OK/Cancel, Yes/No and Yes/No/Cancel: Affirmative is OK or
    // Yes, Negative is No, Cancelled is Cancel or the window being closed.
    internal enum DialogOutcome { Affirmative, Negative, Cancelled }

    internal partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public DialogOutcome Outcome { get; private set; } = DialogOutcome.Cancelled;

        // Closing with the title bar's X leaves Outcome as Cancelled, which is
        // what MessageBox did for every shape that had a Cancel.
        public static MessageDialog Build(string title, string message, DialogSeverity severity,
                                          string primary, string secondary = null, string tertiary = null,
                                          string detail = null, bool defaultIsPrimary = true)
        {
            var dlg = new MessageDialog { Title = string.IsNullOrEmpty(title) ? "OnStepX" : title };

            dlg.FindControl<TextBlock>("MessageText").Text = message ?? "";
            dlg.ApplySeverity(severity);

            if (!string.IsNullOrEmpty(detail))
            {
                var box = dlg.FindControl<TextBox>("DetailText");
                box.Text = detail;
                box.IsVisible = true;
                var copy = dlg.FindControl<Button>("BtnCopy");
                copy.IsVisible = true;
                copy.Click += async (_, __) =>
                {
                    try
                    {
                        var clip = Avalonia.Application.Current?.ApplicationLifetime
                            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                            ? d.MainWindow?.Clipboard : dlg.Clipboard;
                        if (clip != null) await clip.SetTextAsync(detail);
                    }
                    catch { }
                };
            }

            var p = dlg.FindControl<Button>("BtnPrimary");
            p.Content = primary;
            p.Click += (_, __) => { dlg.Outcome = DialogOutcome.Affirmative; dlg.Close(); };

            if (!string.IsNullOrEmpty(secondary))
            {
                var s = dlg.FindControl<Button>("BtnSecondary");
                s.Content = secondary;
                s.IsVisible = true;
                s.Click += (_, __) => { dlg.Outcome = DialogOutcome.Negative; dlg.Close(); };
                s.IsDefault = !defaultIsPrimary;
                p.IsDefault = defaultIsPrimary;
            }

            if (!string.IsNullOrEmpty(tertiary))
            {
                var t = dlg.FindControl<Button>("BtnTertiary");
                t.Content = tertiary;
                t.IsVisible = true;
                t.Click += (_, __) => { dlg.Outcome = DialogOutcome.Cancelled; dlg.Close(); };
                t.IsCancel = true;
            }
            else if (!string.IsNullOrEmpty(secondary))
            {
                // Two-button shapes: Escape means the negative answer.
                dlg.FindControl<Button>("BtnSecondary").IsCancel = true;
            }

            return dlg;
        }

        private void ApplySeverity(DialogSeverity severity)
        {
            string glyph;
            string brushKey;
            switch (severity)
            {
                case DialogSeverity.Error:   glyph = "!"; brushKey = "Brush.Danger"; break;
                case DialogSeverity.Warning: glyph = "!"; brushKey = "Brush.Warn"; break;
                case DialogSeverity.Question: glyph = "?"; brushKey = "Brush.Info"; break;
                default: glyph = "i"; brushKey = "Brush.Info"; break;
            }

            this.FindControl<TextBlock>("MarkGlyph").Text = glyph;
            if (this.TryFindResource(brushKey, out var res) && res is IBrush brush)
                this.FindControl<Border>("Mark").Background = brush;
        }
    }
}
