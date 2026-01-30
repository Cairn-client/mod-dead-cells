using System.ComponentModel;
using System.Runtime.CompilerServices;
using dc.en;
using dc.tool;
using dc.tool.hero;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using ModCore.Storage;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod
{
    public class KingWeaponsManager : HeroWeaponsManager
    {
        private static Hero me;

        private static GhostKing king;

        public KingWeaponsManager(Hero _me, GhostKing _king) : base(me)
        {
            me = _me;
            king = _king;
        }


        public override void init()
        {
            // king.inventory.nbWeapons = this.mainWeapons.length;
            // base.init();
        }



    }
}