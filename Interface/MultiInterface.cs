

using System.Diagnostics.Tracing;
using ModCore.Events;

namespace DeadCellsMultiplayerMod.Interface.ModuleInitializing
{

    public static class ModuleInitializing
    {
        [ModCore.Events.Event]
        public interface IOnAdvancedModuleInitializing
        {
            void OnAdvancedModuleInitializing();
        }
    }
}

