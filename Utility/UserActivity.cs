﻿//
// UserActivity.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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

namespace MKAh
{
	public static class User
	{
		/// <summary>
		/// User idle time in seconds.
		/// </summary>
		public static TimeSpan IdleTime()
		{
			uint ums = LastActive();
			uint ems = Taskmaster.NativeMethods.GetTickCount();

			long fms = ems;
			if (ums > ems) fms -= uint.MaxValue - ums; // overflow
			else fms -= ums;
			//Console.WriteLine($"IdleTime\n- Idle:  {ums}\n- Env:   {ems}\n-   {(ums > ems ? "Over+"+(uint.MaxValue - ems) : "Std")}\n- Final: {ms}");

			return LastActiveTimespan(fms);
		}

		/// <summary>
		/// Wrapper for GetLastInputInfo() which returns 32 bit "tick" count when user was last active.
		/// Does not detect gamepad activity.
		/// Actually returns milliseconds instead of ticks despite the documentation.
		/// </summary>
		/// <returns>Milliseconds since boot.</returns>
		// BUG: This gets weird if the system has not been rebooted in 24.9 days
		// https://docs.microsoft.com/en-us/dotnet/api/system.environment.tickcount?view=netframework-4.7.2
		// https://docs.microsoft.com/en-us/windows/desktop/api/winuser/ns-winuser-taglastinputinfo
		public static uint LastActive()
		{
			var info = new LASTINPUTINFO();
			info.cbSize = (uint)Marshal.SizeOf(info);
			info.dwTime = 0;
			GetLastInputInfo(ref info); // ignore failure to retrieve data

			return info.dwTime;
		}

		/// <summary>
		/// Should be called in same thread as LastActive. Odd behaviour expected if the code runs on different core.
		/// </summary>
		/// <param name="ms">Last active time, as returned by LastActive</param>
		/// <returns>Seconds for how long user has been idle</returns>
		public static TimeSpan LastActiveTimespan(long ms)
		{
			return TimeSpan.FromMilliseconds(ms);
		}

		/// <summary>
		/// Official documentation lies about dwTime being ticks. It's actually milliseconds
		/// ... unless the concept of ticks has changed since.
		/// </summary>
		/// <param name="lastinputinfo"></param>
		/// <returns></returns>
		[DllImport("user32.dll")]
		internal static extern bool GetLastInputInfo(ref LASTINPUTINFO lastinputinfo);

		[StructLayout(LayoutKind.Sequential)]
		internal struct LASTINPUTINFO
		{
			public static readonly int SizeOf = Marshal.SizeOf(typeof(LASTINPUTINFO));

			[MarshalAs(UnmanagedType.U4)]
			public UInt32 cbSize;
			[MarshalAs(UnmanagedType.U4)]
			public UInt32 dwTime;
		}
	}
}
