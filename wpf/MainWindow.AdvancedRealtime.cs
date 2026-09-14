using System;
using System.Windows.Threading;
using DisplayPresetPrototype;

namespace ShadePilotWpf
{
    public partial class MainWindow
    {
        readonly DispatcherTimer advancedApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
        StylePresetData pendingAdvancedState;
        bool advancedRealtimeReady;

        void InitializeAdvancedRealtimeApply()
        {
            advancedApplyTimer.Tick += async (_,__) =>
            {
                advancedApplyTimer.Stop();
                var state = pendingAdvancedState?.DeepClone();
                pendingAdvancedState = null;
                if (state != null && sessionReady && !exiting)
                    await ApplyPresetAsync(state);
            };
            advancedRealtimeReady = true;
        }

        void QueueAdvancedStyleApply()
        {
            if (!advancedRealtimeReady || loading || !sessionReady || exiting || settings.SimpleStyleMode || runtimeState == null)
                return;
            pendingAdvancedState = runtimeState.DeepClone();
            advancedApplyTimer.Stop();
            advancedApplyTimer.Start();
        }
    }
}
