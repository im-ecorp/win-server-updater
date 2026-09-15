using System;

namespace WinUpdateManager.Core
{
    /// <summary>Maps Windows build numbers to product families and support lifecycle dates.</summary>
    public static class OsCatalog
    {
        public static string GetFamily(int build, bool isServer)
        {
            if (isServer)
            {
                switch (build)
                {
                    case 6001:
                    case 6002:
                    case 6003: return "Windows Server 2008";
                    case 7600:
                    case 7601: return "Windows Server 2008 R2";
                    case 9200: return "Windows Server 2012";
                    case 9600: return "Windows Server 2012 R2";
                    case 14393: return "Windows Server 2016";
                    case 16299: return "Windows Server, version 1709";
                    case 17134: return "Windows Server, version 1803";
                    case 17763: return "Windows Server 2019";
                    case 18362: return "Windows Server, version 1903";
                    case 18363: return "Windows Server, version 1909";
                    case 19041: return "Windows Server, version 2004";
                    case 19042: return "Windows Server, version 20H2";
                    case 20348: return "Windows Server 2022";
                    case 25398: return "Windows Server, version 23H2";
                    case 26100: return "Windows Server 2025";
                }
                if (build > 26100) return "Windows Server (build " + build + ")";
                return "Windows Server (build " + build + ")";
            }

            if (build >= 22000) return "Windows 11";
            if (build >= 10240) return "Windows 10";
            if (build == 9600) return "Windows 8.1";
            if (build == 9200) return "Windows 8";
            if (build == 7600 || build == 7601) return "Windows 7";
            if (build >= 6000 && build <= 6003) return "Windows Vista";
            return "Windows (build " + build + ")";
        }

        /// <summary>End of extended support for Long-Term Servicing server releases (null when unknown).</summary>
        public static DateTime? GetExtendedSupportEnd(int build, bool isServer)
        {
            if (!isServer) return null;
            switch (build)
            {
                case 6001:
                case 6002:
                case 6003:
                case 7600:
                case 7601: return new DateTime(2020, 1, 14);
                case 9200:
                case 9600: return new DateTime(2023, 10, 10);
                case 14393: return new DateTime(2027, 1, 12);
                case 17763: return new DateTime(2029, 1, 9);
                case 20348: return new DateTime(2031, 10, 14);
                default: return null;
            }
        }
    }
}
