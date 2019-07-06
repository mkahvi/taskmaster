﻿//
// Utility.cs
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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Taskmaster
{
	public static class Logging
	{
		static void AppendStacktace(Exception ex, ref StringBuilder output)
		{
			output.AppendLine()
				.Append("Exception:    ").AppendLine(ex.GetType().Name)
				.Append("Message:      ").AppendLine(ex.Message).AppendLine();

			var projectdir = Properties.Resources.ProjectDirectory.Trim();
			var trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
			output.AppendLine("----- Stacktrace -----")
				.AppendLine(trace);
		}

		[Conditional("DEBUG")]
		public static void DebugMsg(string message)
			=> System.Diagnostics.Debug.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);

		public static void Stacktrace(Exception ex, bool crashsafe = false, [CallerMemberName] string method = "", [CallerLineNumber] int lineNo = -1, [CallerFilePath] string file = "")
		{
			var projectdir = Properties.Resources.ProjectDirectory.Trim();
			if (!crashsafe)
			{
				string trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
				Serilog.Log.Fatal($"Exception [{method}:{lineNo}]: {ex.GetType().Name} : {ex.Message}\n{trace}");
				if (ex is InitFailure iex)
				{
					if ((iex.InnerExceptions?.Length ?? 0) > 1)
					{
						for (int i = 1; i < iex.InnerExceptions.Length; i++)
						{
							trace = iex.InnerExceptions[i].StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
							Serilog.Log.Fatal($"Exception: {iex.InnerExceptions[i].GetType().Name} : {iex.InnerExceptions[i].Message}\n{trace}");
						}
					}
				}
			}
			else
			{
				if (Taskmaster.NoLogging) return;

				try
				{
					if (!System.IO.Directory.Exists(Taskmaster.LogPath)) System.IO.Directory.CreateDirectory(Taskmaster.LogPath);

					string logfilename = Taskmaster.UniqueCrashLogs ? $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")}.log" : "crash.log";
					var logfile = System.IO.Path.Combine(Taskmaster.LogPath, logfilename);

					var now = DateTime.Now;

					file = file.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);

					var sbs = new StringBuilder();
					sbs.Append("Datetime:     ").Append(now.ToLongDateString()).Append(" ").AppendLine(now.ToLongTimeString())
						.Append("Caught at: ").Append(method).Append(":").Append(lineNo).Append(" [").Append(file).AppendLine("]")
						.AppendLine()
						.Append("Command line: ").AppendLine(Environment.CommandLine);

#if DEBUG
					var exceptionsbs = new StringBuilder();
#endif
					AppendStacktace(ex, ref sbs);
#if DEBUG
					AppendStacktace(ex, ref exceptionsbs);
#endif
					if (ex.InnerException != null)
					{
						sbs.AppendLine().AppendLine("--- Inner Exception ---");
						AppendStacktace(ex.InnerException, ref sbs);
#if DEBUG
						AppendStacktace(ex.InnerException, ref exceptionsbs);
#endif
					}

					if (ex is InitFailure iex && (iex.InnerExceptions?.Length ?? 0) > 1)
					{
						for (int i = 1; i < iex.InnerExceptions.Length; i++)
							AppendStacktace(iex.InnerExceptions[i], ref exceptionsbs);
					}

					System.IO.File.WriteAllText(logfile, sbs.ToString(), Encoding.Unicode);
					DebugMsg("Crash log written to " + logfile);
#if DEBUG
					Debug.WriteLine(exceptionsbs.ToString());
#endif

				}
				catch (OutOfMemoryException) { throw; }
				catch
				{
					throw; // nothing to be done, we're already crashing and burning by this point
				}
			}
		}
	}
}