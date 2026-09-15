using System;

namespace WinUpdateManager.Core
{
    /// <summary>Invalid command line or user input.</summary>
    public sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    /// <summary>The requested operation needs an elevated (Run as administrator) process.</summary>
    public sealed class NotElevatedException : Exception
    {
        public NotElevatedException()
            : base("This operation requires administrator rights. Open Command Prompt or PowerShell with " +
                   "'Run as administrator' (or run as SYSTEM) and try again.")
        {
        }
    }

    /// <summary>An error returned by a Windows API (Windows Update Agent, Task Scheduler, SCM...).</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(string message, int errorCode, Exception inner = null)
            : base(message, inner)
        {
            ErrorCode = errorCode;
        }

        /// <summary>HRESULT / Win32 error code (0 when unknown).</summary>
        public int ErrorCode { get; private set; }
    }
}
