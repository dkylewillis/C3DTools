using Autodesk.AutoCAD.Runtime;
using C3DTools.Infrastructure;

[assembly: ExtensionApplication(typeof(C3DTools.C3DToolsApp))]

namespace C3DTools
{
    /// <summary>
    /// Plugin entry point. AutoCAD calls Initialize() when the DLL is loaded
    /// and Terminate() when it is unloaded.
    /// </summary>
    public class C3DToolsApp : IExtensionApplication
    {
        private CancellationTokenSource? _cts;

        public void Initialize()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => PipeListener.StartAsync(_cts.Token));
        }

        public void Terminate()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
