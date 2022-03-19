using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Mule
{
    internal static class AuctionSiteUtilities
    {
        public const int HASH_SIZE = 24; // size in bytes
        private static byte[] SALT_KEY = new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 };
        public static byte[] CreateHash(string input)
        {
            // Generate the hash
            Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(input,SALT_KEY);
            return pbkdf2.GetBytes(HASH_SIZE);
        }
    }
}
