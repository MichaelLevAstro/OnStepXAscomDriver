using System;
using Avalonia.Threading;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Hub.Platform
{
    // Avalonia has no CommandManager, so there is no equivalent of WPF's
    // automatic requery after keyboard and mouse input — the behaviour that
    // made a button bound to a command whose CanExecute reads a text box
    // enable itself as the user typed.
    //
    // CommandRequery's own weak event already covers every explicit
    // Invalidate() call in the view models. What it does not cover is the
    // implicit requery, so anything that enabled itself for free under WPF now
    // needs its view model to say so. ConsoleViewModel.CommandInput is the one
    // known case: its setter raises PropertyChanged, so it needs a matching
    // CommandRequery.Invalidate() when the console is ported.
    //
    // This exists so the UI thread is the one raising CanExecuteChanged;
    // Invalidate is called from poll threads as well as from commands.
    internal sealed class AvaloniaCommandRequerySource : ICommandRequerySource
    {
        private readonly ICommandRequerySource _inner;

        public AvaloniaCommandRequerySource(ICommandRequerySource inner) => _inner = inner;

        public void Subscribe(EventHandler handler) => _inner.Subscribe(handler);

        public void Unsubscribe(EventHandler handler) => _inner.Unsubscribe(handler);

        public void RequestRequery()
        {
            if (Dispatcher.UIThread.CheckAccess()) _inner.RequestRequery();
            else Dispatcher.UIThread.Post(_inner.RequestRequery);
        }
    }
}
