

using System.Diagnostics.Tracing;
using ModCore.Events;

namespace DeadCellsMultiplayerMod.Interface.ModuleInitializing
{


    [ModCore.Events.Event(true)]
    public interface IOnAdvancedModuleInitializing
    {
        void OnAdvancedModuleInitializing(ModEntry entry);
    }

}

