using System;
using dc;
using dc.en;
using dc.haxe.ds;
using dc.hl.types;
using dc.hxd;
using dc.libs.heaps;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using dc.tool._AnimationTrack;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using HaxeProxy.Runtime;
using ModCore.Storage;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod.KingHead
{
    public class Kinghead : HeroHead, IHxbitSerializable<object>
    {
        private Hero? me;
        private GhostKing? king;
        private Level? lvl;
        private dc.h2d.Object? headContainer;
        private dc.h2d.Object? headParticleContainer;
        private dc.h2d.Tile? headMaterial;
        private ArrayBytes_Int? headSkeleton;
        private bool? useLocalSpace;
        private FPoint? kingLastHeadPos;

        public Kinghead()
        {
        }

        public Kinghead(Hero _me, GhostKing _kingSkin, Level level)
        {
            me = _me;
            king = _kingSkin;
            lvl = level;
        }

        object IHxbitSerializable<object>.GetData()
        {
            return new();
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
        }


        public override void init(Level parent, dc.h2d.Object fromUI, Ref<bool> fromUI1)
        {
            var headSprite = king?.spr;
            if (headSprite != null)
            {
                headMaterial = headSprite.frameData?.tile;
                headSkeleton = ResolveHeadSkeleton(headSprite);
                var useLocal = UseLocalSpace();
                if (useLocal)
                {
                    headContainer = new dc.h2d.Object(headSprite);
                    headParticleContainer = new dc.h2d.Object(headContainer);
                }
                else
                {
                    headContainer = null;
                    headParticleContainer = new dc.h2d.Object(fromUI);
                }
                base.init(parent, headParticleContainer, fromUI1);
                RebuildHeadParticles(headParticleContainer, headMaterial);
                this.heroHasHead = true;
                this.alwaysShowHead = true;
                this.alwaysShowEye = true;
                return;
            }

            base.init(parent, fromUI, fromUI1);
            this.heroHasHead = true;
            this.alwaysShowHead = true;
            this.alwaysShowEye = true;
        }

        private void RebuildHeadParticles(dc.h2d.Object particleParent, dc.h2d.Tile? material)
        {
            if (material == null)
            {
                return;
            }

            if (this.pool != null)
            {
                this.pool.dispose();
            }
            this.pool = new ParticlePool(material, 100, 30);

            if (this.headNormalSb != null && this.headNormalSb.parent != null)
            {
                this.headNormalSb.parent.removeChild(this.headNormalSb);
            }
            if (this.headAddSb != null && this.headAddSb.parent != null)
            {
                this.headAddSb.parent.removeChild(this.headAddSb);
            }

            this.headNormalSb = new HSpriteBatch(material, particleParent);
            this.headNormalSb.hasRotationScale = true;

            this.headAddSb = new HSpriteBatch(material, particleParent);
            this.headAddSb.hasRotationScale = true;
            this.headAddSb.blendMode = new dc.h2d.BlendMode.Add();
        }
        public override void updateHeadFx(double c1)
        {
            if (king == null)
            {
                return;
            }

            var sprite = king.spr;
            double headX;
            double headY;
            if (!TryGetHeadSkeletonPosition(sprite, out headX, out headY))
            {
                if (this.forcedPos == null)
                {
                    return;
                }

                UpdateHeadFxWithKingContext(c1);
                return;
            }

            if (sprite != null && UseLocalSpace())
            {
                this.setForcedPos(headX - sprite.x, headY - sprite.y);
            }
            else
            {
                this.setForcedPos(headX, headY);
            }
            UpdateHeadFxWithKingContext(c1);
        }

        private void UpdateHeadFxWithKingContext(double c1)
        {
            var hero = me;
            var ghost = king;
            if (hero == null || ghost == null || ghost.spr == null)
            {
                return;
            }

            var savedLastHeadPos = hero.lastHeadPos;
            if (kingLastHeadPos == null)
            {
                kingLastHeadPos = new FPoint(0, 0);
            }
            hero.lastHeadPos = kingLastHeadPos;

            // Mirror king state onto hero so HeroHead logic uses the ghost context.
            var swap = new HeroStateSwap(hero, ghost);
            try
            {
                base.updateHeadFx(c1);
                this.postUpdate();
            }
            finally
            {
                kingLastHeadPos = hero.lastHeadPos;
                hero.lastHeadPos = savedLastHeadPos;
                swap.Dispose();
            }
        }

        private bool TryGetHeadSkeletonPosition(HSprite? sprite, out double headX, out double headY)
        {
            headX = 0;
            headY = 0;

            if (sprite == null)
            {
                return false;
            }

            headSkeleton = ResolveHeadSkeleton(sprite);
            if (headSkeleton == null)
            {
                return false;
            }

            var frameData = sprite.frameData;
            var pivot = sprite.pivot;
            if (frameData == null || pivot == null)
            {
                return false;
            }

            int dir = king?.dir ?? 1;
            int frame = sprite.frame;
            headX = sprite.x - frameData.realWid * pivot.centerFactorX;
            headX += AnimationTrack_Impl_.Class.x(headSkeleton, frame);
            headY = sprite.y - frameData.realHei * pivot.centerFactorY - 3;
            headY += AnimationTrack_Impl_.Class.y(headSkeleton, frame);
            return true;
        }

        private ArrayBytes_Int? ResolveHeadSkeleton(HSprite sprite)
        {
            var tracks = king?.animationTracks;
            var groupName = sprite.groupName;
            if (tracks == null || groupName == null)
            {
                return null;
            }

            var groupTracks = tracks.get(groupName) as StringMap;
            if (groupTracks == null)
            {
                return null;
            }

            return groupTracks.get("headBone".AsHaxeString()) as ArrayBytes_Int;
        }

        private bool UseLocalSpace()
        {
            if (useLocalSpace.HasValue)
            {
                return useLocalSpace.Value;
            }

            var hero = me;
            var heroHead = hero?.heroHead;
            var heroSprite = hero?.spr;
            if (hero == null || heroHead == null || heroSprite == null)
            {
                useLocalSpace = true;
                return true;
            }

            var forced = heroHead.forcedPos;
            if (forced == null)
            {
                useLocalSpace = true;
                return true;
            }

            var heroHeadX = hero.get_headX();
            var heroHeadY = hero.get_headY();
            var localX = heroHeadX - heroSprite.x;
            var localY = heroHeadY - heroSprite.y;
            var distLocal = global::System.Math.Abs(forced.x - localX) + global::System.Math.Abs(forced.y - localY);
            var distWorld = global::System.Math.Abs(forced.x - heroHeadX) + global::System.Math.Abs(forced.y - heroHeadY);
            useLocalSpace = distLocal <= distWorld;
            return useLocalSpace.Value;
        }

        private sealed class HeroStateSwap : IDisposable
        {
            private readonly Hero hero;
            private readonly HSprite? spr;
            private readonly double dx;
            private readonly double dy;
            private readonly double bdx;
            private readonly double bdy;
            private readonly double xr;
            private readonly double yr;
            private readonly int dir;
            private readonly double sprAlpha;
            private readonly bool visible;
            private readonly ArrayObj? affects;
            private readonly Level? level;
            private readonly int cx;
            private readonly int cy;

            public HeroStateSwap(Hero hero, GhostKing king)
            {
                this.hero = hero;
                spr = hero.spr;
                dx = hero.dx;
                dy = hero.dy;
                bdx = hero.bdx;
                bdy = hero.bdy;
                xr = hero.xr;
                yr = hero.yr;
                dir = hero.dir;
                sprAlpha = hero.sprAlpha;
                visible = hero.visible;
                affects = hero.affects;
                level = hero._level;
                cx = hero.cx;
                cy = hero.cy;

                hero.spr = king.spr;
                hero.dx = king.dx;
                hero.dy = king.dy;
                hero.bdx = king.bdx;
                hero.bdy = king.bdy;
                hero.xr = king.xr;
                hero.yr = king.yr;
                hero.dir = king.dir;
                hero.sprAlpha = king.sprAlpha;
                hero.visible = king.visible;
                if (king.affects != null)
                {
                    hero.affects = king.affects;
                }
                if (king._level != null)
                {
                    hero._level = king._level;
                    hero.cx = king.cx;
                    hero.cy = king.cy;
                }
            }

            public void Dispose()
            {
                hero.spr = spr;
                hero.dx = dx;
                hero.dy = dy;
                hero.bdx = bdx;
                hero.bdy = bdy;
                hero.xr = xr;
                hero.yr = yr;
                hero.dir = dir;
                hero.sprAlpha = sprAlpha;
                hero.visible = visible;
                hero.affects = affects;
                hero._level = level;
                hero.cx = cx;
                hero.cy = cy;
            }
        }

    }


}
