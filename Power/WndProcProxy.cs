﻿//
// Power.WndProcProxy.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2020 M.A.
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

namespace Taskmaster.Power
{
	public delegate void PowerModeDelegate(Guid guid);

	public class PowerModeEventArgs : EventArgs
	{
		public Guid Mode { get; set; }

		public PowerModeEventArgs(Guid guid) => Mode = guid;
	}

	// TODO: Merge all WndProc proxies, or make them partial definitions, make it extensible, or something.
	class WndProcProxy : Form, IDisposable
	{
		public PowerModeDelegate? PowerModeChanged;
		public MonitorPowerModeDelegate? MonitorPowerChange;

		public WndProcProxy() => _ = Handle; // HACK

		public void RegisterEventHooks()
		{
			var lpersonality = NativeMethods.GUID_POWERSCHEME_PERSONALITY;
			var displaystate = NativeMethods.GUID_CONSOLE_DISPLAY_STATE;

			NativeMethods.RegisterPowerSettingNotification(Handle, ref lpersonality, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
			NativeMethods.RegisterPowerSettingNotification(Handle, ref displaystate, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
		}

		protected override void WndProc(ref Message m)
		{
			if (disposed || !IsHandleCreated) return;

			if (m.Msg == NativeMethods.WM_POWERBROADCAST
				&& m.WParam.ToInt64() == NativeMethods.PBT_POWERSETTINGCHANGE)
			{
				var ps = (NativeMethods.PowerBroadcastSetting)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.PowerBroadcastSetting));

				if (ps.PowerSetting == NativeMethods.GUID_POWERSCHEME_PERSONALITY && ps.DataLength == Marshal.SizeOf(typeof(Guid)))
				{
					var pData = (IntPtr)(m.LParam.ToInt64() + Marshal.SizeOf(ps) - 4); // -8 is to align to the ps.Data
					var newPersonality = (Guid)Marshal.PtrToStructure(pData, typeof(Guid));

					PowerModeChanged?.Invoke(newPersonality);

					m.Result = IntPtr.Zero;
				}
				else if (ps.PowerSetting == NativeMethods.GUID_CONSOLE_DISPLAY_STATE)
				{
					MonitorPowerMode mode = ps.Data switch
					{
						0x0 => MonitorPowerMode.Off,
						0x1 => MonitorPowerMode.On,
						0x2 => MonitorPowerMode.Standby,
						_ => MonitorPowerMode.Invalid
					};

					MonitorPowerChange?.Invoke(mode);

					m.Result = IntPtr.Zero;
				}
			}

			base.WndProc(ref m); // is this necessary?
		}

		#region IDisposable Support
		bool disposed; // To detect redundant calls

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				PowerModeChanged = null;
				MonitorPowerChange = null;
			}

			base.Dispose(disposing);
		}

		// ~WndProcProxy()
		// {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		void IDisposable.Dispose() => Dispose(true);
		#endregion
	}
}
