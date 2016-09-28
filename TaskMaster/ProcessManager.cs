﻿//
// ProcessManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Diagnostics;

	/// <summary>
	/// Process control.
	/// </summary>
	public class ProcessController : AbstractProcessControl
	{
		public bool Increase = false;
		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr Affinity = IntPtr.Zero;
		/// <summary>
		/// Priority boost for foreground applications.
		/// </summary>
		public bool Boost = true;

		int _rescan;
		/// <summary>
		/// Delay before we try to use Scan again.
		/// </summary>
		public int Rescan
		{
			get { return _rescan; }
			set { _rescan = value >= 0 ? value : 0; }
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:TaskMaster.ProcessControl"/> class.
		/// </summary>
		/// <param name="friendlyname">Human-readable name for the process. For display purposes only.</param>
		/// <param name="executable">Executable filename.</param>
		/// <param name="priority">Target process priority.</param>
		/// <param name="increase">Increase.</param>
		/// <param name="affinity">CPU core affinity.</param>
		/// <param name="boost">Foreground process priority boost.</param>
		public ProcessController(string friendlyname, string executable, ProcessPriorityClass priority=ProcessPriorityClass.Normal, bool increase=false, int affinity=0, bool boost=true, int rescan=0)
		{
			FriendlyName = friendlyname;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(executable);
			Priority = priority;
			Increase = increase;
			Affinity = new IntPtr(affinity != 0 ? affinity : ProcessManager.allCPUsMask);
			Boost = boost;

			Rescan = rescan;

			Log.Trace(FriendlyName + " (" + Executable + "), " + Priority + (Affinity != IntPtr.Zero ? ", Mask:" + Affinity : "") + (Rescan>0 ? ", Rescan: " + Rescan + " minutes":""));
		}

		// TODO EVENT(??)
		public bool Touch(Process process)
		{
			Debug.Assert(process != null);

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.VeryVerbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", Executable, process.Id));
					return false;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				return false; // we don't care what this error is
			}

			if (TaskMaster.VeryVerbose)
				Log.Debug(string.Format("{0} ({1}, pid:{2})", FriendlyName, Executable, process.Id));

			bool mAffinity, mPriority, mBoost, mModified=false;
			lock (process)
			{
				mBoost = mPriority = mAffinity = false;
				IntPtr oldAffinity = process.ProcessorAffinity;
				ProcessPriorityClass oldPriority = process.PriorityClass;
				LastSeen = DateTime.Now;

				if (((ProcessManager.PriorityToInt(process.PriorityClass) < ProcessManager.PriorityToInt(Priority)) && Increase) ||
					 (ProcessManager.PriorityToInt(process.PriorityClass) > ProcessManager.PriorityToInt(Priority)))
				{
					process.PriorityClass = Priority;
					mModified = mPriority = true;
				}

				if (process.ProcessorAffinity != Affinity)
				{
					//CLEANUP: System.Console.WriteLine("Current affinity: {0}", Convert.ToString(item.ProcessorAffinity.ToInt32(), 2));
					//CLEANUP: System.Console.WriteLine("Target affinity: {0}", Convert.ToString(proc.Affinity.ToInt32(), 2));
					try
					{
						process.ProcessorAffinity = Affinity;
						mModified = mAffinity = true;
					}
					catch (System.ComponentModel.Win32Exception)
					{
						Log.Warn(string.Format("Couldn't modify process ({0}, #{1}) affinity [{2} -> {3}].", Executable, process.Id, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32()));
					}
				}

				if (process.PriorityBoostEnabled != Boost)
				{
					process.PriorityBoostEnabled = Boost;
					mModified = mBoost = true;
				}

				if (mModified)
				{
					Adjusts += 1;

					LastTouch = DateTime.Now;

					// TODO: Is StringBuilder fast enough for this to be good idea?
					System.Text.StringBuilder ls = new System.Text.StringBuilder();
					ls.Append(Executable).Append(" (pid:").Append(process.Id).Append(") - ");
					//ls.Append("(").Append(control.Executable).Append(") =");
					if (mPriority)
						ls.Append(" Priority(").Append(oldPriority).Append(" -> ").Append(Priority).Append(")");
					if (mAffinity)
						ls.Append(" Affinity(").Append(oldAffinity).Append(" -> ").Append(Affinity).Append(")");
					if (mBoost)
						ls.Append(" Boost(").Append(Boost).Append(")");
					//ls.Append("; Start: ").Append(process.StartTime); // when the process was started // DEBUG
					Log.Info(ls.ToString());
				}
				else
					if (TaskMaster.VeryVerbose)
						Log.Trace(string.Format("'{0}' (pid:{1}) seems to be OK already.", Executable, process.Id));
			}

			if (mModified)
				onTouchHandler(this, new ProcessEventArgs { Control = this, Process = process });

			ScanScheduler();

			return mModified;
		}

		int ScanScheduled = 0;
		void ScanScheduler()
		{
			if (Rescan > 0 && (DateTime.Now - LastScan).TotalMinutes >= Rescan)
			{
				if (System.Threading.Interlocked.CompareExchange(ref ScanScheduled, 1, 0) == 1)
					return;

				Log.Info(string.Format("'{0}' detected, rescanning.", FriendlyName));
				System.Threading.Tasks.Task.Run(async () =>
				{
					await Scan();
					ScanScheduled = 0;
				});
			}
		}

		DateTime LastScan = DateTime.MinValue;
		public async System.Threading.Tasks.Task Scan()
		{
			await System.Threading.Tasks.Task.Delay(100);

			Process[] procs = Process.GetProcessesByName(ExecutableFriendlyName);
			if (procs.Length == 0)
				return;

			LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (TaskMaster.VeryVerbose)
				Log.Trace("Scanning '" + FriendlyName + "' (found " + procs.Length + " instances)");

			int tc = 0;
			foreach (Process process in procs)
			{
				try
				{
					if (Touch(process))
						tc++;
				}
				catch (Exception ex)
				{
					Log.Warn(string.Format("Failed to control '{0}' (pid:{1})", Executable, process.Id));
					Console.Error.WriteLine(ex);
				}
			}

			if (TaskMaster.Verbose)
				Log.Trace("Scan for '" + FriendlyName + "' modified " + tc + " instance(s)");
		}

		public static event EventHandler<ProcessEventArgs> onTouch;
		void onTouchHandler(object sender, ProcessEventArgs e)
		{
			EventHandler<ProcessEventArgs> handler = onTouch;
			if (handler != null)
				handler(this, e);
		}
	}

	public class PathControl : AbstractProcessControl
	{
		public string Subpath;
		public string Path;

		public PathControl(string name, string executable, ProcessPriorityClass priority, string subpath, string path=null)
		{
			FriendlyName = name;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(Executable);
			Priority = priority;
			Subpath = subpath;
			Path = path;
			if (path != null)
				Log.Info(string.Format("'{0}' watched in: {1} [{2}]", FriendlyName, Path, Priority));
			else
				Log.Info(string.Format("'{0}' matching for '{1}' [{2}]", Executable, Subpath, Priority));
		}

		public bool Touch(Process process, string path)
		{
			Debug.Assert(process != null);
			Debug.Assert(path != null && path.Length != 0);

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.Verbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", process.ProcessName, process.Id));
					return false;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				return false; // we don't care wwhat this error is
			}

			string name = System.IO.Path.GetFileName(path);
			try
			{
				ProcessPriorityClass oldPriority = process.PriorityClass;
				if (ProcessManager.PriorityToInt(process.PriorityClass) < ProcessManager.PriorityToInt(Priority)) // TODO: possibly allow decreasing priority, but for this 
				{
					process.PriorityClass = Priority;
					LastSeen = DateTime.Now;
					Adjusts += 1;
					onTouchHandler(this, new PathControlEventArgs());

					Log.Info(string.Format("{0} (pid:{1}); Priority({2} -> {3})", name, process.Id, oldPriority, Priority));

					return true;
				}

				Log.Debug(string.Format("{0} (pid:{1}); looks OK, not touched.", name, process.Id));
			}
			catch
			{
				Log.Info(string.Format("Failed to touch '{0}' (pid:{1})", name, process.Id));
			}

			return false;
		}

		public bool Locate()
		{
			if (TaskMaster.Verbose)
				Log.Trace(FriendlyName + " (" + Executable + ")");
			if (Path != null && System.IO.Directory.Exists(Path))
				return true;
			
			Process process = Process.GetProcessesByName(ExecutableFriendlyName)[0];
			if (process == null)
				return false;
			
			Log.Trace("Watched item '" + FriendlyName + "' encountered.");
			try
			{
				string corepath = System.IO.Path.GetDirectoryName(process.MainModule.FileName);
				string fullpath = System.IO.Path.Combine(corepath, Subpath);
				if (System.IO.Directory.Exists(fullpath))
				{
					Path = fullpath;
					Log.Debug(string.Format("'{0}' bound to: {1}", FriendlyName, Path));

					onLocateHandler(this, new PathControlEventArgs());

					return true;
				}
			}
			catch (Exception ex)
			{
				Log.Warn(string.Format("Access failure with '{0}'", FriendlyName));
				Console.Error.WriteLine(ex);
			}
			return false;
		}

		public static event EventHandler<PathControlEventArgs> onTouch;
		void onTouchHandler(object sender, PathControlEventArgs e)
		{
			EventHandler<PathControlEventArgs> handler = onTouch;
			if (handler != null)
				handler(this, e);
		}

		public static event EventHandler<PathControlEventArgs> onLocate;
		void onLocateHandler(object sender, PathControlEventArgs e)
		{
			EventHandler<PathControlEventArgs> handler = onLocate;
			if (handler != null)
				handler(this, e);
		}

	}

	public class PathControlEventArgs : EventArgs
	{
	}

	public class ProcessEventArgs : EventArgs
	{
		public ProcessController Control { get; set; }
		public Process Process { get; set; }
	}

	public class ProcessManager : IDisposable
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Actively watched process images.
		/// </summary>
		public List<ProcessController> images = new List<ProcessController>();
		/// <summary>
		/// Actively watched paths.
		/// </summary>
		public List<PathControl> pathwatch = new List<PathControl>();
		/// <summary>
		/// Paths not yet properly initialized.
		/// </summary>
		public List<PathControl> pathinit = new List<PathControl>();
		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		IDictionary<string, ProcessController> execontrol = new Dictionary<string,ProcessController>();

		int numCPUs = 1;

		public static int allCPUsMask = 1;

		/// <summary>
		/// Gets the control class instance of the executable if it exists.
		/// </summary>
		/// <returns>ProcessControl </returns>
		/// <param name="executable">Executable.</param>
		public ProcessController getControl(string executable)
		{
			foreach (ProcessController ctrl in images)
			{
				if (ctrl.Executable == executable)
					   return ctrl;
			}
			Log.Warn(executable + " was not found!");
			return null;
		}

		void UpdatePathWatch()
		{
			Log.Trace("Locating watched paths.");
			if (pathinit.Count > 0)
			{
				foreach (PathControl path in pathinit.ToArray())
				{
					if (!pathwatch.Contains(path) && path.Locate())
					{
						pathwatch.Add(path);
						pathinit.Remove(path);
					}
				}
			}
			Log.Trace("Path location complete.");
		}

		string[] ignoredProcesses = { "svchost", "taskeng", "dllhost", "consent", "taskeng", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon" };
		/// <summary>
		/// Processes everything. Pointlessly thorough, but there's no nicer way around for now.
		/// </summary>
		public void ProcessEverything()
		{
			Log.Trace("Processing everything.");

			Process[] procs = Process.GetProcesses();

			Log.Trace(string.Format("Scanning {0} processes.", procs.Length));
			foreach (Process process in procs)
			{
				// Skip 0 [Idle] and 4 [System], shouldn't rely on this, but nothing else to do about it.
				if (process.Id <= 4 || ignoredProcesses.Contains(process.ProcessName))
				{
					if (TaskMaster.VeryVerbose)
						Log.Trace("Ignoring system process: " + process.ProcessName + " (#" + process.Id + ")");
					continue;
				}
				CheckProcess(process);
			}

			Log.Trace("Going through process control list.");
			foreach (ProcessController control in images)
				control.Scan();

			UpdatePathWatch();
			Log.Trace("Done processing everything.");
		}

		/// <summary>
		/// Converts ProcessPriorityClass to ordered int for programmatic comparison.
		/// </summary>
		/// <returns>0 [Idle] to 4 [High]; defaultl: 2 [Normal]</returns>
		public static int PriorityToInt(ProcessPriorityClass priority)
		{
			switch (priority)
			{
				case ProcessPriorityClass.Idle: return 0;
				case ProcessPriorityClass.BelowNormal: return 1;
				default: return 2; //ProcessPriorityClass.Normal, 2
				case ProcessPriorityClass.AboveNormal: return 3;
				case ProcessPriorityClass.High: return 4;
			}
		}

		/// <summary>
		/// Converts int to ProcessPriorityClass.
		/// </summary>
		/// <returns>Idle [0] to High [4]; default: Normal [2]</returns>
		/// <param name="priority">0 [Idle] to 4 [High]</param>
		public static ProcessPriorityClass IntToPriority(int priority)
		{
			switch (priority)
			{
				case 0: return ProcessPriorityClass.Idle;
				case 1: return ProcessPriorityClass.BelowNormal;
				default: return ProcessPriorityClass.Normal;
				case 3: return ProcessPriorityClass.AboveNormal;
				case 4: return ProcessPriorityClass.High;
			}
		}

		SharpConfig.Configuration stats;
		bool stats_dirty = false;
		public void loadConfig()
		{
			Log.Trace("Loading watchlist");
			cfg = TaskMaster.loadConfig(configfile);
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			foreach (SharpConfig.Section section in cfg)
			{
				if (TaskMaster.VeryVerbose)
					Log.Debug("Section: "+section.Name);
				if (!section.Contains("image"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(string.Format("'{0}' has no image.", section.Name));
					continue;
				}
				if (!section.Contains("priority") && !section.Contains("affinity"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(string.Format("'{0}' has no priority or affinity.", section.Name));
					continue;
				}

				ProcessController cnt = new ProcessController(
					section.Name,
					section["image"].StringValue,
					section.Contains("priority") ? IntToPriority(section["priority"].IntValue) : ProcessPriorityClass.Normal,
					section.Contains("increase") ? section["increase"].BoolValue : false,
					section.Contains("affinity") ? section["affinity"].IntValue : 0,
					section.Contains("boost") ? section["boost"].BoolValue : true,
					section.Contains("rescan") ? section["rescan"].IntValue : 0
				);

				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				if (stats.Contains(cnt.Executable))
				{
					cnt.Adjusts = stats[cnt.Executable].Contains("Adjusts") ? stats[cnt.Executable]["Adjusts"].IntValue : 0;
					cnt.LastSeen = stats[cnt.Executable].Contains("Last seen") ? stats[cnt.Executable]["Last seen"].DateTimeValue : DateTime.MinValue;
					stats_dirty = true;
				}

				images.Add(cnt);
				execontrol.Add(new KeyValuePair<string, ProcessController>(cnt.ExecutableFriendlyName, cnt));
				if (TaskMaster.VeryVerbose)
					Log.Trace(string.Format("'{0}' added to monitoring.", section.Name));
			}
		}

		/// <summary>
		/// Retrieve file path for the process. Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		string GetProcessPath(int processId)
		{
			string wmiQueryString = "SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
			{
				using (var results = searcher.Get())
				{
					System.Management.ManagementObject mo = results.Cast<System.Management.ManagementObject>().FirstOrDefault();
					if (mo != null)
					{
						string path = (string)mo["ExecutablePath"];
						if (path != null && TaskMaster.VeryVerbose)
							Log.Debug("WMI fetch (#" + processId + "): " + path);
						return path;
					}
				}
			}
			return null;
		}

		void CheckPathWatch(Process process)
		{
			Debug.Assert(process != null);

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.Verbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", process.ProcessName, process.Id));
					return;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				return; // we don't care wwhat this error is
			}

			bool slow = false;
			string path;
			try
			{
				path = process.MainModule.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (NullReferenceException)
			{
				Log.Warn("[Unexpected] Null reference: " + process.ProcessName + " (pid:" + process.Id + ")");
				return;
			}
			catch (NotSupportedException)
			{
				Log.Warn("[Unexpected] Not supported operation: " + process.ProcessName + " (pid:" + process.Id + ")");
				return;
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				path = GetProcessPath(process.Id);
				if (path == null)
				{
					switch (ex.NativeErrorCode)
					{
						case 5:
							Log.Trace(string.Format("Access denied to '{0}' (pid:{1})", process.ProcessName, process.Id));
							break;
						case 299: // 32/64 bit taskmaster accessing opposite
							Log.Debug(string.Format("Can not fully access '{0}' (pid:{1})", process.ProcessName, process.Id));
							break;
						default:
							Log.Debug(string.Format("Unknown failure with '{0}' (pid:{1}), error: {2}", process.ProcessName, process.Id, ex.NativeErrorCode));
							Log.Debug(ex);
							break;
					}
					// we can not touch this so we shouldn't even bother trying
					Log.Trace("Failed to access '{0}' (pid:{1})", process.ProcessName, process.Id);
					return;
				}
				slow = true;
			}

			// TODO: This needs to be FASTER
			foreach (PathControl pc in pathwatch)
			{
				//Log.Debug("with: "+ pc.Path);
				if (path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
				{
					Log.Info("[" + pc.FriendlyName + "] matched " + (slow ? "~slowly~ " : "") + "at: " + path);
					pc.Touch(process, path);
					return;
				}
			}

			if (TaskMaster.VeryVerbose)
				Log.Trace("Not for us: " + path);
		}

		async System.Threading.Tasks.Task CheckProcess(Process process)
		{
			Debug.Assert(process != null);

			if (TaskMaster.VeryVerbose)
				Log.Debug("Processing: " + process.ProcessName);

			// TODO: check proc.processName for presence in images.
			ProcessController control;
			if (execontrol.TryGetValue(process.ProcessName, out control))
			{
				if (TaskMaster.VeryVerbose)
					Log.Debug(string.Format("Delaying touching of '{0}' (pid:{1})", control.Executable, process.Id));
				if (TaskMaster.VeryVerbose)
					Log.Debug("Control group: " + control.FriendlyName + ", process: " + process.ProcessName + " (#" + process.Id + ")");
				control.Touch(process);
			}
			else if (pathwatch.Count > 0)
			{
				if (TaskMaster.VeryVerbose)
					Log.Debug(string.Format("Checking paths for '{0}' (pid:{1})", process.ProcessName, process.Id));
				CheckPathWatch(process);
			}
			else
				Log.Trace("No paths watched, ignoring: " + process.ProcessName + " (#" + process.Id + ")");
		}

		void NewInstanceHandler(object sender, System.Management.EventArrivedEventArgs e)
		{
			System.Management.ManagementBaseObject targetInstance;
			int pid;
			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value);
			}
			catch (Exception)
			{
				Log.Warn("Failed to extract process ID from WMI event.");
				throw;
			}

			Process process = null;
			try
			{
				// since targetinstance actually has fuckall information, we need to extract it...
				process = Process.GetProcessById(pid);
			}
			catch (Exception)
			{
				Log.Trace("Process exited before we had time to identify it."); // technically an error [Warn], but not that interesting for anyone
				return;
			}

			if (TaskMaster.Verbose)
				Log.Trace("Caught: " + process.ProcessName + " (pid:"+process.Id+")");
			
			CheckProcess(process);
		}

		void LoadPathList()
		{
			Log.Trace("Loading user defined paths...");
			pathcfg = TaskMaster.loadConfig(pathfile);
			foreach (SharpConfig.Section section in pathcfg)
			{
				string name = section.Name;
				string executable = section.Contains("image") ? section["image"].StringValue : null;
				string path = section.Contains("path") ? section["path"].StringValue : null;
				string subpath = section.Contains("subpath") ? section["subpath"].StringValue : null;
				bool increase = section.Contains("increase") ? section["increase"].BoolValue : true;
				ProcessPriorityClass priority = section.Contains("priority") ? IntToPriority(section["priority"].IntValue) : ProcessPriorityClass.Normal;

				// TODO: technically subpath should be enough...
				if (path == null)
				{
					if (subpath == null)
					{
						Log.Warn(name + " does not have 'path' nor 'subpath'.");
						continue;
					}
					if (executable == string.Empty)
					{
						Log.Warn(name + " has no 'path' nor 'image'.");
						continue;
					}
				}

				if (!System.IO.Directory.Exists(path))
				{
					Log.Warn(path + "(" + name + ") does not exist.");
					if (subpath == null && executable != null)
						continue; // we can't use this info to figure out new path
					path = null; // should be enough to construct new path
				}

				if (path != null && subpath != null && !path.Contains(subpath))
					Log.Warn(name + " is misconfigured: " + subpath + " not in " + path); // we don't really care

				PathControl pc = new PathControl(name, executable, priority, subpath, path);
				if (pc.Locate())
				{
					pathwatch.Add(pc);
					pathinit.Remove(pc);
					if (!section.Contains("path") || section["path"].StringValue != pc.Path)
					{
						section["path"].StringValue = pc.Path;
						pathfile_dirty = true;
					}
					Log.Trace(name + " (" + pc.Path + ") added to active watch list.");
				}
				else
				{
					pathinit.Add(pc);
					Log.Trace(name + " ("+subpath+") added to init list.");
				}
			}
			Log.Trace("Path loading complete.");
		}

		public List<PathControl> ActivePaths()
		{
			return pathwatch;
		}

		System.Management.ManagementEventWatcher watcher;
		SharpConfig.Configuration cfg;
		SharpConfig.Configuration pathcfg;
		const string configfile = "Apps.ini";
		const string pathfile = "Paths.ini";
		bool pathfile_dirty = false;
		const string statfile = "Apps.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Trace("Starting...");

			numCPUs = Environment.ProcessorCount;
			Log.Info(string.Format("Processor count: {0}", numCPUs));

			// TODO: Use something simpler?
			// is there really no easier way?
			System.Collections.BitArray bits = new System.Collections.BitArray(numCPUs);
			for (int i = 0; i < numCPUs; i++)
				bits.Set(i, true);
			int[] bint = new int[1];
			bits.CopyTo(bint, 0);
			allCPUsMask = bint[0];
			Log.Info(string.Format("Full mask: {0} ({1})", Convert.ToString(allCPUsMask, 2), allCPUsMask));

			loadConfig();
			LoadPathList();

			// FIXME: doesn't seem to work when lots of new processes start at the same time.
			try
			{
				watcher = new System.Management.ManagementEventWatcher(
					@"\\.\root\CIMV2", // @"\\.\root\CIMV2"
					"SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'"
				);
			}
			catch (System.Management.ManagementException e)
			{
				Log.Error("Failed to initialize WMI event watcher: " + e.Message);
				throw;
			}
			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceHandler;
				/*
				// Only useful for debugging the watcher, but there doesn't seem to be any unwanted stops happening.
				watcher.Stopped += (object sender, System.Management.StoppedEventArgs e) =>
				{
					Log.Warn("New instance watcher stopped.");
				};
				*/
				watcher.Start();
				Log.Debug("New instance watcher initialized.");
			}
			else
			{
				Log.Error("Failed to initialize new instance watcher.");
				throw new InitFailure("New instance watcher not initialized");
			}
		}

		bool disposed = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				//TaskMaster.saveConfig(configfile, cfg); // we aren't modifyin it yet
				if (stats_dirty)
					saveStats();
				if (pathfile_dirty)
					TaskMaster.saveConfig(pathfile, pathcfg);
				
				watcher.Stop(); // shouldn't be necessary

				// Free any other managed objects here.
				//
			}

			// Free any unmanaged objects here.
			//
			disposed = true;
		}

		void saveStats()
		{
			Log.Trace("Saving stats...");
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			foreach (ProcessController proc in images)
			{
				if (proc.Adjusts > 0)
				{
					stats[proc.Executable]["Adjusts"].IntValue = proc.Adjusts;
					stats_dirty = true;
				}
				if (proc.LastSeen != DateTime.MinValue)
				{
					stats[proc.Executable]["Last seen"].DateTimeValue = proc.LastSeen;
					stats_dirty = true;
				}
			}

			TaskMaster.saveConfig(statfile, stats);
		}
	}
}

