using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// Collects the exceptions R3 swallows. A Screen's ReSolve runs inside its Variants.Changed
    /// subscription, so a throw there never reaches the <c>UI.Variants.Set</c> / <c>UI.Orientation.Set</c>
    /// caller: R3 hands it to the unhandled-exception handler snapshotted at Subscribe time
    /// (Debug.LogException with R3 for Unity, Console.WriteLine with core R3 alone). Created before
    /// <c>UI.Open</c> subscribes the Screen, it sees them whichever R3 the host project has.
    /// </summary>
    internal sealed class R3ExceptionCapture : IDisposable
    {
        private readonly Action<Exception> _previous = ObservableSystem.GetUnhandledExceptionHandler();
        public readonly List<Exception> Caught = new();

        public R3ExceptionCapture() => ObservableSystem.RegisterUnhandledExceptionHandler(Caught.Add);

        public void Dispose() => ObservableSystem.RegisterUnhandledExceptionHandler(_previous);
    }
}
