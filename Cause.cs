﻿//
// Cause.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

namespace Taskmaster
{
	public enum OriginType
	{
		None,
		Watchlist,
		Internal,
		AutoAdjust,
		User,
		Session
	}

	public sealed class Cause
	{
		public Cause(OriginType origin = OriginType.None, string detail = "")
		{
			Origin = origin;
			Detail = detail;
		}

		readonly public OriginType Origin = OriginType.None;
		readonly public string Detail = string.Empty;

		public override string ToString()
		{
			string str = string.Empty;
			switch (Origin)
			{
				case OriginType.User:
					return "User Action";
				case OriginType.Session:
					return "Session " + Detail;
				case OriginType.AutoAdjust:
					return "Auto-adjust: " + Detail; // ugly, but...
				case OriginType.Watchlist:
					return "Watchlist: " + Detail;
				case OriginType.Internal:
				default:
					return string.IsNullOrEmpty(Detail) ? HumanReadable.Generic.Undefined : Detail;
			}
		}
	}
}
