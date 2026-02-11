
using System;
using System.Security.Cryptography;
using System.Xml.Serialization;
using dc;
using dc.cine;
using dc.en;
using dc.en.mob;
using dc.h2d;
using dc.haxe.io;
using dc.hl.types;
using dc.hxd;
using dc.level.@struct;
using dc.libs._Cooldown;
using dc.pow;
using dc.pr;
using dc.tool;
using dc.tool.log;
using dc.ui;
using dc.ui.hud;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;
using Serilog;
using Cooldown = CooldownHelper.Cooldown;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using ModCore.Events.Interfaces.Game.Hero;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI
{

    public class MultiplayerUI :
        IEventReceiver,
        IOnAdvancedModuleInitializing,
        IOnHeroUpdate
    {
        private sealed class LifeSlot
        {
            public int SlotIndex { get; }
            public dc.ui.hud.LifeBar LifeBar { get; }
            public FlowBox Container { get; }
            public FlowBox LabelBox { get; }
            public dc.h2d.Text? LabelText { get; set; }
            public string? LastLabel { get; set; }

            public LifeSlot(int slotIndex, dc.ui.hud.LifeBar lifeBar, FlowBox container, FlowBox labelBox)
            {
                SlotIndex = slotIndex;
                LifeBar = lifeBar;
                Container = container;
                LabelBox = labelBox;
            }
        }

        private ModEntry mod { get; set; }
        private dc.h2d.Flow toplib { get; set; } = null!;
        public static dc.h2d.Flow flowContainer = null!;
        private static NetNode? _net;
        private NetNode? _boundNet;
        public int SlotIndex { get; set; }

        private LifeSlot?[] _slots = System.Array.Empty<LifeSlot?>();
        private bool[] _slotActive = System.Array.Empty<bool>();
        private HUD? _hud;

        private int lastLife = 0;
        private int lastMaxLife = 0;
        private int _lastHostConnectedClientCount = -1;

        private static MultiplayerUI? _instance;

        private dc.h2d.Object? _chatRoot;
        private Graphics? _chatBackground;
        private Graphics? _chatSeparator;
        private Graphics? _chatInputBackground;
        private Flow? _chatMessagesFlow;
        private dc.h2d.TextInput? _chatInput;
        private readonly List<dc.h2d.Text> _chatLineTexts = new();
        private readonly List<string> _chatHistory = new();

        private static readonly object ChatSync = new();
        private static readonly Queue<string> PendingChatLines = new();

        private bool _chatOpened;
        private double _chatAlpha;
        private double _chatTargetAlpha;
        private bool _chatLayoutDirty = true;

        private const int KeyEnter = 13;
        private const int KeyEsc = 27;
        private const int MaxChatLines = 10;
        private const int MaxChatHistory = 80;
        private const int MaxChatInputLength = 180;
        private const double ChatPanelXOffsetPx = 20.0;
        private const double ChatPanelBottomOffsetPx = 24.0;
        private const double ChatPanelWidthPx = 260.0;
        private const double ChatPanelHeightPx = 146.0;
        private const double ChatPanelCornerRadiusPx = 8.0;
        private const double ChatPanelBorderThicknessPx = 1.5;
        private const double ChatPaddingPx = 10.0;
        private const double ChatInputHeightPx = 22.0;
        private const double ChatControlLockSeconds = 0.12;

        public MultiplayerUI(ModEntry Entry, int slotIndex = 0)
        {
            mod = Entry;
            SlotIndex = slotIndex;
            _instance = this;
            EventSystem.AddReceiver(this);
        }

        void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
        {

            entry.Logger.Information("\x1b[32m[[ModEntry.MultiplayerUI] Initializing MultiplayerUI...]\x1b[0m ");
            Hook_HUD.initHero += Hook_HUD_initking;
            Hook_Hero.updateLifeBar += Hook_Hero_kinglifupdate;
        }


        private void Hook_HUD_initking(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);

            _hud = self;
            int slotCount = NetNode.MaxClientSlots;
            _slots = new LifeSlot?[slotCount];
            _slotActive = new bool[slotCount];
            ResetChatUi();
        }
        public bool CanUseJumpHit()
        {
            try
            {
                int key = Cooldown.Encode(Cooldown.Keys.JUMP_HIT);
                return !ModEntry.me.cd.fastCheck.exists(key);
            }
            catch { return false; }
        }
        public void Debugkeys()
        {

            if (Key.Class.isPressed(97))//num1
            {
                //LevelTransition.Class.@goto("Custom".AsHaxeString());
                Log.Debug("KeyPress");
                ConnectionUI connectionUI = new ConnectionUI(HUD.Class.ME);

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


                //ModEntry.me.deathRespawn();

                InventItem inventItem = new InventItem(new InventItemKind.Perk("P_Yolo".AsHaxeString()));
                ModEntry.me.applyItemPickEffect(ModEntry.me, inventItem);
                inventItem.clone(true, "P_Yolo".AsHaxeString());

                // int length = items.array.Count;
                // for (int i = 0; i < length; i++)
                // {
                //     inventItem = (InventItem?)items.array[i]!;
                // }
                // virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_ itemData = (virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_)item.byId.get(string3);
                // inventItem._itemData = itemData;
                ModEntry.me.tryToApplyYoloPerk();
                ModEntry.me.removeTemporaryItems();



            }
            if (!CanUseJumpHit())
            {
                return;
            }

        }
        private void Hook_Hero_kinglifupdate(Hook_Hero.orig_updateLifeBar orig, Hero self)
        {
            orig(self);
            KingLifeUpdate(self);
        }

        private dc.libs.Process process()
        {
            bool? titleLib = null;
            return new TitleScreen(titleLib);
        }

        public void KingLifeUpdate(Hero self)
        {
            _net = ModEntry._net;
            var net = _net;
            if (net == null)
            {
                if (_boundNet != null)
                {
                    _boundNet = null;
                    lastLife = int.MinValue;
                    lastMaxLife = int.MinValue;
                    _lastHostConnectedClientCount = -1;
                    ClearSlots();
                }
                return;
            }

            if (!ReferenceEquals(_boundNet, net))
            {
                _boundNet = net;
                lastLife = int.MinValue;
                lastMaxLife = int.MinValue;
                _lastHostConnectedClientCount = -1;
                ClearSlots();
            }

            if (net.IsHost)
            {
                var connectedClients = NetNode.ConnectedClientCount;
                if (connectedClients != _lastHostConnectedClientCount)
                {
                    _lastHostConnectedClientCount = connectedClients;
                    lastLife = int.MinValue;
                    lastMaxLife = int.MinValue;
                }
            }
            else
            {
                _lastHostConnectedClientCount = -1;
            }


            if (lastLife != self.life || lastMaxLife != self.maxLife)
            {
                net.SendHP(self.life, self.maxLife, self.life, self.bonusLife, self.radius);
                lastLife = self.life;
                lastMaxLife = self.maxLife;
            }

            if (_slots.Length == 0)
                return;

            if (!net.TryGetRemoteHpSnapshots(out var snapshots) || snapshots.Count == 0)
            {
                ClearSlots();
                return;
            }

            System.Array.Clear(_slotActive, 0, _slotActive.Length);
            var localId = net.id;
            foreach (var remote in snapshots)
            {
                if (!ModEntry.TryGetClientIndex(localId, remote.Id, out var slotIndex))
                    continue;

                if (slotIndex < 0 || slotIndex >= _slots.Length)
                    continue;

                var slot = _slots[slotIndex];
                if (slot == null)
                {
                    var hud = _hud;
                    if (hud == null)
                        continue;
                    var lifeBar = new dc.ui.hud.LifeBar(new LifeBarColorMode.Normal(), null);
                    slot = initkingLife(hud, slotIndex, lifeBar);
                    _slots[slotIndex] = slot;
                }

                var displayName = ModEntry.GetClientLabel(slotIndex);
                if (string.IsNullOrWhiteSpace(displayName) ||
                    string.Equals(displayName, "Guest", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(displayName, GameMenu.RemoteUsername, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(remote.Username))
                        displayName = remote.Username.Trim();
                }
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = "Guest";
                UpdateSlotLabel(slot, displayName);
                UpdateLifeBar(slot.LifeBar, remote.Life, remote.MaxLife, remote.Lif, remote.BonusLife, remote.Recover);
                _slotActive[slotIndex] = true;
            }

            RemoveInactiveSlots();
        }


        private LifeSlot initkingLife(HUD self, int slotIndex, dc.ui.hud.LifeBar kinglifeui)
        {
            this.toplib = self.topRightFlowT;

            var displayName = ModEntry.GetClientLabel(slotIndex);
            dc.String remoteUsername = displayName.AsHaxeString();
            double wh = remoteUsername.length + 2;
            double hh = 1.5;
            bool logo = true;

            FlowBox flowBox = FlowBox.Class.createBoxValidation(null, Ref<double>.Null, Ref<double>.Null, Ref<bool>.Null, null);
            flowBox.isVertical = false;
            flowBox.box.alpha = 0;


            flowBox.set_horizontalAlign(new FlowAlign.Middle());
            flowBox.set_verticalAlign(new FlowAlign.Middle());

            FlowBox uibox = FlowBox.Class.createBoxValidation(null, Ref<double>.From(ref wh), Ref<double>.From(ref hh), Ref<bool>.From(ref logo), null);
            dc.h2d.Text text_h2d = Assets.Class.makeText(remoteUsername, dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()), false, uibox);
            text_h2d.textColor = 16766720;

            flowBox.addChild(kinglifeui);
            flowBox.addChild(uibox);

            this.toplib.addChild(flowBox);
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

            kinglifeui.setSize(w, h);
            kinglifeui.get_pixelScale = self.get_pixelScale;
            kinglifeui.enableText();

            int horizontalSpacing = (int)(5 * pixelScale);

            //horizontalContainer.horizontalSpacing = horizontalSpacing;
            var slot = new LifeSlot(slotIndex, kinglifeui, flowBox, uibox)
            {
                LabelText = text_h2d,
                LastLabel = displayName
            };
            return slot;
        }

        private static void UpdateSlotLabel(LifeSlot slot, string displayName)
        {
            if (slot.LabelText != null && slot.LastLabel != displayName)
            {
                slot.LabelText.text = displayName.AsHaxeString();
                slot.LastLabel = displayName;
            }
        }

        private static void UpdateLifeBar(dc.ui.hud.LifeBar lifeBar, int max, int maxLife, int lif, int bonusLife, int recover)
        {
            lifeBar.init(max, maxLife);
            lifeBar.curState.life = (double)lif;
            lifeBar.curState.bonusLife = (double)bonusLife;
            lifeBar.curState.recover = (double)recover;
        }


        private void ClearSlots()
        {
            if (_slots.Length == 0)
                return;

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null)
                    continue;
                try
                {
                    toplib?.removeChild(slot.Container);
                    slot.Container.remove();
                }
                catch { }
                _slots[i] = null;
            }
        }

        private void RemoveInactiveSlots()
        {
            if (_slots.Length == 0)
                return;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slotActive[i])
                    continue;
                var slot = _slots[i];
                if (slot == null)
                    continue;
                try
                {
                    toplib?.removeChild(slot.Container);
                    slot.Container.remove();
                }
                catch { }
                _slots[i] = null;
            }
        }


        private sealed class SystemMessageEntry
        {
            public dc.h2d.Text Text = null!;
            public double LifetimeSeconds;
            public double FadeSeconds;
            public double ElapsedSeconds;
        }

        private sealed class PendingSystemMessage
        {
            public string Text = string.Empty;
            public double LifetimeSeconds;
            public double FadeSeconds;
        }

        private static readonly object SystemMsgSync = new();
        private static readonly Queue<PendingSystemMessage> PendingSystemMessages = new();
        private static readonly List<SystemMessageEntry> ActiveSystemMessages = new();

        private const int MaxSystemMessages = 8;
        private const double DefaultSystemMsgLifetimeSeconds = 10.0; // dont change that
        private const double DefaultSystemMsgFadeSeconds = 2.5;
        private const double SystemMsgXOffsetPx = 20.0;
        private const double SystemMsgYOffsetPx = 250.0; // dont change that
        private const double SystemMsgScale = 1.15;

        public static void PushSystemMessage(string message, double lifetimeSeconds = DefaultSystemMsgLifetimeSeconds, double fadeSeconds = DefaultSystemMsgFadeSeconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            EnqueueChatLine($"System: {message.Trim()}");

            var normalizedLifetime = System.Math.Max(0.25, lifetimeSeconds);
            var normalizedFade = System.Math.Max(0.15, System.Math.Min(fadeSeconds, normalizedLifetime));
            lock (SystemMsgSync)
            {
                PendingSystemMessages.Enqueue(new PendingSystemMessage
                {
                    Text = message.Trim(),
                    LifetimeSeconds = normalizedLifetime,
                    FadeSeconds = normalizedFade
                });
            }
        }

        public void DebugUI(string @string)
        {
            PushSystemMessage(@string, 5.0, 1.5);
        }

        private static bool EnsureSystemMessageFlow()
        {
            var hud = HUD.Class.ME;
            var root = hud?.root;
            if (root == null)
                return false;
            if (hud == null)
                return false;

            if (flowContainer == null || flowContainer.parent == null || !ReferenceEquals(flowContainer.parent, root))
            {
                try { flowContainer?.remove(); } catch { }
                flowContainer = new dc.h2d.Flow(root);
                flowContainer.multiline = true;
                flowContainer.isVertical = true;
                flowContainer.set_verticalAlign(new FlowAlign.Top());
                flowContainer.set_horizontalAlign(new FlowAlign.Left());
            }

            var pixelScale = hud.get_pixelScale.Invoke();
            flowContainer.x = SystemMsgXOffsetPx * pixelScale;
            flowContainer.y = SystemMsgYOffsetPx * pixelScale;
            return true;
        }

        private static void RemoveSystemMessageAt(int index)
        {
            if (index < 0 || index >= ActiveSystemMessages.Count)
                return;

            var entry = ActiveSystemMessages[index];
            try
            {
                flowContainer?.removeChild(entry.Text);
                entry.Text.remove();
            }
            catch
            {
            }
            ActiveSystemMessages.RemoveAt(index);
        }

        private static void EnqueueSystemMessageInternal(PendingSystemMessage pending)
        {
            if (flowContainer == null || pending == null)
                return;

            var text = Assets.Class.makeText(
                pending.Text.AsHaxeString(),
                dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                false,
                flowContainer);
            text.scaleX = SystemMsgScale;
            text.scaleY = SystemMsgScale;
            text.textColor = 16766720;
            text.alpha = 1;

            ActiveSystemMessages.Add(new SystemMessageEntry
            {
                Text = text,
                LifetimeSeconds = pending.LifetimeSeconds,
                FadeSeconds = pending.FadeSeconds,
                ElapsedSeconds = 0
            });

            while (ActiveSystemMessages.Count > MaxSystemMessages)
                RemoveSystemMessageAt(0);
        }

        private void UpdateSystemMessages(double dt)
        {
            if (!EnsureSystemMessageFlow())
                return;

            lock (SystemMsgSync)
            {
                while (PendingSystemMessages.Count > 0)
                    EnqueueSystemMessageInternal(PendingSystemMessages.Dequeue());
            }

            if (ActiveSystemMessages.Count == 0)
                return;

            for (int i = ActiveSystemMessages.Count - 1; i >= 0; i--)
            {
                var msg = ActiveSystemMessages[i];
                msg.ElapsedSeconds += dt;

                var fadeStart = System.Math.Max(0.0, msg.LifetimeSeconds - msg.FadeSeconds);
                if (msg.ElapsedSeconds >= fadeStart)
                {
                    var fadeT = (msg.ElapsedSeconds - fadeStart) / System.Math.Max(0.01, msg.FadeSeconds);
                    var alpha = 1.0 - fadeT;
                    if (alpha < 0) alpha = 0;
                    msg.Text.alpha = alpha;
                }

                if (msg.ElapsedSeconds >= msg.LifetimeSeconds)
                    RemoveSystemMessageAt(i);
            }
        }

        private static void EnqueueChatLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (ChatSync)
            {
                PendingChatLines.Enqueue(line.Trim());
            }
        }

        private void FlushPendingChatLines()
        {
            lock (ChatSync)
            {
                while (PendingChatLines.Count > 0)
                    AppendChatHistoryLine(PendingChatLines.Dequeue());
            }
        }

        private void AppendChatHistoryLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _chatHistory.Add(line);
            while (_chatHistory.Count > MaxChatHistory)
                _chatHistory.RemoveAt(0);

            _chatLayoutDirty = true;
        }

        private void ResetChatUi()
        {
            try
            {
                _chatRoot?.remove();
            }
            catch
            {
            }

            _chatRoot = null;
            _chatBackground = null;
            _chatSeparator = null;
            _chatInputBackground = null;
            _chatMessagesFlow = null;
            _chatInput = null;
            _chatLineTexts.Clear();
            _chatOpened = false;
            _chatAlpha = 0;
            _chatTargetAlpha = 0;
            _chatLayoutDirty = true;
        }

        private bool EnsureChatUi()
        {
            var hud = HUD.Class.ME;
            var root = hud?.root;
            if (root == null)
                return false;
            if (hud == null)
                return false;

            if (_chatRoot != null && _chatRoot.parent != null && ReferenceEquals(_chatRoot.parent, root))
                return true;

            ResetChatUi();

            _chatRoot = new dc.h2d.Object(root);
            _chatRoot.visible = false;
            _chatRoot.alpha = 0;

            _chatBackground = new Graphics(_chatRoot);
            _chatSeparator = new Graphics(_chatRoot);
            _chatInputBackground = new Graphics(_chatRoot);

            _chatMessagesFlow = new Flow(_chatRoot);
            _chatMessagesFlow.isVertical = true;
            _chatMessagesFlow.set_verticalAlign(new FlowAlign.Top());
            _chatMessagesFlow.set_horizontalAlign(new FlowAlign.Left());

            var sampleText = Assets.Class.makeText(
                "chat".AsHaxeString(),
                dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                false,
                _chatRoot);
            var font = sampleText.font;
            sampleText.remove();

            _chatInput = new dc.h2d.TextInput(font, _chatRoot);
            _chatInput.text = string.Empty.AsHaxeString();
            _chatInput.textColor = 0xFFFFFF;
            _chatInput.cursorBlinkTime = 0.45;
            _chatInput.onChange = new HlAction(() =>
            {
                if (_chatInput == null)
                    return;

                var txt = _chatInput.text?.ToString() ?? string.Empty;
                if (txt.Length > MaxChatInputLength)
                    _chatInput.text = txt[..MaxChatInputLength].AsHaxeString();
            });

            _chatLayoutDirty = true;
            return true;
        }

        private static void DrawRoundedRect(Graphics target, double x, double y, double width, double height, double radius)
        {
            if (target == null || width <= 0 || height <= 0)
                return;

            var r = System.Math.Max(0, System.Math.Min(radius, System.Math.Min(width, height) * 0.5));
            if (r <= 0.01)
            {
                target.drawRect(x, y, width, height);
                return;
            }

            target.drawRect(x + r, y, width - (r * 2), height);
            target.drawRect(x, y + r, width, height - (r * 2));
            target.drawCircle(x + r, y + r, r, Ref<int>.Null);
            target.drawCircle(x + width - r, y + r, r, Ref<int>.Null);
            target.drawCircle(x + r, y + height - r, r, Ref<int>.Null);
            target.drawCircle(x + width - r, y + height - r, r, Ref<int>.Null);
        }

        private void UpdateChatLayout()
        {
            if (!EnsureChatUi())
                return;

            var hud = HUD.Class.ME;
            if (hud == null || _chatRoot == null)
                return;

            var pixelScale = hud.get_pixelScale.Invoke();
            var width = ChatPanelWidthPx * pixelScale;
            var height = ChatPanelHeightPx * pixelScale;
            var padding = ChatPaddingPx * pixelScale;
            var inputHeight = ChatInputHeightPx * pixelScale;
            var inputAreaY = height - inputHeight - padding;
            var separatorY = inputAreaY - (3 * pixelScale);
            var radius = ChatPanelCornerRadiusPx * pixelScale;
            var borderThickness = ChatPanelBorderThicknessPx * pixelScale;

            var win = dc.hxd.Window.Class.getInstance();
            _chatRoot.x = ChatPanelXOffsetPx * pixelScale;
            _chatRoot.y = win.get_height() - height - (ChatPanelBottomOffsetPx * pixelScale);

            if (_chatBackground != null)
            {
                _chatBackground.clear();

                int borderColor = 0xFFFFFF;
                double borderAlpha = 0.9;
                _chatBackground.beginFill(Ref<int>.From(ref borderColor), Ref<double>.From(ref borderAlpha));
                DrawRoundedRect(_chatBackground, 0, 0, width, height, radius);
                _chatBackground.endFill();

                int bgColor = 0x575757;
                double bgAlpha = 0.72;
                _chatBackground.beginFill(Ref<int>.From(ref bgColor), Ref<double>.From(ref bgAlpha));
                DrawRoundedRect(
                    _chatBackground,
                    borderThickness,
                    borderThickness,
                    width - (borderThickness * 2),
                    height - (borderThickness * 2),
                    System.Math.Max(0, radius - borderThickness));
                _chatBackground.endFill();
            }

            if (_chatSeparator != null)
            {
                _chatSeparator.clear();
                int lineColor = 0xBDBDBD;
                double lineAlpha = 0.9;
                _chatSeparator.beginFill(Ref<int>.From(ref lineColor), Ref<double>.From(ref lineAlpha));
                _chatSeparator.drawRect(padding, separatorY, width - (padding * 2), 1.5 * pixelScale);
                _chatSeparator.endFill();
            }

            if (_chatInputBackground != null)
            {
                _chatInputBackground.clear();
                int inputBgColor = 0x575757;
                double inputBgAlpha = 0.72;
                _chatInputBackground.beginFill(Ref<int>.From(ref inputBgColor), Ref<double>.From(ref inputBgAlpha));
                DrawRoundedRect(_chatInputBackground, padding, inputAreaY, width - (padding * 2), inputHeight, 4 * pixelScale);
                _chatInputBackground.endFill();
            }

            if (_chatMessagesFlow != null)
            {
                _chatMessagesFlow.x = padding;
                _chatMessagesFlow.y = padding;
                _chatMessagesFlow.set_verticalSpacing((int)(2 * pixelScale));
            }

            if (_chatInput != null)
            {
                _chatInput.x = padding + (4 * pixelScale);
                _chatInput.y = inputAreaY + (2 * pixelScale);
                _chatInput.inputWidth = (int)(width - (padding * 2) - (8 * pixelScale));
            }
        }

        private void RebuildChatText()
        {
            if (_chatMessagesFlow == null)
                return;

            for (int i = 0; i < _chatLineTexts.Count; i++)
            {
                try
                {
                    var line = _chatLineTexts[i];
                    _chatMessagesFlow.removeChild(line);
                    line.remove();
                }
                catch
                {
                }
            }
            _chatLineTexts.Clear();

            var start = System.Math.Max(0, _chatHistory.Count - MaxChatLines);
            for (int i = start; i < _chatHistory.Count; i++)
            {
                var line = _chatHistory[i];
                var text = Assets.Class.makeText(
                    line.AsHaxeString(),
                    dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                    false,
                    _chatMessagesFlow);
                text.textColor = line.StartsWith("System:", StringComparison.OrdinalIgnoreCase)
                    ? 0xFFD487
                    : 0xF2F2F2;
                _chatLineTexts.Add(text);
            }

            _chatLayoutDirty = false;
        }

        private static string SanitizeChatInput(string raw)
        {
            var text = (raw ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            if (text.Length > MaxChatInputLength)
                text = text[..MaxChatInputLength];

            return text;
        }

        private void OpenChat()
        {
            if (!EnsureChatUi())
                return;

            _chatOpened = true;
            _chatTargetAlpha = 1;
            if (_chatRoot != null)
                _chatRoot.visible = true;
            _chatInput?.focus();
        }

        private void HideChat()
        {
            _chatOpened = false;
            _chatTargetAlpha = 0;
        }

        private void SubmitChatMessage()
        {
            if (_chatInput == null)
                return;

            var message = SanitizeChatInput(_chatInput.text?.ToString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(message))
            {
                HideChat();
                return;
            }

            _chatInput.text = string.Empty.AsHaxeString();

            var net = ModEntry._net;
            net?.SendChatMessage(message);

            var localName = string.IsNullOrWhiteSpace(GameMenu.Username)
                ? "Guest"
                : GameMenu.Username.Trim();
            EnqueueChatLine($"{localName}: {message}");
            _chatInput.focus();
        }

        private static string ResolveChatAuthor(NetNode.RemoteChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.Username))
                return message.Username.Trim();

            var net = ModEntry._net;
            var localId = net?.id ?? 0;
            if (ModEntry.TryGetClientIndex(localId, message.Id, out var slotIndex))
            {
                var label = ModEntry.GetClientLabel(slotIndex);
                if (!string.IsNullOrWhiteSpace(label))
                    return label.Trim();
            }

            if (message.Id == 1)
                return "Host";

            return $"Player {message.Id}";
        }

        private void ConsumeNetworkChatMessages()
        {
            var net = ModEntry._net;
            if (net == null)
                return;

            if (!net.TryConsumeChatMessages(out var messages) || messages.Count == 0)
                return;

            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                var body = SanitizeChatInput(message.Message);
                if (string.IsNullOrWhiteSpace(body))
                    continue;

                var author = ResolveChatAuthor(message);
                EnqueueChatLine($"{author}: {body}");
            }
        }

        private void UpdateChatFade(double dt)
        {
            if (_chatRoot == null)
                return;

            var fadeDuration = System.Math.Max(0.08, DefaultSystemMsgFadeSeconds);
            var step = dt / fadeDuration;
            if (_chatAlpha < _chatTargetAlpha)
                _chatAlpha = System.Math.Min(_chatTargetAlpha, _chatAlpha + step);
            else if (_chatAlpha > _chatTargetAlpha)
                _chatAlpha = System.Math.Max(_chatTargetAlpha, _chatAlpha - step);

            _chatRoot.alpha = _chatAlpha;
            _chatRoot.visible = _chatAlpha > 0.001 || _chatOpened;
        }

        private void RefreshChatControlLock()
        {
            if (!_chatOpened)
                return;

            var hero = ModEntry.me;
            if (hero == null)
                return;

            hero.lockControlsS(ChatControlLockSeconds);
        }

        private void UpdateChat(double dt)
        {
            ConsumeNetworkChatMessages();
            FlushPendingChatLines();
            UpdateChatLayout();

            if (_chatLayoutDirty)
                RebuildChatText();

            if (Key.Class.isPressed(KeyEnter))
            {
                if (_chatOpened)
                    SubmitChatMessage();
                else
                    OpenChat();
            }
            else if (_chatOpened && Key.Class.isPressed(KeyEsc))
            {
                HideChat();
            }

            if (_chatOpened && _chatInput != null && !_chatInput.hasFocus())
                _chatInput.focus();

            RefreshChatControlLock();

            UpdateChatFade(dt);
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            UpdateSystemMessages(dt);
            UpdateChat(dt);
            var hero = ModEntry.me;
            if (hero != null)
                KingLifeUpdate(hero);
            Debugkeys();
        }
    }
}
