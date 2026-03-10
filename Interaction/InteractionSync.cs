using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.en.inter;
using dc.hl.types;
using dc.pr;
using Hashlink.Virtuals;
using dc.tool.atk;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;
using Serilog;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod.Interaction;

public class InteractionSync :
    IEventReceiver,
    IOnAdvancedModuleInitializing,
    IOnHeroUpdate
{
    private const double PosTolerance = 1.0;

    private readonly ILogger _log;

    public InteractionSync(ModEntry entry)
    {
        _log = entry.Logger;
        EventSystem.AddReceiver(this);
    }

    void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
    {
        entry.Logger.Information("\x1b[32m[[InteractionSync] Initializing InteractionSync...]\x1b[0m ");

        Hook_CureMachine.postUpdate += Hook_CureMachine_postUpdate;
        Hook_Door.open += Hook_Door_open;
        Hook_Door.close += Hook_Door_close;
        Hook_Door.onDamage += Hook_Door_onDamage;
        Hook_Elevator.onStep += Hook_Elevator_onStep;
        Hook_PressurePlate.trigger += Hook_PressurePlate_trigger;
    }

    private void Hook_CureMachine_postUpdate(Hook_CureMachine.orig_postUpdate orig, CureMachine self)
    {
        orig(self);

        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive || net.IsHost)
            return;

        try
        {
            self.maxCells = 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] CureMachine maxCells set failed");
        }
    }

    private void Hook_Door_open(Hook_Door.orig_open orig, Door self, int durationMs, int? finalRatio, double? _tween)
    {
        orig(self, durationMs, finalRatio, _tween);
        TrySendDoorEvent(self, "open");
    }

    private void Hook_Door_close(Hook_Door.orig_close orig, Door self, Ref<int> delayMs)
    {
        orig(self, delayMs);
        TrySendDoorEvent(self, "close");
    }

    private void Hook_Door_onDamage(Hook_Door.orig_onDamage orig, Door self, AttackData a)
    {
        orig(self, a);
        TrySendDoorEvent(self, "damage");
    }

    private void TrySendDoorEvent(Door self, string action)
    {
        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive || net.id <= 0)
            return;

        try
        {
            var (x, y) = GetEntityPixelPos(self);
            var broken = SafeRead(() => self.broken, false);
            net.SendInterDoor(x, y, action, broken);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] Door send failed");
        }
    }

    private void Hook_Elevator_onStep(Hook_Elevator.orig_onStep orig, Elevator self)
    {
        orig(self);

        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive || net.id <= 0)
            return;

        try
        {
            var (x, y) = GetEntityPixelPos(self);
            net.SendInterElevator(x, y);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] Elevator send failed");
        }
    }

    private void Hook_PressurePlate_trigger(Hook_PressurePlate.orig_trigger orig, PressurePlate self, Entity by)
    {
        orig(self, by);

        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive || net.id <= 0)
            return;

        try
        {
            var (x, y) = GetEntityPixelPos(self);
            net.SendInterPressurePlate(x, y);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] PressurePlate send failed");
        }
    }

    private static (double x, double y) GetEntityPixelPos(Entity e)
    {
        if (e?.spr == null)
            return (0, 0);
        try
        {
            return (e.spr.x, e.spr.y);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static T SafeRead<T>(Func<T> fn, T fallback)
    {
        try { return fn(); }
        catch { return fallback; }
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive)
            return;

        if (net.TryConsumeInterDoorEvents(out var doorEvents))
        {
            ApplyRemoteDoorEvents(doorEvents);
        }

        if (net.TryConsumeInterElevatorEvents(out var elevEvents))
        {
            ApplyRemoteElevatorEvents(elevEvents);
        }

        if (net.TryConsumeInterPressurePlateEvents(out var plateEvents))
        {
            ApplyRemotePressurePlateEvents(plateEvents);
        }
    }

    private void ApplyRemoteDoorEvents(List<InterDoorEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null)
            return;

        foreach (var ev in events)
        {
            var door = FindDoorByPos(level, ev.X, ev.Y);
            if (door == null)
                continue;

            try
            {
                switch (ev.Action)
                {
                    case "open":
                        door.open(300, null, null);
                        break;
                    case "close":
                        int delayMs = 0;
                        door.close(Ref<int>.From(ref delayMs));
                        break;
                    case "damage":
                        if (ev.Broken)
                            door.broken = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Apply door event failed x={X} y={Y} action={Action}", ev.X, ev.Y, ev.Action);
            }
        }
    }

    private void ApplyRemoteElevatorEvents(List<InterElevatorEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null)
            return;

        foreach (var ev in events)
        {
            var elevator = FindElevatorByPos(level, ev.X, ev.Y);
            if (elevator == null)
                continue;

            try
            {
                elevator.onStep();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Apply elevator event failed x={X} y={Y}", ev.X, ev.Y);
            }
        }
    }

    private void ApplyRemotePressurePlateEvents(List<InterPressurePlateEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null)
            return;

        var localHero = ModEntry.me;
        if (localHero == null)
            return;

        foreach (var ev in events)
        {
            var plate = FindPressurePlateByPos(level, ev.X, ev.Y);
            if (plate == null)
                continue;

            try
            {
                plate.trigger(localHero);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Apply pressure plate event failed x={X} y={Y}", ev.X, ev.Y);
            }
        }
    }

    private static Door? FindDoorByPos(Level level, double x, double y)
    {
        return FindInteractByPos<Door>(level, x, y);
    }

    private static Elevator? FindElevatorByPos(Level level, double x, double y)
    {
        return FindInteractByPos<Elevator>(level, x, y);
    }

    private static PressurePlate? FindPressurePlateByPos(Level level, double x, double y)
    {
        return FindInteractByPos<PressurePlate>(level, x, y);
    }

    private static T? FindInteractByPos<T>(Level level, double x, double y) where T : Entity
    {
        if (level?.entities == null)
            return null;

        var entities = level.entities;
        for (var i = 0; i < entities.length; i++)
        {
            var e = entities.getDyn(i) as T;
            if (e == null)
                continue;
            try
            {
                if (e.spr != null &&
                    System.Math.Abs(e.spr.x - x) < PosTolerance &&
                    System.Math.Abs(e.spr.y - y) < PosTolerance)
                {
                    return e;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
