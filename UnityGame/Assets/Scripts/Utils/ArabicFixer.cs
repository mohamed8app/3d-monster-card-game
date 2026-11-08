using System;

namespace MonsterCardGame.Utils
{
    /// <summary>
    /// Text utility helper (Pass-through for 100% English Left-to-Right UI).
    /// </summary>
    public static class ArabicFixer
    {
        public static string Fix(string input)
        {
            return input ?? string.Empty;
        }
    }
}
