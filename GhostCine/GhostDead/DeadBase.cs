using System;
using dc.en;
using DeadCellsMultiplayerMod.Ghost.GhostBase;

namespace DeadCellsMultiplayerMod
{
    public class DeadBase : dc.GameCinematic
    {
        private readonly Hero _hero;
        private HeroDeadCorpse? _corpse;
        private Homunculus? _homunculus;
        private bool _lethalFallStarted;
        private bool _cineSuppressed;
        private bool _hadHeroVisibleState;
        private bool _heroWasVisible;
        private bool _hadHeroHeadBlackState;
        private int _heroHeadBlackValue;

        public DeadBase(Hero hero, GhostKing? king)
        {
            _DeadBase.EnterGhostDead(this, hero, king);
            _hero = hero;

            CaptureHeroVisibility();
            HideHero();
            CreateCorpse();
            SuppressCineEffects();
            EnsureViewportTracksHero(immediate: true);
        }

        public override void update()
        {
            base.update();

            if (_hero == null || _hero.destroyed)
            {
                destroy();
                return;
            }

            SuppressCineEffects();

            var hasLiveHomunculus = HasLiveHomunculus();

            try { _hero.cancelVelocities(); } catch { }
            if (!hasLiveHomunculus)
            {
                try { _hero.lockControlsS(0.25); } catch { }
            }
            try { _hero.cancelSkillControlLock(); } catch { }

            HideHero();
            EnsureCorpse();
            EnsureHomunculus();
            MaintainLocalHomunculusControl();
            EnsureCorpseFalling();
            EnsureViewportTracksHero(immediate: false);
        }

        public override void onDispose()
        {
            base.onDispose();
            RestoreCineState();
            DisposeCorpse();
            DisposeHomunculus();
            RestoreHeroVisibility();
            EnsureViewportTracksHero(immediate: true);
        }

        private void EnsureCorpse()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
                CreateCorpse();
        }

        private void EnsureHomunculus()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
            {
                DisposeHomunculus();
                return;
            }

            if (!IsCorpseStabilized(corpse))
                return;

            var hom = _homunculus;
            if (hom != null)
            {
                try
                {
                    if (!hom.destroyed)
                        return;
                }
                catch
                {
                }
            }

