﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Jojatekok.MoneroAPI.Extensions.ProcessManagers
{
    public abstract class BaseRpcProcessManager : IDisposable
    {
        public event EventHandler RpcAvailabilityChanged;
        public event EventHandler<LogMessageReceivedEventArgs> OnLogMessage;

        protected event EventHandler<ProcessExitedEventArgs> Exited;

        private bool _isRpcAvailable;
        private bool _isDisposeProcessKillNecessary = true;

        private string Path { get; set; }
        private Process Process { get; set; }

        private string RpcHost { get; set; }
        private ushort RpcPort { get; set; }

        private Timer TimerCheckRpcAvailability { get; set; }

        public bool IsRpcAvailable {
            get { return _isRpcAvailable; }

            protected set {
                if (value == _isRpcAvailable) return;

                _isRpcAvailable = value;
                if (value) TimerCheckRpcAvailability.Stop();
                if (RpcAvailabilityChanged != null) RpcAvailabilityChanged(this, EventArgs.Empty);
            }
        }

        private bool IsDisposing { get; set; }

        protected bool IsDisposeProcessKillNecessary {
            get { return _isDisposeProcessKillNecessary; }
            set { _isDisposeProcessKillNecessary = value; }
        }

        private bool IsProcessAlive {
            get { return Process != null && !Process.HasExited; }
        }

        internal BaseRpcProcessManager(string path, string rpcHost, ushort rpcPort) {
            TimerCheckRpcAvailability = new Timer(delegate { CheckRpcAvailability(); });

            Path = path;
            RpcHost = rpcHost;
            RpcPort = rpcPort;
        }

        protected void StartProcess(IEnumerable<string> arguments)
        {
            if (Process != null) Process.Dispose();

            Process = new Process {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo(Path) {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                }
            };

            if (arguments != null) {
                Process.StartInfo.Arguments = string.Join(" ", arguments);
            }

            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.Exited += Process_Exited;

            Process.Start();
            Utilities.JobManager.AddProcess(Process);
            Process.BeginOutputReadLine();

            // Constantly check for the RPC port's activeness
            TimerCheckRpcAvailability.Change(Utilities.TimerSettingRpcCheckAvailabilityDueTime, Utilities.TimerSettingRpcCheckAvailabilityPeriod);
        }

        private void CheckRpcAvailability()
        {
            IsRpcAvailable = Utilities.IsPortInUse(RpcPort);
        }

        public void SendConsoleCommand(string input)
        {
            if (IsProcessAlive) {
                if (OnLogMessage != null) OnLogMessage(this, new LogMessageReceivedEventArgs(new LogMessage("> " + input, DateTime.UtcNow)));
                Process.StandardInput.WriteLine(input);
            }
        }

        protected void KillBaseProcess()
        {
            if (IsProcessAlive) {
                Process.Kill();
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            if (line == null) return;

            if (OnLogMessage != null) OnLogMessage(this, new LogMessageReceivedEventArgs(new LogMessage(line, DateTime.UtcNow)));
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            if (IsDisposing) return;

            IsRpcAvailable = false;
            Process.CancelOutputRead();
            if (Exited != null) Exited(this, new ProcessExitedEventArgs(Process.ExitCode));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing && !IsDisposing) {
                IsDisposing = true;

                TimerCheckRpcAvailability.Dispose();
                TimerCheckRpcAvailability = null;

                if (Process == null) return;

                if (IsDisposeProcessKillNecessary) {
                    if (!Process.HasExited) {
                        if (Process.Responding) {
                            if (!Process.WaitForExit(10000)) Process.Kill();
                        } else {
                            Process.Kill();
                        }
                    }

                    Process.Dispose();
                    Process = null;

                } else {
                    Process.WaitForExit();
                }
            }
        }
    }
}
