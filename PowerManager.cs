﻿//
// PowerManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Serilog;
using Microsoft.Win32;
using System.ComponentModel;

namespace TaskMaster
{
	public class PowerModeEventArgs : EventArgs
	{
		public PowerManager.PowerMode OldMode { get; set; }
		public PowerManager.PowerMode NewMode { get; set; }
	}

	public class PowerManager : Form // form is required for receiving messages, no other reason
	{
		//static Guid GUID_POWERSCHEME_PERSONALITY = new Guid("245d8541-3943-4422-b025-13A7-84F679B7");
		static Guid GUID_POWERSCHEME_PERSONALITY = new Guid(0x245D8541, 0x3943, 0x4422, 0xB0, 0x25, 0x13, 0xA7, 0x84, 0xF6, 0x79, 0xB7);

		public PowerManager()
		{
			RegisterPowerSettingNotification(Handle, ref GUID_POWERSCHEME_PERSONALITY, DEVICE_NOTIFY_WINDOW_HANDLE);
			getPowerMode();
			Original = Current;

			SystemEvents.PowerModeChanged += BatteryChargingEvent;
			SystemEvents.SessionEnding += TaskMaster.ExitRequest;
			SystemEvents.SessionSwitch += SessionLockEvent;

			LoadConfig();
		}

		public event EventHandler<ProcessorEventArgs> onAutoAdjust;

		PowerMode PreviousReaction = PowerMode.Balanced;
		public void CPULoadEvent(object sender, ProcessorEventArgs ev)
		{
			PowerMode Reaction = PowerMode.Balanced;

			float hight = HighThreshold;
			if (PreviousReaction == PowerMode.HighPerformance) hight = HighThresholdBackoff;
			float lowt = LowThreshold;
			if (PreviousReaction == PowerMode.PowerSaver) lowt = LowThresholdBackoff;

			if (ev.Low > hight)
				Reaction = PowerMode.HighPerformance;
			else if (ev.High < lowt)
				Reaction = PowerMode.PowerSaver;
			else
				Reaction = PowerMode.Balanced;

			if (Reaction != Current && !ForcedMode)
				Log.Verbose("<Power Mode> Auto-adjust: {Mode}", Reaction.ToString());

			RequestMode(Reaction);

			PreviousReaction = Reaction;

			ev.Action = (int)Reaction;
			onAutoAdjust?.Invoke(this, ev);
		}

		void LoadConfig()
		{
			var power = TaskMaster.cfg["Power"];
			bool modified = false, tdirty = false;

			AutoAdjust = power.GetSetDefault("Auto-adjust", false, out modified).BoolValue;
			power["Auto-adjust"].Comment = "Automatically adjust power mode based on the criteria here.";
			tdirty |= modified;

			HighThreshold = power.GetSetDefault("High threshold", 60, out modified).FloatValue;
			power["High threshold"].Comment = "If low CPU value keeps over this, we swap to high mode.";
			tdirty |= modified;
			HighThresholdBackoff = power.GetSetDefault("High threshold backoff", 50, out modified).FloatValue;
			power["High threshold backoff"].Comment = "Entering high mode requires we go below this to get away from it.\nThis should be at least 5% lower than normal high threshold.";
			tdirty |= modified;

			string highmode = power.GetSetDefault("High mode", "High Performance", out modified).StringValue;
			HighMode = GetModeByName(highmode);
			tdirty |= modified;

			LowThreshold = power.GetSetDefault("Low threshold", 20, out modified).FloatValue;
			power["Low threshold"].Comment = "If high CPU value keeps under this, we swap to low mode.";
			tdirty |= modified;
			LowThresholdBackoff = power.GetSetDefault("Low threshold backoff", 25, out modified).FloatValue;
			power["Low threshold backoff"].Comment = "Entering low mode requires we go above this to get away from it.\nThis should be at least 5% higher than normal low threhshold.";
			tdirty |= modified;

			string lowmode = power.GetSetDefault("Low mode", "Power Saver", out modified).StringValue;
			LowMode = GetModeByName(lowmode);
			tdirty |= modified;

			string medmode = power.GetSetDefault("Normal mode", "Balanced", out modified).StringValue;
			power["Normal mode"].Comment = "This is what we keep at when neither high or low match.";
			NormalMode = GetModeByName(medmode);
			tdirty |= modified;

			var saver = TaskMaster.cfg["Power / Power Save"];
			bool sessionlock = power.GetSetDefault("Session lock", true, out modified).BoolValue;
			tdirty |= modified;
			bool screenlock = power.GetSetDefault("Monitor sleep", true, out modified).BoolValue;
			tdirty |= modified;
			int afktimer = power.GetSetDefault("User idle", 30, out modified).IntValue;
			tdirty |= modified;

			if (AutoAdjust)
				Log.Information("Power Manager: Auto-adjust enabled – Low({Low}→{LowB}), High({High}→{HighB})",
								string.Format("{0:N0}%", LowThreshold), string.Format("{0:N0}%", LowThresholdBackoff),
								string.Format("{0:N0}%", HighThreshold), string.Format("{0:N0}%", HighThresholdBackoff));
			else
				Log.Information("Power Manager: Auto-adjust disabled");

			if (tdirty)
				TaskMaster.MarkDirtyINI(TaskMaster.cfg);
		}

