using dc.en;
using DeadCellsMultiplayerMod.Ghost.GhostBase;

namespace DeadCellsMultiplayerMod
{
    public class DeadBase : dc.GameCinematic
    {
        private readonly Hero _hero;
        private HeroDeadCorpse? _corpse;
        private bool _hadHeroVisibleState;
        private bool _heroWasVisible;

        public DeadBase(Hero hero, GhostKing? king)
        {
            _DeadBase.EnterGhostDead(this, hero, king);
            _hero = hero;

            CaptureHeroVisibility();
            HideHero();
            CreateCorpse();
        }

        public override void update()
        {
            base.update();

            if (_hero == null || _hero.destroyed)
            {
                destroy();
                return;
            }

            try { _hero.cancelVelocities(); } catch { }
            try { _hero.lockControlsS(0.25); } catch { }
            try { _hero.cancelSkillControlLock(); } catch { }

            HideHero();
            EnsureCorpse();
            EnsureCorpseFalling();
        }

        public override void onDispose()
        {
            base.onDispose();
            DisposeCorpse();
            RestoreHeroVisibility();
        }

        private void EnsureCorpse()
        {
            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
                CreateCorpse();
        }

        private void CreateCorpse()
        {
            DisposeCorpse();

            try
            {
                var corpse = new HeroDeadCorpse(this, _hero);
                corpse.init();
                _corpse = corpse;
                TryStartLethalFall(corpse);
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

            try
            {
                if (!corpse.hasGravity)
                    TryStartLethalFall(corpse);
            }
            catch
            {
            }
        }

        private static void TryStartLethalFall(HeroDeadCorpse corpse)
        {
            try { corpse.startLethalFall(); } catch { }
        }

        public bool TryGetCorpsePixelPosition(out double x, out double y)
        {
            x = 0;
            y = 0;

            var corpse = _corpse;
            if (corpse == null || corpse.destroyed)
                return false;

            var sprite = corpse.spr;
            if (sprite == null)
                return false;

            x = sprite.x;
            y = sprite.y;
            return true;
        }

        private void HideHero()
        {
            try { _hero.visible = false; } catch { }
        }

        private void CaptureHeroVisibility()
        {
            if (_hadHeroVisibleState)
                return;

            try { _heroWasVisible = _hero.visible; }
            catch { _heroWasVisible = true; }
            _hadHeroVisibleState = true;
        }

        private void RestoreHeroVisibility()
        {
            if (!_hadHeroVisibleState || _hero == null)
                return;

            try { _hero.visible = _heroWasVisible; } catch { }
        }

        private void DisposeCorpse()
        {
            var corpse = _corpse;
            _corpse = null;
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
    }
}
