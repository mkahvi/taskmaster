﻿//
// HealthMonitor.cs
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster.Settings
{
	sealed class HealthMonitor
	{
		/// <summary>
		/// Scanning frequency.
		/// </summary>
		public TimeSpan Frequency { get; set; } = TimeSpan.FromMinutes(5);

		/// <summary>
		/// Free megabytes.
		/// </summary>
		public ulong MemLevel { get; set; } = 1000;

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

		/// <summary>
		/// Low drive space threshold in megabytes.
		/// </summary>
		public long LowDriveSpaceThreshold { get; set; } = 150;
	}
}