		public float HighThreshold { get; private set; } = 60;
		public float HighThresholdBackoff { get; private set; } = 50;
		PowerMode HighMode { get; set; }
		public float LowThreshold { get; private set; } = 25;
		public float LowThresholdBackoff { get; private set; } = 35;
		PowerMode LowMode { get; set; }
		PowerMode NormalMode { get; set; }

		void SessionLockEvent(object sender, SessionSwitchEventArgs ev)
		{
			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLock:
					// SET POWER SAVER
					break;
				case SessionSwitchReason.SessionLogon:
					// RESTORE POWER MODE
					break;
				default:
					// HANDS OFF MODE
					break;
			}
		}

		public event EventHandler onResume;

		void BatteryChargingEvent(object sender, PowerModeChangedEventArgs ev)
		{
			switch (ev.Mode)
			{
				case Microsoft.Win32.PowerModes.StatusChange:
					Log.Information("Undefined battery/AC change detected.");
					break;
				case Microsoft.Win32.PowerModes.Suspend:
					// DON'T TOUCH
					Log.Information("Hibernation/Suspend start detected.");
					break;
				case Microsoft.Win32.PowerModes.Resume:
					Log.Information("Hibernation/Suspend end detected.");
					onResume?.Invoke(this, null);
					// Invoke whatever is necessary to restore functionality after suspend breaking shit.
					break;
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
			{
				POWERBROADCAST_SETTING ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(POWERBROADCAST_SETTING));

				if (ps.PowerSetting == GUID_POWERSCHEME_PERSONALITY && ps.DataLength == Marshal.SizeOf(typeof(Guid)))
				{
					IntPtr pData = (IntPtr)(m.LParam.ToInt32() + Marshal.SizeOf(ps));  // (*1)
					Guid newPersonality = (Guid)Marshal.PtrToStructure(pData, typeof(Guid));
					PowerMode old = Current;
					if (newPersonality == Balanced) { Current = PowerMode.Balanced; }
					else if (newPersonality == HighPerformance) { Current = PowerMode.HighPerformance; }
					else if (newPersonality == PowerSaver) { Current = PowerMode.PowerSaver; }
					else { Current = PowerMode.Undefined; }

					onModeChange?.Invoke(this, new PowerModeEventArgs { OldMode = old, NewMode = Current });

					Log.Verbose("Power plan changed to: {PlanName} ({PlanGuid})", Current.ToString(), newPersonality.ToString());
				}
			}

			base.WndProc(ref m); // is this necessary
		}

		public static string[] PowerModes { get; } = { "Power Saver", "Balanced", "High Performance", "Undefined" };

		public static PowerMode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return PowerMode.Undefined;