            CreateHomunculus(corpse);
        }

        private void CreateCorpse()
        {
            DisposeCorpse();
            DisposeHomunculus();

            try
            {
                var corpse = new HeroDeadCorpse(this, _hero);
                corpse.init();
                _corpse = corpse;
                _lethalFallStarted = false;
                EnsureLethalFallStarted();
            }
            catch
            {
                _corpse = null;
            }
        }

        private void EnsureCorpseFalling()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
                return;

            KeepCorpseActive(corpse);
            EnsureLethalFallStarted();
        }

        private void EnsureLethalFallStarted()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed || _lethalFallStarted)
                return;

            _lethalFallStarted = true;
            try { corpse.startLethalFall(); } catch { }
        }

        private void CreateHomunculus(HeroDeadCorpse corpse)
        {
            if (corpse == null || corpse.destroyed)
                return;

            try
            {
                var level = corpse._level ?? _hero?._level;
                if (level == null)
                    return;

                var sourceSkill = GetHomunculusSkill(_hero);
                var hom = new Homunculus(level, corpse.cx, corpse.cy, forCinematic: false, attachedToHero: false, sourceSkill);
                hom.init();
                hom.initGfx();
                try { hom.hasMoveSounds = false; } catch { }
                try { hom.dash(_hero != null && _hero.dir < 0 ? -1 : 1); } catch { }
                try { hom.controlsToMe(); } catch { }

                try
                {
                    var px = corpse.get_targetSprPosX();
                    var py = corpse.get_targetSprPosY() - 24.0;
                    hom.setPosPixel(px, py);
                }
                catch
                {
                }

                _homunculus = hom;
                MaintainLocalHomunculusControl();
            }
            catch
            {
                _homunculus = null;
            }
        }

        private bool HasLiveHomunculus()
        {
            var hom = _homunculus;
            if (hom == null)
                return false;

            try { return !hom.destroyed; }
            catch { return false; }
        }

        private void MaintainLocalHomunculusControl()
        {
            var hom = _homunculus;
            if (hom == null)
                return;

            try
            {
                if (hom.destroyed)
                    return;
            }
            catch
            {
                return;
            }

            try
            {
                var game = dc.pr.Game.Class.ME;
                if (game != null && ReferenceEquals(game.curCine, this))
                    game.curCine = null;
            }
            catch
            {
            }

            try { hom.controlsToMe(); } catch { }
        }

        private static dc.tool.mainSkills.Homunculus? GetHomunculusSkill(Hero? hero)
        {
            if (hero == null)
                return null;

            try
            {
                var manager = hero.mainSkillsManager;
                if (manager == null)
                    return null;

                return manager.getMainSkill(dc.tool.mainSkills.Homunculus.Class) as dc.tool.mainSkills.Homunculus;
            }
            catch
            {
                return null;
            }
        }

        private static void KeepCorpseActive(HeroDeadCorpse corpse)
        {
            if (corpse == null || corpse.destroyed)
                return;

            var wasOutOfGame = false;
            try { wasOutOfGame = corpse.isOutOfGame; } catch { }

            try { corpse.isOnScreen = true; } catch { }
            try
            {
                if (corpse.onScreenRecent < 1200.0)
                    corpse.onScreenRecent = 1200.0;
            }
            catch { }

            try { corpse.lastOutOfGame = false; } catch { }
            try { corpse.isOutOfGame = false; } catch { }

            if (!wasOutOfGame)
                return;

            try { corpse.onOutOfGameChange(); } catch { }
        }

        public bool TryGetCorpsePixelPosition(out double x, out double y)
        {
            x = 0;
            y = 0;

            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
                return false;

            try
            {
                // Use physics-driven target coordinates so hero follows corpse reliably
                // even when sprite position is temporarily unavailable or delayed.
                x = corpse.get_targetSprPosX();
                y = corpse.get_targetSprPosY();
                return true;
            }
            catch
            {
            }

            var sprite = corpse.spr;
            if (sprite != null)
            {
                x = sprite.x;
                y = sprite.y;
                return true;
            }

            try
            {
                x = (corpse.cx + corpse.xr) * 24.0;
                y = (corpse.cy + corpse.yr) * 24.0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetHomunculusPixelPosition(out double x, out double y)
        {
            x = 0;
            y = 0;

            var hom = _homunculus;
            if (hom == null)
                return false;

            try
            {
                if (hom.destroyed)
                    return false;
            }
            catch
            {
                return false;
            }

                x = hom.spr.x;
                y = hom.spr.y - 20;
                return true;

        }

        public bool TryGetHomunculusAnim(out string? anim)
        {
            anim = null;

            var hom = _homunculus;
            if (hom == null)
                return false;

            try
            {
                if (hom.destroyed)
                    return false;
            }
            catch
            {
                return false;
            }

            try
            {
                var spr = hom.spr;
                var animManager = spr?.get_anim();
                if (animManager != null)
                {
                    dynamic am = animManager;
                    dynamic stack = am.stack;
                    if (stack != null)
                    {
                        int len = stack.length;
                        if (len > 0)
                        {
                            dynamic top = ((object[])stack.array)[len - 1];
                            var group = top?.group?.ToString();
                            if (!string.IsNullOrWhiteSpace(group))
                            {
                                anim = group;
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                var group = hom.spr?.groupName?.ToString();
                if (!string.IsNullOrWhiteSpace(group))
                {
                    anim = group;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public bool IsHomunculusNearCorpse(double maxDistancePx)
        {
            if (maxDistancePx <= 0)
                return false;

            if (!TryGetCorpsePixelPosition(out var corpseX, out var corpseY))
                return false;
            if (!TryGetHomunculusPixelPosition(out var headX, out var headY))
                return false;

            var dx = headX - corpseX;
            var dy = headY - corpseY;
            return dx * dx + dy * dy <= maxDistancePx * maxDistancePx;
        }

        public bool IsCorpseInLethalFall()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed || !_lethalFallStarted)
                return false;

            if (IsCorpseStabilized(corpse))
                return false;

            try
            {
                var group = corpse.spr?.groupName?.ToString();
                if (!string.IsNullOrEmpty(group) &&
                    group.IndexOf("lethalFall", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool IsCorpseStabilized(HeroDeadCorpse corpse)
        {
            try
            {
                var group = corpse.spr?.groupName?.ToString();
                if (!string.IsNullOrEmpty(group) &&
                    group.IndexOf("lethalSlam", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void HideHero()
        {
            try { _hero.visible = false; } catch { }
            SetHeroHeadVisible(false);
        }

        private void SuppressCineEffects()
        {
            if (_cineSuppressed)
            {
                TryKeepHudVisibleWhenAllowed();
                return;
            }

            try { disableBars(); } catch { }
            try { bars = 0.0; } catch { }

            try
            {
                var top = topBar;
                if (top != null)
                    top.set_visible(false);
            }
            catch
            {
            }

            try
            {
                var bottom = bottomBar;
                if (bottom != null)
                    bottom.set_visible(false);
            }
            catch
            {
            }

            // Dead player should keep normal HUD visible during fake-death state,
            // but do not override pause/full-map/UI-hidden states.
            TryKeepHudVisibleWhenAllowed();
            _cineSuppressed = true;
        }

        private static void TryKeepHudVisibleWhenAllowed()
        {
            try
            {
                var game = dc.pr.Game.Class.ME;
                if (game == null)
                    return;

                try
                {
                    if (game.paused)
                        return;
                }
                catch
                {
                }

                if (ShouldRespectMenuHiddenHud(game))
                    return;

                try
                {
                    var console = dc.ui.Console.Class.ME;
                    if (console != null && console.flags.exists(dc.ui.Console.Class.HIDE_UI))
                        return;
                }
                catch
                {
                }

                try
                {
                    dynamic hudDyn = game.hud;
                    if (hudDyn != null)
                    {
                        dynamic mini = hudDyn.minimap;
                        if (mini != null && mini.isFullscreen)
                            return;
                    }
                }
                catch
                {
                }

                try { game.hud?.show(null); } catch { }
            }
            catch
            {
            }
        }

        private static bool ShouldRespectMenuHiddenHud(dc.pr.Game game)
        {
            if (game == null)
                return false;

            try
            {
                if (game._pauseAfterFrames > 0)
                    return true;
            }
            catch
            {
            }

            try
            {
                var cine = game.curCine;
                if (cine != null && !cine.destroyed)
                {
                    var t = cine.GetType().Name;
                    if (!string.IsNullOrEmpty(t))
                    {
                        if (t.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                dynamic g = game;
                var maybeMenu = g.pauseMenu ?? g.menu ?? g.curMenu ?? g.inventoryMenu ?? g.modal;
                if (maybeMenu != null)
                {
                    try
                    {
                        if (!(bool)maybeMenu.destroyed)
                            return true;
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void RestoreCineState()
        {
            var game = dc.pr.Game.Class.ME;
            if (game == null)
                return;

            try
            {
                if (ReferenceEquals(game.curCine, this))
                    game.curCine = null;
            }
            catch
            {
            }
        }

        private void EnsureViewportTracksHero(bool immediate)
        {
            if (_hero == null || _hero.destroyed)
                return;
            if (_homunculus != null)
            {
                try
                {
                    if (!_homunculus.destroyed)
                        return;
                }
                catch
                {
                }
            }

            try
            {
                var viewport = _hero._level?.viewport;
                if (viewport == null)
                    return;

                if (!ReferenceEquals(viewport.tracked, _hero))
                    viewport.track(_hero, immediate);
            }
            catch
            {
            }
        }

        private void CaptureHeroVisibility()
        {
            if (_hadHeroVisibleState)
                return;

            try { _heroWasVisible = _hero.visible; }
            catch { _heroWasVisible = true; }
            _hadHeroVisibleState = true;

            try
            {
                var head = _hero?.heroHead;
                if (head != null)
                {
                    _heroHeadBlackValue = head.headBlack;
                    _hadHeroHeadBlackState = true;
                }
            }
            catch
            {
            }
        }

        private void RestoreHeroVisibility()
        {
            if (!_hadHeroVisibleState || _hero == null)
                return;

            try { _hero.visible = _heroWasVisible; } catch { }
            SetHeroHeadVisible(_heroWasVisible);
        }

        private void SetHeroHeadVisible(bool visible)
        {
            try
            {
                var head = _hero?.heroHead;
                if (head == null)
                    return;

                try { head.customHeadSpr?.set_visible(visible); } catch { }
                try { head.customBackSpr?.set_visible(visible); } catch { }
                try { head.headNormalSb?.set_visible(visible); } catch { }
                try { head.headAddSb?.set_visible(visible); } catch { }
                if (visible && _hadHeroHeadBlackState)
                {
                    try { head.headBlack = _heroHeadBlackValue; } catch { }
                }
                else
                {
                    try { head.headBlack = 0; } catch { }
                }
                try { head.eye?.set_visible(visible); } catch { }
            }
            catch
            {
            }
        }

        private void DisposeCorpse()
        {
            var corpse = _corpse;
            _corpse = null;
            _lethalFallStarted = false;
            if (corpse == null)
                return;

            try
            {
                if (!corpse.destroyed)
                    corpse.destroy();
            }
            catch { }

            try { corpse.dispose(); } catch { }
        }

        private void DisposeHomunculus()
        {
            var hom = _homunculus;
            _homunculus = null;
            if (hom == null)
                return;

            RemoveFromHomunculusSkillEntityList(hom);
            try
            {
                if (!hom.destroyed)
                    hom.destroy();
            }
            catch
            {
            }

            try { hom.dispose(); } catch { }
        }

        private static void RemoveFromHomunculusSkillEntityList(Homunculus hom)
        {
            if (hom == null)
                return;

            try
            {
                var bucketObj = hom._level?.entitiesByClass?.get(17969);
                if (bucketObj is dc.hl.types.ArrayObj bucket)
                    bucket.remove(hom);
            }
            catch
            {
            }
        }
    }
}
