namespace MTGProxyBuilder.Core
{
    /// <summary>
    /// Deterministic string hash (FNV-1a, 32-bit). Unlike <see cref="string.GetHashCode()"/> — which is
    /// randomized per process in .NET — this produces the SAME value across app launches, so it's safe
    /// for ON-DISK cache keys (bleed cache filenames, project extraction folders). Using GetHashCode
    /// there meant the cache never matched after a restart and everything was reprocessed every time.
    /// </summary>
    public static class StableHash
    {
        public static string Hex(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash.ToString("X8");
            }
        }
    }
}