			switch (name)
			{
				case "Power Saver":
					return PowerMode.PowerSaver;
				case "Balanced":
					return PowerMode.Balanced;
				case "High Performance":
					return PowerMode.HighPerformance;
				default:
					return PowerMode.Custom;
			}
		}

		public enum PowerMode
		{
			PowerSaver = 0,
			Balanced = 1,
			HighPerformance = 2,
			Custom = 9,
			Undefined = 3
		};

		public static PowerMode Original { get; private set; } = PowerMode.Balanced;
		public static PowerMode Current { get; private set; } = PowerMode.Balanced;

		static PowerMode SavedMode = PowerMode.Undefined;

		static Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // SCHEME_MIN
		static Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"); // SCHEME_BALANCED
		static Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"); // SCHEME_MAX

		public static event EventHandler<PowerModeEventArgs> onModeChange;

		public static bool AutoMode(bool enabled = true)
		{
			AutoAdjust = enabled;

			Log.Information("<Power Mode> Auto-adjust: {State}", (AutoAdjust ? "Enabled" : "Disabled"));
			return AutoAdjust;
		}

		public static void SaveMode()
		{
			if (AutoAdjust) return;

			if (SavedMode != PowerMode.Undefined) Log.Warning("<Power Mode> Saved mode is being overriden.");

			SavedMode = Current;

			if (SavedMode == PowerMode.Undefined)
			{
				Log.Warning("Failed to get current power plan, defafulting to balanced as restore option.");
				SavedMode = PowerMode.Balanced;
			}
		}

		public static void RestoreMode()
		{
			if (AutoAdjust) return;

			lock (powerLock)
			{
				if (SavedMode != PowerMode.Undefined)
				{
					setMode(SavedMode);
					ForcedMode = false;
					SavedMode = PowerMode.Undefined;
					Log.Verbose("<Power Mode> Restored to: {PowerMode}", Current.ToString());
				}
			}
		}

		static PowerMode getPowerMode()
		{
			Guid plan;
			IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));
			lock (powerLock)
			{
				if (PowerGetActiveScheme((IntPtr)null, out ptr) == 0)
				{
					plan = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
					Marshal.FreeHGlobal(ptr);

					if (plan == Balanced) { Current = PowerMode.Balanced; }
					else if (plan == PowerSaver) { Current = PowerMode.PowerSaver; }
					else if (plan == HighPerformance) { Current = PowerMode.HighPerformance; }
					else { Current = PowerMode.Undefined; }

					Log.Information("<Power Mode> Current: {Plan} ({Guid})", Current.ToString(), plan.ToString());
				}
			}
			return Current;
		}

		public static object powerLock = new object();
		public static object powerLockI = new object();

		public static bool AutoAdjust { get; private set; } = false;

		public static bool RequestMode(PowerMode mode)
		{
			if (!AutoAdjust) return false;

			lock (powerLock)
			{
				if (mode != Current && ForcedMode == false)
				{
					setMode(mode, verbose: false);
				}
				return false;
			}
		}

		static bool ForcedMode = false;

		public static bool ForceMode(PowerMode mode)
		{
			lock (powerLock)
			{
				if (mode != Current)
				{
					setMode(mode);
					Log.Verbose("Power mode forced to: {PowerMode}", Current);
					ForcedMode = true;
					return true;
				}
				return false;
			}
		}


		public static void setMode(PowerMode mode, bool verbose = true)
		{
			Guid plan = Guid.Empty;
			switch (mode)
			{
				default:
				case PowerMode.Balanced:
					plan = Balanced;
					break;
				case PowerMode.HighPerformance:
					plan = HighPerformance;
					break;
				case PowerMode.PowerSaver:
					plan = PowerSaver;
					break;
			}

			if (verbose)
				Log.Information("<Power Mode> Setting to: {Mode} ({Guid})", mode.ToString(), plan.ToString());
			lock (powerLockI)
				PowerSetActiveScheme((IntPtr)null, ref plan);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				Log.Verbose("Disposing power manager...");

				if (SavedMode != PowerMode.Undefined)
				{
					RestoreMode();
					Log.Information("Power mode restored.");
				}
				else if (AutoAdjust)
					RequestMode(Original);
			}

			disposed = true;
		}

		// UserPowerKey is reserved for future functionality and must always be null
		[DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
		static extern uint PowerSetActiveScheme(IntPtr UserPowerKey, ref Guid PowerPlanGuid);

		[DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme")]
		static extern uint PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr PowerPlanGuid);

		const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
		[DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterPowerSettingNotification", CallingConvention = CallingConvention.StdCall)]
		static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, Int32 Flags);

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		internal struct POWERBROADCAST_SETTING
		{
			public Guid PowerSetting;
			public uint DataLength;
			//public byte Data;
		}

		const int WM_SYSCOMMAND = 0x0112;
		const int WM_POWERBROADCAST = 0x218;
		const int SC_MONITORPOWER = 0xF170;
		const int PBT_POWERSETTINGCHANGE = 0x8013;
		const int HWND_BROADCAST = 0xFFFF;

		[Flags]
		enum SendMessageTimeoutFlags : uint
		{
			SMTO_NORMAL = 0x0,
			SMTO_BLOCK = 0x1,
			SMTO_ABORTIFHUNG = 0x2,
			SMTO_NOTIMEOUTIFNOTHUNG = 0x8,
			SMTO_ERRORONEXIT = 0x2
		}
	}
}
