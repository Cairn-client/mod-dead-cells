using System.Collections.Generic;
using dc.en;
using dc.tool;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        private static readonly object s_remoteFakeDeathHomunculusLock = new();
        private static readonly HashSet<dc.en.Homunculus> s_remoteFakeDeathHomunculi = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<dc.en.Homunculus, int> s_remoteFakeDeathHomunculusWarmup = new(ReferenceEqualityComparer.Instance);
        private const int RemoteFakeDeathHomunculusWarmupPreUpdates = 6;
        private int _remoteFakeDeathHomunculusPreUpdateDepth;

        internal static void RegisterRemoteFakeDeathHomunculus(dc.en.Homunculus? hom)
        {
            if (hom == null)
                return;

            lock (s_remoteFakeDeathHomunculusLock)
            {
                s_remoteFakeDeathHomunculi.Add(hom);
                s_remoteFakeDeathHomunculusWarmup[hom] = 0;
            }
        }

        internal static void UnregisterRemoteFakeDeathHomunculus(dc.en.Homunculus? hom)
        {
            if (hom == null)
                return;

            lock (s_remoteFakeDeathHomunculusLock)
            {
                s_remoteFakeDeathHomunculi.Remove(hom);
                s_remoteFakeDeathHomunculusWarmup.Remove(hom);
            }
        }

        internal static bool IsRemoteFakeDeathHomunculus(dc.en.Homunculus? hom)
        {
            if (hom == null)
                return false;

            lock (s_remoteFakeDeathHomunculusLock)
                return s_remoteFakeDeathHomunculi.Contains(hom);
        }

        private void Hook_Homunculus_preUpdate(dc.en.Hook_Homunculus.orig_preUpdate orig, dc.en.Homunculus self)
        {
            _remoteFakeDeathHomunculusPreUpdateDepth++;
            try
            {
                orig(self);
            }
            catch
            {
            }
            finally
            {
                if (_remoteFakeDeathHomunculusPreUpdateDepth > 0)
                    _remoteFakeDeathHomunculusPreUpdateDepth--;
            }

            KeepRemoteFakeDeathHomunculusVisible(self);
        }

        private void Hook_Hero_setHeadMode(Hook_Hero.orig_setHeadMode orig, Hero self, dc.tool.HeadMode m, double durationS, int? id)
        {
            if (_localFakeDead && me != null && self != null && ReferenceEquals(self, me))
                return;

            if (_remoteFakeDeathHomunculusPreUpdateDepth > 0)
                return;

            orig(self, m, durationS, id);
        }

        private void Hook_Homunculus_controlsToMe(dc.en.Hook_Homunculus.orig_controlsToMe orig, dc.en.Homunculus self)
        {
            if (!IsRemoteFakeDeathHomunculus(self))
                orig(self);
        }

        private void Hook_Homunculus_controlsToHero(dc.en.Hook_Homunculus.orig_controlsToHero orig, dc.en.Homunculus self)
        {
            if (!IsRemoteFakeDeathHomunculus(self))
                orig(self);
        }

        private void Hook_Homunculus_onDie(dc.en.Hook_Homunculus.orig_onDie orig, dc.en.Homunculus self)
        {
            if (IsRemoteFakeDeathHomunculus(self))
            {
                UnregisterRemoteFakeDeathHomunculus(self);
                return;
            }

            orig(self);
        }

        private static void KeepRemoteFakeDeathHomunculusVisible(dc.en.Homunculus self)
        {
            try { self.isOutOfGame = false; } catch { }
            try { self.lastOutOfGame = false; } catch { }
            try { self.isOnScreen = true; } catch { }
            try
            {
                if (self.onScreenRecent < 1200.0)
                    self.onScreenRecent = 1200.0;
            }
            catch
            {
            }
        }

        private static bool ShouldRunRemoteFakeDeathNativeUpdate(dc.en.Homunculus? hom, bool isPreUpdate)
        {
            if (hom == null)
                return false;

            lock (s_remoteFakeDeathHomunculusLock)
            {
                if (!s_remoteFakeDeathHomunculi.Contains(hom))
                    return true;

                if (!s_remoteFakeDeathHomunculusWarmup.TryGetValue(hom, out var warmupCount))
                    warmupCount = 0;

                if (isPreUpdate)
                {
                    if (warmupCount < RemoteFakeDeathHomunculusWarmupPreUpdates)
                    {
                        s_remoteFakeDeathHomunculusWarmup[hom] = warmupCount + 1;
                        return true;
                    }

                    return false;
                }

                return warmupCount <= RemoteFakeDeathHomunculusWarmupPreUpdates;
            }
        }
    }
}
