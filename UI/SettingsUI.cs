using System;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using dc.hl.types;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.UI;

public class SettingsUI :
    IEventReceiver,
    IOnAdvancedModuleInitializing
{
    private const string MultiplayerSettingsButtonLabel = "Multiplayer settings";
    private const string MultiplayerSettingsMenuTitle = "Multiplayer settings";
    private const string MultiplayerSettingsBackLabel = "Back";
    private const int ControlActionConfirm = 14;
    private const int ControlActionQuit = 16;
    private static bool _hooksAttached;
    private static bool _pendingMultiplayerMenuCreation;
    private static int _multiplayerMenuOptionsId = -1;
    private static int _sourceOptionsId = -1;
    private static Options? _sourceOptions;
    private static Options? _multiplayerOptions;

    private ModEntry mod { get; set; }

    public SettingsUI(ModEntry entry)
    {
        mod = entry;
        EventSystem.AddReceiver(this);
    }

    void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
    {
        entry.Logger.Information("\x1b[32m[[ModEntry.SettingsUI] Initializing SettingsUI...]\x1b[0m ");

        if (_hooksAttached)
            return;

        Hook_Options.showMain += Hook_Options_showMain;
        Hook_Options.onDispose += Hook_Options_onDispose;
        Hook_OptionsBase.addSimpleWidget += Hook_OptionsBase_addSimpleWidget;
        Hook_OptionsBase.onValidate += Hook_OptionsBase_onValidate;
        Hook_OptionsBase.update += Hook_OptionsBase_update;
        Hook_OptionsBase.onQuit += Hook_OptionsBase_onQuit;
        _hooksAttached = true;
    }

    private void Hook_Options_showMain(Hook_Options.orig_showMain orig, Options self)
    {
        if (self == null)
        {
            orig(self);
            return;
        }

        if (IsMultiplayerMenuInstance(self) || _pendingMultiplayerMenuCreation)
        {
            BindMultiplayerMenuInstance(self);
            ShowMultiplayerSettingsMenu(self);
            return;
        }

        try
        {
            int leftPadding = 5;
            HlAction onSelect = new HlAction(() =>
            {
                OpenMultiplayerSettingsMenu(self);
            });

            // Insert before vanilla entries so it appears at the top.
            self.addSimpleWidget(
                MultiplayerSettingsButtonLabel.AsHaxeString(),
                null,
                onSelect,
                Ref<int>.From(ref leftPadding),
                null);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to add Multiplayer settings button");
        }

        orig(self);
    }

    private void Hook_Options_onDispose(Hook_Options.orig_onDispose orig, Options self)
    {
        bool wasSource = IsSourceOptionsInstance(self);
        bool wasMultiplayerMenu = IsMultiplayerMenuInstance(self);

        orig(self);

        if (wasMultiplayerMenu)
        {
            ClearMultiplayerMenuState();
            RestoreSourceOptionsUi();
        }

        if (!wasSource)
            return;

        _sourceOptionsId = -1;
        _sourceOptions = null;

        if (_multiplayerOptions != null && !_multiplayerOptions.destroyed)
            _multiplayerOptions.destroy();

        ClearMultiplayerMenuState();
    }

    private void OpenMultiplayerSettingsMenu(Options self)
    {
        try
        {
            if (self == null || self.destroyed)
                return;

            if (_multiplayerOptions != null && !_multiplayerOptions.destroyed)
                return;

            _sourceOptions = self;
            _sourceOptionsId = self.uniqId;

            self.pause();
            self.root?.set_visible(b: false);

            _pendingMultiplayerMenuCreation = true;
            _multiplayerOptions = new Options(null, new OptionsSection.S_Main(), false);
            _multiplayerMenuOptionsId = _multiplayerOptions.uniqId;
            _pendingMultiplayerMenuCreation = false;
        }
        catch (Exception ex)
        {
            _pendingMultiplayerMenuCreation = false;
            RestoreSourceOptionsUi();
            mod.Logger.Warning(ex, "[NetMod] Failed to open multiplayer settings menu");
        }
    }

    private void ShowMultiplayerSettingsMenu(Options self)
    {
        try
        {
            ClearMultiplayerMenuWidgets(self);
            self.title?.set_text(MultiplayerSettingsMenuTitle.AsHaxeString());

            int leftPadding = 5;
            HlAction onBack = new HlAction(() =>
            {
                CloseMultiplayerSettingsMenu();
            });

            self.addSimpleWidget(
                MultiplayerSettingsBackLabel.AsHaxeString(),
                null,
                onBack,
                Ref<int>.From(ref leftPadding),
                null);

            FilterMultiplayerMenuControlHints(self);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to build multiplayer settings menu");
        }
    }

    private void Hook_OptionsBase_onValidate(Hook_OptionsBase.orig_onValidate orig, OptionsBase self)
    {
        orig(self);
        FilterMultiplayerMenuControlHints(self);
    }

    private void Hook_OptionsBase_update(Hook_OptionsBase.orig_update orig, OptionsBase self)
    {
        orig(self);
        FilterMultiplayerMenuControlHints(self);
    }

    private void Hook_OptionsBase_onQuit(Hook_OptionsBase.orig_onQuit orig, OptionsBase self)
    {
        if (self is Options options && IsMultiplayerMenuInstance(options))
        {
            CloseMultiplayerSettingsMenu();
            return;
        }

        orig(self);
    }

    private OptionWidget Hook_OptionsBase_addSimpleWidget(
        Hook_OptionsBase.orig_addSimpleWidget orig,
        OptionsBase self,
        dc.String subStr,
        dc.String onVal,
        HlAction offsetX,
        Ref<int> parentFlow,
        dc.h2d.Flow offsetX2)
    {
        OptionWidget widget = orig(self, subStr, onVal, offsetX, parentFlow, offsetX2);

        if (!ShouldSuppressForeignWidgetInMultiplayerMenu(self, subStr))
            return widget;

        try
        {
            if (widget?.parent != null)
                widget.parent.removeChild(widget);

            self.widgets?.remove(widget);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to filter modding button from multiplayer settings menu");
        }

        return widget!;
    }

    private void CloseMultiplayerSettingsMenu()
    {
        try
        {
            Options? menu = _multiplayerOptions;
            if (menu != null && !menu.destroyed)
                menu.destroy();

            ClearMultiplayerMenuState();
            RestoreSourceOptionsUi();
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to return to vanilla settings menu");
        }
    }

    private static bool IsMultiplayerMenuInstance(Options self)
    {
        return self != null && _multiplayerMenuOptionsId >= 0 && self.uniqId == _multiplayerMenuOptionsId;
    }

    private static bool IsSourceOptionsInstance(Options self)
    {
        return self != null && _sourceOptionsId >= 0 && self.uniqId == _sourceOptionsId;
    }

    private static void BindMultiplayerMenuInstance(Options self)
    {
        if (self == null)
            return;

        _multiplayerOptions = self;
        _multiplayerMenuOptionsId = self.uniqId;
        _pendingMultiplayerMenuCreation = false;
    }

    private static void ClearMultiplayerMenuState()
    {
        _pendingMultiplayerMenuCreation = false;
        _multiplayerMenuOptionsId = -1;
        _multiplayerOptions = null;
    }

    private static bool ShouldSuppressForeignWidgetInMultiplayerMenu(OptionsBase self, dc.String subStr)
    {
        if (self is not Options options || !IsMultiplayerMenuInstance(options))
            return false;

        string label = subStr?.ToString() ?? string.Empty;
        return !IsAllowedMultiplayerMenuLabel(label);
    }

    private static bool IsAllowedMultiplayerMenuLabel(string label)
    {
        return string.Equals(label, MultiplayerSettingsBackLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearMultiplayerMenuWidgets(Options self)
    {
        if (self == null)
            return;

        self.mainFlow?.removeChildren();
        self.scrollerFlow?.removeChildren();

        ArrayObj? widgets = self.widgets;
        if (widgets == null)
            return;

        for (int i = widgets.length - 1; i >= 0; i--)
        {
            if (widgets.getDyn(i) is OptionWidget widget && widget.parent != null)
                widget.parent.removeChild(widget);
        }

        widgets.resize(0);
    }

    private static void FilterMultiplayerMenuControlHints(OptionsBase self)
    {
        if (self is not Options options || !IsMultiplayerMenuInstance(options))
            return;

        if (TitleScreenReflection.GetMemberValue(self, "fControlLabel", true) is not dc.h2d.Flow controlLabel)
            return;

        var children = controlLabel.children;
        if (children == null)
            return;

        var changed = false;
        for (var i = children.length - 1; i >= 0; i--)
        {
            if (children.array[i] is not ControlLabel label)
                continue;

            if (ShouldKeepMultiplayerControlHint(label))
                continue;

            controlLabel.removeChild(label);
            changed = true;
        }

        if (changed)
            controlLabel.reflow();
    }

    private static bool ShouldKeepMultiplayerControlHint(ControlLabel label)
    {
        if (label == null)
            return false;

        return HasControlAction(label, ControlActionConfirm)
            || HasControlAction(label, ControlActionQuit);
    }

    private static bool HasControlAction(ControlLabel label, int actionId)
    {
        ArrayObj? icons = label.icons;
        if (icons == null)
            return false;

        for (int i = 0; i < icons.length; i++)
        {
            if (icons.getDyn(i) is not ControlIcon icon)
                continue;

            if ((icon.act ?? -1) == actionId)
                return true;
        }

        return false;
    }

    private void RestoreSourceOptionsUi()
    {
        Options? source = _sourceOptions;
        if (source == null || source.destroyed)
            return;

        source.root?.set_visible(b: true);
        source.resume();
        RebuildMainSettingsSection(source);
    }

    private static void RebuildMainSettingsSection(Options self)
    {
        if (self == null)
            return;

        OptionsSection mainSection = self.mainSection;
        if (!ReferenceEquals(mainSection, null))
        {
            self.setSection(mainSection);
            return;
        }

        self.showMain();
    }
}
