﻿//
// HealthMonitor.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	[Serializable]
	sealed internal class HealthMonitorSettings
	{
		/// <summary>
		/// Scanning frequency.
		/// </summary>
		public int Frequency { get; set; } = 5 * 60;

		/// <summary>
		/// Free megabytes.
		/// </summary>
		public int MemLevel { get; set; } = 1000;

		/// <summary>
		/// Ignore foreground application.
		/// </summary>
		public bool MemIgnoreFocus { get; set; } = true;

		/// <summary>
		/// Ignore applications.
		/// </summary>
		public string[] IgnoreList { get; set; } = { };

		/// <summary>
		/// Cooldown in minutes before we attempt to do anything about low memory again.
		/// </summary>
		public int MemCooldown { get; set; } = 60;

		/// <summary>
		/// Fatal errors until we force exit.
		/// </summary>
		public int FatalErrorThreshold { get; set; } = 10;

		/// <summary>
		/// Log file total size at which we force exit.
		/// </summary>
		public int FatalLogSizeThreshold { get; set; } = 5;
	}

	/// <summary>
	/// Monitors for variety of problems and reports on them.
	/// </summary>
	sealed public class HealthMonitor : IDisposable // Auto-Doc
	{
		//Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();
		HealthMonitorSettings Settings = new HealthMonitorSettings();

		public HealthMonitor()
		{
			// TODO: Add different problems to monitor for

			// --------------------------------------------------------------------------------------------------------
			// What? : Fragmentation. Per drive.
			// How? : Keep reading disk access splits, warn when they exceed a threshold with sufficient samples or the split becomes excessive.
			// Use? : Recommend defrag.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : NVM performance. Per drive.
			// How? : Disk queue length. Disk delay.
			// Use? : Recommend reducing multitasking and/or moving the tasks to faster drive.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : CPU insufficiency.
			// How? : CPU instruction queue length.
			// Use? : Warn about CPU being insuffficiently scaled for the tasks being done.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Free disk space
			// How? : Make sure drives have at least 4G free space. Possibly configurable.
			// Use? : Recommend disk cleanup and/or uninstalling unused apps.
			// Opt? : Auto-empty trash.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Problematic apps
			// How? : Detect apps like TrustedInstaller, MakeCab, etc. running.
			// Use? : Inform user of high resource using background tasks that should be let to run.
			// .... Potentially inform how to temporarily mitigate the issue.
			// Opt? : Priority and affinity reduction.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Driver crashes
			// How? : No idea.
			// Use? : Recommend driver up or downgrade.
			// Opt? : None. Analyze situation. This might've happened due to running out of memory.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Underscaled network
			// How? : Network queue length
			// Use? : Recommend throttling network using apps.
			// Opt? : Check ECN state and recommend toggling it. Make sure CTCP is enabled.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Background task taking too many resources.
			// How? : Monitor total CPU usage until it goes past certain threshold, check highest CPU usage app. ...
			// ... Check if the app is in foreground.
			// Use? : Warn about intense background tasks.
			// Opt? : 
			// --------------------------------------------------------------------------------------------------------

			LoadConfig();

			if (Settings.MemLevel > 0)
			{
				memfree = new PerformanceCounterWrapper("Memory", "Available MBytes", null);
				commitbytes = new PerformanceCounterWrapper("Memory", "Committed Bytes", null);
				commitlimit = new PerformanceCounterWrapper("Memory", "Commit Limit", null);
				commitpercentile = new PerformanceCounterWrapper("Memory", "% Committed Bytes in Use", null);

				Log.Information("<Auto-Doc> Memory auto-paging level: {Level} MB", Settings.MemLevel);
			}

			healthTimer = new System.Threading.Timer(TimerCheck, null, 5000, Settings.Frequency * 60 * 1000);

			Log.Information("<Auto-Doc> Loaded");
		}

		System.Threading.Timer healthTimer = null;

		DateTime MemFreeLast = DateTime.MinValue;

		SharpConfig.Configuration cfg = null;
		void LoadConfig()
		{
			cfg = Taskmaster.LoadConfig("Health.ini");
			bool modified = false, configdirty = false;

			var gensec = cfg["General"];
			Settings.Frequency = gensec.GetSetDefault("Frequency", 5, out modified).IntValue.Constrain(1, 60 * 24);
			gensec["Frequency"].Comment = "How often we check for anything. In minutes.";
			configdirty |= modified;

			var freememsec = cfg["Free Memory"];
			freememsec.Comment = "Attempt to free memory when available memory goes below a threshold.";

			Settings.MemLevel = freememsec.GetSetDefault("Threshold", 1000, out modified).IntValue;
			// MemLevel = MemLevel > 0 ? MemLevel.Constrain(1, 2000) : 0;
			freememsec["Threshold"].Comment = "When memory goes down to this level, we act.";
			configdirty |= modified;
			if (Settings.MemLevel > 0)
			{
				Settings.MemIgnoreFocus = freememsec.GetSetDefault("Ignore foreground", true, out modified).BoolValue;
				freememsec["Ignore foreground"].Comment = "Foreground app is not touched, regardless of anything.";
				configdirty |= modified;

				Settings.IgnoreList = freememsec.GetSetDefault("Ignore list", new string[] { }, out modified).StringValueArray;
				freememsec["Ignore list"].Comment = "List of apps that we don't touch regardless of anything.";
				configdirty |= modified;

				Settings.MemCooldown = freememsec.GetSetDefault("Cooldown", 60, out modified).IntValue.Constrain(1, 180);
				freememsec["Cooldown"].Comment = "Don't do this again for this many minutes.";
			}

			// SELF-MONITORING
			var selfsec = cfg["Self"];
			Settings.FatalErrorThreshold = selfsec.GetSetDefault("Fatal error threshold", 10, out modified).IntValue.Constrain(1, 30);
			selfsec["Fatal error threshold"].Comment = "Auto-exit once number of fatal errors reaches this. 10 is very generous default.";
			configdirty |= modified;

			Settings.FatalLogSizeThreshold = selfsec.GetSetDefault("Fatal log size threshold", 10, out modified).IntValue.Constrain(1, 500);
			selfsec["Fatal log size threshold"].Comment = "Auto-exit if total log file size exceeds this. In megabytes.";
			configdirty |= modified;

			if (configdirty) Taskmaster.MarkDirtyINI(cfg);
		}

		PerformanceCounterWrapper memfree = null;
		PerformanceCounterWrapper commitbytes = null;
		PerformanceCounterWrapper commitlimit = null;
		PerformanceCounterWrapper commitpercentile = null;

		int HealthCheck_lock = 0;
		async void TimerCheck(object state)
		{
			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;

			try
			{
				await CheckErrors();
				await CheckLogs();
				await CheckMemory();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
			finally
			{
				Atomic.Unlock(ref HealthCheck_lock);
			}
		}

		DateTime FreeMemory_last = DateTime.MinValue;
		float FreeMemory_cached = 0f;
		/// <summary>
		/// Free memory, in megabytes.
		/// </summary>
		public float FreeMemory()
		{
			// this might be pointless
			var now = DateTime.Now;
			if ((now - FreeMemory_last).TotalSeconds > 2)
			{
				FreeMemory_last = now;
				return FreeMemory_cached = memfree.Value;
			}

			return FreeMemory_cached;
		}

		async Task CheckErrors()
		{
			await Task.Delay(0);

			// TODO: Maybe make this errors within timeframe instead of total...?
			if (Statistics.FatalErrors >= Settings.FatalErrorThreshold)
			{
				Log.Fatal("<Auto-Doc> Fatal error count too high, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		string logpath = System.IO.Path.Combine(Taskmaster.datapath, "Logs");
		async Task CheckLogs()
		{
			await Task.Delay(0);

			long size = 0;

			var files = System.IO.Directory.GetFiles(logpath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				var fi = new System.IO.FileInfo(System.IO.Path.Combine(logpath, filename));
				size += fi.Length;
			}

			if (size >= Settings.FatalLogSizeThreshold * 1000000)
			{
				Log.Fatal("<Auto-Doc> Log files exceeding allowed size, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		async Task CheckMemory()
		{
			await Task.Delay(0);

			// Console.WriteLine("<<Auto-Doc>> Checking...");

			if (Settings.MemLevel > 0)
			{
				var memfreemb = memfree?.Value ?? 0; // MB
				var commitb = commitbytes?.Value ?? 0;
				var commitlimitb = commitlimit?.Value ?? 0;
				var commitp = commitpercentile?.Value ?? 0;

				// Console.WriteLine("Memory free: " + string.Format("{0:N1}", memfreet) + " / " + MemLevel);
				if (memfreemb <= Settings.MemLevel)
				{
					// Console.WriteLine("<<Auto-Doc>> Memlevel below threshold.");

					var now = DateTime.Now;
					var cooldown = (now - MemFreeLast).TotalMinutes; // passed time since MemFreeLast
					MemFreeLast = now;

					// Console.WriteLine(string.Format("Cooldown: {0:N2} minutes [{1}]", cooldown, MemCooldown));

					if (cooldown >= Settings.MemCooldown)
					{
						// The following should just call something in ProcessManager

						var ignorepid = -1;
						try
						{
							if (Settings.MemIgnoreFocus)
							{
								ignorepid = Taskmaster.activeappmonitor.Foreground;
								Taskmaster.processmanager.Ignore(ignorepid);
							}

							Log.Information("<<Auto-Doc>> Free memory low [{Memory}], attempting to improve situation.", HumanInterface.ByteString((long)memfreemb * 1000000));

							await Taskmaster.processmanager?.FreeMemory(null, quiet:true);
						}
						finally
						{
							if (Settings.MemIgnoreFocus)
								Taskmaster.processmanager.Unignore(ignorepid);
						}

						// sampled too soon, OS has had no significant time to swap out data

						var memfreemb2 = memfree?.Value ?? 0; // MB
						var commitp2 = commitpercentile?.Value ?? 0;
						var commitb2 = commitbytes?.Value ?? 0;
						var actualbytes = commitb * (commitp / 100);
						var actualbytes2 = commitb2 * (commitp2 / 100);

						Log.Information("<<Auto-Doc>> Free memory: {Memory} ({Change} change observed)",
							HumanInterface.ByteString((long)(memfreemb2 * 1000)),
							//HumanInterface.ByteString((long)commitb2), HumanInterface.ByteString((long)commitlimitb),
							HumanInterface.ByteString((long)(actualbytes2 - actualbytes), true));
					}
				}
				else if (memfreemb * 1.5f <= Settings.MemLevel)
				{
					if (Taskmaster.DebugMemory)
						Log.Debug("<Memory> Free memory fairly low: {Memory}",
							HumanInterface.ByteString((long)(memfreemb * 1000000)));
				}
			}
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing health monitor...");

				healthTimer?.Dispose();
				healthTimer = null;

				commitbytes?.Dispose();
				commitbytes = null;
				commitlimit?.Dispose();
				commitlimit = null;
				commitpercentile?.Dispose();
				commitpercentile = null;
				memfree?.Dispose();
				memfree = null;

				PerformanceCounterWrapper.Sensors.Clear();
			}

			disposed = true;
		}
	}

	/*
	enum ProblemState
	{
		New,
		Interacted,
		Invalid,
		Dismissed
	}

	sealed class Problem
	{
		int Id;
		string Description;

		// 
		DateTime Occurrence;

		// don't re-state the problem in this time
		TimeSpan Cooldown;

		// user actions on this
		bool Acknowledged;
		bool Dismissed;

		// State
		ProblemState State;
	}

	interface AutoDoc
	{
		int Hooks();

	}

	sealed class MemoryAutoDoc : AutoDoc
	{
		public int Hooks() => 0;

		public MemoryAutoDoc()
		{
		}
	}
	*/
}