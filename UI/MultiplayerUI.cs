
using System.Security.Cryptography;
using System.Xml.Serialization;
using dc;
using dc.cine;
using dc.en;
using dc.en.mob;
using dc.h2d;
using dc.hl.types;
using dc.hxd;
using dc.level.@struct;
using dc.libs._Cooldown;
using dc.pr;
using dc.tool;
using dc.tool.log;
using dc.ui;
using dc.ui.hud;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utitities;
using Serilog;
using Cooldown = CooldownHelper.Cooldown;

namespace DeadCellsMultiplayerMod;

public class MultiplayerUI
{
    private ModEntry mod { get; set; }
    public dc.ui.hud.LifeBar kingLife { get; set; } = null!;
    public dc.h2d.Flow toplib { get; set; } = null!;
    public static dc.h2d.Flow flowContainer = null!;
    public FlowBox flowBox = null!;
    private static NetNode? _net;
    public int SlotIndex { get; set; }
    private dc.h2d.Text? kingNameText;
    private string? lastNameText;

    private int lastLife = 0;
    private int lastMaxLife = 0;
    private int kingmaxlife = 100;

    public FlowBox box { get; set; } = null!;

    public MultiplayerUI(ModEntry Entry, int slotIndex = 0)
    {
        mod = Entry;
        SlotIndex = slotIndex;
    }

    public void init()
    {
        Hook_HUD.initHero += Hook_HUD_initking;

        Hook_Hero.updateLifeBar += Hook_Hero_kinglifupdate;
    }

    private dc.ui.hud.LifeBar? king_1;
    private dc.ui.hud.LifeBar? king_2;
    private dc.ui.hud.LifeBar? king_3;
    private void Hook_HUD_initking(Hook_HUD.orig_initHero orig, HUD self)
    {
        orig(self);
        dc.ui.hud.LifeBar[] kingLifeBars = new dc.ui.hud.LifeBar[3];

        for (int i = 0; i < kingLifeBars.Length; i++)
        {
            kingLifeBars[i] = new dc.ui.hud.LifeBar(new LifeBarColorMode.Normal(), null);
            initkingLife(self, kingLifeBars[i]);
        }
        king_1 = kingLifeBars[0];
        king_2 = kingLifeBars[1];
        king_3 = kingLifeBars[2];
    }
    public bool CanUseJumpHit()
    {
        int key = Cooldown.Encode(Cooldown.Keys.JUMP_HIT);
        return !ModEntry.me.cd.fastCheck.exists(key);
    }

    public bool CanUseAirSkill()
    {
        int key = Cooldown.Encode(Cooldown.Keys.AIR_SKILL);
        return !ModEntry.me.cd.fastCheck.exists(key);
    }
    public void Debugkeys()
    {

        if (Key.Class.isPressed(97))//num1
        {
            LevelTransition.Class.@goto("Custom".AsHaxeString());

        }
        if (Key.Class.isPressed(98))//num2
        {
            var hero = ModCore.Modules.Game.Instance.HeroInstance!;
            Zombie zombie = new Zombie(hero._level, hero.cx, hero.cy, 0, 100);
            zombie.init();
            int key = Cooldown.Encode(Cooldown.Keys.JUMP_HIT);
            ModEntry.me.cd.fastCheck.set(key, new CdInst(key, 3.0));
        }
        if (Key.Class.isPressed(99))//num3
        {
            int key = Cooldown.Encode(Cooldown.Keys.JUMP_HIT);
            ModEntry.me.cd.fastCheck.remove(key);
        }
        if (!CanUseJumpHit())
        {
            Log.Debug("跳跃命中冷却中");
            return;
        }

    }
    private bool initlif = true;
    private void Hook_Hero_kinglifupdate(Hook_Hero.orig_updateLifeBar orig, Hero self)
    {
        orig(self);

    }

    private dc.libs.Process process()
    {
        bool? titleLib = null;
        return new TitleScreen(titleLib);
    }

