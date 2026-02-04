using System.Diagnostics;
using dc.tool;

namespace DeadCellsMultiplayerMod;

public partial class ModEntry
{
    internal InventItem NotifyInventoryAddFromKingWeaponHooks(Hook_Inventory.orig_add orig, Inventory self, InventItem i)
    {
        if(_inventorySyncGuard)
            return orig(self, i);

        if(me != null && ReferenceEquals(self, me.inventory))
            inventItem = i;

        var result = orig(self, i);

        if(_netRole != NetRole.None && IsLocalInventory(self))
            SendEquippedWeapons(self);

        return result;
    }

    internal bool NotifyInventoryEquipFromKingWeaponHooks(Hook_Inventory.orig_equip orig, Inventory self, InventItem i)
    {
        var result = orig(self, i);
        if(_inventorySyncGuard)
            return result;
        if(!IsLocalInventory(self))
            return result;
        SendEquippedWeapons(self);
        return result;
    }

    internal void NotifyInventorySwapWeaponsFromKingWeaponHooks(Hook_Inventory.orig_swapWeapons orig, Inventory self)
    {
        orig(self);
        if(_inventorySyncGuard)
            return;
        if(!IsLocalInventory(self))
            return;
        SendEquippedWeapons(self);
    }

    internal void NotifyInventoryReplaceFromKingWeaponHooks(Hook_Inventory.orig_replace orig, Inventory self, InventItem by, InventItem oldPos)
    {
        orig(self, by, oldPos);
        if(_inventorySyncGuard)
            return;
        if(!IsLocalInventory(self))
            return;
        SendEquippedWeapons(self);
    }

    internal void NotifyLocalWeaponPrepareFromKingWeaponHooks(Weapon self)
    {
        if(_netRole == NetRole.None || self == null || me == null)
            return;

        if(!ReferenceEquals(self.owner, me))
            return;

        var item = self.item;
        if(item != null && TryGetWeaponKindId(item, out var kindId))
        {
            var slot = GetWeaponSlot(me.inventory, item);
            _net?.SendAttack(kindId!, slot, item.permanentId);
            _suppressHeroAnimUntilTicks = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 0.25);
        }
    }
}
