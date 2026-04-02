namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public partial class MobsSynchronization
    {
        private const int AdaptiveRateStartMobCount = 32;
        private const int AdaptiveRateEndMobCount = 160;
        private const double HostPayloadRefreshBaseSeconds = 0.45;
        private const double HostPayloadRefreshMaxSeconds = 1.25;
        private const double ClientAffectSampleBaseSeconds = 0.20;
        private const double ClientAffectSampleMaxSeconds = 0.60;
        private const double ClientAffectResendBaseSeconds = 0.45;
        private const double ClientAffectResendMaxSeconds = 0.90;
        private const double ClientAnimPayloadRefreshSeconds = 0.55;
        private const int ParsedAnimPayloadCacheLimit = 1024;
        private const double ClientDrawKeepAliveSeconds = 0.9;
        private const double ClientInterpolationAlpha = 0.62;
        private const double MobSyncDistance = 20;
        private const double MobSyncDistanceSq = MobSyncDistance * MobSyncDistance;
        private const double MobDrawNearDistance = 20;
        private const double MobDrawNearDistanceSq = MobDrawNearDistance * MobDrawNearDistance;
        private const double ClientAiLockSeconds = 0.3;
        private const double ClientAiLockRefreshBaseSeconds = 0.09;
        private const double ClientAiLockRefreshMaxSeconds = 0.16;
        private const double ClientNetworkAttackMotionPreserveSeconds = 0.05;
        private const double ClientBossNetworkAttackMotionPreserveSeconds = 0.85;
        private const double ClientBossNetworkAttackAiPreserveSeconds = 1.2;
        private const double HostContactAttackSendCooldownSeconds = 0.3;
        private const double HostRetargetRefreshBaseSeconds = 0.05;
        private const double HostRetargetRefreshMaxSeconds = 0.16;
        private const double HostFarStateEvalBaseSeconds = 0.16;
        private const double HostFarStateEvalMaxSeconds = 0.42;
        private const double HostDormantStateEvalBaseSeconds = 0.45;
        private const double HostDormantStateEvalMaxSeconds = 1.10;
        private const double ClientFarAffectEvalBaseSeconds = 0.26;
        private const double ClientFarAffectEvalMaxSeconds = 0.70;
        private const double ClientDormantAffectEvalBaseSeconds = 0.60;
        private const double ClientDormantAffectEvalMaxSeconds = 1.45;
        private const double ClientFarDrawEvalBaseSeconds = 0.20;
        private const double ClientFarDrawEvalMaxSeconds = 0.55;
        private const double ClientDormantDrawEvalBaseSeconds = 0.65;
        private const double ClientDormantDrawEvalMaxSeconds = 1.60;
        private const double ClientMobHitReportMinIntervalSeconds = 0.05;
        private const double ClientAnimSpeedEpsilon = 0.05;
        private static readonly bool ClientSyncVerticalPosition = false;
        private const double ClientTurnSnapDeltaPx = 2.0;
        private const double MobStatePositionEpsilon = 0.35;
        private const double HostMobStateMidPositionEpsilon = 1.20;
        private const double HostMobStateFarPositionEpsilon = 2.75;
        private const double HostMobStateDormantPositionEpsilon = 6.00;
        private const double PixelsPerCase = 24.0;
        private const double MaxCoordinateMatchDistance = 96.0;
        private const double MaxCoordinateMatchDistanceSq = MaxCoordinateMatchDistance * MaxCoordinateMatchDistance;
        private const double MobStateTypeRebindSearchRadius = 96.0;
        private const double MobStateTypeRebindSearchRadiusSq = MobStateTypeRebindSearchRadius * MobStateTypeRebindSearchRadius;
        private const string ContactAttackPacketSkillId = "@contact";
        private const string OldSkillPreparePacketPrefix = "@oldprep:";
        private const string OldSkillChargeCompletePacketPrefix = "@oldcc:";
        private const string OldSkillExecutePacketPrefix = "@oldexec:";
        private const string NewSkillExecutePacketPrefix = "@newexec:";
        private const double HostQueuedOldSkillMarkerSeconds = 3.0;
        private const double ClientQueuedOldSkillMarkerSeconds = 0.4;
        private const double HostContactRetargetLockSeconds = 0.25;
        private const double HostOldSkillRetargetLockSeconds = 0.75;
        private const double ClientAffectSyncSeconds = 0.35;
        private const double AffectFramesPerSecond = 60.0;
        private const int ClientAffectSyncDefaultFrames = 21;
        private const int AffectTimeIncreaseThresholdFrames = 12;

        private static double GetClientInterpolationAlpha()
        {
            var configured = MultiplayerSettingsStorage.MobsInterpolationQuality;
            if (double.IsNaN(configured) || double.IsInfinity(configured))
                return ClientInterpolationAlpha;

            return System.Math.Clamp(configured, 0.20, 1.00);
        }

        private static bool IsClientVerticalSyncEnabled()
        {
            return ClientSyncVerticalPosition || MultiplayerSettingsStorage.SyncVerticalPosition;
        }
    }
}
