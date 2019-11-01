﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace MemTestHelper2
{
    class MemTest
    {
        public static readonly string EXE_NAME = "memtest.exe";
        public static readonly int WIDTH = 217, HEIGHT = 247,
                                   MAX_RAM = 2048;

        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public const string CLASSNAME = "#32770",
                            BTN_START = "Button1",
                            BTN_STOP = "Button2",
                            EDT_RAM = "Edit1",
                            STATIC_COVERAGE = "Static1",
                            // If you find this free version useful...
                            STATIC_FREE_VER = "Static2",
                            MSGBOX_OK = "Button1",
                            MSGBOX_YES = "Button1",
                            MSGBOX_NO = "Button2",
                            MSG1 = "Welcome, New MemTest User",
                            MSG2 = "Message for first-time users";

        private Process process = null;
        private bool hasStarted = false, isFinished = false;

        public enum MsgBoxButton { OK, YES, NO }

        public bool Started
        {
            get { return hasStarted; }
        }

        public bool Finished
        {
            get { return isFinished; }
        }

        public bool Minimised
        {
            get { return hasStarted ? WinAPI.IsIconic(process.MainWindowHandle) : false; }
            set
            {
                if (hasStarted)
                {
                    var hwnd = process.MainWindowHandle;

                    if (value)
                        WinAPI.ShowWindow(hwnd, WinAPI.SW_MINIMIZE);
                    else
                    {
                        if (WinAPI.IsIconic(hwnd))
                            WinAPI.ShowWindow(hwnd, WinAPI.SW_RESTORE);
                        else
                            WinAPI.SetForegroundWindow(hwnd);
                    }
                }
            }
        }

        public Point Location
        {
            get
            {
                var rect = new WinAPI.Rect();
                WinAPI.GetWindowRect(process.MainWindowHandle, ref rect);
                return new Point(rect.Left, rect.Top);
            }
            set
            {
                if (process != null && !process.HasExited)
                    WinAPI.MoveWindow(process.MainWindowHandle, (int)value.X, (int)value.Y, WIDTH, HEIGHT, true);
            }
        }

        public bool Stopping
        {
            get
            {
                if (!hasStarted || isFinished || process == null || process.HasExited)
                    return false;

                string str = WinAPI.ControlGetText(process.MainWindowHandle, STATIC_COVERAGE);
                if (str != "" && str.Contains("Ending")) return true;

                return false;
            }
        }

        public int PID
        {
            get { return process != null ? process.Id : 0; }
        }

        public void Start(double ram, bool startMinimised, int timeoutms = 3000)
        {
            process = Process.Start(EXE_NAME);
            hasStarted = true;
            isFinished = false;
            
            log.Info($"Started MemTest {PID} with ${ram} MB, " +
                     $"start minimised: {startMinimised}, " +
                     $"timeout: {timeoutms}");

            var end = DateTime.Now + TimeSpan.FromMilliseconds(timeoutms);
            // Wait for process to start.
            while (true)
            {
                if (DateTime.Now > end)
                {
                    log.Error($"Process {process.Id}: Failed to close message box 1");
                    hasStarted = false;
                    return;
                }

                if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    break;

                CloseNagMessageBox(MSG1);
                Thread.Sleep(100);
                process.Refresh();
            }

            var hwnd = process.MainWindowHandle;
            WinAPI.ControlSetText(hwnd, EDT_RAM, $"{ram:f2}");
            WinAPI.ControlSetText(hwnd, STATIC_FREE_VER, "MemTestHelper by ∫ntegral#7834");
            WinAPI.ControlClick(hwnd, BTN_START);

            end = DateTime.Now + TimeSpan.FromMilliseconds(timeoutms);
            while (true)
            {
                if (DateTime.Now > end)
                {
                    log.Error($"Process {process.Id}: Failed to close message box 2");
                    hasStarted = false;
                    return;
                }

                if (CloseNagMessageBox(MSG2))
                    break;

                Thread.Sleep(100);
            }

            if (startMinimised) Minimised = true;
        }

        public void Stop()
        {
            if (process != null && !process.HasExited && hasStarted && !isFinished)
            {
                log.Info($"Stopping MemTest {PID}");
                WinAPI.ControlClick(process.MainWindowHandle, BTN_STOP);
                isFinished = true;
            }
        }

        public void Close()
        {
            if (hasStarted && !process.HasExited)
                process.Kill();

            process = null;
            hasStarted = false;
            isFinished = false;
        }

        // Returns (coverage, errors).
        public Tuple<double, int> GetCoverageInfo()
        {
            if (process == null || process.HasExited)
                return null;

            var str = WinAPI.ControlGetText(process.MainWindowHandle, STATIC_COVERAGE);
            log.Info($"MemTest {PID} coverage string: '{str}'");
            if (str == "" || !str.Contains("Coverage")) return null;

            // Test over. 47.3% Coverage, 0 Errors
            //            ^^^^^^^^^^^^^^^^^^^^^^^^
            var start = str.IndexOfAny("0123456789".ToCharArray());
            if (start == -1)
            {
                log.Error("Failed to find start of coverage number");
                return null;
            }
            str = str.Substring(start);

            // 47.3% Coverage, 0 Errors
            // ^^^^
            // some countries use a comma as the decimal point
            var coverageStr = str.Split("%".ToCharArray())[0].Replace(',', '.');
            log.Info($"Coverage string: '{coverageStr}'");
            double coverage;
            var result = Double.TryParse(coverageStr, NumberStyles.Float, CultureInfo.InvariantCulture, out coverage);
            if (!result)
            {
                log.Error($"Failed to parse coverage % from coverage string");
                return null;
            }

            // 47.3% Coverage, 0 Errors
            //                 ^^^^^^^^
            start = str.IndexOf("Coverage, ") + "Coverage, ".Length;
            str = str.Substring(start);
            log.Info($"Error string: '{str}");
            // 0 Errors
            // ^
            int errors;
            result = Int32.TryParse(str.Substring(0, str.IndexOf(" Errors")), out errors);
            if (!result)
            {
                log.Error($"Failed to parse error count from error string");
                return null;
            }

            return Tuple.Create(coverage, errors);
        }

        public bool CloseNagMessageBox(string messageBoxCaption, int timeoutms = 3000)
        {
            if (!hasStarted || isFinished || process == null || process.HasExited)
                return false;

            log.Info($"MemTest {PID} nag message box caption: '{messageBoxCaption}'");

            var end = DateTime.Now + TimeSpan.FromMilliseconds(timeoutms);
            var hwnd = IntPtr.Zero;
            do
            {
                hwnd = WinAPI.GetHWNDFromPID(process.Id, messageBoxCaption);
                Thread.Sleep(10);
            } while (hwnd == IntPtr.Zero && DateTime.Now < end);

            if (hwnd == IntPtr.Zero)
            {
                log.Error($"Failed to find nag message box");
                return false;
            }

            end = DateTime.Now + TimeSpan.FromMilliseconds(timeoutms);
            while (true)
            {
                if (DateTime.Now > end)
                {
                    log.Error($"Failed to close nag message box");
                    return false;
                }
                   
                if (WinAPI.SendNotifyMessage(hwnd, WinAPI.WM_CLOSE, IntPtr.Zero, null) != 0)
                    return true;

                Thread.Sleep(100);
            }
        }
    }
}
