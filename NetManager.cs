//
// NetManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
	public sealed class NetTrafficDelta
	{
		public float Input = float.NaN;
		public float Output = float.NaN;
		public float Queue = float.NaN;
	}

	public sealed class NetTrafficEventArgs : EventArgs
	{
		public NetTrafficDelta Delta = null;
	}

	sealed public class NetManager : IDisposable
	{
		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler IPChanged;
		public event EventHandler<NetworkStatus> NetworkStatusChange;

		public event EventHandler<NetTrafficEventArgs> NetworkTraffic;

		PerformanceCounterWrapper NetInTrans = null;
		PerformanceCounterWrapper NetOutTrans = null;
		PerformanceCounterWrapper NetQueue = null;

		string dnstestaddress = "google.com"; // should be fine, www is omitted to avoid deeper DNS queries

		int DeviceTimerInterval = 15 * 60;
		int PacketStatTimerInterval = 15; // second
		int ErrorReportLimit = 5;

		System.Timers.Timer SampleTimer;

		public event EventHandler<NetDeviceTrafficEventArgs> onSampling;

		void LoadConfig()
		{
			var cfg = Taskmaster.Config.Load("Net.ini");

			var dirty = false;
			var dirtyconf = false;

			var monsec = cfg.Config["Monitor"];
			dnstestaddress = monsec.GetSetDefault("DNS test", "www.google.com", out dirty).StringValue;
			dirtyconf |= dirty;

			var devsec = cfg.Config["Devices"];
			DeviceTimerInterval = devsec.GetSetDefault("Check frequency", 15, out dirty).IntValue.Constrain(1, 30) * 60;
			devsec["Check frequency"].Comment = "Minutes";
			dirtyconf |= dirty;

			var pktsec = cfg.Config["Traffic"];
			PacketStatTimerInterval = pktsec.GetSetDefault("Sample rate", 15, out dirty).IntValue.Constrain(1, 60);
			pktsec["Sample rate"].Comment = "Seconds";
			PacketWarning.Peak = PacketStatTimerInterval;
			dirtyconf |= dirty;

			ErrorReportLimit = pktsec.GetSetDefault("Error report limit", 5, out dirty).IntValue.Constrain(1, 60);
			ErrorReports.Peak = ErrorReportLimit;
			dirtyconf |= dirty;

			if (dirtyconf) cfg.MarkDirty();

			Log.Information("<Network> Traffic sample frequency: " + PacketStatTimerInterval + "s");
		}

		public NetManager()
		{
			var now = DateTimeOffset.UtcNow;

			UptimeRecordStart = now;
			LastUptimeStart = now;

			LoadConfig();

			InterfaceInitialization();

			UpdateInterfaces(); // initialize

			SampleTimer = new System.Timers.Timer(PacketStatTimerInterval * 1_000);
			SampleTimer.Elapsed += AnalyzeTrafficBehaviour;
			//SampleTimer.Elapsed += DeviceSampler;
			SampleTimer.Start();

			AnalyzeTrafficBehaviour(this, EventArgs.Empty); // initialize, not really needed

			/*
			// Reset time could be used for initial internet start time as it is the only even remotely relevant one
			// ... but it's not honestly truly indicative of it.
			using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DeviceID,TimeOfLastReset FROM Win32_NetworkAdapter"))
			{
				foreach (ManagementObject mo in searcher.Get())
				{
					string netreset = mo["TimeOfLastReset"] as string;
					var reset = ManagementDateTimeConverter.ToDateTime(netreset);
					Console.WriteLine("NET RESET: " + reset);
				}
			}
			*/

			// TODO: SUPPORT MULTIPLE NICS
			var firstnic = new PerformanceCounterCategory("Network Interface").GetInstanceNames()[1]; // 0 = loopback
			NetInTrans = new PerformanceCounterWrapper("Network Interface", "Bytes Received/sec", firstnic);
			NetOutTrans = new PerformanceCounterWrapper("Network Interface", "Bytes Sent/sec", firstnic);
			NetQueue = new PerformanceCounterWrapper("Network Interface", "Output Queue Length", firstnic);

			if (Taskmaster.DebugNet) Log.Information("<Network> Component loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		public NetTrafficDelta GetTraffic()
		{
			return new NetTrafficDelta()
			{
				Input = NetInTrans.Value,
				Output = NetOutTrans.Value,
				Queue = NetQueue.Value,
			};
		}

		private void DeviceSampler(object sender, System.Timers.ElapsedEventArgs e)
		{
			RecordUptimeState(InternetAvailable, false);
		}

		public string GetDeviceData(string devicename)
		{
			foreach (var device in CurrentInterfaceList)
			{
				if (device.Name.Equals(devicename))
				{
					return devicename + " – " + device.IPv4Address.ToString() + " [" + IPv6Address.ToString() + "]" +
						" – " + (device.Incoming.Bytes / 1_000_000) + " MB in, " + (device.Outgoing.Bytes / 1_000_000) + " MB out, " +
						(device.Outgoing.Errors + device.Incoming.Errors) + " errors";
				}
			}

			return null;
		}

		LinearMeter PacketWarning = new LinearMeter(15); // UNUSED
		LinearMeter ErrorReports = new LinearMeter(5, 4);

		List<NetDevice> CurrentInterfaceList = new List<NetDevice>(2);

		int TrafficAnalysisLimiter = 0;
		NetTrafficData outgoing, incoming, oldoutgoing, oldincoming;

		long errorsSinceLastReport = 0;
		DateTimeOffset lastErrorReport = DateTimeOffset.MinValue;

		async void AnalyzeTrafficBehaviour(object _, EventArgs _ea)
		{
			Debug.Assert(CurrentInterfaceList != null);

			if (!Atomic.Lock(ref TrafficAnalysisLimiter)) return;

			try
			{
				//PacketWarning.Drain();

				var oldifaces = CurrentInterfaceList;
				UpdateInterfaces(); // force refresh
				var ifaces = CurrentInterfaceList;

				if (oldifaces.Count != ifaces.Count)
				{
					if (Taskmaster.DebugNet) Log.Warning("<Network> Interface count mismatch (" + oldifaces.Count + " vs " + ifaces.Count + "), skipping analysis.");
					return;
				}

				if (ifaces == null) return; // no interfaces, just quit

				for (int index = 0; index < ifaces.Count; index++)
				{
					outgoing = ifaces[index].Outgoing;
					incoming = ifaces[index].Incoming;
					oldoutgoing = oldifaces[index].Outgoing;
					oldincoming = oldifaces[index].Incoming;

					long totalerrors = outgoing.Errors + incoming.Errors;
					long totaldiscards = outgoing.Errors + incoming.Errors;
					long totalunicast = outgoing.Errors + incoming.Errors;
					long errorsInSample = (incoming.Errors - oldincoming.Errors) + (outgoing.Errors - oldoutgoing.Errors);
					long discards = (incoming.Discards - oldincoming.Discards) + (outgoing.Discards - oldoutgoing.Discards);
					long packets = (incoming.Unicast - oldincoming.Unicast) + (outgoing.Unicast - oldoutgoing.Unicast);

					errorsSinceLastReport += errorsInSample;

					bool reportErrors = false;

					// TODO: Better error limiter.
					// Goals:
					// - Show initial error.
					// - Show errors in increasing rarity
					// - Reset the increased rarity once it becomes too rare

					if (Taskmaster.ShowNetworkErrors // user wants to see this
						&& errorsInSample > 0 // only if errors
						&& !ErrorReports.Peaked // we're not waiting for report counter to go down
						&& ErrorReports.Pump(errorsInSample)) // error reporting not full
					{
						reportErrors = true;
					}
					else
					{
						// no error reporting until the meter goes down, giving ErrorReports.Peak worth of samples to ignore for error reporting
						ErrorReports.Drain();
						reportErrors = (ErrorReports.IsEmpty && errorsSinceLastReport > 0);
					}

					var now = DateTimeOffset.UtcNow;
					TimeSpan period = lastErrorReport.TimeTo(now);
					double pmins = period.TotalHours < 24 ? period.TotalMinutes : double.NaN; // NaN-ify too large periods

					if (reportErrors)
					{
						Log.Warning($"<Network> {ifaces[index].Name} is suffering from traffic errors! (+{errorsSinceLastReport}, {errorsInSample} in last sample; period: {pmins:N1} minutes)");
						errorsSinceLastReport = 0;
						lastErrorReport = now;

						// TODO: Slow down reports if they're excessively frequent

						if (pmins < 1) ErrorReports.Peak += 5; // this slows down some reporting, but not in a good way
					}
					else
					{
						if (period.TotalMinutes > 5 && errorsSinceLastReport > 0) // report anyway
						{
							Log.Warning($"<Network> {ifaces[index].Name} had some traffic errors (+{errorsSinceLastReport}; period: {pmins:N1} minutes)");
							errorsSinceLastReport = 0;
							lastErrorReport = now;

							ErrorReports.Peak = 5; // reset
						}
					}

					onSampling?.Invoke(this, new NetDeviceTrafficEventArgs
					{
						Traffic =
						new NetDeviceTraffic
						{
							Index = index,
							Delta = new NetTrafficData { Unicast = packets, Errors = errorsInSample, Discards = discards },
							Total = new NetTrafficData { Unicast = totalunicast, Errors = totalerrors, Discards = totaldiscards, Bytes = incoming.Bytes + outgoing.Bytes },
						}
					});
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref TrafficAnalysisLimiter);
			}
		}

		public UI.TrayAccess Tray { get; set; } = null; // bad design

		public bool NetworkAvailable { get; private set; } = false;
		public bool InternetAvailable { get; private set; } = false;

		readonly int MaxSamples = 20;
		List<double> UptimeSamples = new List<double>(20);
		DateTimeOffset UptimeRecordStart; // since we started recording anything
		DateTimeOffset LastUptimeStart; // since we last knew internet to be initialized
		readonly object uptime_lock = new object();

		/// <summary>
		/// Current uptime in minutes.
		/// </summary>
		/// <value>The uptime.</value>
		public TimeSpan Uptime
		{
			get
			{
				if (InternetAvailable)
					return (DateTimeOffset.UtcNow - LastUptimeStart);

				return TimeSpan.Zero;
			}
		}

		/// <summary>
		/// Returns uptime in minutes or positive infinite if no average is known
		/// </summary>
		public double UptimeMean()
		{
			lock (uptime_lock)
			{
				return UptimeSamples.Count > 0 ? UptimeSamples.Average() : double.PositiveInfinity;
			}
		}

		bool InternetAvailableLast = false;

		Stopwatch Downtime = null;
		void ReportCurrentUpstate()
		{
			if (InternetAvailable != InternetAvailableLast) // prevent spamming available message
			{
				if (InternetAvailable)
				{
					Downtime?.Stop();
					var sbs = new StringBuilder();
					sbs.Append("<Network> Internet available.");
					if (Downtime != null)
					{
						sbs.Append($"{Downtime.Elapsed.TotalMinutes:N1}").Append(" minutes downtime.");
						Downtime = null;
					}
					Log.Information(sbs.ToString());
				}
				else
				{
					Log.Warning("<Network> Internet unavailable.");
					Downtime = Stopwatch.StartNew();
				}

				InternetAvailableLast = InternetAvailable;
			}
		}

		void ReportUptime()
		{
			var sbs = new System.Text.StringBuilder();

			sbs.Append("<Network> Average uptime: ");
			lock (uptime_lock)
			{
				var currentUptime = DateTimeOffset.UtcNow.TimeSince(LastUptimeStart).TotalMinutes;

				int cnt = UptimeSamples.Count;
				sbs.Append($"{(UptimeSamples.Sum() + currentUptime) / (cnt + 1):N1}").Append(" minutes");

				if (cnt >= 3)
					sbs.Append(" (").Append($"{(UptimeSamples.GetRange(cnt-3, 3).Sum() / 3f):N1}").Append(" minutes for last 3 samples");
			}

			sbs.Append(" since: ").Append(UptimeRecordStart)
			   .Append(" (").Append($"{(DateTimeOffset.UtcNow - UptimeRecordStart).TotalHours:N2}").Append("h ago)")
			   .Append(".");

			Log.Information(sbs.ToString());
		}

		bool lastOnlineState = false;
		int DeviceStateRecordLimiter = 0;

		DateTimeOffset LastUptimeSample = DateTimeOffset.MinValue;

		async void RecordUptimeState(bool online_state, bool address_changed)
		{
			if (!Atomic.Lock(ref DeviceStateRecordLimiter)) return;

			try
			{
				var now = DateTimeOffset.UtcNow;
				if (LastUptimeSample.TimeTo(now).TotalMinutes < DeviceTimerInterval)
					return;

				LastUptimeSample = now;
				if (online_state != lastOnlineState)
				{
					lastOnlineState = online_state;

					if (online_state)
					{
						LastUptimeStart = now;

						Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(x => ReportCurrentUpstate());
					}
					else // went offline
					{
						lock (uptime_lock)
						{
							var newUptime = (now - LastUptimeStart).TotalMinutes;
							UptimeSamples.Add(newUptime);

							if (UptimeSamples.Count > MaxSamples)
								UptimeSamples.RemoveAt(0);
						}

						//ReportUptime();
					}

					return;
				}
				else if (address_changed)
				{
					// same state but address change was detected
					Log.Verbose("<Network> Address changed but internet connectivity unaffected.");
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
			finally
			{
				Atomic.Unlock(ref DeviceStateRecordLimiter);
			}
		}

		bool Notified = false;

		int InetCheckLimiter; // = 0;
		bool CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (Atomic.Lock(ref InetCheckLimiter))
			{
				if (Taskmaster.Trace) Log.Verbose("<Network> Checking internet connectivity...");

				try
				{
					var oldInetAvailable = InternetAvailable;
					bool timeout = false;
					bool dnsfail = false;
					bool interrupt = false;
					if (NetworkAvailable)
					{
						try
						{
							Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
							InternetAvailable = true;
							Notified = false;
							// TODO: Don't rely on DNS?
						}
						catch (System.Net.Sockets.SocketException ex)
						{
							InternetAvailable = false;
							switch (ex.SocketErrorCode)
							{
								case System.Net.Sockets.SocketError.AccessDenied:
								case System.Net.Sockets.SocketError.SystemNotReady:
									break;
								case System.Net.Sockets.SocketError.TryAgain:
								case System.Net.Sockets.SocketError.TimedOut:
								default:
									timeout = true;
									InternetAvailable = true;
									return InternetAvailable;
								case System.Net.Sockets.SocketError.SocketError:
								case System.Net.Sockets.SocketError.Interrupted:
								case System.Net.Sockets.SocketError.Fault:
									interrupt = true;
									break;
								case System.Net.Sockets.SocketError.HostUnreachable:
									break;
								case System.Net.Sockets.SocketError.HostNotFound:
								case System.Net.Sockets.SocketError.HostDown:
									dnsfail = true;
									break;
								case System.Net.Sockets.SocketError.NetworkDown:
								case System.Net.Sockets.SocketError.NetworkReset:
								case System.Net.Sockets.SocketError.NetworkUnreachable:
									break;
							}
						}
					}
					else
						InternetAvailable = false;

					if (Taskmaster.Trace) RecordUptimeState(InternetAvailable, address_changed);

					if (oldInetAvailable != InternetAvailable)
					{
						needUpdate = true;
						ReportNetAvailability();
					}
					else
					{
						if (timeout)
							Log.Information("<Network> Internet availability test inconclusive, assuming connected.");

						if (!Notified && NetworkAvailable)
						{
							if (interrupt)
								Log.Warning("<Network> Internet check interrupted. Potential hardware/driver issues.");

							if (dnsfail)
								Log.Warning("<Network> DNS test failed, test host unreachable. Test host may be down.");

							Notified = dnsfail || interrupt;
						}

						if (Taskmaster.Trace) Log.Verbose("<Network> Connectivity unchanged.");
					}
				}
				finally
				{
					Atomic.Unlock(ref InetCheckLimiter);
				}
			}

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = LastUptimeStart, Uptime = Uptime });

			return InternetAvailable;
		}

		List<IPAddress> AddressList = new List<IPAddress>(2);
		// List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);
		IPAddress IPv4Address = IPAddress.None;
		NetworkInterface IPv4Interface;
		IPAddress IPv6Address = IPAddress.IPv6None;
		NetworkInterface IPv6Interface;

		void InterfaceInitialization()
		{
			bool ipv4 = false, ipv6 = false;
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				// TODO: Implement early exit and smarter looping

				IPAddress[] ipa = n.GetAddresses();
				foreach (IPAddress ip in ipa)
				{
					switch (ip.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							IPv4Address = ip;
							IPv4Interface = n;
							ipv4 = true;
							// PublicInterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							ipv6 = true;
							// PublicInterfaceList.Add(n);
							break;
					}
				}

				if (ipv4 && ipv6) break;
			}
		}

		readonly object interfaces_lock = new object();
		int InterfaceUpdateLimiter = 0;
		bool needUpdate = true;

		public void UpdateInterfaces()
		{
			if (!Atomic.Lock(ref InterfaceUpdateLimiter)) return;

			needUpdate = false;

			try
			{
				if (Taskmaster.DebugNet) Log.Verbose("<Network> Enumerating network interfaces...");

				var ifacelistt = new List<NetDevice>();
				// var ifacelist = new List<string[]>();

				var index = 0;
				NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface dev in adapters)
				{
					var ti = index++;
					if (dev.NetworkInterfaceType == NetworkInterfaceType.Loopback || dev.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
						continue;

					var stats = dev.GetIPStatistics();

					bool found4 = false, found6 = false;
					IPAddress _ipv4 = IPAddress.None, _ipv6 = IPAddress.None;
					foreach (UnicastIPAddressInformation ip in dev.GetIPProperties().UnicastAddresses)
					{
						switch (ip.Address.AddressFamily)
						{
							case System.Net.Sockets.AddressFamily.InterNetwork:
								_ipv4 = ip.Address;
								found4 = true;
								break;
							case System.Net.Sockets.AddressFamily.InterNetworkV6:
								_ipv6 = ip.Address;
								found6 = true;
								break;
						}

						if (found4 && found6) break; // kinda bad, but meh
					}

					var devi = new NetDevice
					{
						Index = ti,
						Id = Guid.Parse(dev.Id),
						Name = dev.Name,
						Type = dev.NetworkInterfaceType,
						Status = dev.OperationalStatus,
						Speed = dev.Speed,
						IPv4Address = _ipv4,
						IPv6Address = _ipv6,
					};

					devi.Incoming.From(stats, true);
					devi.Outgoing.From(stats, false);
					// devi.PrintStats();
					ifacelistt.Add(devi);

					if (Taskmaster.DebugNet) Log.Verbose("<Network> Interface: " + dev.Name);
				}

				lock (interfaces_lock) CurrentInterfaceList = ifacelistt;
			}
			finally
			{
				Atomic.Unlock(ref InterfaceUpdateLimiter);
			}
		}

		public List<NetDevice> GetInterfaces()
		{
			lock (interfaces_lock)
			{
				if (needUpdate) UpdateInterfaces();
				return CurrentInterfaceList;
			}
		}

		async void NetAddrChanged(object _, EventArgs _ea)
		{
			var now = DateTimeOffset.UtcNow;

			bool AvailabilityChanged = InternetAvailable;

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			CheckInet(address_changed: true);
			AvailabilityChanged = AvailabilityChanged != InternetAvailable;

			if (InternetAvailable)
			{
				IPAddress oldV6Address = IPv6Address;
				IPAddress oldV4Address = IPv4Address;

				InterfaceInitialization(); // Update IPv4Address & IPv6Address

				bool ipv4changed = false, ipv6changed = false;
				ipv4changed = !oldV4Address.Equals(IPv4Address);

				var sbs = new System.Text.StringBuilder();

				if (AvailabilityChanged)
				{
					Log.Information("<Network> Internet connection restored.");
					sbs.Append("Internet connection restored!").AppendLine();
				}

				if (ipv4changed)
				{
					var outstr4 = new System.Text.StringBuilder();
					outstr4.Append("<Network> IPv4 address changed: ").Append(oldV4Address).Append(" → ").Append(IPv4Address);
					Log.Information(outstr4.ToString());
					sbs.Append(outstr4).AppendLine();
				}

				ipv6changed = !oldV6Address.Equals(IPv6Address);

				if (ipv6changed)
				{
					var outstr6 = new System.Text.StringBuilder();
					outstr6.Append("<Network> IPv6 address changed: ").Append(oldV6Address).Append(" → ").Append(IPv6Address);
					Log.Information(outstr6.ToString());
					sbs.Append(outstr6).AppendLine();
				}

				if (sbs.Length > 0)
				{
					Tray.Tooltip(4000, sbs.ToString(), "Taskmaster",
						System.Windows.Forms.ToolTipIcon.Warning);

					// bad since if it's not clicked, we react to other tooltip clicks, too
					//Tray.TrayTooltipClicked += (s, e) => { /* something */ };
				}

				if (ipv6changed || ipv6changed)
				{
					IPChanged?.Invoke(this, EventArgs.Empty);
				}
			}
			else
			{
				if (AvailabilityChanged)
				{
					//Log.Warning("<Network> Unstable connectivity detected.");

					Tray.Tooltip(2000, "Unstable internet connection detected!", "Taskmaster",
						System.Windows.Forms.ToolTipIcon.Warning);
				}
			}

			//NetworkChanged(null,null);
		}

		public void SetupEventHooks()
		{
			NetworkChanged(this, EventArgs.Empty); // initialize event handler's initial values

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			// CheckInet().Wait(); // unnecessary?
		}

		bool LastReportedNetAvailable = false;
		bool LastReportedInetAvailable = false;

		void ReportNetAvailability()
		{
			var sbs = new System.Text.StringBuilder();

			bool changed = (LastReportedInetAvailable != InternetAvailable) || (LastReportedNetAvailable != NetworkAvailable);
			if (!changed) return; // bail out if nothing has changed

			sbs.Append("<Network> Status: ")
				.Append(NetworkAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(", Internet: ")
				.Append(InternetAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(" - ");

			if (NetworkAvailable && !InternetAvailable) sbs.Append("Route problems");
			else if (!NetworkAvailable) sbs.Append("Cable unplugged or router/modem down");
			else sbs.Append("All OK");

			if (!NetworkAvailable || !InternetAvailable) Log.Warning(sbs.ToString());
			else Log.Information(sbs.ToString());

			LastReportedInetAvailable = InternetAvailable;
			LastReportedNetAvailable = NetworkAvailable;
		}

		/// <summary>
		/// Non-blocking lock for NetworkChanged event output
		/// </summary>
		int NetworkChangeAntiFlickerLock = 0;
		/// <summary>
		/// For tracking how many times NetworkChanged is triggered
		/// </summary>
		int NetworkChangeCounter = 4; // 4 to force fast inet check on start
		/// <summary>
		/// Last time NetworkChanged was triggered
		/// </summary>
		DateTimeOffset LastNetworkChange = DateTimeOffset.MinValue;
		async void NetworkChanged(object _, EventArgs _ea)
		{
			var oldNetAvailable = NetworkAvailable;
			bool available = NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			LastNetworkChange = DateTimeOffset.UtcNow;

			NetworkChangeCounter++;

			NetworkStatusChange?.Invoke(this, new NetworkStatus { Available = available });

			// do stuff only if this is different from last time
			if (oldNetAvailable != available)
			{
				if (Atomic.Lock(ref NetworkChangeAntiFlickerLock))
				{
					try
					{
						await Task.Delay(0).ConfigureAwait(false);

						int loopbreakoff = 0;
						while (LastNetworkChange.TimeTo(DateTimeOffset.UtcNow).TotalSeconds < 5)
						{
							if (loopbreakoff++ >= 3) break; // arbitrary end based on double reconnect behaviour of some routers
							if (NetworkChangeCounter >= 4) break; // break off in case NetworkChanged event is received often enough
							await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
						}

						CheckInet();
						NetworkChangeCounter = 0;
						ReportNetAvailability();
					}
					finally
					{
						Atomic.Unlock(ref NetworkChangeAntiFlickerLock);
					}
				}

				ReportNetAvailability();
			}
			else
			{
				if (Taskmaster.DebugNet) Log.Debug("<Net> Network changed but still as available as before.");
			}
		}

		public void Dispose() => Dispose(true);

		bool disposed; // = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			// base.Dispose(disposing);

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing network monitor...");

				onSampling = null;
				InternetStatusChange = null;
				IPChanged = null;
				NetworkStatusChange = null;

				ReportUptime();

				SampleTimer?.Dispose();
			}

			disposed = true;
		}
	}
}