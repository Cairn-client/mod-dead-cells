
namespace CooldownHelper
{
    public static class Cooldown
    {
        public static (int index, int sub) Decode(int key)
        {
            int index = (int)((uint)key >> 21);
            int sub = key & 0x1FFFFF;
            return (index, sub);
        }

        public static int Encode(int index, int sub = 0)
        {
            return (index << 21) | sub;
        }

        public static class Keys
        {
            public const int JUMP_HIT = 585;
            public const int KING_Create = 31;
            public const int AIR_SKILL = 1087;
            public const int SPECIAL_240 = 240;
        }
    }
}