using System;
using Serilog;

using dc.en;
using dc.pr;
using dc.cine;
using ModCore.Utitities;

using dc.level;
using dc.tool;
using dc.libs;
using dc.en.deco;
using HaxeProxy.Runtime;


namespace DeadCellsMultiplayerMod;

public class HeroReborn
{
    public HomunculusAnal? homunculusAnal = null!;
    public UsableBody? usableBody = null!;
    public dc.en.Homunculus? homunculus = null!;

    private ModEntry entry;
    private ILogger Logger;




    public HeroReborn(ModEntry mod, ILogger logger)
    {
        //_companion = ModEntry._companion;
        Starthook();
        entry = mod;
        Logger = logger;

    }
    public void Starthook()
    {
        dc.pr.Hook_Level.attachSpecialEquipments += hook_level_herostarMain;
    }



    public void hook_level_herostarMain(Hook_Level.orig_attachSpecialEquipments orig, Level self, Room rseed, Rand cineTrans, LevelTransition pt)
    {

        if (rseed != null)
        {
            dc.String rtype = rseed.rType;
            var StringLength = rtype.length;
            if (StringLength == 12)
            {
                if (rtype.ToString().Equals("TubeEntrance", StringComparison.CurrentCultureIgnoreCase))
                {

                    bool makbool = true;
                    Marker marker = rseed.getMarker("TubeSpawn".AsHaxeString(), null, new Ref<bool>(ref makbool));
                    if (!self.game.hero.awake)
                    {
                        Marker marker2 = rseed.getMarker("UsableBody".AsHaxeString(), "main".AsHaxeString(), new Ref<bool>(ref makbool));
                        int mapx = rseed.x + marker2.cx;
                        int mapy = rseed.y + marker2.cy;
                        var hasghostskin = self.game.user.heroSkin;
                        usableBody = new UsableBody(self, mapx, mapy, 1, hasghostskin);
                        usableBody.init();
                        mapx = rseed.x + marker.cx;
                        mapy = self.map.getCeilY(rseed.x + marker.cx, rseed.y + marker.cy, null, null) - 1;
                        new HomunculusFlush(self, mapx, mapy).init();
                        Game game = self.game;
                        int? judgement = 0;

                        if (judgement == null)
                        {
                            mapx = 16711680;
                            judgement = mapx;
                        }

                        mapx = rseed.x + marker.cx;
                        mapy = self.map.getCeilY(rseed.x + marker.cx, rseed.y + marker.cy, null, null);
                        bool attachedToHero = false;
                        homunculus = new dc.en.Homunculus(self, mapx, mapy, true, attachedToHero, null);
                        homunculus.init();

                        hasghostskin = self.game.user.heroSkin;
                        homunculusAnal = new HomunculusAnal(homunculus, usableBody, false, hasghostskin);

                        var _companionKing = ModEntry._companionKing;
                        if (ModEntry._ghost == null) ModEntry._ghost = new GhostHero(Game.Class.ME, self.game.hero, Logger, entry);
                        ModEntry._ghost.SetLabel(self.game.hero, GameMenu.Username);
                        _companionKing = ModEntry._ghost.CreateGhostKing(usableBody._level);
                        Log.Debug<KingSkin>($"[DEBUG|KING]KING:{DateTime.Now},{_companionKing}", _companionKing);

                    }
                }
                return;
            }
        }
        orig(self, rseed, cineTrans, pt);

    }



}
