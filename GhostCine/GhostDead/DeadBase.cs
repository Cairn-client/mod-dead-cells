using dc.en;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod
{
    public class DeadBase : dc.GameCinematic
    {
        private Hero _hero = null!;
        private bool _animInitialized;

        public DeadBase(Hero hero, GhostKing? king)
        {
            _DeadBase.EnterGhostDead(this, hero, king);
            _hero = hero;
        }

        public override void update()
        {
            base.update();

            if (_hero == null)
            {
                destroy();
                return;
            }

            try { _hero.cancelVelocities(); } catch { }
            try { _hero.lockControlsS(0.25); } catch { }
            try { _hero.cancelSkillControlLock(); } catch { }

            // Keep a stable downed anim while waiting revive.
            try
            {
                var anim = _hero?.spr?._animManager;
                if (anim != null)
                {
                    if (!_animInitialized)
                    {
                        anim.play("stun".AsHaxeString(), null, null).loop(null);
                        _animInitialized = true;
                    }
                    else
                    {
                        var group = _hero?.spr?.groupName?.ToString();
                        if (!string.Equals(group, "stun", StringComparison.Ordinal))
                            anim.play("stun".AsHaxeString(), null, null).loop(null);
                    }
                }
            }
            catch { }
        }
    }
}
