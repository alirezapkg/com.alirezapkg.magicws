namespace MagicWS
{
    public static class FNV1a
    {
        private const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        public static uint Hash(string str)
        {
            uint hash = OffsetBasis;
            for (int i = 0; i < str.Length; i++)
            {
                hash ^= str[i];
                hash *= Prime;
            }
            return hash;
        }

        public static uint ReadHeader(byte[] buffer, int offset = 0)
        {
            return (uint)(buffer[offset] << 24 |
                          buffer[offset + 1] << 16 |
                          buffer[offset + 2] << 8 |
                          buffer[offset + 3]);
        }

        public static void WriteHeader(byte[] buffer, uint hash, int offset = 0)
        {
            buffer[offset] = (byte)(hash >> 24);
            buffer[offset + 1] = (byte)(hash >> 16);
            buffer[offset + 2] = (byte)(hash >> 8);
            buffer[offset + 3] = (byte)hash;
        }
    }
}