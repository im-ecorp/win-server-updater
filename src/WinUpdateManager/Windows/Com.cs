using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using WinUpdateManager.Core;

namespace WinUpdateManager.Platform
{
    /// <summary>
    /// Late-bound (IDispatch) COM access. Using late binding instead of interop assemblies keeps a single
    /// executable compatible with every Windows Update Agent version from Windows Server 2008 to 2025.
    /// </summary>
    internal static class Com
    {
        public static object Create(string progId)
        {
            Type type = Type.GetTypeFromProgID(progId, false);
            if (type == null)
                throw new ApiException("The COM class '" + progId + "' is not registered on this computer.", unchecked((int)0x80040154));
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (TargetInvocationException ex)
            {
                throw Wrap("Create " + progId, ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                if (ex is COMException || ex is UnauthorizedAccessException) throw Wrap("Create " + progId, ex);
                throw;
            }
        }

        public static object Get(object target, string name, params object[] args)
        {
            return Invoke(target, name, BindingFlags.GetProperty, args);
        }

        public static void Set(object target, string name, object value)
        {
            Invoke(target, name, BindingFlags.SetProperty, new[] { value });
        }

        public static object Call(object target, string name, params object[] args)
        {
            return Invoke(target, name, BindingFlags.InvokeMethod, args);
        }

        public static bool TrySet(object target, string name, object value)
        {
            try
            {
                Set(target, name, value);
                return true;
            }
            catch (ApiException)
            {
                return false;
            }
        }

        public static object TryGet(object target, string name)
        {
            if (target == null) return null;
            try
            {
                var value = Get(target, name);
                return value is DBNull ? null : value;
            }
            catch (ApiException)
            {
                return null;
            }
        }

        public static string GetString(object target, string name)
        {
            var v = TryGet(target, name);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(object target, string name)
        {
            var v = TryGet(target, name);
            if (v == null) return false;
            if (v is bool) return (bool)v;
            try
            {
                return Convert.ToBoolean(v, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static int GetInt(object target, string name)
        {
            return ToInt(TryGet(target, name));
        }

        public static int ToInt(object v)
        {
            if (v == null || v is DBNull) return 0;
            try
            {
                if (v is uint) return unchecked((int)(uint)v);
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static decimal GetDecimal(object target, string name)
        {
            var v = TryGet(target, name);
            if (v == null) return 0m;
            try
            {
                return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0m;
            }
        }

        /// <summary>Returns the date, or null for empty/"never" values (OLE date 1899-12-30).</summary>
        public static DateTime? GetDate(object target, string name)
        {
            var v = TryGet(target, name);
            if (!(v is DateTime)) return null;
            var d = (DateTime)v;
            return d.Year < 1901 ? (DateTime?)null : d;
        }

        public static IEnumerable<object> Items(object collection)
        {
            if (collection == null) yield break;
            int count = GetInt(collection, "Count");
            for (int i = 0; i < count; i++)
            {
                yield return Get(collection, "Item", i);
            }
        }

        public static List<string> Strings(object collection)
        {
            var list = new List<string>();
            foreach (var item in Items(collection))
            {
                if (item != null) list.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
            }
            return list;
        }

        public static void Release(object target)
        {
            try
            {
                if (target != null && Marshal.IsComObject(target)) Marshal.ReleaseComObject(target);
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private static object Invoke(object target, string name, BindingFlags flags, object[] args)
        {
            if (target == null) throw new ApiException("Internal error: COM object is null when accessing '" + name + "'.", 0);
            try
            {
                return target.GetType().InvokeMember(name, flags, null, target, args ?? new object[0], CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException ex)
            {
                throw Wrap(name, ex.InnerException ?? ex);
            }
            catch (MissingMethodException ex)
            {
                throw new ApiException("'" + name + "' is not supported by the Windows components on this server.", unchecked((int)0x80020006), ex);
            }
            catch (COMException ex)
            {
                throw Wrap(name, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw Wrap(name, ex);
            }
        }

        private static ApiException Wrap(string member, Exception ex)
        {
            var api = ex as ApiException;
            if (api != null) return api;
            int code = ex is COMException ? ((COMException)ex).ErrorCode : Marshal.GetHRForException(ex);
            string message = ex.Message == null ? string.Empty : ex.Message.Trim();
            return new ApiException(member + " failed: " + message, code, ex);
        }
    }
}