    public void KingLifeUpdate(Hero self)
    {
        if (initlif) this.kingLife.init(100, 100);
        initlif = false;
        var king = ModEntry._companionKing;
        if (king == null) return;
        _net = ModEntry._net;
        var net = _net;
        if (net == null) return;


        if (lastLife != self.life || lastMaxLife != self.maxLife)
        {
            net.SendHP(self.life, self.maxLife, self.life, self.bonusLife, self.radius);
            lastLife = self.life;
            lastMaxLife = self.maxLife;
        }

        var displayName = ModEntry.GetClientLabel(SlotIndex);
        if (kingNameText != null && lastNameText != displayName)
        {
            kingNameText.text = displayName.AsHaxeString();
            lastNameText = displayName;
        }

        if (!net.TryGetRemoteHP(out int life, out int maxLife, out int lif, out int bonusLife, out int recover))
            return;

        //kingLifeUpdate(king!, life, maxLife, lif, bonusLife, recover);
        this.kingmaxlife = maxLife;
        if (self.life <= 0)
        {
            life = -1;
        }
        if (this.kingLife.curState.life < 0 || self.life <= 0 || life < 0)
        {
            //self.startDeathCine();
            // GameMenu.Initialize(mod.Logger);
            // Main me = Main.Class.ME;
            // HlFunc<dc.libs.Process> pause = new HlFunc<dc.libs.Process>(this.process);
            // me.transition(null, pause, Ref<bool>.Null, null, null);
        }
    }


    public void initkingLife(HUD self, dc.ui.hud.LifeBar kinglifeui)
    {
        this.toplib = self.topRightFlowT;

        var displayName = ModEntry.GetClientLabel(SlotIndex);
        dc.String remoteUsername = displayName.AsHaxeString();
        double wh = remoteUsername.length + 2;
        double hh = 1.5;
        bool logo = true;

        this.flowBox = FlowBox.Class.createBoxValidation(null, Ref<double>.Null, Ref<double>.Null, Ref<bool>.Null, null);
        this.flowBox.isVertical = false;
        this.flowBox.box.alpha = 0;


        this.flowBox.set_horizontalAlign(new FlowAlign.Middle());
        this.flowBox.set_verticalAlign(new FlowAlign.Middle());

        this.kingLife = kinglifeui;

        FlowBox uibox = FlowBox.Class.createBoxValidation(null, Ref<double>.From(ref wh), Ref<double>.From(ref hh), Ref<bool>.From(ref logo), null);
        this.box = uibox;
        dc.h2d.Text text_h2d = Assets.Class.makeText(remoteUsername, dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()), false, this.box);
        text_h2d.textColor = 16766720;
        kingNameText = text_h2d;
        lastNameText = displayName;

        this.flowBox.addChild(this.kingLife);
        this.flowBox.addChild(this.box);

        this.toplib.addChild(this.flowBox);
        this.toplib.isVertical = true;
        this.toplib.set_verticalAlign(new FlowAlign.Top());
        this.toplib.set_horizontalAlign(new FlowAlign.Right());

        var geth = Viewport.Class.NATIVE_HEIGHT;
        var getw = Viewport.Class.NATIVE_WIDTH;
        double pixelScale = self.get_pixelScale.Invoke();

        int rightMargin = (int)(5 * pixelScale);
        int topMargin = (int)(5 * pixelScale);
        int w = (int)(100 * pixelScale);
        int h = (int)(10 * pixelScale);
        int labelHeight = (int)(hh * pixelScale);
        int labelBarGap = (int)(2 * pixelScale);
        int slotGap = (int)(6 * pixelScale);

        this.kingLife.setSize(w, h);
        this.kingLife.get_pixelScale = self.get_pixelScale;
        this.kingLife.enableText();

        int horizontalSpacing = (int)(5 * pixelScale);

        //horizontalContainer.horizontalSpacing = horizontalSpacing;
    }

    public void kingLifeUpdate(KingSkin king, dc.ui.hud.LifeBar kingLife, int max, int maxLife, int lif, int bonusLife, int recover)
    {
        var k = this.kingLife;
        k.init(max, maxLife);
        k.curState.life = (double)lif;
        k.curState.bonusLife = (double)bonusLife!;
        k.curState.recover = (double)recover;
    }


    private static Queue<dc.h2d.Text> textQueue = new Queue<dc.h2d.Text>();
    private const int MAX_TEXTS = 10;

    public void DebugUI(string @string)
    {
        if (flowContainer == null)
        {
            flowContainer = new dc.h2d.Flow(HUD.Class.ME.root);
            flowContainer.multiline = true;
            flowContainer.isVertical = true;
            flowContainer.set_verticalAlign(new FlowAlign.Top());
            flowContainer.set_horizontalAlign(new FlowAlign.Left());
        }

        dc.h2d.Text text_h2d = Assets.Class.makeText(@string.AsHaxeString(),
            dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
            false, flowContainer);
        text_h2d.scaleX = 1.5;
        text_h2d.scaleY = 1.5;
        text_h2d.textColor = 16766720;

        textQueue.Enqueue(text_h2d);

        if (textQueue.Count > MAX_TEXTS)
        {
            var oldestText = textQueue.Dequeue();
            flowContainer.removeChild(oldestText);
            oldestText.remove();
        }


    }


}
