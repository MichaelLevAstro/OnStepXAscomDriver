using System;
using System.Windows.Input;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Hub.Platform
{
    // Hands RelayCommand straight back to WPF's CommandManager, which is where
    // it was wired before the view models moved out of this assembly.
    //
    // This is not only about the explicit Invalidate calls. CommandManager also
    // raises RequerySuggested after keyboard and mouse input of its own accord,
    // which is why the console's Send button enables itself as the user types
    // into the command box without anything asking it to. Keeping it means the
    // Windows hub's buttons behave exactly as they always have.
    internal sealed class WpfCommandRequerySource : ICommandRequerySource
    {
        public void Subscribe(EventHandler handler) => CommandManager.RequerySuggested += handler;

        public void Unsubscribe(EventHandler handler) => CommandManager.RequerySuggested -= handler;

        public void RequestRequery() => CommandManager.InvalidateRequerySuggested();
    }
}
