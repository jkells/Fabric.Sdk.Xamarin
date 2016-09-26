﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Android.Content;
using Android.Runtime;
using FabricSdk;
using Java.Lang;
using Exception = System.Exception;
using String = System.String;

namespace CrashlyticsKit
{
    public class Crashlytics : Kit, ICrashlytics
    {
        private static readonly Lazy<Crashlytics> LazyInstance = new Lazy<Crashlytics>(() => new Crashlytics());
        private static readonly Regex StackTraceRegex = new Regex(@"\s*at (?<ClassName>\S*)\.(?<MethodName>\S*) (?<MethodArguments>\(.*?\)) <?(?<Offset>.*?)>? in (?<Filename>.*?):(?<LineNumber>\d*)\s*");

        private Crashlytics() : base(new Bindings.CrashlyticsKit.Crashlytics())
        {
        }

        public static ICrashlytics Instance => LazyInstance.Value;

        public string Version => Bindings.CrashlyticsKit.Crashlytics.Instance.Version;

        public void Crash()
        {
            Bindings.CrashlyticsKit.Crashlytics.Instance.Crash();
        }

        public void RecordException(Exception exception)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetString("non-fatal exception stack trace", exception.StackTrace);
            Bindings.CrashlyticsKit.Crashlytics.SetString("non-fatal exception message", exception.Message);
            Bindings.CrashlyticsKit.Crashlytics.SetString("non-fatal exception", exception.GetType().FullName);

            Bindings.CrashlyticsKit.Crashlytics.LogException(ToThrowable(exception));
        }

        public ICrashlytics SetStringValue(string key, string value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetString(key, value);
            return this;
        }

        public ICrashlytics SetBoolValue(string key, bool value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetBool(key, value);
            return this;
        }

        public ICrashlytics SetFloatValue(string key, float value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetFloat(key, value);
            return this;
        }

        public ICrashlytics SetDoubleValue(string key, double value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetDouble(key, value);
            return this;
        }

        public ICrashlytics SetIntValue(string key, int value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetInt(key, value);
            return this;
        }

        public ICrashlytics SetLongValue(string key, long value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetLong(key, value);
            return this;
        }

        public ICrashlytics SetObjectValue(string key, object value)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetString(key, value?.ToString());
            return this;
        }

        public ICrashlytics SetUserEmail(string email)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetUserEmail(email);
            return this;
        }

        public ICrashlytics SetUserIdentifier(string identifier)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetUserIdentifier(identifier);
            return this;
        }

        public ICrashlytics SetUserName(string name)
        {
            Bindings.CrashlyticsKit.Crashlytics.SetUserName(name);
            return this;
        }

        internal static Throwable ToThrowable(Exception exception)
        {
            while (exception is AggregateException)
                exception = exception.InnerException;

            var throwable = exception as Throwable;

            if (throwable != null)
            {
                return throwable;
            }

            var exceptionMessage = $"{exception.GetType().FullName}: {exception.Message}";
            throwable = new Throwable(exceptionMessage);

            var stackTrace = new List<StackTraceElement>();
            foreach (Match match in StackTraceRegex.Matches(exception.StackTrace))
            {                
                var cls = match.Groups["ClassName"].Value;
                var method = match.Groups["MethodName"].Value;
                var methodArgs = match.Groups["MethodArguments"].Value;
                var file = match.Groups["Filename"].Value;
                var offset = match.Groups["Offset"].Value;

                // -2 is Java standard for native crash without line number
                int line;
                if (int.TryParse(match.Groups["LineNumber"].Value, out line))
                {
                    if (line == 0)
                        line = -2;
                }
                else
                    line = -2;                 
                
                offset = offset.Split('+', ' ').LastOrDefault() ?? "";

                // Filename is important to Crashlytics. Without it the crashes
                // aggregate together.
                file = $"{cls}.{method}.{offset}.cs";
                
                stackTrace.Add(new StackTraceElement(cls, method + methodArgs, file, line));
            }
            throwable.SetStackTrace(stackTrace.ToArray());

            if (exception.InnerException != null)
            {
                throwable.InitCause(ToThrowable(exception.InnerException));
            }

            return throwable;
        }
    }

    public static class Initializer
    {        
        private static readonly object InitializeLock = new object();
        private static Thread.IUncaughtExceptionHandler _defaultExceptionHandler;
        private static Thread.IUncaughtExceptionHandler _uncaughtExceptionHandler;
        private static bool _initialized;

        private static void UncaughtException(object exeptionObject)
        {
            var exception = exeptionObject as Exception;
            if (exception == null)
                return;

            Bindings.CrashlyticsKit.Crashlytics.SetString("fatal exception stack trace", exception.StackTrace);
            Bindings.CrashlyticsKit.Crashlytics.SetString("fatal exception message", exception.Message);
            Bindings.CrashlyticsKit.Crashlytics.SetString("fatal exception", exception.GetType().FullName);

            var throwable = Crashlytics.ToThrowable(exception);

            _uncaughtExceptionHandler.UncaughtException(Thread.CurrentThread(), throwable);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            Environment.Exit(10);
        }

        
        public static void Initialize(this ICrashlytics crashlytics)
        {
            if (_initialized) return;
            lock (InitializeLock)
            {
                if (_initialized) return;

                Fabric.Instance.Kits.Add(crashlytics);
                Fabric.Instance.BeforeInitialize += Fabric_BeforeInitialize;
                Fabric.Instance.AfterInitialize += Fabric_AfterInitialize;

                _initialized = true;

            }
        }
        static void Fabric_BeforeInitialize(object sender, EventArgs e)
        {
            _defaultExceptionHandler = Thread.DefaultUncaughtExceptionHandler;

            Thread.DefaultUncaughtExceptionHandler = new DummyExceptionHandler();
        }

        static void Fabric_AfterInitialize(object sender, EventArgs e)
        {
            _uncaughtExceptionHandler = Thread.DefaultUncaughtExceptionHandler;
            Thread.DefaultUncaughtExceptionHandler = _defaultExceptionHandler;

            AndroidEnvironment.UnhandledExceptionRaiser += (s, a) => UncaughtException(a.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, a) => UncaughtException(a.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, a) => UncaughtException(a.Exception);
        }
    }

    internal class DummyExceptionHandler : Java.Lang.Object, Thread.IUncaughtExceptionHandler
    {
        public void UncaughtException(Thread thread, Throwable ex)
        {

        }
    }
}