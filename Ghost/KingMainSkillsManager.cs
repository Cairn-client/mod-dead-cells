using System.ComponentModel;
using System.Runtime.CompilerServices;
using dc.en;
using dc.pr;
using dc.tool;
using dc.tool.hero;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using ModCore.Storage;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod
{
    public class KingMainSkillsManager : HeroMainSkillsManager
    {
        private static Hero me;

        private static Game game;

        private static GhostKing king;

        public KingMainSkillsManager(Hero _me, GhostKing _kingSkin, Game _game) : base(me, game)
        {
            me = _me;
            game = _game;
            king = _kingSkin;
        }



        public override void init()
        {
            base.init();
        }



    }
}